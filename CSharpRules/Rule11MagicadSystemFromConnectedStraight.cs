#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace CW.Assistant.Generated
{
    internal sealed class GeneratedAction
    {
        private const string ScriptVersion = "0.0.1";
        private const string LogPath = @"D:\Revit\Python\Revit_BIM_Agent\logs\history\Rule11_MagicadSystemFromConnectedStraight.log";
        private static readonly string[] SystemParameters = { "MC System Code", "MC System Name" };

        private sealed class PendingWrite
        {
            internal Element Owner { get; }
            internal Parameter Parameter { get; }
            internal string Value { get; }
            internal string SourceInfo { get; }

            internal PendingWrite(Element owner, Parameter parameter, string value, string sourceInfo)
            {
                Owner = owner;
                Parameter = parameter;
                Value = value;
                SourceInfo = sourceInfo;
            }
        }

        public string Execute(UIApplication uiApplication, Document? activeDocument)
        {
            if (activeDocument is null) return "FEIL: Ingen aktiv Revit-modell. Regel 11 stoppet.";

            var log = new List<string>
            {
                string.Format(CultureInfo.InvariantCulture, "=== Regel 11 Magicad v{0} | {1:O} | {2} ===", ScriptVersion, DateTime.Now, activeDocument.Title)
            };
            var writes = new List<PendingWrite>();
            int checkedCount = 0;
            int preservedCount = 0;
            int unresolvedCount = 0;

            List<Element> fittings = new FilteredElementCollector(activeDocument)
                .WhereElementIsNotElementType()
                .ToElements()
                .Where(element => IsForingsveiFitting(element))
                .OrderBy(element => element.Id.Value)
                .ToList();

            foreach (Element fitting in fittings)
            {
                if (!TryGetSingleStringParameter(fitting, "FOB_Entreprise", out Parameter? enterpriseParameter, out string enterpriseIssue)
                    || enterpriseParameter is null)
                {
                    unresolvedCount++;
                    log.Add(FormatIssue(fitting, "FOB_Entreprise", enterpriseIssue));
                    continue;
                }
                if (!string.Equals(enterpriseParameter.AsString(), "K5B", StringComparison.Ordinal)) continue;

                var missingParameters = new List<(string Name, Parameter Parameter)>();
                foreach (string name in SystemParameters)
                {
                    if (!TryGetSingleStringParameter(fitting, name, out Parameter? parameter, out string issue)
                        || parameter is null)
                    {
                        unresolvedCount++;
                        log.Add(FormatIssue(fitting, name, issue));
                        continue;
                    }
                    if (string.IsNullOrWhiteSpace(parameter.AsString())) missingParameters.Add((name, parameter));
                    else preservedCount++;
                }
                if (missingParameters.Count == 0) continue;
                checkedCount++;

                if (!TryGetConnectedStraightSources(fitting, out List<Element> sources, out string sourceIssue))
                {
                    unresolvedCount += missingParameters.Count;
                    foreach ((string name, _) in missingParameters) log.Add(FormatIssue(fitting, name, sourceIssue));
                    continue;
                }

                foreach ((string name, Parameter parameter) in missingParameters)
                {
                    if (!TryResolveSourceValue(sources, name, out string value, out string sourceInfo))
                    {
                        unresolvedCount++;
                        log.Add(FormatIssue(fitting, name, sourceInfo));
                        continue;
                    }
                    if (parameter.IsReadOnly)
                    {
                        unresolvedCount++;
                        log.Add(FormatIssue(fitting, name, "målparameteren er skrivebeskyttet"));
                        continue;
                    }
                    writes.Add(new PendingWrite(fitting, parameter, value, sourceInfo));
                }
            }

            int updated = 0;
            if (writes.Count > 0)
            {
                using var transaction = new Transaction(activeDocument, "Regel 11 - Magicad systemverdier fra tilkoblet rettstrekk");
                TransactionStatus startStatus = transaction.Start();
                if (startStatus != TransactionStatus.Started)
                {
                    log.Add("TRANSAKSJONSFEIL: transaksjonen startet ikke (" + startStatus + ").");
                    return SaveAndReturn(log, "Regel 11 stoppet uten endringer: transaksjonen startet ikke.");
                }

                try
                {
                    foreach (PendingWrite write in writes)
                    {
                        if (!write.Parameter.Set(write.Value))
                        {
                            throw new InvalidOperationException("Parameter.Set returnerte false for ElementId " + write.Owner.Id.Value + ", " + write.Parameter.Definition.Name + ".");
                        }
                        updated++;
                        log.Add(string.Format(CultureInfo.InvariantCulture,
                            "OPPDATERT ElementId {0}, {1} = '{2}' fra {3}.",
                            write.Owner.Id.Value, write.Parameter.Definition.Name, EscapeLog(write.Value), write.SourceInfo));
                    }

                    TransactionStatus commitStatus = transaction.Commit();
                    if (commitStatus != TransactionStatus.Committed)
                    {
                        throw new InvalidOperationException("Transaksjonen ble ikke committed: " + commitStatus);
                    }
                }
                catch (Exception exception)
                {
                    if (transaction.GetStatus() == TransactionStatus.Started) transaction.RollBack();
                    log.Add("TRANSAKSJONSFEIL: " + exception);
                    return SaveAndReturn(log, "Regel 11 ble ikke bekreftet committed: " + exception.Message);
                }
            }

            log.Add(string.Format(CultureInfo.InvariantCulture,
                "Oppsummering: føringsveifittings {0}; kontrollert med manglende Magicad-verdi {1}; oppdatert {2}; eksisterende verdier bevart {3}; uavklart {4}.",
                fittings.Count, checkedCount, updated, preservedCount, unresolvedCount));
            return SaveAndReturn(log, string.Format(CultureInfo.InvariantCulture,
                "Regel 11 v{0}: oppdatert {1}; uavklart {2}; logg: {3}.", ScriptVersion, updated, unresolvedCount, LogPath));
        }

        private static bool TryGetConnectedStraightSources(Element fitting, out List<Element> sources, out string issue)
        {
            sources = new List<Element>();
            issue = "fitting mangler tilgjengelige MEP-koblinger";
            if (fitting is not FamilyInstance familyInstance || familyInstance.MEPModel?.ConnectorManager is null) return false;

            var uniqueSources = new Dictionary<long, Element>();
            foreach (Connector connector in familyInstance.MEPModel.ConnectorManager.Connectors)
            {
                if (connector.ConnectorType != ConnectorType.End) continue;
                foreach (Connector connected in connector.AllRefs)
                {
                    Element owner = connected.Owner;
                    if (owner.Id.Value == fitting.Id.Value || connected.ConnectorType != ConnectorType.End
                        || !connector.IsConnectedTo(connected) || !IsMatchingStraightCategory(fitting, owner)
                        || owner.Location is not LocationCurve locationCurve || locationCurve.Curve is not Line)
                    {
                        continue;
                    }
                    if (!TryGetSingleStringParameter(owner, "FOB_Entreprise", out Parameter? enterpriseParameter, out _)
                        || enterpriseParameter is null
                        || !string.Equals(enterpriseParameter.AsString(), "K5B", StringComparison.Ordinal))
                    {
                        continue;
                    }
                    uniqueSources[owner.Id.Value] = owner;
                }
            }

            sources = uniqueSources.Values.OrderBy(element => element.Id.Value).ToList();
            if (sources.Count > 0) return true;
            issue = "fant ingen direkte tilkoblet rett føringsvei i K5B med samme føringsveikategori";
            return false;
        }

        private static bool TryResolveSourceValue(List<Element> sources, string parameterName, out string value, out string sourceInfo)
        {
            value = string.Empty;
            var values = new Dictionary<string, List<long>>(StringComparer.Ordinal);
            foreach (Element source in sources)
            {
                if (!TryGetSingleStringParameter(source, parameterName, out Parameter? parameter, out string issue)
                    || parameter is null)
                {
                    sourceInfo = "rettstrekk ElementId " + source.Id.Value + " har ikke en entydig brukbar '" + parameterName + "': " + issue;
                    return false;
                }
                string sourceValue = parameter.AsString()?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(sourceValue))
                {
                    sourceInfo = "rettstrekk ElementId " + source.Id.Value + " har tom '" + parameterName + "'";
                    return false;
                }
                if (!values.TryGetValue(sourceValue, out List<long>? sourceIds))
                {
                    sourceIds = new List<long>();
                    values.Add(sourceValue, sourceIds);
                }
                sourceIds.Add(source.Id.Value);
            }

            if (values.Count != 1)
            {
                sourceInfo = "tilkoblede rettstrekk har ulike '" + parameterName + "'-verdier: "
                    + string.Join("; ", values.Select(pair => pair.Key + " fra ElementId " + string.Join(",", pair.Value)));
                return false;
            }

            KeyValuePair<string, List<long>> match = values.Single();
            value = match.Key;
            sourceInfo = "direkte tilkoblede rettstrekk ElementId " + string.Join(",", match.Value);
            return true;
        }

        private static bool TryGetSingleStringParameter(Element element, string name, out Parameter? parameter, out string issue)
        {
            IList<Parameter> parameters = element.GetParameters(name);
            if (parameters.Count == 0)
            {
                parameter = null;
                issue = "parameteren mangler";
                return false;
            }
            if (parameters.Count > 1)
            {
                parameter = null;
                issue = "flere parametere med samme navn";
                return false;
            }

            parameter = parameters[0];
            if (parameter.StorageType != StorageType.String)
            {
                issue = "parameteren har ikke lagringstype String";
                return false;
            }
            issue = string.Empty;
            return true;
        }

        private static bool IsForingsveiFitting(Element element)
        {
            return IsCategory(element, BuiltInCategory.OST_CableTrayFitting)
                || IsCategory(element, BuiltInCategory.OST_ConduitFitting);
        }

        private static bool IsMatchingStraightCategory(Element fitting, Element straight)
        {
            return (IsCategory(fitting, BuiltInCategory.OST_CableTrayFitting) && IsCategory(straight, BuiltInCategory.OST_CableTray))
                || (IsCategory(fitting, BuiltInCategory.OST_ConduitFitting) && IsCategory(straight, BuiltInCategory.OST_Conduit));
        }

        private static bool IsCategory(Element element, BuiltInCategory category)
        {
            return element.Category is not null && element.Category.Id.Value == (long)category;
        }

        private static string FormatIssue(Element element, string parameterName, string reason)
        {
            return string.Format(CultureInfo.InvariantCulture, "UAVKLART ElementId {0}, {1}: {2}.", element.Id.Value, parameterName, reason);
        }

        private static string EscapeLog(string value)
        {
            return value.Replace("\r", " ").Replace("\n", " ").Replace("'", "''");
        }

        private static string SaveAndReturn(List<string> lines, string result)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                File.AppendAllLines(LogPath, lines, new UTF8Encoding(false));
                return result;
            }
            catch (Exception exception)
            {
                return result + " Logg kunne ikke skrives: " + exception.Message;
            }
        }
    }
}