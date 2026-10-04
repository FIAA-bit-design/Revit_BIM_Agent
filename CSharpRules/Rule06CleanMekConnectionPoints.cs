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
        private const string ScriptVersion = "0.0.2";
        private const string LogPath = @"D:\Revit\Python\Revit_BIM_Agent\logs\history\Rule06_CleanMekConnectionPoints.log";
        private const string Marker = "--";
        private static readonly HashSet<string> TargetFamilyNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "koblingspunkt el",
            "koblingspunkt automatikk",
            "servicebryter",
            "frekvensomformer"
        };

        private sealed class PendingWrite
        {
            internal Element Element { get; }
            internal Parameter Parameter { get; }
            internal string ParameterName { get; }
            internal string Value { get; }

            internal PendingWrite(Element element, Parameter parameter, string parameterName, string value)
            {
                Element = element;
                Parameter = parameter;
                ParameterName = parameterName;
                Value = value;
            }
        }

        public string Execute(UIApplication uiApplication, Document? activeDocument)
        {
            if (activeDocument is null)
            {
                return "FEIL: Ingen aktiv Revit-modell. Regel 6 stoppet før elementlesing.";
            }

            var log = new List<string>
            {
                string.Format(CultureInfo.InvariantCulture, "=== Regel 6 v{0} | {1:O} | {2} ===", ScriptVersion, DateTime.Now, activeDocument.Title)
            };
            var targets = new Dictionary<long, FamilyInstance>();
            BuiltInCategory[] categories = { BuiltInCategory.OST_ElectricalFixtures, BuiltInCategory.OST_CommunicationDevices };
            foreach (BuiltInCategory category in categories)
            {
                foreach (FamilyInstance instance in new FilteredElementCollector(activeDocument)
                    .OfCategory(category)
                    .WhereElementIsNotElementType()
                    .OfType<FamilyInstance>())
                {
                    if (TargetFamilyNames.Contains(GetFamilyName(instance)))
                    {
                        targets[instance.Id.Value] = instance;
                    }
                }
            }

            var writes = new List<PendingWrite>();
            int withCircuitNumber = 0;
            int withoutCircuitNumber = 0;
            int updatedConnected = 0;
            int updatedBelongsTo = 0;
            int updatedBuildingSystems = 0;
            int updatedPhysicalMark = 0;
            int skippedPhysicalMark = 0;
            int missingParameters = 0;
            int invalidParameters = 0;
            int readOnlyParameters = 0;
            int setFailures = 0;

            foreach (FamilyInstance instance in targets.Values.OrderBy(item => item.Id.Value))
            {
                string familyName = GetFamilyName(instance);
                string circuitNumber = GetParameterText(instance, "Circuit Number", log, ref missingParameters, ref invalidParameters);
                string mcCircuitNumber = GetParameterText(instance, "MC Circuit Number", log, ref missingParameters, ref invalidParameters);
                string panelName = GetParameterText(instance, "Panel", log, ref missingParameters, ref invalidParameters);
                bool hasCircuitNumber = !string.IsNullOrWhiteSpace(circuitNumber);
                string connectedValue = hasCircuitNumber ? panelName + "-" + mcCircuitNumber : Marker;
                string belongsToValue = hasCircuitNumber ? panelName : Marker;

                if (hasCircuitNumber) withCircuitNumber++;
                else withoutCircuitNumber++;

                AddWrite(instance, "PGF_RIE_Tilkoblet", connectedValue, writes, log,
                    ref missingParameters, ref invalidParameters, ref readOnlyParameters, ref setFailures);
                AddWrite(instance, "FOB_TilhorerObjekt", belongsToValue, writes, log,
                    ref missingParameters, ref invalidParameters, ref readOnlyParameters, ref setFailures);
                AddMarkerIfDifferent(instance, "PGF_RIE_Bygg_VVSsystemer", writes, log,
                    ref missingParameters, ref invalidParameters, ref readOnlyParameters, ref setFailures);

                if (IsPhysicalMarkExempt(familyName))
                {
                    skippedPhysicalMark++;
                }
                else
                {
                    AddMarkerIfDifferent(instance, "FOB_FysiskMerke", writes, log,
                        ref missingParameters, ref invalidParameters, ref readOnlyParameters, ref setFailures);
                }
            }

            if (writes.Count > 0)
            {
                using (var transaction = new Transaction(activeDocument, "Vask koblingspunkter MEK"))
                {
                    TransactionStatus startStatus = transaction.Start();
                    if (startStatus != TransactionStatus.Started)
                    {
                        log.Add("TRANSAKSJONSFEIL: transaksjonen startet ikke (" + startStatus + ").");
                        return SaveAndReturn(log, "Regel 6 v" + ScriptVersion + ": transaksjonen startet ikke; ingen endringer utført.");
                    }

                    foreach (PendingWrite write in writes)
                    {
                        try
                        {
                            if (write.Parameter.Set(write.Value))
                            {
                                switch (write.ParameterName)
                                {
                                    case "PGF_RIE_Tilkoblet": updatedConnected++; break;
                                    case "FOB_TilhorerObjekt": updatedBelongsTo++; break;
                                    case "PGF_RIE_Bygg_VVSsystemer": updatedBuildingSystems++; break;
                                    case "FOB_FysiskMerke": updatedPhysicalMark++; break;
                                }
                                log.Add(string.Format(CultureInfo.InvariantCulture, "OPPDATERT ElementId {0}: {1}='{2}'.", write.Element.Id.Value, write.ParameterName, write.Value));
                            }
                            else
                            {
                                setFailures++;
                                log.Add(string.Format(CultureInfo.InvariantCulture, "SKRIVEFEIL ElementId {0}: Parameter.Set returnerte false for {1}.", write.Element.Id.Value, write.ParameterName));
                            }
                        }
                        catch (Exception exception)
                        {
                            setFailures++;
                            log.Add(string.Format(CultureInfo.InvariantCulture, "SKRIVEFEIL ElementId {0}: {1}: {2}.", write.Element.Id.Value, write.ParameterName, exception.Message));
                        }
                    }

                    TransactionStatus commitStatus = transaction.Commit();
                    if (commitStatus != TransactionStatus.Committed)
                    {
                        if (transaction.GetStatus() == TransactionStatus.Started)
                        {
                            transaction.RollBack();
                        }
                        log.Add("TRANSAKSJONSFEIL: commit-status " + commitStatus + ".");
                        return SaveAndReturn(log, "Regel 6 v" + ScriptVersion + ": transaksjonen ble ikke committed (" + commitStatus + ").");
                    }
                }
            }

            string summary = string.Format(
                CultureInfo.InvariantCulture,
                "Regel 6 v{0}: behandlet {1}; med Circuit Number {2}; uten Circuit Number {3}; oppdatert PGF_RIE_Tilkoblet {4}; FOB_TilhorerObjekt {5}; PGF_RIE_Bygg_VVSsystemer {6}; FOB_FysiskMerke {7}; fysisk merke unntatt {8}; manglende parametere {9}; feil lagringstype/duplikat {10}; skrivebeskyttede {11}; skrivefeil {12}.",
                ScriptVersion,
                targets.Count,
                withCircuitNumber,
                withoutCircuitNumber,
                updatedConnected,
                updatedBelongsTo,
                updatedBuildingSystems,
                updatedPhysicalMark,
                skippedPhysicalMark,
                missingParameters,
                invalidParameters,
                readOnlyParameters,
                setFailures);
            log.Insert(1, summary);
            return SaveAndReturn(log, summary);
        }

        private static void AddMarkerIfDifferent(
            FamilyInstance element,
            string parameterName,
            List<PendingWrite> writes,
            List<string> log,
            ref int missing,
            ref int invalid,
            ref int readOnly,
            ref int setFailures)
        {
            AddWrite(element, parameterName, Marker, writes, log, ref missing, ref invalid, ref readOnly, ref setFailures);
        }

        private static void AddWrite(
            FamilyInstance element,
            string parameterName,
            string value,
            List<PendingWrite> writes,
            List<string> log,
            ref int missing,
            ref int invalid,
            ref int readOnly,
            ref int setFailures)
        {
            if (!TryGetSingleInstanceParameter(element, parameterName, out Parameter? parameter, out string issue)
                || parameter is null)
            {
                missing++;
                log.Add(FormatIssue(element.Id.Value, parameterName, issue));
                return;
            }
            if (parameter.StorageType != StorageType.String)
            {
                invalid++;
                log.Add(FormatIssue(element.Id.Value, parameterName, "parameteren har ikke lagringstypen String"));
                return;
            }

            string current = (parameter.AsString() ?? string.Empty).Trim();
            if (string.Equals(current, value, StringComparison.Ordinal))
            {
                return;
            }
            if (parameter.IsReadOnly)
            {
                readOnly++;
                log.Add(FormatIssue(element.Id.Value, parameterName, "parameteren er skrivebeskyttet"));
                return;
            }

            writes.Add(new PendingWrite(element, parameter, parameterName, value));
        }

        private static string GetParameterText(
            FamilyInstance element,
            string parameterName,
            List<string> log,
            ref int missing,
            ref int invalid)
        {
            if (!TryGetSingleInstanceParameter(element, parameterName, out Parameter? parameter, out string issue)
                || parameter is null)
            {
                missing++;
                log.Add(FormatIssue(element.Id.Value, parameterName, issue));
                return string.Empty;
            }
            if (parameter.StorageType == StorageType.String)
            {
                return (parameter.AsString() ?? string.Empty).Trim();
            }

            string value = (parameter.AsValueString() ?? string.Empty).Trim();
            if (value.Length == 0)
            {
                invalid++;
                log.Add(FormatIssue(element.Id.Value, parameterName, "parameteren kan ikke leses som tekst"));
            }
            return value;
        }

        private static bool TryGetSingleInstanceParameter(Element element, string name, out Parameter? parameter, out string issue)
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

        private static bool IsPhysicalMarkExempt(string familyName)
        {
            return string.Equals(familyName, "servicebryter", StringComparison.OrdinalIgnoreCase)
                || string.Equals(familyName, "frekvensomformer", StringComparison.OrdinalIgnoreCase);
        }

        private static string FormatIssue(long elementId, string parameterName, string reason)
        {
            return string.Format(CultureInfo.InvariantCulture, "UAVKLART ElementId {0}: {1}; årsak: {2}.", elementId, parameterName, reason);
        }

        private static string SaveAndReturn(List<string> log, string result)
        {
            try
            {
                File.AppendAllText(LogPath, string.Join(Environment.NewLine, log) + Environment.NewLine, new UTF8Encoding(false));
                return result + " Logg: " + LogPath;
            }
            catch (Exception exception)
            {
                return result + " Logg kunne ikke skrives: " + exception.Message;
            }
        }
    }
}