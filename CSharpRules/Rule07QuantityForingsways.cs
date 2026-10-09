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
        private const string ScriptVersion = "0.0.15";
        private const string EnterpriseParameterName = "FOB_Entreprise";
        private const string TargetEnterprise = "K5B";
        private const string QuantityParameterName = "FOB_Mengde";
        private const string LogPath = @"D:\Revit\Python\Revit_BIM_Agent\logs\history\Rule07_QuantityForingsways.log";
        private static readonly BuiltInCategory[] StraightCategories = { BuiltInCategory.OST_Conduit, BuiltInCategory.OST_CableTray };
        private static readonly BuiltInCategory[] FittingCategories = { BuiltInCategory.OST_ConduitFitting, BuiltInCategory.OST_CableTrayFitting };
        private static readonly HashSet<long> ConduitBendLengthOverrideIds = new HashSet<long> { 17760381L, 17760457L };

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
                if (eventArgs is not TaskDialogShowingEventArgs taskDialog)
                {
                    return;
                }

                string message = taskDialog.Message ?? string.Empty;
                string normalizedMessage = new string(message.Where(char.IsLetterOrDigit).ToArray());
                string normalizedTrigger = new string(LargeCheckoutMessage.Where(char.IsLetterOrDigit).ToArray());
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

        private sealed class CableTrayRunLength
        {
            internal double TotalMillimeters { get; }
            internal List<long> CableTrayElementIds { get; }
            internal List<long> BranchFittingIds { get; }

            internal CableTrayRunLength(double totalMillimeters, List<long> cableTrayElementIds, List<long> branchFittingIds)
            {
                TotalMillimeters = totalMillimeters;
                CableTrayElementIds = cableTrayElementIds;
                BranchFittingIds = branchFittingIds;
            }
        }

        private sealed class BillOfQuantityPostWrite
        {
            internal Element Element { get; }
            internal Parameter Parameter { get; }
            internal string Value { get; }
            internal long[] SourceIds { get; }

            internal BillOfQuantityPostWrite(Element element, Parameter parameter, string value, long[] sourceIds)
            {
                Element = element;
                Parameter = parameter;
                Value = value;
                SourceIds = sourceIds;
            }
        }

        public string Execute(UIApplication uiApplication, Document? activeDocument)
        {
            if (activeDocument is null)
            {
                return "FEIL: Ingen aktiv Revit-modell. Regel 7 stoppet før elementlesing.";
            }

            List<Element> allCollectedStraight = CollectElements(activeDocument, StraightCategories);
            List<Element> allCollectedFittings = CollectElements(activeDocument, FittingCategories);
            int aspirationExcludedCount = allCollectedStraight.Concat(allCollectedFittings).Count(IsAspirationElement);
            int ignoredStrømskinneBends = allCollectedFittings.Count(element =>
                IsStrømskinneBend(element) && GetEnterpriseStatus(element, out _) == EnterpriseStatus.K5B);
            List<Element> allStraight = allCollectedStraight.Where(element => !IsAspirationElement(element)).ToList();
            List<Element> allFittings = allCollectedFittings
                .Where(element => !IsAspirationElement(element) && !IsStrømskinneBend(element))
                .ToList();
            var log = new List<string>
            {
                string.Format(CultureInfo.InvariantCulture, "=== Regel 7 v{0} | {1:O} | {2} ===", ScriptVersion, DateTime.Now, activeDocument.Title),
                EnterpriseParameterName + "-filter: eksakt instansverdi " + TargetEnterprise + "; andre verdier ignoreres.",
                "Aspirasjonsfamilier/-typer utelatt før enterprise- og lengdekontroll: " + aspirationExcludedCount.ToString(CultureInfo.InvariantCulture) + ".",
                "Strømskinne-bend utelatt fra mengdeberegning: " + ignoredStrømskinneBends.ToString(CultureInfo.InvariantCulture) + ".",
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
            int skippedCableTrayUnions = 0;
            int skippedCableTrayRunFittings = 0;
            int calculatedCableTrayRunLengths = 0;
            int unresolvedCableTrayRunLengths = 0;
            int branchQuantityUpdated = 0;
            int branchUnitUpdated = 0;
            int branchQuantityUnchanged = 0;
            int missingLength = 0;
            int missingParameter = 0;
            int readOnlyParameter = 0;
            int unsupportedStorage = 0;
            int writeFailures = 0;
            var changes = new List<ChangeRecord>();
            List<Element> targets = straightElements.Concat(fittingElements).OrderBy(element => element.Id.Value).ToList();
            int unresolvedBillOfQuantityPostCopies = 0;
            List<BillOfQuantityPostWrite> billOfQuantityPostWrites = PlanCableTrayBillOfQuantityPosts(
                targets, log, out unresolvedBillOfQuantityPostCopies);
            int updatedBillOfQuantityPosts = 0;
            var quantityTargets = new List<Element>();
            var branchQuantityTargets = new List<FamilyInstance>();
            var cableTrayRunLengths = new Dictionary<long, CableTrayRunLength>();
            var resolvedBranchFittings = new HashSet<long>();
            var unresolvedBranchFittings = new HashSet<long>();

            foreach (Element element in targets)
            {
                if (IsCableTrayUnion(element))
                {
                    skippedCableTrayUnions++;
                    log.Add(string.Format(CultureInfo.InvariantCulture,
                        "UNION UTELATT ElementId {0}: kabelbro-/kabelstige-skjøt; eksisterende FOB_Mengde '{1}' bevart.",
                        element.Id.Value, GetParameterText(element, QuantityParameterName)));
                    continue;
                }

                if (IsCableTrayRunLengthFitting(element))
                {
                    skippedCableTrayRunFittings++;
                    if (element is FamilyInstance branchFitting)
                    {
                        branchQuantityTargets.Add(branchFitting);
                    }
                    if (cableTrayRunLengths.TryGetValue(element.Id.Value, out CableTrayRunLength? cachedRun))
                    {
                        calculatedCableTrayRunLengths++;
                        LogCableTrayRunLength(log, element, cachedRun);
                    }
                    else if (unresolvedBranchFittings.Contains(element.Id.Value))
                    {
                        unresolvedCableTrayRunLengths++;
                    }
                    else if (TryGetCableTrayRunLength(element, out CableTrayRunLength? runLength, out string issue)
                        && runLength is not null)
                    {
                        foreach (long fittingId in runLength.BranchFittingIds)
                        {
                            cableTrayRunLengths[fittingId] = runLength;
                            resolvedBranchFittings.Add(fittingId);
                        }
                        calculatedCableTrayRunLengths++;
                        LogCableTrayRunLength(log, element, runLength);
                    }
                    else
                    {
                        unresolvedCableTrayRunLengths++;
                        foreach (long fittingId in runLength?.BranchFittingIds ?? new List<long> { element.Id.Value })
                        {
                            unresolvedBranchFittings.Add(fittingId);
                        }
                        log.Add(FormatIssue(element, "FOB_Mengde", "kunne ikke beregne samlet lengde fra fysisk tilkoblede kabelbroelementer: " + issue));
                    }
                    continue;
                }

                quantityTargets.Add(element);
            }

            if (quantityTargets.Count > 0 || branchQuantityTargets.Count > 0 || billOfQuantityPostWrites.Count > 0)
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
                            foreach (BillOfQuantityPostWrite write in billOfQuantityPostWrites)
                            {
                                if (!write.Parameter.Set(write.Value))
                                {
                                    writeFailures++;
                                    unresolvedBillOfQuantityPostCopies++;
                                    log.Add(FormatIssue(write.Element, "FOB_Mengdelistepost", "Parameter.Set returnerte false for connectorbasert kopiering"));
                                    continue;
                                }

                                updatedBillOfQuantityPosts++;
                                log.Add(string.Format(CultureInfo.InvariantCulture,
                                    "OPPDATERT FOB_Mengdelistepost ElementId {0} = '{1}' fra fysisk tilkoblede ElementId-er {2}.",
                                    write.Element.Id.Value, EscapeLogField(write.Value), string.Join(",", write.SourceIds)));
                            }

                            foreach (Element element in quantityTargets)
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

                            foreach (FamilyInstance fitting in branchQuantityTargets)
                            {
                                if (!TryGetSingleParameter(fitting, QuantityParameterName, out Parameter? quantityParameter, out string quantityIssue)
                                    || quantityParameter is null)
                                {
                                    missingParameter++;
                                    log.Add(FormatIssue(fitting, QuantityParameterName, quantityIssue));
                                    continue;
                                }
                                if (!TryGetSingleParameter(fitting, "FOB_Mengdeenhet", out Parameter? unitParameter, out string unitIssue)
                                    || unitParameter is null)
                                {
                                    missingParameter++;
                                    log.Add(FormatIssue(fitting, "FOB_Mengdeenhet", unitIssue));
                                    continue;
                                }
                                if (quantityParameter.StorageType != StorageType.String
                                    || unitParameter.StorageType != StorageType.String)
                                {
                                    unsupportedStorage++;
                                    log.Add(FormatIssue(fitting, QuantityParameterName + "/FOB_Mengdeenhet", "begge målparameterne må ha lagringstypen String"));
                                    continue;
                                }
                                if (quantityParameter.IsReadOnly || unitParameter.IsReadOnly)
                                {
                                    readOnlyParameter++;
                                    log.Add(FormatIssue(fitting, QuantityParameterName + "/FOB_Mengdeenhet", "minst én målparameter er skrivebeskyttet"));
                                    continue;
                                }

                                string oldQuantity = quantityParameter.AsString() ?? string.Empty;
                                string oldUnit = unitParameter.AsString() ?? string.Empty;
                                bool quantityChanged = !string.Equals(oldQuantity, "1", StringComparison.Ordinal);
                                bool unitChanged = !string.Equals(oldUnit, "stk", StringComparison.Ordinal);
                                if (!quantityChanged && !unitChanged)
                                {
                                    branchQuantityUnchanged++;
                                    continue;
                                }

                                using (var subTransaction = new SubTransaction(activeDocument))
                                {
                                    TransactionStatus subStatus = subTransaction.Start();
                                    if (subStatus != TransactionStatus.Started)
                                    {
                                        writeFailures++;
                                        log.Add(FormatIssue(fitting, QuantityParameterName + "/FOB_Mengdeenhet", "subtransaksjonen startet ikke (" + subStatus + ")"));
                                        continue;
                                    }
                                    try
                                    {
                                        if (quantityChanged && !quantityParameter.Set("1"))
                                        {
                                            throw new InvalidOperationException("FOB_Mengde kunne ikke settes til 1.");
                                        }
                                        if (unitChanged && !unitParameter.Set("stk"))
                                        {
                                            throw new InvalidOperationException("FOB_Mengdeenhet kunne ikke settes til stk.");
                                        }
                                        TransactionStatus subCommit = subTransaction.Commit();
                                        if (subCommit != TransactionStatus.Committed)
                                        {
                                            throw new InvalidOperationException("subtransaksjonen ble ikke committed (" + subCommit + ").");
                                        }
                                    }
                                    catch (Exception exception)
                                    {
                                        if (subTransaction.GetStatus() == TransactionStatus.Started)
                                        {
                                            subTransaction.RollBack();
                                        }
                                        writeFailures++;
                                        log.Add(FormatIssue(fitting, QuantityParameterName + "/FOB_Mengdeenhet", exception.Message));
                                        continue;
                                    }
                                }

                                if (quantityChanged)
                                {
                                    branchQuantityUpdated++;
                                    changes.Add(new ChangeRecord(fitting, oldQuantity, "1"));
                                }
                                if (unitChanged) branchUnitUpdated++;
                                log.Add(string.Format(CultureInfo.InvariantCulture,
                                    "OPPDATERT T/KRYSS ElementId {0}: FOB_Mengde '{1}' -> '1'; FOB_Mengdeenhet '{2}' -> 'stk'.",
                                    fitting.Id.Value, oldQuantity, oldUnit));
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
                "Aspirasjon utelatt {0}; strømskinne-bend utelatt {1}; oppdatert {2}; uendret {3}; Union utelatt {4}; T/kryss fittinger {5}; T/kryss FOB_Mengde satt til 1 {6}; FOB_Mengdeenhet satt til stk {7}; T/kryss mengde uendret {8}; tilkoblet runlengde beregnet {9}; runlengde uavklart {10}; mangler lengde {11}; mangler FOB_Mengde {12}; skrivebeskyttet {13}; feil lagringstype {14}; skrivefeil {15}.",
                aspirationExcludedCount, ignoredStrømskinneBends, updated, unchanged, skippedCableTrayUnions, skippedCableTrayRunFittings,
                branchQuantityUpdated, branchUnitUpdated, branchQuantityUnchanged, calculatedCableTrayRunLengths,
                unresolvedCableTrayRunLengths,
                missingLength, missingParameter, readOnlyParameter, unsupportedStorage, writeFailures));
            log.Add(string.Format(CultureInfo.InvariantCulture,
                "FOB_Mengdelistepost connector-kopiering: oppdatert {0}; uavklart {1}.",
                updatedBillOfQuantityPosts, unresolvedBillOfQuantityPostCopies));
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
                "Regel 7 v{0}: entreprise {1}; aspirasjon utelatt {2}; strømskinne-bend utelatt {3}; oppdatert {4}; uendret {5}; Union utelatt {6}; T/kryss fittinger {7}; mengde satt til 1 {8}; enhet satt til stk {9}; runlengde beregnet {10}; runlengde uavklart {11}; mangler lengde {12}; mangler FOB_Mengde {13}; skrivebeskyttet {14}; feil lagringstype {15}; skrivefeil {16}; FOB_Entreprise mangler/blank {17}; duplikat/feil lagringstype {18}; FOB_Mengdelistepost kopiert {19}; uavklart postkopiering {20}.",
                ScriptVersion, TargetEnterprise, aspirationExcludedCount, ignoredStrømskinneBends, updated, unchanged, skippedCableTrayUnions, skippedCableTrayRunFittings,
                branchQuantityUpdated, branchUnitUpdated, calculatedCableTrayRunLengths, unresolvedCableTrayRunLengths,
                missingLength, missingParameter, readOnlyParameter, unsupportedStorage,
                writeFailures, missingEnterprise, invalidEnterprise, updatedBillOfQuantityPosts, unresolvedBillOfQuantityPostCopies));
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
            if (ConduitBendLengthOverrideIds.Contains(element.Id.Value)
                && element.Category?.Id.Value == new ElementId(BuiltInCategory.OST_ConduitFitting).Value)
            {
                IList<Parameter> conduitLengthParameters = element.GetParameters("Conduit Length");
                if (conduitLengthParameters.Count == 1)
                {
                    double? conduitLength = GetLengthMillimeters(conduitLengthParameters[0]);
                    if (conduitLength is not null && conduitLength.Value > 0.0) return conduitLength;
                }
            }

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

        private static bool IsCableTrayUnion(Element element)
        {
            if (element.Category?.Id.Value != new ElementId(BuiltInCategory.OST_CableTrayFitting).Value)
            {
                return false;
            }
            if (element is not FamilyInstance familyInstance) return false;

            Parameter? partTypeParameter = familyInstance.Symbol?.Family?.get_Parameter(BuiltInParameter.FAMILY_CONTENT_PART_TYPE)
                ?? familyInstance.Symbol?.get_Parameter(BuiltInParameter.FAMILY_CONTENT_PART_TYPE)
                ?? element.get_Parameter(BuiltInParameter.FAMILY_CONTENT_PART_TYPE);
            if (partTypeParameter is null || !partTypeParameter.HasValue
                || partTypeParameter.StorageType != StorageType.Integer)
            {
                return false;
            }

            string? partType = Enum.GetName(typeof(PartType), partTypeParameter.AsInteger());
            return partType is "Union" or "ChannelCableTrayUnion" or "LadderCableTrayUnion";
        }

        private static bool IsAspirationElement(Element element)
        {
            if (element is not FamilyInstance instance) return false;
            string familyName = instance.Symbol?.Family?.Name ?? string.Empty;
            string typeName = instance.Symbol?.Name ?? string.Empty;
            string instanceName = element.Name ?? string.Empty;
            return ContainsAspirationToken(familyName)
                || ContainsAspirationToken(typeName)
                || ContainsAspirationToken(instanceName);
        }

        private static bool IsStrømskinneBend(Element element)
        {
            if (element.Category?.Id.Value != new ElementId(BuiltInCategory.OST_CableTrayFitting).Value
                || element is not FamilyInstance instance)
            {
                return false;
            }
            string familyName = instance.Symbol?.Family?.Name ?? string.Empty;
            string typeName = instance.Symbol?.Name ?? string.Empty;
            string partType = GetCableTrayPartTypeName(instance);
            bool isBend = partType.IndexOf("Elbow", StringComparison.Ordinal) >= 0;
            bool isStrømskinne = familyName.IndexOf("strømskinne", StringComparison.OrdinalIgnoreCase) >= 0
                || typeName.IndexOf("strømskinne", StringComparison.OrdinalIgnoreCase) >= 0;
            return isBend && isStrømskinne;
        }

        private static bool ContainsAspirationToken(string value)
        {
            return value.IndexOf("aspirasjon", StringComparison.OrdinalIgnoreCase) >= 0
                || value.IndexOf("aspiration", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsCableTrayRunLengthFitting(Element element)
        {
            if (element.Category?.Id.Value != new ElementId(BuiltInCategory.OST_CableTrayFitting).Value
                || element is not FamilyInstance familyInstance)
            {
                return false;
            }

            Parameter? partTypeParameter = familyInstance.Symbol?.Family?.get_Parameter(BuiltInParameter.FAMILY_CONTENT_PART_TYPE)
                ?? familyInstance.Symbol?.get_Parameter(BuiltInParameter.FAMILY_CONTENT_PART_TYPE)
                ?? element.get_Parameter(BuiltInParameter.FAMILY_CONTENT_PART_TYPE);
            if (partTypeParameter is null || !partTypeParameter.HasValue
                || partTypeParameter.StorageType != StorageType.Integer)
            {
                return false;
            }

            string? partType = Enum.GetName(typeof(PartType), partTypeParameter.AsInteger());
            return partType is "Tee" or "Cross" or "ChannelCableTrayTee" or "ChannelCableTrayCross"
                or "LadderCableTrayTee" or "LadderCableTrayCross";
        }

        private static bool TryGetCableTrayRunLength(
            Element startFitting,
            out CableTrayRunLength? runLength,
            out string issue)
        {
            runLength = null;
            issue = "fitting mangler tilgjengelige MEP-koblinger";
            if (startFitting is not FamilyInstance startInstance || startInstance.MEPModel?.ConnectorManager is null)
            {
                return false;
            }

            try
            {
                var visited = new HashSet<long> { startFitting.Id.Value };
                var pending = new Queue<Element>();
                var cableTrayLengths = new Dictionary<long, double>();
                var branchFittingIds = new HashSet<long>();
                pending.Enqueue(startFitting);

                while (pending.Count > 0)
                {
                    Element current = pending.Dequeue();
                    long categoryId = current.Category?.Id.Value ?? long.MinValue;
                    bool isStraightCableTray = categoryId == new ElementId(BuiltInCategory.OST_CableTray).Value;
                    bool isCableTrayFitting = categoryId == new ElementId(BuiltInCategory.OST_CableTrayFitting).Value;
                    if (!isStraightCableTray && !isCableTrayFitting) continue;

                    if (isStraightCableTray)
                    {
                        if (current.Location is not LocationCurve locationCurve || locationCurve.Curve is null)
                        {
                            issue = "rett kabelbro ElementId " + current.Id.Value.ToString(CultureInfo.InvariantCulture) + " mangler LocationCurve";
                            return false;
                        }
                        cableTrayLengths[current.Id.Value] = UnitUtils.ConvertFromInternalUnits(
                            locationCurve.Curve.Length, UnitTypeId.Millimeters);
                    }
                    else if (IsCableTrayRunLengthFitting(current))
                    {
                        branchFittingIds.Add(current.Id.Value);
                    }

                    ConnectorManager? connectorManager = GetConnectorManager(current);
                    if (connectorManager is null)
                    {
                        if (isCableTrayFitting)
                        {
                            issue = "kabelbrofitting ElementId " + current.Id.Value.ToString(CultureInfo.InvariantCulture) + " mangler MEP-koblinger";
                            return false;
                        }
                        issue = "rett kabelbro ElementId " + current.Id.Value.ToString(CultureInfo.InvariantCulture) + " mangler connector-manager";
                        return false;
                    }

                    foreach (Connector connector in connectorManager.Connectors)
                    {
                        if (connector.ConnectorType != ConnectorType.End) continue;
                        foreach (Connector connected in connector.AllRefs)
                        {
                            Element owner = connected.Owner;
                            long ownerCategory = owner.Category?.Id.Value ?? long.MinValue;
                            if (owner.Id.Value == current.Id.Value
                                || connected.ConnectorType != ConnectorType.End
                                || !connector.IsConnectedTo(connected)
                                || !connected.IsConnectedTo(connector)
                                || IsAspirationElement(owner)
                                || (ownerCategory != new ElementId(BuiltInCategory.OST_CableTray).Value
                                    && ownerCategory != new ElementId(BuiltInCategory.OST_CableTrayFitting).Value)
                                || GetEnterpriseStatus(owner, out _) != EnterpriseStatus.K5B)
                            {
                                continue;
                            }
                            if (visited.Add(owner.Id.Value)) pending.Enqueue(owner);
                        }
                    }
                }

                if (cableTrayLengths.Count == 0)
                {
                    issue = "fant ingen fysisk tilkoblet kabelbro med LocationCurve i K5B-nettverket";
                    return false;
                }

                runLength = new CableTrayRunLength(
                    cableTrayLengths.Values.Sum(),
                    cableTrayLengths.Keys.OrderBy(id => id).ToList(),
                    branchFittingIds.OrderBy(id => id).ToList());
                issue = string.Empty;
                return true;
            }
            catch (Exception exception)
            {
                issue = "connector-/geometrilesing feilet: " + exception.Message;
                return false;
            }
        }

        private static ConnectorManager? GetConnectorManager(Element element)
        {
            if (element is MEPCurve curve) return curve.ConnectorManager;
            if (element is FamilyInstance familyInstance) return familyInstance.MEPModel?.ConnectorManager;
            return null;
        }

        private static List<BillOfQuantityPostWrite> PlanCableTrayBillOfQuantityPosts(
            List<Element> targets,
            List<string> log,
            out int unresolvedCount)
        {
            unresolvedCount = 0;
            var writes = new List<BillOfQuantityPostWrite>();
            List<Element> cableTrayElements = targets
                .Where(element => element.Category?.Id.Value == new ElementId(BuiltInCategory.OST_CableTray).Value
                    || element.Category?.Id.Value == new ElementId(BuiltInCategory.OST_CableTrayFitting).Value)
                .ToList();
            var adjacency = cableTrayElements.ToDictionary(element => element.Id.Value, _ => new HashSet<long>());
            var elementsById = cableTrayElements.ToDictionary(element => element.Id.Value);

            foreach (Element element in cableTrayElements)
            {
                ConnectorManager? connectorManager = GetConnectorManager(element);
                if (connectorManager is null) continue;
                foreach (Connector connector in connectorManager.Connectors)
                {
                    if (connector.ConnectorType != ConnectorType.End) continue;
                    foreach (Connector connected in connector.AllRefs)
                    {
                        long connectedId = connected.Owner.Id.Value;
                        if (connectedId == element.Id.Value
                            || !adjacency.ContainsKey(connectedId)
                            || connected.ConnectorType != ConnectorType.End
                            || !connector.IsConnectedTo(connected)
                            || !connected.IsConnectedTo(connector))
                        {
                            continue;
                        }
                        adjacency[element.Id.Value].Add(connectedId);
                        adjacency[connectedId].Add(element.Id.Value);
                    }
                }
            }

            var visited = new HashSet<long>();
            foreach (long startId in adjacency.Keys.OrderBy(id => id))
            {
                if (!visited.Add(startId)) continue;
                var pending = new Queue<long>();
                var component = new List<long>();
                pending.Enqueue(startId);
                while (pending.Count > 0)
                {
                    long currentId = pending.Dequeue();
                    component.Add(currentId);
                    foreach (long neighborId in adjacency[currentId])
                    {
                        if (visited.Add(neighborId)) pending.Enqueue(neighborId);
                    }
                }

                var sourceValues = new List<(long Id, string Value)>();
                var missingTargets = new List<long>();
                foreach (long elementId in component)
                {
                    Element element = elementsById[elementId];
                    if (!TryGetSingleParameter(element, "FOB_Mengdelistepost", out Parameter? parameter, out string issue)
                        || parameter is null)
                    {
                        missingTargets.Add(elementId);
                        if (issue != "parameteren mangler")
                        {
                            unresolvedCount++;
                            log.Add(FormatIssue(element, "FOB_Mengdelistepost", issue));
                        }
                        continue;
                    }
                    if (parameter.StorageType != StorageType.String)
                    {
                        unresolvedCount++;
                        log.Add(FormatIssue(element, "FOB_Mengdelistepost", "parameteren må ha lagringstypen String"));
                        continue;
                    }

                    string value = (parameter.AsString() ?? string.Empty).Trim();
                    if (string.IsNullOrWhiteSpace(value) || string.Equals(value, "--", StringComparison.Ordinal))
                    {
                        missingTargets.Add(elementId);
                    }
                    else
                    {
                        sourceValues.Add((elementId, value));
                    }
                }

                if (missingTargets.Count == 0) continue;
                var distinctValues = sourceValues.Select(source => source.Value).Distinct(StringComparer.Ordinal).ToList();
                if (sourceValues.Count < 2 || distinctValues.Count != 1)
                {
                    string reason = sourceValues.Count < 2
                        ? "færre enn to gyldige FOB_Mengdelistepost-kilder i connectorgruppen"
                        : "motstridende FOB_Mengdelistepost-kilder: "
                            + string.Join(", ", sourceValues.Select(source => source.Id.ToString(CultureInfo.InvariantCulture) + "='" + EscapeLogField(source.Value) + "'"));
                    foreach (long targetId in missingTargets)
                    {
                        unresolvedCount++;
                        log.Add(FormatIssue(elementsById[targetId], "FOB_Mengdelistepost", reason));
                    }
                    continue;
                }

                string resolvedValue = distinctValues[0];
                long[] sourceIds = sourceValues.Select(source => source.Id).OrderBy(id => id).ToArray();
                foreach (long targetId in missingTargets)
                {
                    Element target = elementsById[targetId];
                    if (!TryGetSingleParameter(target, "FOB_Mengdelistepost", out Parameter? parameter, out string issue)
                        || parameter is null)
                    {
                        unresolvedCount++;
                        log.Add(FormatIssue(target, "FOB_Mengdelistepost", issue));
                        continue;
                    }
                    if (parameter.StorageType != StorageType.String || parameter.IsReadOnly)
                    {
                        unresolvedCount++;
                        log.Add(FormatIssue(target, "FOB_Mengdelistepost", parameter.IsReadOnly
                            ? "parameteren er skrivebeskyttet"
                            : "parameteren må ha lagringstypen String"));
                        continue;
                    }
                    writes.Add(new BillOfQuantityPostWrite(target, parameter, resolvedValue, sourceIds));
                }
            }

            return writes;
        }

        private static void LogCableTrayRunLength(List<string> log, Element fitting, CableTrayRunLength runLength)
        {
            string partType = fitting is FamilyInstance familyInstance
                ? GetCableTrayPartTypeName(familyInstance)
                : "<unknown>";
            log.Add(string.Format(CultureInfo.InvariantCulture,
                "RUNLENGDE T/KRYSS ElementId {0}; deltype {1}; unike kabelbroelementer {2}; samlet nettverkslengde {3:F3} m; segment-ID-er {4}; nettverkslengden skrives ikke på fitting.",
                fitting.Id.Value, partType, runLength.CableTrayElementIds.Count,
                runLength.TotalMillimeters / 1000.0,
                string.Join(",", runLength.CableTrayElementIds)));
        }

        private static string GetCableTrayPartTypeName(FamilyInstance familyInstance)
        {
            Parameter? partTypeParameter = familyInstance.Symbol?.Family?.get_Parameter(BuiltInParameter.FAMILY_CONTENT_PART_TYPE)
                ?? familyInstance.Symbol?.get_Parameter(BuiltInParameter.FAMILY_CONTENT_PART_TYPE)
                ?? familyInstance.get_Parameter(BuiltInParameter.FAMILY_CONTENT_PART_TYPE);
            return partTypeParameter is not null && partTypeParameter.HasValue
                && partTypeParameter.StorageType == StorageType.Integer
                ? Enum.GetName(typeof(PartType), partTypeParameter.AsInteger()) ?? "<unknown>"
                : "<unknown>";
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
            string reportResult;
            try
            {
                string reportDirectory = Path.Combine(Path.GetDirectoryName(LogPath)!, "..", "reports");
                Directory.CreateDirectory(reportDirectory);
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
                string reportPath = Path.Combine(reportDirectory, "Rule07_QuantityForingsways " + timestamp + ".txt");
                File.WriteAllLines(reportPath, log, new UTF8Encoding(false));
                reportResult = "Rapport: " + reportPath;
            }
            catch (Exception exception) { reportResult = "Rapport kunne ikke skrives: " + exception.Message; }
            try
            {
                File.AppendAllText(LogPath, string.Join(Environment.NewLine, log) + Environment.NewLine + Environment.NewLine, new UTF8Encoding(false));
                return result + " " + reportResult + " Logg: " + LogPath;
            }
            catch (Exception exception)
            {
                return result + " " + reportResult + " Logg kunne ikke skrives: " + exception.Message;
            }
        }
    }
}