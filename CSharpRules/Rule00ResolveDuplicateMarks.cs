#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace CW.Assistant.Generated
{
    internal sealed class GeneratedAction
    {
        private const string ScriptVersion = "0.0.2";

        private sealed class MarkEntry
        {
            internal Element Owner { get; }
            internal Parameter Parameter { get; }
            internal string Value { get; }
            internal long CategoryId { get; }

            internal MarkEntry(Element owner, Parameter parameter, string value)
            {
                Owner = owner;
                Parameter = parameter;
                Value = value;
                CategoryId = owner.Category?.Id.Value ?? long.MinValue;
            }
        }

        private sealed class PendingWrite
        {
            internal MarkEntry Entry { get; }
            internal string NewValue { get; }

            internal PendingWrite(MarkEntry entry, string newValue)
            {
                Entry = entry;
                NewValue = newValue;
            }
        }

        public string Execute(UIApplication uiApplication, Document? activeDocument)
        {
            if (activeDocument is null)
            {
                return "FEIL: Ingen aktiv Revit-modell. Regel 0 stoppet før elementlesing.";
            }

            var entriesByCategory = new Dictionary<long, List<MarkEntry>>();
            var reservedValuesByCategory = new Dictionary<long, HashSet<string>>();
            int missingMarkCount = 0;
            int unsupportedStorageCount = 0;

            foreach (Element element in new FilteredElementCollector(activeDocument)
                .WhereElementIsNotElementType()
                .ToElements())
            {
                if (element is null)
                {
                    continue;
                }

                Parameter? markParameter = element.get_Parameter(BuiltInParameter.ALL_MODEL_MARK);
                if (markParameter is null)
                {
                    missingMarkCount++;
                    continue;
                }
                if (markParameter.StorageType != StorageType.String)
                {
                    unsupportedStorageCount++;
                    continue;
                }

                string value = markParameter.AsString() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                long categoryId = element.Category?.Id.Value ?? long.MinValue;
                if (!reservedValuesByCategory.TryGetValue(categoryId, out HashSet<string>? reservedValues))
                {
                    reservedValues = new HashSet<string>(StringComparer.Ordinal);
                    reservedValuesByCategory.Add(categoryId, reservedValues);
                }
                reservedValues.Add(value);

                if (!entriesByCategory.TryGetValue(categoryId, out List<MarkEntry>? categoryEntries))
                {
                    categoryEntries = new List<MarkEntry>();
                    entriesByCategory.Add(categoryId, categoryEntries);
                }
                categoryEntries.Add(new MarkEntry(element, markParameter, value));
            }

            var pendingWrites = new List<PendingWrite>();
            int duplicateGroupCount = 0;
            int duplicateInstanceCount = 0;
            int readOnlyCount = 0;

            foreach (KeyValuePair<long, List<MarkEntry>> category in entriesByCategory)
            {
                HashSet<string> reservedValues = reservedValuesByCategory[category.Key];
                IEnumerable<IGrouping<string, MarkEntry>> duplicateGroups = category.Value
                    .GroupBy(entry => entry.Value, StringComparer.Ordinal)
                    .Where(group => group.Count() > 1)
                    .OrderBy(group => group.Key, StringComparer.Ordinal);

                foreach (IGrouping<string, MarkEntry> duplicateGroup in duplicateGroups)
                {
                    duplicateGroupCount++;
                    List<MarkEntry> orderedEntries = duplicateGroup
                        .OrderBy(entry => entry.Owner.Id.Value)
                        .ToList();
                    duplicateInstanceCount += orderedEntries.Count - 1;

                    foreach (MarkEntry duplicate in orderedEntries.Skip(1))
                    {
                        if (duplicate.Parameter.IsReadOnly)
                        {
                            readOnlyCount++;
                            continue;
                        }

                        string newValue = CreateUniqueValue(duplicate.Value, reservedValues);
                        reservedValues.Add(newValue);
                        pendingWrites.Add(new PendingWrite(duplicate, newValue));
                    }
                }
            }

            int updatedCount = 0;
            if (pendingWrites.Count > 0)
            {
                using (var transaction = new Transaction(activeDocument, "Regel 0 - gi dupliserte Mark-verdier nye verdier"))
                {
                    TransactionStatus startStatus = transaction.Start();
                    if (startStatus != TransactionStatus.Started)
                    {
                        return string.Format(CultureInfo.InvariantCulture,
                            "Regel 0 v{0}: ingen Mark-verdier ble endret; transaksjonen startet ikke ({1}). Duplikatgrupper {2}; planlagte endringer {3}; skrivebeskyttede dubletter {4}.",
                            ScriptVersion, startStatus, duplicateGroupCount, pendingWrites.Count, readOnlyCount);
                    }

                    try
                    {
                        foreach (PendingWrite write in pendingWrites)
                        {
                            if (!write.Entry.Parameter.Set(write.NewValue))
                            {
                                throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture,
                                    "Parameter.Set returnerte false for ElementId {0}.", write.Entry.Owner.Id.Value));
                            }
                            updatedCount++;
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
                        return string.Format(CultureInfo.InvariantCulture,
                            "Regel 0 v{0}: transaksjonen feilet, og alle endringer ble rullet tilbake. Ingen Mark-verdier ble lagret. Feil: {1}",
                            ScriptVersion, exception.Message);
                    }
                }
            }

            return string.Format(CultureInfo.InvariantCulture,
                "Regel 0 v{0}: Mark-parametere kontrollert {1}; duplikatgrupper {2}; ekstra dublettinstanser {3}; oppdatert {4}; skrivebeskyttede dubletter {5}; manglende Mark-parameter {6}; feil lagringstype {7}.",
                ScriptVersion,
                entriesByCategory.Values.Sum(entries => entries.Count),
                duplicateGroupCount,
                duplicateInstanceCount,
                updatedCount,
                readOnlyCount,
                missingMarkCount,
                unsupportedStorageCount);
        }

        private static string CreateUniqueValue(string originalValue, HashSet<string> reservedValues)
        {
            int suffix = 1;
            string candidate;
            do
            {
                candidate = string.Format(CultureInfo.InvariantCulture, "{0}-DUP-{1:D3}", originalValue, suffix);
                suffix++;
            }
            while (reservedValues.Contains(candidate));
            return candidate;
        }

    }
}
