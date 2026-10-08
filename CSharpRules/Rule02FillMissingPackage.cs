#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;

namespace CW.Assistant.Generated
{
    internal sealed class GeneratedAction
    {
        private const string ScriptVersion = "0.0.5";
        private const string ParameterName = "FOB_Leveransepakke";
        private const string IfcExcludeParameterName = "MC Exclude From IFC View";
        private const double PrimaryRadiusMm = 1500.0;
        private const double FallbackRadiusMm = 3000.0;
        private const double TieToleranceMm = 1.0;
        private const int MajorityCandidateLimit = 5;
        private const string LogPath = @"D:\Revit\Python\Revit_BIM_Agent\logs\history\Rule02_FOB_Leveransepakke.log";
        private static readonly HashSet<long> CenterLineCategoryIds = new HashSet<long>(
            Enum.GetValues(typeof(BuiltInCategory))
                .Cast<BuiltInCategory>()
                .Where(category => category.ToString().EndsWith("CenterLine", StringComparison.Ordinal))
                .Select(category => new ElementId(category).Value));

        private sealed class WorksetCheckoutDialogHandler
        {
            private const string TriggerMessage = "trying to check out a large number of elements";
            private readonly Action<string> log;

            internal int HandledCount { get; private set; }

            internal WorksetCheckoutDialogHandler(Action<string> log) => this.log = log;

            internal void HandleDialogBoxShowing(object? sender, DialogBoxShowingEventArgs eventArgs)
            {
                if (eventArgs is not TaskDialogShowingEventArgs taskDialog) return;
                string message = taskDialog.Message ?? string.Empty;
                string normalizedMessage = new string(message.Where(char.IsLetterOrDigit).ToArray());
                string normalizedTrigger = new string(TriggerMessage.Where(char.IsLetterOrDigit).ToArray());
                if (normalizedMessage.IndexOf(normalizedTrigger, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    if (normalizedMessage.IndexOf("checkout", StringComparison.OrdinalIgnoreCase) >= 0
                        && normalizedMessage.IndexOf("workset", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        log("WORKSHARING-DIAGNOSTIK: ikke-gjenkjent checkout-dialog: " + message.Substring(0, Math.Min(500, message.Length)).Replace("\r", " ").Replace("\n", " "));
                    }
                    return;
                }

                log("WORKSHARING-DIAGNOSTIK: gjenkjent dialog: " + message.Substring(0, Math.Min(500, message.Length)).Replace("\r", " ").Replace("\n", " "));
                try
                {
                    if (eventArgs.OverrideResult((int)TaskDialogResult.CommandLink1))
                    {
                        HandledCount++;
                        log("WORKSHARING: Revit godtok Check Out Worksets.");
                    }
                    else
                    {
                        log("WORKSHARING-BLOKKERING: Revit godtok ikke Check Out Worksets.");
                    }
                }
                catch (Exception exception)
                {
                    log("WORKSHARING-BLOKKERING: kunne ikke velge Check Out Worksets: " + exception.Message);
                }
            }
        }

        private sealed class Candidate
        {
            internal FamilyInstance Element { get; }
            internal string FamilyName { get; }
            internal string Package { get; }
            internal XYZ Point { get; }

            internal Candidate(FamilyInstance element, string familyName, string package, XYZ point)
            {
                Element = element;
                FamilyName = familyName;
                Package = package;
                Point = point;
            }
        }

        private sealed class Match
        {
            internal Candidate Candidate { get; }
            internal double Distance { get; }

            internal Match(Candidate candidate, double distance)
            {
                Candidate = candidate;
                Distance = distance;
            }
        }

        private sealed class PendingWrite
        {
            internal FamilyInstance Element { get; }
            internal Parameter Parameter { get; }
            internal string Package { get; }
            internal int CandidateCount { get; }
            internal bool UsedFallback { get; }

            internal PendingWrite(FamilyInstance element, Parameter parameter, string package, int candidateCount, bool usedFallback)
            {
                Element = element;
                Parameter = parameter;
                Package = package;
                CandidateCount = candidateCount;
                UsedFallback = usedFallback;
            }
        }

        public string Execute(UIApplication uiApplication, Document? activeDocument)
        {
            if (activeDocument is null)
            {
                return "FEIL: Ingen aktiv Revit-modell. Regel 2 stoppet før elementlesing.";
            }

            var log = new List<string>
            {
                string.Format(CultureInfo.InvariantCulture, "=== Regel 2 v{0} | {1:O} | {2} ===", ScriptVersion, DateTime.Now, activeDocument.Title)
            };
            var instances = new FilteredElementCollector(activeDocument)
                .OfClass(typeof(FamilyInstance))
                .WhereElementIsNotElementType()
                .Cast<FamilyInstance>()
                .Where(instance => !IsCenterLine(instance))
                .ToList();
            var targets = new List<FamilyInstance>();
            var candidates = new List<Candidate>();
            int missingParameterCount = 0;
            int unsupportedParameterCount = 0;
            int excludedFromIfcCount = 0;

            foreach (FamilyInstance instance in instances)
            {
                if (IsExcludedFromIfc(instance))
                {
                    excludedFromIfcCount++;
                    continue;
                }

                Parameter? parameter = instance.LookupParameter(ParameterName);
                if (parameter is null)
                {
                    missingParameterCount++;
                    continue;
                }
                if (parameter.StorageType != StorageType.String)
                {
                    unsupportedParameterCount++;
                    continue;
                }

                string? value = parameter.AsString();
                if (IsMissing(value))
                {
                    targets.Add(instance);
                    continue;
                }

                XYZ? point = GetPlacementPoint(instance);
                if (point is not null)
                {
                    candidates.Add(new Candidate(instance, GetFamilyName(instance), value!, point));
                }
            }

            var writes = new List<PendingWrite>();
            int unresolvedCount = 0;
            int readOnlyCount = 0;
            int fallbackCount = 0;

            foreach (FamilyInstance target in targets)
            {
                Parameter? parameter = target.LookupParameter(ParameterName);
                if (parameter is null || parameter.StorageType != StorageType.String)
                {
                    unresolvedCount++;
                    log.Add(FormatIssue(target.Id.Value, 0, "målparameteren mangler eller har feil lagringstype"));
                    continue;
                }
                string familyName = GetFamilyName(target);
                XYZ? point = GetPlacementPoint(target);
                if (point is null)
                {
                    unresolvedCount++;
                    log.Add(FormatIssue(target.Id.Value, 0, "plassering mangler"));
                    continue;
                }

                List<Match> nearby = FindMajorityCandidates(target, familyName, point, candidates, PrimaryRadiusMm);
                bool usedFallback = false;
                if (nearby.Count == 0)
                {
                    nearby = FindMajorityCandidates(target, familyName, point, candidates, FallbackRadiusMm);
                    usedFallback = nearby.Count > 0;
                }

                if (nearby.Count == 0)
                {
                    unresolvedCount++;
                    log.Add(FormatIssue(target.Id.Value, 0, "ingen kandidat innen 3000 mm"));
                    continue;
                }

                if (parameter.IsReadOnly)
                {
                    unresolvedCount++;
                    readOnlyCount++;
                    log.Add(FormatIssue(target.Id.Value, nearby.Count, "parameteren kan ikke skrives"));
                    continue;
                }

                var packageCounts = nearby
                    .GroupBy(match => match.Candidate.Package, StringComparer.Ordinal)
                    .Select(group => new { Package = group.Key, Count = group.Count() })
                    .OrderByDescending(group => group.Count)
                    .ToList();
                var winner = packageCounts[0];
                if (winner.Count <= nearby.Count / 2)
                {
                    unresolvedCount++;
                    log.Add(FormatIssue(target.Id.Value, nearby.Count, "ingen streng pakke-majoritet; fallback brukes ikke"));
                    continue;
                }

                writes.Add(new PendingWrite(target, parameter, winner.Package, nearby.Count, usedFallback));
            }

            int updatedCount = 0;
            if (writes.Count > 0)
            {
                var dialogHandler = new WorksetCheckoutDialogHandler(log.Add);
                uiApplication.DialogBoxShowing += dialogHandler.HandleDialogBoxShowing;
                try
                {
                    using (var transaction = new Transaction(activeDocument, "Fyll FOB_Leveransepakke fra nærliggende elementer"))
                    {
                        TransactionStatus startStatus = transaction.Start();
                        if (startStatus != TransactionStatus.Started)
                        {
                            return SaveAndReturn(log, string.Format(CultureInfo.InvariantCulture, "Regel 2 v{0}: transaksjonen startet ikke ({1}); ingen endringer utført.", ScriptVersion, startStatus));
                        }

                        try
                        {
                            foreach (PendingWrite write in writes)
                            {
                                if (!write.Parameter.Set(write.Package))
                                {
                                    throw new InvalidOperationException("Parameter.Set returnerte false for ElementId " + write.Element.Id.Value.ToString(CultureInfo.InvariantCulture));
                                }
                            }

                            TransactionStatus commitStatus = transaction.Commit();
                            if (commitStatus != TransactionStatus.Committed)
                            {
                                throw new InvalidOperationException("Transaksjonen ble ikke committed: " + commitStatus);
                            }
                        }
                        catch (Exception exception)
                        {
                            if (transaction.GetStatus() == TransactionStatus.Started)
                            {
                                transaction.RollBack();
                            }
                            log.Add("TRANSAKSJONSFEIL: " + exception.Message);
                            return SaveAndReturn(log, "Regel 2 v" + ScriptVersion + ": transaksjonen ble rullet tilbake; 0 verdier lagret. " + exception.Message);
                        }
                    }
                }
                finally
                {
                    uiApplication.DialogBoxShowing -= dialogHandler.HandleDialogBoxShowing;
                }

                updatedCount = writes.Count;
                foreach (PendingWrite write in writes)
                {
                    if (write.UsedFallback)
                    {
                        fallbackCount++;
                    }
                    log.Add(string.Format(
                        CultureInfo.InvariantCulture,
                        "OPPDATERT ElementId {0}: pakke '{1}', kandidater {2}{3}",
                        write.Element.Id.Value,
                        write.Package,
                        write.CandidateCount,
                        write.UsedFallback ? ", fallback" : string.Empty));
                }
            }

            log.Insert(1, string.Format(
                CultureInfo.InvariantCulture,
                "Instanser {0}; ekskludert IFC {1}; mål {2}; kandidater {3}; parameter mangler {4}; feil lagringstype {5}; oppdatert {6}; uavklart {7}; skrivebeskyttet {8}; fallback {9}",
                instances.Count,
                excludedFromIfcCount,
                targets.Count,
                candidates.Count,
                missingParameterCount,
                unsupportedParameterCount,
                updatedCount,
                unresolvedCount,
                readOnlyCount,
                fallbackCount));

            return SaveAndReturn(log, string.Format(
                CultureInfo.InvariantCulture,
                "Regel 2 v{0}: oppdatert {1}, uavklart {2}, fallback {3}, parameter mangler {4}, feil lagringstype {5}, ekskludert IFC {6}.",
                ScriptVersion,
                updatedCount,
                unresolvedCount,
                fallbackCount,
                missingParameterCount,
                unsupportedParameterCount,
                excludedFromIfcCount));
        }

            private static bool IsExcludedFromIfc(FamilyInstance instance)
            {
                IList<Parameter> parameters = instance.GetParameters(IfcExcludeParameterName);
                return parameters.Count == 1
                && parameters[0].StorageType == StorageType.Integer
                && parameters[0].HasValue
                && parameters[0].AsInteger() != 0;
            }

        private static List<Match> FindMajorityCandidates(
            FamilyInstance target,
            string targetFamilyName,
            XYZ targetPoint,
            List<Candidate> candidates,
            double radiusMm)
        {
            double radiusFeet = radiusMm / 304.8;
            List<Match> inRadius = candidates
                .Where(candidate => candidate.Element.Id.Value != target.Id.Value)
                .Where(candidate => !string.Equals(candidate.FamilyName, targetFamilyName, StringComparison.Ordinal))
                .Select(candidate => new Match(candidate, targetPoint.DistanceTo(candidate.Point)))
                .Where(match => match.Distance <= radiusFeet)
                .OrderBy(match => match.Distance)
                .ThenBy(match => match.Candidate.Element.Id.Value)
                .ToList();

            if (inRadius.Count <= MajorityCandidateLimit)
            {
                return inRadius;
            }

            double inclusionDistance = inRadius[MajorityCandidateLimit - 1].Distance + TieToleranceMm / 304.8;
            return inRadius.Where(match => match.Distance <= inclusionDistance).ToList();
        }

        private static XYZ? GetPlacementPoint(FamilyInstance instance)
        {
            if (instance.Location is LocationPoint pointLocation)
            {
                return pointLocation.Point;
            }
            if (instance.Location is LocationCurve curveLocation && curveLocation.Curve is not null)
            {
                return curveLocation.Curve.Evaluate(0.5, true);
            }

            BoundingBoxXYZ? bounds = instance.get_BoundingBox(null);
            return bounds is null
                ? null
                : (bounds.Min + bounds.Max) * 0.5;
        }

        private static string GetFamilyName(FamilyInstance instance)
        {
            try
            {
                return instance.Symbol?.Family?.Name ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static bool IsMissing(string? value)
        {
            return string.IsNullOrWhiteSpace(value)
                || string.Equals(value.Trim(), "--", StringComparison.Ordinal);
        }

        private static string FormatIssue(long elementId, int candidateCount, string reason)
        {
            return string.Format(CultureInfo.InvariantCulture, "UAVKLART ElementId {0}: kandidater {1}; årsak: {2}", elementId, candidateCount, reason);
        }

        private static string SaveAndReturn(List<string> log, string result)
        {
            string logResult;
            try
            {
                File.AppendAllText(LogPath, string.Join(Environment.NewLine, log) + Environment.NewLine, new UTF8Encoding(false));
                logResult = "Logg: " + LogPath;
            }
            catch (Exception exception)
            {
                logResult = "Logg kunne ikke skrives: " + exception.Message;
            }

            return result + " " + logResult;
        }

        private static bool IsCenterLine(Element element)
        {
            long categoryId = element.Category?.Id.Value ?? long.MinValue;
            return CenterLineCategoryIds.Contains(categoryId);
        }
    }
}