#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace CW.Assistant.Generated
{
    internal sealed class GeneratedAction
    {
        private const string ParameterName = "PGF_RIE_ElementId";
        private const string ScriptVersion = "0.0.2";
        private const long MaxExactlyRepresentableIntegerAsDouble = 9007199254740992L;

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
                if (element is null)
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
                return string.Format(
                    CultureInfo.InvariantCulture,
                    "Regel 1 v{0}: Ingen oppdateringer nødvendig i '{1}'. Instanser undersøkt: {2}; parameter mangler: {3}; skrivebeskyttet: {4}; ikke støttet lagringstype: {5}; utenfor verdiområde: {6}.",
                    ScriptVersion,
                    activeDocument.Title,
                    instanceCount,
                    missingParameterCount,
                    readOnlyCount,
                    unsupportedStorageCount,
                    outOfRangeCount);
            }

            TransactionStatus commitStatus;
            using (var transaction = new Transaction(activeDocument, "Synkroniser PGF_RIE_ElementId med ElementId"))
            {
                TransactionStatus startStatus = transaction.Start();
                if (startStatus != TransactionStatus.Started)
                {
                    return string.Format(
                        CultureInfo.InvariantCulture,
                        "Regel 1 v{0}: Transaksjonen startet ikke (status {1}); ingen endringer ble forsøkt.",
                        ScriptVersion,
                        startStatus);
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
                        return string.Format(
                            CultureInfo.InvariantCulture,
                            "Regel 1 v{0}: Transaksjonen ble ikke committed (status {1}); endrede elementer ble ikke valgt.",
                            ScriptVersion,
                            commitStatus);
                    }
                }
                catch (Exception exception)
                {
                    if (transaction.GetStatus() == TransactionStatus.Started)
                    {
                        transaction.RollBack();
                    }
                    return string.Format(
                        CultureInfo.InvariantCulture,
                        "Regel 1 v{0}: Transaksjonsfeil i '{1}': {2}",
                        ScriptVersion,
                        activeDocument.Title,
                        exception.Message);
                }
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

            return string.Format(
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
        }
    }
}
