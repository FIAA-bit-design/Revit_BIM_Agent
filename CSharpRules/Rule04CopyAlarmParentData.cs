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
        private const string ScriptVersion = "0.0.3";
        private const string ParentFamilyToken = "kule";
        private const string ChildFamilyName = "Brannalarm tilkoblingspunkt magnethold glideskinne";
        private const string MatchParameterName = "FOB_ID";
        private const string ParentMarkParameterName = "FOB_Merkestreng";
        private const string ParentNameParameterName = "PGF_Mengdetype";
        private const string ChildMarkParameterName = "PGF_RIE_BetjenerMerke";
        private const string ChildNameParameterName = "PGF_RIE_BetjenerNavn";
        private const double InitialRadiusMm = 1000.0;
        private const double RadiusStepMm = 250.0;
        private const double MaximumRadiusMm = 5000.0;
        private const string LogPath = @"D:\Revit\Python\Revit_BIM_Agent\logs\history\Rule04_CopyAlarmParentData.log";

        private sealed class WorksetCheckoutDialogHandler
        {
            private const string TriggerMessage = "trying to check out a large number of elements";
            private readonly List<string> log;

            internal int HandledCount { get; private set; }

            internal WorksetCheckoutDialogHandler(List<string> log) => this.log = log;

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
                        log.Add("WORKSHARING-DIAGNOSTIK: ikke-gjenkjent checkout-dialog: " + message.Substring(0, Math.Min(500, message.Length)).Replace("\r", " ").Replace("\n", " "));
                    }
                    return;
                }

                log.Add("WORKSHARING-DIAGNOSTIK: gjenkjent dialog: " + message.Substring(0, Math.Min(500, message.Length)).Replace("\r", " ").Replace("\n", " "));
                try
                {
                    if (eventArgs.OverrideResult((int)TaskDialogResult.CommandLink1))
                    {
                        HandledCount++;
                        log.Add("WORKSHARING: Revit godtok Check Out Worksets.");
                    }
                    else log.Add("WORKSHARING-BLOKKERING: Revit godtok ikke Check Out Worksets.");
                }
                catch (Exception exception)
                {
                    log.Add("WORKSHARING-BLOKKERING: kunne ikke velge Check Out Worksets: " + exception.Message);
                }
            }
        }

        private sealed class SourceData
        {
            internal long ElementId { get; }
            internal string FamilyName { get; }
            internal string Mark { get; }
            internal string Name { get; }
            internal XYZ HostPoint { get; }

            internal SourceData(long elementId, string familyName, string mark, string name, XYZ hostPoint)
            {
                ElementId = elementId;
                FamilyName = familyName;
                Mark = mark;
                Name = name;
                HostPoint = hostPoint;
            }
        }

        private sealed class PendingWrite
        {
            internal Element Element { get; }
            internal Parameter Parameter { get; }
            internal string Value { get; }
            internal string ParameterName { get; }

            internal PendingWrite(Element element, Parameter parameter, string value, string parameterName)
            {
                Element = element;
                Parameter = parameter;
                Value = value;
                ParameterName = parameterName;
            }
        }

        public string Execute(UIApplication uiApplication, Document? activeDocument)
        {
            if (activeDocument is null)
            {
                return "FEIL: Ingen aktiv Revit-modell. Regel 4 stoppet før elementlesing.";
            }

            var log = new List<string>
            {
                string.Format(CultureInfo.InvariantCulture, "=== Regel 4 v{0} | {1:O} | {2} ===", ScriptVersion, DateTime.Now, activeDocument.Title)
            };
            var sources = new List<SourceData>();
            int sourceIssueCount = 0;
            AddSources(activeDocument, Transform.Identity, sources, ref sourceIssueCount);
            foreach (RevitLinkInstance linkInstance in new FilteredElementCollector(activeDocument)
                .OfClass(typeof(RevitLinkInstance))
                .Cast<RevitLinkInstance>())
            {
                Document? linkedDocument = linkInstance.GetLinkDocument();
                if (linkedDocument is null)
                {
                    continue;
                }

                AddSources(linkedDocument, linkInstance.GetTotalTransform(), sources, ref sourceIssueCount);
            }

            if (sources.Count == 0)
            {
                log.Add(string.Format(CultureInfo.InvariantCulture, "STOPPET: ingen brukbare kule-kilder i aktiv modell eller lastede lenker; kilder med mangler/feil {0}.", sourceIssueCount));
                return SaveAndReturn(log, "Regel 4 v" + ScriptVersion + ": ingen brukbare kule-kilder i aktiv modell eller lastede lenker.");
            }

            var targets = new FilteredElementCollector(activeDocument)
                .OfClass(typeof(FamilyInstance))
                .WhereElementIsNotElementType()
                .Cast<FamilyInstance>()
                .Where(target => string.Equals(GetFamilyName(target), ChildFamilyName, StringComparison.Ordinal))
                .ToList();
            var writes = new List<PendingWrite>();
            int matchedCount = 0;
            int fallbackCount = 0;
            int noMatchCount = 0;
            int unresolvedCount = 0;

            foreach (FamilyInstance target in targets)
            {
                if (!TryGetSingleParameter(target, MatchParameterName, out Parameter? matchParameter, out string matchIssue))
                {
                    unresolvedCount++;
                    log.Add(FormatIssue(target.Id.Value, MatchParameterName, matchIssue));
                }

                string matchValue = matchParameter is not null && matchParameter.StorageType == StorageType.String
                    ? matchParameter.AsString() ?? string.Empty
                    : string.Empty;
                bool useDistanceFallback = IsMissing(matchValue);
                XYZ? targetPoint = GetPlacementPoint(target);
                if (targetPoint is null)
                {
                    unresolvedCount++;
                    noMatchCount++;
                    log.Add(FormatIssue(target.Id.Value, MatchParameterName, "child mangler brukbar 3D-plassering"));
                    continue;
                }

                SourceData? matchedSource = null;
                double matchedDistance = 0.0;
                for (double radiusMm = InitialRadiusMm; radiusMm <= MaximumRadiusMm; radiusMm += RadiusStepMm)
                {
                    double radiusFeet = radiusMm / 304.8;
                    var candidates = sources
                        .Where(source => useDistanceFallback || string.Equals(source.Mark, matchValue, StringComparison.Ordinal))
                        .Select(source => new { Source = source, Distance = targetPoint.DistanceTo(source.HostPoint) })
                        .Where(item => item.Distance <= radiusFeet)
                        .OrderBy(item => item.Distance)
                        .ThenBy(item => item.Source.ElementId)
                        .ToList();
                    if (candidates.Count == 0)
                    {
                        continue;
                    }

                    matchedSource = candidates[0].Source;
                    matchedDistance = candidates[0].Distance;
                    break;
                }

                if (matchedSource is null)
                {
                    noMatchCount++;
                    log.Add(string.Format(
                        CultureInfo.InvariantCulture,
                        "INGEN MATCH ElementId {0}: FOB_ID='{1}', avstandsfallback={2}, maks {3} mm.",
                        target.Id.Value,
                        matchValue,
                        useDistanceFallback,
                        MaximumRadiusMm));
                    continue;
                }

                matchedCount++;
                if (useDistanceFallback)
                {
                    fallbackCount++;
                }
                log.Add(string.Format(
                    CultureInfo.InvariantCulture,
                    "MATCH child {0} til parent {1}: FOB_ID='{2}', fallback={3}, avstand={4:F1} mm.",
                    target.Id.Value,
                    matchedSource.ElementId,
                    matchValue,
                    useDistanceFallback,
                    matchedDistance * 304.8));

                AddMissingTargetWrite(target, ChildMarkParameterName, matchedSource.Mark, writes, log, ref unresolvedCount);
                if (IsMissing(matchedSource.Name))
                {
                    unresolvedCount++;
                    log.Add(FormatIssue(target.Id.Value, ParentNameParameterName, "parent har ingen brukbar tekstverdi"));
                }
                else
                {
                    AddMissingTargetWrite(target, ChildNameParameterName, matchedSource.Name, writes, log, ref unresolvedCount);
                }
            }

            if (writes.Count > 0)
            {
                var dialogHandler = new WorksetCheckoutDialogHandler(log);
                uiApplication.DialogBoxShowing += dialogHandler.HandleDialogBoxShowing;
                try
                {
                    using (var transaction = new Transaction(activeDocument, "Kopier brannalarm FOB-verdier fra parent"))
                    {
                        TransactionStatus startStatus = transaction.Start();
                        if (startStatus != TransactionStatus.Started)
                        {
                            log.Add("TRANSAKSJONSFEIL: transaksjonen startet ikke (" + startStatus + ").");
                            return SaveAndReturn(log, "Regel 4 v" + ScriptVersion + ": transaksjonen startet ikke; ingen endringer utført.");
                        }

                        try
                        {
                            foreach (PendingWrite write in writes)
                            {
                                if (!write.Parameter.Set(write.Value))
                                {
                                    throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture, "Parameter.Set feilet for ElementId {0}, parameter {1}.", write.Element.Id.Value, write.ParameterName));
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
                            return SaveAndReturn(log, "Regel 4 v" + ScriptVersion + ": transaksjonen ble rullet tilbake; 0 verdier lagret. " + exception.Message);
                        }
                    }
                }
                finally { uiApplication.DialogBoxShowing -= dialogHandler.HandleDialogBoxShowing; }

                foreach (PendingWrite write in writes)
                {
                    log.Add(string.Format(CultureInfo.InvariantCulture, "OPPDATERT ElementId {0}: {1} ble fylt fra matched parent.", write.Element.Id.Value, write.ParameterName));
                }
            }

            string summary = string.Format(
                CultureInfo.InvariantCulture,
                "Regel 4 v{0}: child-elementer {1}; kilder {2}; matchet {3}; fallback {4}; uten match {5}; verdier skrevet {6}; uavklart {7}; kilder med mangler/feil {8}.",
                ScriptVersion,
                targets.Count,
                sources.Count,
                matchedCount,
                fallbackCount,
                noMatchCount,
                writes.Count,
                unresolvedCount,
                sourceIssueCount);
            log.Insert(1, summary);
            return SaveAndReturn(log, summary);
        }

        private static void AddSources(Document sourceDocument, Transform transform, List<SourceData> sources, ref int sourceIssueCount)
        {
            foreach (FamilyInstance source in new FilteredElementCollector(sourceDocument)
                .OfClass(typeof(FamilyInstance))
                .WhereElementIsNotElementType()
                .Cast<FamilyInstance>())
            {
                string sourceFamily = GetFamilyName(source);
                if (!ContainsToken(sourceFamily, ParentFamilyToken))
                {
                    continue;
                }

                XYZ? point = GetPlacementPoint(source);
                if (point is null)
                {
                    sourceIssueCount++;
                    continue;
                }

                if (!TryGetSingleParameter(source, ParentMarkParameterName, out Parameter? markParameter, out _)
                    || markParameter is null
                    || markParameter.StorageType != StorageType.String)
                {
                    sourceIssueCount++;
                    continue;
                }

                string mark = markParameter.AsString() ?? string.Empty;
                if (IsMissing(mark))
                {
                    continue;
                }

                string parentName = string.Empty;
                if (TryGetSingleParameter(source, ParentNameParameterName, out Parameter? nameParameter, out _)
                    && nameParameter is not null
                    && nameParameter.StorageType == StorageType.String)
                {
                    parentName = nameParameter.AsString() ?? string.Empty;
                }

                sources.Add(new SourceData(source.Id.Value, sourceFamily, mark, parentName, transform.OfPoint(point)));
            }
        }

        private static void AddMissingTargetWrite(
            FamilyInstance target,
            string parameterName,
            string value,
            List<PendingWrite> writes,
            List<string> log,
            ref int unresolvedCount)
        {
            if (!TryGetSingleParameter(target, parameterName, out Parameter? parameter, out string issue)
                || parameter is null)
            {
                unresolvedCount++;
                log.Add(FormatIssue(target.Id.Value, parameterName, issue));
                return;
            }
            if (parameter.StorageType != StorageType.String)
            {
                unresolvedCount++;
                log.Add(FormatIssue(target.Id.Value, parameterName, "målparameteren har ikke lagringstypen String"));
                return;
            }

            string existingValue = parameter.AsString() ?? string.Empty;
            if (!IsMissing(existingValue))
            {
                log.Add(string.Format(CultureInfo.InvariantCulture, "BEVART ElementId {0}: {1} hadde allerede verdi.", target.Id.Value, parameterName));
                return;
            }
            if (parameter.IsReadOnly)
            {
                unresolvedCount++;
                log.Add(FormatIssue(target.Id.Value, parameterName, "målparameteren er skrivebeskyttet"));
                return;
            }

            writes.Add(new PendingWrite(target, parameter, value, parameterName));
        }

        private static bool TryGetSingleParameter(Element element, string name, out Parameter? parameter, out string issue)
        {
            IList<Parameter> matches = element.GetParameters(name);
            if (matches.Count == 0)
            {
                parameter = null;
                issue = "instansparameteren mangler";
                return false;
            }
            if (matches.Count != 1)
            {
                parameter = null;
                issue = "flere instansparametere med samme navn";
                return false;
            }

            parameter = matches[0];
            issue = string.Empty;
            return true;
        }

        private static XYZ? GetPlacementPoint(FamilyInstance instance)
        {
            if (instance.Location is LocationPoint pointLocation)
            {
                return pointLocation.Point;
            }
            if (instance.Location is LocationCurve curveLocation && curveLocation.Curve is not null)
            {
                try
                {
                    return curveLocation.Curve.Evaluate(0.5, true);
                }
                catch
                {
                    return curveLocation.Curve.GetEndPoint(0);
                }
            }

            BoundingBoxXYZ? bounds = instance.get_BoundingBox(null);
            return bounds is null ? null : (bounds.Min + bounds.Max) * 0.5;
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

        private static bool ContainsToken(string? value, string token)
        {
            return value?.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsMissing(string? value)
        {
            return string.IsNullOrWhiteSpace(value) || string.Equals(value.Trim(), "--", StringComparison.Ordinal);
        }

        private static string FormatIssue(long elementId, string parameterName, string reason)
        {
            return string.Format(CultureInfo.InvariantCulture, "UAVKLART ElementId {0}: {1}; årsak: {2}.", elementId, parameterName, reason);
        }

        private static string SaveAndReturn(List<string> log, string result)
        {
            string reportResult;
            try
            {
                string reportDirectory = Path.Combine(Path.GetDirectoryName(LogPath)!, "..", "reports");
                Directory.CreateDirectory(reportDirectory);
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
                string reportPath = Path.Combine(reportDirectory, "Rule04_CopyAlarmParentData " + timestamp + ".txt");
                File.WriteAllLines(reportPath, log, new UTF8Encoding(false));
                reportResult = "Rapport: " + reportPath;
            }
            catch (Exception exception) { reportResult = "Rapport kunne ikke skrives: " + exception.Message; }
            try
            {
                File.AppendAllText(LogPath, string.Join(Environment.NewLine, log) + Environment.NewLine, new UTF8Encoding(false));
                return result + " " + reportResult + " Logg: " + LogPath;
            }
            catch (Exception exception)
            {
                return result + " " + reportResult + " Logg kunne ikke skrives: " + exception.Message;
            }
        }
    }
}