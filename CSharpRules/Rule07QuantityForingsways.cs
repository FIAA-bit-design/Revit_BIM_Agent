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
        private const string ScriptVersion = "0.0.6";
        private const string EnterpriseParameterName = "FOB_Entreprise";
        private const string TargetEnterprise = "K5B";
        private const string QuantityParameterName = "FOB_Mengde";
        private const string LogPath = @"D:\Revit\Python\Revit_BIM_Agent\logs\history\Rule07_QuantityForingsways.log";
        private static readonly BuiltInCategory[] StraightCategories = { BuiltInCategory.OST_Conduit, BuiltInCategory.OST_CableTray };
        private static readonly BuiltInCategory[] FittingCategories = { BuiltInCategory.OST_ConduitFitting, BuiltInCategory.OST_CableTrayFitting };

        private enum EnterpriseStatus
        {
            K5B,
            Other,
            Missing,
            Invalid
        }

        private sealed class WorksetCheckoutDialogHandler
        {
            private const string LargeCheckoutMessage = "trying to check out a large number of elements";
            private readonly List<string> log;

            internal int HandledCount { get; private set; }

            internal WorksetCheckoutDialogHandler(List<string> log)
            {
                this.log = log;
            }

            internal void HandleDialogBoxShowing(object? sender, DialogBoxShowingEventArgs eventArgs)
            {
                if (eventArgs is not TaskDialogShowingEventArgs taskDialog
                    || taskDialog.Message.IndexOf(LargeCheckoutMessage, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    return;
                }

                try
                {
                    if (eventArgs.OverrideResult((int)TaskDialogResult.CommandLink1))
                    {
                        HandledCount++;
                        log.Add("WORKSHARING: valgte Check Out Worksets for Revit-dialogen om mange elementer.");
                    }
                    else
                    {
                        log.Add("WORKSHARING-BLOKKERING: Revit godtok ikke automatisk valg av Check Out Worksets.");
                    }
                }
                catch (Exception exception)
                {
                    log.Add("WORKSHARING-BLOKKERING: kunne ikke svare på Check Out Worksets-dialogen: " + exception.Message);
                }
            }
        }

        private sealed class ChangeRecord
        {
            internal long ElementId { get; }
            internal string Category { get; }
            internal string Package { get; }
            internal string ElementGuid { get; }
            internal string Mark { get; }
            internal string BillOfQuantityItem { get; }
            internal string OldQuantity { get; }
            internal string NewQuantity { get; }

            internal ChangeRecord(Element element, string oldQuantity, string newQuantity)
            {
                ElementId = element.Id.Value;
                Category = element.Category?.Name ?? string.Empty;
                Package = GetParameterText(element, "FOB_Leveransepakke");
                ElementGuid = GetParameterText(element, "PGF_RIE_ElementId");
                Mark = GetParameterText(element, "FOB_Merkestreng");
                BillOfQuantityItem = GetParameterText(element, "FOB_Mengdelistepost");
                OldQuantity = oldQuantity;
                NewQuantity = newQuantity;
            }
        }

        public string Execute(UIApplication uiApplication, Document? activeDocument)
        {
            if (activeDocument is null)
            {
                return "FEIL: Ingen aktiv Revit-modell. Regel 7 stoppet før elementlesing.";
            }

            List<Element> allStraight = CollectElements(activeDocument, StraightCategories);
            List<Element> allFittings = CollectElements(activeDocument, FittingCategories);
            var log = new List<string>
            {
                string.Format(CultureInfo.InvariantCulture, "=== Regel 7 v{0} | {1:O} | {2} ===", ScriptVersion, DateTime.Now, activeDocument.Title),
                EnterpriseParameterName + "-filter: eksakt instansverdi " + TargetEnterprise + "; andre verdier ignoreres.",
                "Excel-rapport er fjernet; endringer logges per element nedenfor."
            };
            var k5bElementIds = new HashSet<long>();
            int missingEnterprise = 0;
            int invalidEnterprise = 0;
            foreach (Element element in allStraight.Concat(allFittings))
            {
                EnterpriseStatus status = GetEnterpriseStatus(element, out string issue);
                if (status == EnterpriseStatus.K5B)
                {
                    k5bElementIds.Add(element.Id.Value);
                }
                else if (status == EnterpriseStatus.Missing)
                {
                    missingEnterprise++;
                    log.Add(FormatIssue(element, EnterpriseParameterName, issue));
                }
                else if (status == EnterpriseStatus.Invalid)
                {
                    invalidEnterprise++;
                    log.Add(FormatIssue(element, EnterpriseParameterName, issue));
                }
            }
            List<Element> straightElements = allStraight.Where(element => k5bElementIds.Contains(element.Id.Value)).ToList();
            List<Element> fittingElements = allFittings.Where(element => k5bElementIds.Contains(element.Id.Value)).ToList();
            log.Add(string.Format(CultureInfo.InvariantCulture, "K5B-målsett: rette føringsveier {0}; fittings {1}.", straightElements.Count, fittingElements.Count));
            log.Add(string.Format(CultureInfo.InvariantCulture, "FOB_Entreprise mangler/blank: {0}; duplikat/feil lagringstype: {1}.", missingEnterprise, invalidEnterprise));

            int updated = 0;
            int unchanged = 0;
            int missingLength = 0;
            int missingParameter = 0;
            int readOnlyParameter = 0;
            int unsupportedStorage = 0;
            int writeFailures = 0;
            var changes = new List<ChangeRecord>();
            List<Element> targets = straightElements.Concat(fittingElements).OrderBy(element => element.Id.Value).ToList();

            if (targets.Count > 0)
            {
                var dialogHandler = new WorksetCheckoutDialogHandler(log);
                uiApplication.DialogBoxShowing += dialogHandler.HandleDialogBoxShowing;
                try
                {
                    using (var transaction = new Transaction(activeDocument, "Mengde føringsveier for entreprise K5B"))
                    {
                        TransactionStatus startStatus = transaction.Start();
                        if (startStatus != TransactionStatus.Started)
                        {
                            log.Add("TRANSAKSJONSFEIL: transaksjonen startet ikke (" + startStatus + ").");
                            return SaveAndReturn(log, "Regel 7 v" + ScriptVersion + ": transaksjonen startet ikke; ingen endringer utført.");
                        }

                        try
                        {
                            foreach (Element element in targets)
                            {
                                bool isFitting = IsInCategories(element, FittingCategories);
                                double? lengthMillimeters = GetElementLengthMillimeters(element, isFitting);
                                if (lengthMillimeters is null)
                                {
                                    missingLength++;
                                    continue;
                                }

                                if (!TryGetSingleParameter(element, QuantityParameterName, out Parameter? parameter, out string issue)
                                    || parameter is null)
                                {
                                    missingParameter++;
                                    log.Add(FormatIssue(element, QuantityParameterName, issue));
                                    continue;
                                }
                                if (parameter.IsReadOnly)
                                {
                                    readOnlyParameter++;
                                    log.Add(FormatIssue(element, QuantityParameterName, "parameteren er skrivebeskyttet"));
                                    continue;
                                }

                                double quantityMeters = Math.Round(lengthMillimeters.Value / 1000.0, 2);
                                string oldQuantity = GetParameterText(element, QuantityParameterName);
                                string result = SetQuantity(parameter, quantityMeters);
                                if (result == "updated")
                                {
                                    updated++;
                                    changes.Add(new ChangeRecord(element, oldQuantity, FormatQuantity(quantityMeters)));
                                }
                                else if (result == "unchanged")
                                {
                                    unchanged++;
                                }
                                else if (result == "failed")
                                {
                                    writeFailures++;
                                    log.Add(FormatIssue(element, QuantityParameterName, "Parameter.Set returnerte false"));
                                }
                                else
                                {
                                    unsupportedStorage++;
                                    log.Add(FormatIssue(element, QuantityParameterName, "lagringstypen støttes ikke"));
                                }
                            }

                            TransactionStatus commitStatus = transaction.Commit();
                            if (commitStatus != TransactionStatus.Committed)
                            {
                                throw new InvalidOperationException("Revit fullførte ikke mengdetransaksjonen: " + commitStatus);
                            }
                        }
                        catch (Exception exception)
                        {
                            if (transaction.GetStatus() == TransactionStatus.Started)
                            {
                                transaction.RollBack();
                            }
                            log.Add("TRANSAKSJONSFEIL: " + exception.Message);
                            return SaveAndReturn(log, "Regel 7 v" + ScriptVersion + ": transaksjonen ble rullet tilbake; 0 endringer lagret. " + exception.Message);
                        }
                    }
                }
                finally
                {
                    uiApplication.DialogBoxShowing -= dialogHandler.HandleDialogBoxShowing;
                }

                if (dialogHandler.HandledCount > 0)
                {
                    log.Add(string.Format(CultureInfo.InvariantCulture, "WORKSHARING: automatisk håndterte Check Out Worksets-dialoger {0}.", dialogHandler.HandledCount));
                }
            }

            log.Insert(3, string.Format(
                CultureInfo.InvariantCulture,
                "Oppdatert {0}; uendret {1}; mangler lengde {2}; mangler FOB_Mengde {3}; skrivebeskyttet {4}; feil lagringstype {5}; skrivefeil {6}.",
                updated, unchanged, missingLength, missingParameter, readOnlyParameter, unsupportedStorage, writeFailures));
            log.Add("Elementdetaljer for oppdaterte verdier (tab-separert):");
            log.Add("FOB_Leveransepakke\tPGF_RIE_ElementId\tFOB_Merkestreng\tFOB_Mengdelistepost\tFOB_Mengde gammel\tFOB_Mengde ny\tElementId\tKategori");
            foreach (ChangeRecord change in changes)
            {
                log.Add(string.Join("\t", new[]
                {
                    change.Package,
                    change.ElementGuid,
                    change.Mark,
                    change.BillOfQuantityItem,
                    change.OldQuantity,
                    change.NewQuantity,
                    change.ElementId.ToString(CultureInfo.InvariantCulture),
                    change.Category
                }.Select(EscapeLogField)));
            }

            return SaveAndReturn(log, string.Format(
                CultureInfo.InvariantCulture,
                "Regel 7 v{0}: entreprise {1}; oppdatert {2}; uendret {3}; mangler lengde {4}; mangler FOB_Mengde {5}; skrivebeskyttet {6}; feil lagringstype {7}; skrivefeil {8}; FOB_Entreprise mangler/blank {9}; duplikat/feil lagringstype {10}.",
                ScriptVersion, TargetEnterprise, updated, unchanged, missingLength, missingParameter, readOnlyParameter, unsupportedStorage, writeFailures, missingEnterprise, invalidEnterprise));
        }

        private static EnterpriseStatus GetEnterpriseStatus(Element element, out string issue)
        {
            if (!TryGetSingleParameter(element, EnterpriseParameterName, out Parameter? parameter, out issue))
            {
                return issue == "parameteren mangler" ? EnterpriseStatus.Missing : EnterpriseStatus.Invalid;
            }

            if (parameter is null || parameter.StorageType != StorageType.String)
            {
                issue = "instansparameteren må ha lagringstypen String";
                return EnterpriseStatus.Invalid;
            }

            string value = parameter.AsString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(value))
            {
                issue = "FOB_Entreprise er blank";
                return EnterpriseStatus.Missing;
            }

            issue = string.Empty;
            return string.Equals(value, TargetEnterprise, StringComparison.Ordinal)
                ? EnterpriseStatus.K5B
                : EnterpriseStatus.Other;
        }

        private static List<Element> CollectElements(Document document, BuiltInCategory[] categories)
        {
            var elements = new Dictionary<long, Element>();
            foreach (BuiltInCategory category in categories)
            {
                foreach (Element element in new FilteredElementCollector(document)
                    .OfCategory(category)
                    .WhereElementIsNotElementType()
                    .ToElements())
                {
                    elements[element.Id.Value] = element;
                }
            }
            return elements.Values.OrderBy(element => element.Id.Value).ToList();
        }

        private static double? GetElementLengthMillimeters(Element element, bool fitting)
        {
            if (!fitting)
            {
                Parameter? curveLength = element.get_Parameter(BuiltInParameter.CURVE_ELEM_LENGTH);
                double? length = GetLengthMillimeters(curveLength);
                if (length is not null)
                {
                    return length;
                }
            }

            foreach (string parameterName in new[] { "Arc Length", "PGF_Length_Bend", "Length 3" })
            {
                double? length = GetLengthMillimeters(element.LookupParameter(parameterName));
                if (length is not null && length.Value > 0.0)
                {
                    return length;
                }
            }

            if (element.Location is LocationCurve locationCurve && locationCurve.Curve is not null)
            {
                return UnitUtils.ConvertFromInternalUnits(locationCurve.Curve.Length, UnitTypeId.Millimeters);
            }
            return null;
        }

        private static double? GetLengthMillimeters(Parameter? parameter)
        {
            if (parameter is null || parameter.StorageType != StorageType.Double || !parameter.HasValue)
            {
                return null;
            }
            return UnitUtils.ConvertFromInternalUnits(parameter.AsDouble(), UnitTypeId.Millimeters);
        }

        private static string SetQuantity(Parameter parameter, double quantityMeters)
        {
            switch (parameter.StorageType)
            {
                case StorageType.Double:
                {
                    double desired = UnitUtils.ConvertToInternalUnits(quantityMeters, UnitTypeId.Meters);
                    if (Math.Abs(parameter.AsDouble() - desired) < 1e-9) return "unchanged";
                    return parameter.Set(desired) ? "updated" : "failed";
                }
                case StorageType.Integer:
                {
                    int desired = (int)Math.Round(quantityMeters);
                    if (parameter.AsInteger() == desired) return "unchanged";
                    return parameter.Set(desired) ? "updated" : "failed";
                }
                case StorageType.String:
                {
                    string desired = FormatQuantity(quantityMeters);
                    if (string.Equals(parameter.AsString(), desired, StringComparison.Ordinal)) return "unchanged";
                    return parameter.Set(desired) ? "updated" : "failed";
                }
                default:
                    return "unsupported";
            }
        }

        private static string FormatQuantity(double quantity)
        {
            if (quantity == Math.Truncate(quantity))
            {
                return quantity.ToString("0", CultureInfo.InvariantCulture);
            }
            return quantity.ToString("0.00", CultureInfo.InvariantCulture).Replace('.', ',');
        }

        private static string GetParameterText(Element element, string name)
        {
            Parameter? parameter = element.LookupParameter(name);
            if (parameter is null || !parameter.HasValue)
            {
                return string.Empty;
            }
            return parameter.StorageType == StorageType.String
                ? parameter.AsString() ?? string.Empty
                : parameter.AsValueString() ?? string.Empty;
        }

        private static bool TryGetSingleParameter(Element element, string name, out Parameter? parameter, out string issue)
        {
            IList<Parameter> matches = element.GetParameters(name);
            if (matches.Count == 0)
            {
                parameter = null;
                issue = "parameteren mangler";
                return false;
            }
            if (matches.Count > 1)
            {
                parameter = null;
                issue = "flere parametere med samme navn";
                return false;
            }
            parameter = matches[0];
            issue = string.Empty;
            return true;
        }

        private static bool IsInCategories(Element element, BuiltInCategory[] categories)
        {
            long categoryId = element.Category?.Id.Value ?? long.MinValue;
            return categories.Any(category => categoryId == new ElementId(category).Value);
        }

        private static string EscapeLogField(string value)
        {
            return value.Replace("\t", " ", StringComparison.Ordinal)
                .Replace("\r", " ", StringComparison.Ordinal)
                .Replace("\n", " ", StringComparison.Ordinal);
        }

        private static string FormatIssue(Element element, string parameterName, string reason)
        {
            return string.Format(CultureInfo.InvariantCulture, "UAVKLART ElementId {0}: {1}; årsak: {2}.", element.Id.Value, parameterName, reason);
        }

        private static string SaveAndReturn(List<string> log, string result)
        {
            try
            {
                File.AppendAllText(LogPath, string.Join(Environment.NewLine, log) + Environment.NewLine + Environment.NewLine, new UTF8Encoding(false));
                return result + " Logg: " + LogPath;
            }
            catch (Exception exception)
            {
                return result + " Logg kunne ikke skrives: " + exception.Message;
            }
        }
    }
}