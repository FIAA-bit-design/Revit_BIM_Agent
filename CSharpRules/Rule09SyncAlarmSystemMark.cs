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
        private const string SequenceSourceParameterName = "PGF_RIE_Sekvensnummer";
        private const string SequenceTargetParameterName = "FOB_Sekvensnummer";
        private const string LogPath = @"D:\Revit\Python\Revit_BIM_Agent\logs\history\Rule09_SyncAlarmSystemMark.log";

        private sealed class ParameterPair
        {
            internal string SourceName { get; }
            internal string TargetName { get; }

            internal ParameterPair(string sourceName, string targetName)
            {
                SourceName = sourceName;
                TargetName = targetName;
            }
        }

        private static readonly ParameterPair[] ParameterPairs =
        {
            new ParameterPair("FOB_FysiskMerke", "PGF_RIE_Alarmsystemer"),
            new ParameterPair(SequenceSourceParameterName, SequenceTargetParameterName)
        };

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
                    else log("WORKSHARING-BLOKKERING: Revit godtok ikke Check Out Worksets.");
                }
                catch (Exception exception)
                {
                    log("WORKSHARING-BLOKKERING: kunne ikke velge Check Out Worksets: " + exception.Message);
                }
            }
        }

        private sealed class PendingWrite
        {
            internal Element Element { get; }
            internal Parameter TargetParameter { get; }
            internal string SourceValue { get; }
            internal string SourceName { get; }
            internal string TargetName { get; }

            internal PendingWrite(Element element, Parameter targetParameter, string sourceValue, string sourceName, string targetName)
            {
                Element = element;
                TargetParameter = targetParameter;
                SourceValue = sourceValue;
                SourceName = sourceName;
                TargetName = targetName;
            }
        }

        public string Execute(UIApplication uiApplication, Document? activeDocument)
        {
            if (activeDocument is null)
            {
                return "FEIL: Ingen aktiv Revit-modell. Regel 9 stoppet før elementlesing.";
            }

            var elements = new FilteredElementCollector(activeDocument)
                .OfCategory(BuiltInCategory.OST_FireAlarmDevices)
                .WhereElementIsNotElementType()
                .ToElements();
            var log = new List<string>
            {
                string.Format(CultureInfo.InvariantCulture, "=== Regel 9 v{0} | {1:O} | {2} ===", ScriptVersion, DateTime.Now, activeDocument.Title)
            };
            var writes = new List<PendingWrite>();
            int missingSourceCount = 0;
            int missingTargetCount = 0;
            int ambiguousParameterCount = 0;
            int unsupportedStorageCount = 0;
            int mismatchCount = 0;
            int readOnlyCount = 0;
            int emptySequenceSourceCount = 0;

            foreach (Element element in elements)
            {
                foreach (ParameterPair pair in ParameterPairs)
                {
                    bool hasSource = TryGetSingleParameter(element, pair.SourceName, out Parameter? source, out string sourceIssue);
                    bool hasTarget = TryGetSingleParameter(element, pair.TargetName, out Parameter? target, out string targetIssue);

                    if (!hasSource)
                    {
                        if (sourceIssue == "parameter mangler")
                        {
                            missingSourceCount++;
                        }
                        else
                        {
                            ambiguousParameterCount++;
                        }
                        log.Add(FormatIssue(element.Id.Value, pair.SourceName, sourceIssue));
                    }
                    if (!hasTarget)
                    {
                        if (targetIssue == "parameter mangler")
                        {
                            missingTargetCount++;
                        }
                        else
                        {
                            ambiguousParameterCount++;
                        }
                        log.Add(FormatIssue(element.Id.Value, pair.TargetName, targetIssue));
                    }
                    if (hasSource
                        && source is not null
                        && pair.SourceName == SequenceSourceParameterName
                        && source.StorageType == StorageType.String
                        && IsEmptySequence(source.AsString()))
                    {
                        emptySequenceSourceCount++;
                        log.Add("FEIL ElementId " + element.Id.Value.ToString(CultureInfo.InvariantCulture) + ": " + SequenceSourceParameterName + " er tom eller '--'; " + SequenceTargetParameterName + " ble ikke endret.");
                        continue;
                    }
                    if (!hasSource || !hasTarget || source is null || target is null)
                    {
                        continue;
                    }

                    if (source.StorageType != StorageType.String || target.StorageType != StorageType.String)
                    {
                        unsupportedStorageCount++;
                        log.Add(FormatIssue(element.Id.Value, pair.TargetName, "begge parametere må ha lagringstypen String"));
                        continue;
                    }

                    string sourceValue = source.AsString() ?? string.Empty;
                    string targetValue = target.AsString() ?? string.Empty;
                    if (string.Equals(sourceValue, targetValue, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    mismatchCount++;
                    if (target.IsReadOnly)
                    {
                        readOnlyCount++;
                        log.Add(FormatIssue(element.Id.Value, pair.TargetName, "verdien avviker, men målparameteren er skrivebeskyttet"));
                        continue;
                    }

                    writes.Add(new PendingWrite(element, target, sourceValue, pair.SourceName, pair.TargetName));
                }
            }

            if (writes.Count > 0)
            {
                var worksetDialogHandler = new WorksetCheckoutDialogHandler(log.Add);
                uiApplication.DialogBoxShowing += worksetDialogHandler.HandleDialogBoxShowing;
                try
                {
                    using (var transaction = new Transaction(activeDocument, "Synkroniser brannalarmparametere"))
                    {
                        TransactionStatus startStatus = transaction.Start();
                        if (startStatus != TransactionStatus.Started)
                        {
                            log.Add("TRANSAKSJONSFEIL: transaksjonen startet ikke (" + startStatus + ").");
                            return SaveAndReturn(log, "Regel 9 v" + ScriptVersion + ": transaksjonen startet ikke; ingen endringer utført.");
                        }

                        try
                        {
                            foreach (PendingWrite write in writes)
                            {
                                if (!write.TargetParameter.Set(write.SourceValue))
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
                            return SaveAndReturn(log, "Regel 9 v" + ScriptVersion + ": transaksjonen ble rullet tilbake; 0 verdier lagret. " + exception.Message);
                        }
                    }
                }
                finally { uiApplication.DialogBoxShowing -= worksetDialogHandler.HandleDialogBoxShowing; }

                foreach (PendingWrite write in writes)
                {
                    log.Add("OPPDATERT ElementId " + write.Element.Id.Value.ToString(CultureInfo.InvariantCulture) + ": " + write.TargetName + " synkronisert fra " + write.SourceName + ".");
                }
            }

            string summary = string.Format(
                CultureInfo.InvariantCulture,
                "Regel 9 v{0}: Fire Alarm Devices kontrollert {1}; parameteravvik {2}; oppdateringer planlagt {3}; kildeparametere mangler {4}; målparametere mangler {5}; tvetydige parametere {6}; feil lagringstype {7}; skrivebeskyttede avvik {8}; tomme sekvenskilder {9}.",
                ScriptVersion,
                elements.Count,
                mismatchCount,
                writes.Count,
                missingSourceCount,
                missingTargetCount,
                ambiguousParameterCount,
                unsupportedStorageCount,
                readOnlyCount,
                emptySequenceSourceCount);
            log.Insert(1, summary);
            return SaveAndReturn(log, summary);
        }

        private static bool TryGetSingleParameter(Element element, string name, out Parameter? parameter, out string issue)
        {
            IList<Parameter> matches = element.GetParameters(name);
            if (matches.Count == 0)
            {
                parameter = null;
                issue = "parameter mangler";
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

        private static bool IsEmptySequence(string? value)
        {
            return string.IsNullOrWhiteSpace(value)
                || string.Equals(value.Trim(), "--", StringComparison.Ordinal);
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