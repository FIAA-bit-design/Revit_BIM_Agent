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
        private const string ParameterName = "PGF_RIE_ElementId";
        private const string ScriptVersion = "0.0.4";
        private const long MaxExactlyRepresentableIntegerAsDouble = 9007199254740992L;
        private static readonly HashSet<long> CenterLineCategoryIds = new HashSet<long>(
            Enum.GetValues(typeof(BuiltInCategory))
                .Cast<BuiltInCategory>()
                .Where(category => category.ToString().EndsWith("CenterLine", StringComparison.Ordinal))
                .Select(category => new ElementId(category).Value));

        private sealed class WorksetCheckoutDialogHandler
        {
            private const string TriggerMessage = "trying to check out a large number of elements";
            private readonly Action<string> log;

            internal WorksetCheckoutDialogHandler(Action<string> log)
            {
                this.log = log;
            }

            internal int HandledCount { get; private set; }

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
                        log("WORKSHARING: valgte Check Out Worksets for Revit-dialogen om mange elementer.");
                    }
                    else
                    {
                        log("WORKSHARING-BLOKKERING: Revit godtok ikke automatisk valg av Check Out Worksets.");
                    }
                }
                catch (Exception exception)
                {
                    log("WORKSHARING-BLOKKERING: kunne ikke svare på Check Out Worksets-dialogen: " + exception.Message);
                }
            }
        }

        private sealed class PendingWrite
        {
            internal Element Owner { get; private set; }
            internal Parameter Parameter { get; private set; }
            internal StorageType StorageType { get; private set; }
            internal ElementId ElementIdValue { get; private set; }
            internal int IntegerValue { get; private set; }
            internal double DoubleValue { get; private set; }
            internal string StringValue { get; private set; }

            internal PendingWrite(Element owner, Parameter parameter, ElementId value)
            {
                Owner = owner;
                Parameter = parameter;
                StorageType = parameter.StorageType;
                ElementIdValue = value;
                IntegerValue = 0;
                DoubleValue = 0.0;
                StringValue = string.Empty;
            }

            internal PendingWrite(Element owner, Parameter parameter, int value)
            {
                Owner = owner;
                Parameter = parameter;
                StorageType = parameter.StorageType;
                ElementIdValue = ElementId.InvalidElementId;
                IntegerValue = value;
                DoubleValue = 0.0;
                StringValue = string.Empty;
            }

            internal PendingWrite(Element owner, Parameter parameter, double value)
            {
                Owner = owner;
                Parameter = parameter;
                StorageType = parameter.StorageType;
                ElementIdValue = ElementId.InvalidElementId;
                IntegerValue = 0;
                DoubleValue = value;
                StringValue = string.Empty;
            }

            internal PendingWrite(Element owner, Parameter parameter, string value)
            {
                Owner = owner;
                Parameter = parameter;
                StorageType = parameter.StorageType;
                ElementIdValue = ElementId.InvalidElementId;
                IntegerValue = 0;
                DoubleValue = 0.0;
                StringValue = value;
            }
        }

        public string Execute(UIApplication uiApplication, Document? activeDocument)
        {
            if (activeDocument is null)
            {
                return "FEIL: Ingen aktiv Revit-modell. Regel 1 stoppet før elementlesing.";
            }

            var pendingWrites = new List<PendingWrite>();
            var changedIds = new List<ElementId>();
            int instanceCount = 0;
            int missingParameterCount = 0;
            int readOnlyCount = 0;
            int unsupportedStorageCount = 0;
            int outOfRangeCount = 0;
            int failedWriteCount = 0;

            foreach (Element element in new FilteredElementCollector(activeDocument)
                .WhereElementIsNotElementType()
                .ToElements())
            {
                if (element is null || IsCenterLine(element))
                {
                    continue;
                }

                instanceCount++;
                Parameter? parameter = element.LookupParameter(ParameterName);
                if (parameter is null)
                {
                    missingParameterCount++;
                    continue;
                }
                if (parameter.IsReadOnly)
                {
                    readOnlyCount++;
                    continue;
                }

                long elementIdValue = element.Id.Value;
                switch (parameter.StorageType)
                {
                    case StorageType.ElementId:
                    {
                        ElementId? currentValue = parameter.AsElementId();
                        if (currentValue is null || currentValue.Value != elementIdValue)
                        {
                            pendingWrites.Add(new PendingWrite(element, parameter, element.Id));
                        }
                        break;
                    }
                    case StorageType.Integer:
                    {
                        if (elementIdValue < int.MinValue || elementIdValue > int.MaxValue)
                        {
                            outOfRangeCount++;
                            continue;
                        }

                        int targetValue = (int)elementIdValue;
                        if (parameter.AsInteger() != targetValue)
                        {
                            pendingWrites.Add(new PendingWrite(element, parameter, targetValue));
                        }
                        break;
                    }
                    case StorageType.Double:
                    {
                        if (elementIdValue < -MaxExactlyRepresentableIntegerAsDouble
                            || elementIdValue > MaxExactlyRepresentableIntegerAsDouble)
                        {
                            outOfRangeCount++;
                            continue;
                        }

                        double targetValue = (double)elementIdValue;
                        if (parameter.AsDouble() != targetValue)
                        {
                            pendingWrites.Add(new PendingWrite(element, parameter, targetValue));
                        }
                        break;
                    }
                    case StorageType.String:
                    {
                        string targetValue = elementIdValue.ToString(CultureInfo.InvariantCulture);
                        string? currentValue = parameter.AsString();
                        if (!string.Equals(currentValue, targetValue, StringComparison.Ordinal))
                        {
                            pendingWrites.Add(new PendingWrite(element, parameter, targetValue));
                        }
                        break;
                    }
                    default:
                        unsupportedStorageCount++;
                        break;
                }
            }

            if (pendingWrites.Count == 0)
            {
                string noUpdateResult = string.Format(
                    CultureInfo.InvariantCulture,
                    "Regel 1 v{0}: Ingen oppdateringer nødvendig i '{1}'. Instanser undersøkt: {2}; parameter mangler: {3}; skrivebeskyttet: {4}; ikke støttet lagringstype: {5}; utenfor verdiområde: {6}.",
                    ScriptVersion,
                    activeDocument.Title,
                    instanceCount,
                    missingParameterCount,
                    readOnlyCount,
                    unsupportedStorageCount,
                    outOfRangeCount);
                return SaveRunReport(activeDocument, noUpdateResult, changedIds);
            }

            TransactionStatus commitStatus;
            var worksharingLog = new List<string>();
            var worksetDialogHandler = new WorksetCheckoutDialogHandler(worksharingLog.Add);
            uiApplication.DialogBoxShowing += worksetDialogHandler.HandleDialogBoxShowing;
            try
            {
                using (var transaction = new Transaction(activeDocument, "Synkroniser PGF_RIE_ElementId med ElementId"))
                {
                    TransactionStatus startStatus = transaction.Start();
                    if (startStatus != TransactionStatus.Started)
                    {
                        string startFailureResult = string.Format(
                            CultureInfo.InvariantCulture,
                            "Regel 1 v{0}: Transaksjonen startet ikke (status {1}); ingen endringer ble forsøkt.",
                            ScriptVersion,
                            startStatus);
                        return SaveRunReport(activeDocument, startFailureResult, changedIds, worksharingLog);
                    }

                    try
                    {
                        foreach (PendingWrite write in pendingWrites)
                        {
                            try
                            {
                                bool succeeded;
                                switch (write.StorageType)
                                {
                                    case StorageType.ElementId:
                                        succeeded = write.Parameter.Set(write.ElementIdValue);
                                        break;
                                    case StorageType.Integer:
                                        succeeded = write.Parameter.Set(write.IntegerValue);
                                        break;
                                    case StorageType.Double:
                                        succeeded = write.Parameter.Set(write.DoubleValue);
                                        break;
                                    case StorageType.String:
                                        succeeded = write.Parameter.Set(write.StringValue);
                                        break;
                                    default:
                                        succeeded = false;
                                        break;
                                }

                                if (succeeded)
                                {
                                    changedIds.Add(write.Owner.Id);
                                }
                                else
                                {
                                    failedWriteCount++;
                                }
                            }
                            catch
                            {
                                failedWriteCount++;
                            }
                        }

                        commitStatus = transaction.Commit();
                        if (commitStatus != TransactionStatus.Committed)
                        {
                            string commitFailureResult = string.Format(
                                CultureInfo.InvariantCulture,
                                "Regel 1 v{0}: Transaksjonen ble ikke committed (status {1}); endrede elementer ble ikke valgt.",
                                ScriptVersion,
                                commitStatus);
                            return SaveRunReport(activeDocument, commitFailureResult, changedIds, worksharingLog);
                        }
                    }
                    catch (Exception exception)
                    {
                        if (transaction.GetStatus() == TransactionStatus.Started)
                        {
                            transaction.RollBack();
                        }
                        string transactionFailureResult = string.Format(
                            CultureInfo.InvariantCulture,
                            "Regel 1 v{0}: Transaksjonsfeil i '{1}': {2}",
                            ScriptVersion,
                            activeDocument.Title,
                            exception.Message);
                        return SaveRunReport(activeDocument, transactionFailureResult, changedIds, worksharingLog);
                    }
                }
            }
            finally
            {
                uiApplication.DialogBoxShowing -= worksetDialogHandler.HandleDialogBoxShowing;
            }

            bool selectionSucceeded = false;
            string selectionFailure = string.Empty;
            UIDocument? activeUiDocument = uiApplication.ActiveUIDocument;
            if (activeUiDocument is not null && ReferenceEquals(activeUiDocument.Document, activeDocument))
            {
                try
                {
                    activeUiDocument.Selection.SetElementIds(changedIds);
                    selectionSucceeded = true;
                }
                catch (Exception exception)
                {
                    selectionFailure = exception.Message;
                }
            }
            else
            {
                selectionFailure = "Aktiv UIDocument samsvarer ikke med dokumentet som ble oppdatert.";
            }

            string result = string.Format(
                CultureInfo.InvariantCulture,
                "Regel 1 v{0}: Dokument '{1}'. Vellykket oppdaterte parameterverdier: {2}; mislykkede skrivinger: {3}; parameter mangler: {4}; skrivebeskyttet: {5}; ikke støttet lagringstype: {6}; utenfor verdiområde: {7}; utvalg satt: {8}{9}",
                ScriptVersion,
                activeDocument.Title,
                changedIds.Count,
                failedWriteCount,
                missingParameterCount,
                readOnlyCount,
                unsupportedStorageCount,
                outOfRangeCount,
                selectionSucceeded,
                selectionSucceeded ? string.Empty : " (" + selectionFailure + ")");
            return SaveRunReport(activeDocument, result + (worksharingLog.Count == 0 ? string.Empty : " " + string.Join(" ", worksharingLog)), changedIds, worksharingLog);
        }

        private static string SaveRunReport(Document document, string result, List<ElementId> changedIds, List<string>? worksharingLog = null)
        {
            try
            {
                string reportDirectory = Path.Combine(@"D:\Revit\Python\Revit_BIM_Agent\logs", "reports");
                Directory.CreateDirectory(reportDirectory);
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
                string path = Path.Combine(reportDirectory, "Regel 1 rapport " + timestamp + ".txt");
                int suffix = 1;
                while (File.Exists(path)) path = Path.Combine(reportDirectory, "Regel 1 rapport " + timestamp + "_" + suffix++.ToString(CultureInfo.InvariantCulture) + ".txt");
                var lines = new List<string>
                {
                    string.Format(CultureInfo.InvariantCulture, "=== Regel 1 v{0} | {1:O} | {2} ===", ScriptVersion, DateTime.Now, document.Title),
                    result,
                    "ElementId;PGF_RIE_ElementId"
                };
                lines.AddRange(changedIds.Select(id => id.Value.ToString(CultureInfo.InvariantCulture) + ";" + id.Value.ToString(CultureInfo.InvariantCulture)));
                if (worksharingLog is not null) lines.AddRange(worksharingLog);
                File.WriteAllLines(path, lines, new UTF8Encoding(false));
                return result + " Rapport: " + path;
            }
            catch (Exception exception)
            {
                return result + " Rapport kunne ikke skrives: " + exception.Message;
            }
        }

        private static bool IsCenterLine(Element element)
        {
            long categoryId = element.Category?.Id.Value ?? long.MinValue;
            return CenterLineCategoryIds.Contains(categoryId);
        }
    }
}
