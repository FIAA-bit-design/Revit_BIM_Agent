#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;

namespace CW.Assistant.Generated
{
    internal sealed class GeneratedAction
    {
        private const string ScriptVersion = "0.0.9";
        private const string IgnoredFamilyName = "Beredskapspanel sikkerhetsventilasjon";
        private const string PlaceholderSequenceValue = "--";
        private const string ExtinguishingSystemFamilyPrefix = "Slokkeanlegg";
        private const string InertGasFamilyPrefix = "Inertgass";
        private const string GaseousExtinguishingFamilyToken = "Slokkegass";
        private const string SequenceSourceParameterName = "PGF_RIE_Sekvensnummer";
        private const string SequenceTargetParameterName = "FOB_Sekvensnummer";
        private const string LogPath = @"D:\Revit\Python\Revit_BIM_Agent\logs\history\Rule09_SyncAlarmSystemMark.log";
        private const string OutputDirectory = @"D:\Revit\Python\Revit_BIM_Agent\logs\reports";
        private static readonly string[] ExcelHeaders = { "ElementId", "PGF_Mengdetype", "FOB_Merkestreng", "Feil" };

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
            var errorsByElement = new Dictionary<long, List<string>>();
            var reportContextByElement = new Dictionary<long, string[]>();
            int missingSourceCount = 0;
            int missingTargetCount = 0;
            int ambiguousParameterCount = 0;
            int unsupportedStorageCount = 0;
            int mismatchCount = 0;
            int readOnlyCount = 0;
            int emptySequenceSourceCount = 0;

            foreach (Element element in elements)
            {
                string familyName = (element as FamilyInstance)?.Symbol?.Family?.Name ?? string.Empty;
                if (string.Equals(familyName, IgnoredFamilyName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                bool fixedPlaceholderSequence = IsPlaceholderSequenceFamily(familyName);
                bool fixedPhysicalMark = IsGaseousExtinguishingFamily(familyName);
                reportContextByElement[element.Id.Value] = new[]
                {
                    GetContextValue(element, "PGF_Mengdetype"),
                    GetContextValue(element, "FOB_Merkestreng")
                };

                if (fixedPlaceholderSequence)
                {
                    PlanFixedValueWrite(element, "FOB_Merkestreng", PlaceholderSequenceValue, false, writes, errorsByElement, ref missingSourceCount, ref missingTargetCount, ref ambiguousParameterCount, ref unsupportedStorageCount, ref readOnlyCount);
                    PlanFixedValueWrite(element, SequenceSourceParameterName, PlaceholderSequenceValue, true, writes, errorsByElement, ref missingSourceCount, ref missingTargetCount, ref ambiguousParameterCount, ref unsupportedStorageCount, ref readOnlyCount);
                    PlanFixedValueWrite(element, SequenceTargetParameterName, PlaceholderSequenceValue, false, writes, errorsByElement, ref missingSourceCount, ref missingTargetCount, ref ambiguousParameterCount, ref unsupportedStorageCount, ref readOnlyCount);
                }
                if (fixedPhysicalMark)
                {
                    PlanFixedValueWrite(element, "FOB_FysiskMerke", PlaceholderSequenceValue, true, writes, errorsByElement, ref missingSourceCount, ref missingTargetCount, ref ambiguousParameterCount, ref unsupportedStorageCount, ref readOnlyCount);
                }

                foreach (ParameterPair pair in ParameterPairs)
                {
                    if (fixedPlaceholderSequence && pair.SourceName == SequenceSourceParameterName)
                    {
                        continue;
                    }
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
                        AddError(errorsByElement, element.Id.Value, pair.SourceName + ": " + sourceIssue);
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
                        AddError(errorsByElement, element.Id.Value, pair.TargetName + ": " + targetIssue);
                    }
                    bool emptySequenceSource = hasSource
                        && source is not null
                        && pair.SourceName == SequenceSourceParameterName
                        && source.StorageType == StorageType.String
                        && IsEmptySequence(source.AsString());
                    if (emptySequenceSource)
                    {
                        emptySequenceSourceCount++;
                        AddError(errorsByElement, element.Id.Value, SequenceSourceParameterName + ": tom eller ugyldig verdi ('--'); " + SequenceTargetParameterName + " ble ikke endret");
                    }
                    if (!hasSource || !hasTarget || source is null || target is null)
                    {
                        continue;
                    }

                    if (source.StorageType != StorageType.String || target.StorageType != StorageType.String)
                    {
                        unsupportedStorageCount++;
                        AddError(errorsByElement, element.Id.Value, pair.TargetName + ": kilde og mål må ha lagringstypen String");
                        continue;
                    }
                    if (emptySequenceSource)
                    {
                        continue;
                    }

                    string sourceValue = fixedPhysicalMark && pair.SourceName == "FOB_FysiskMerke"
                        ? PlaceholderSequenceValue
                        : source.AsString() ?? string.Empty;
                    string targetValue = target.AsString() ?? string.Empty;
                    if (string.Equals(sourceValue, targetValue, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    mismatchCount++;
                    if (target.IsReadOnly)
                    {
                        readOnlyCount++;
                        AddError(errorsByElement, element.Id.Value, pair.TargetName + ": verdien avviker, men målparameteren er skrivebeskyttet");
                        continue;
                    }

                    writes.Add(new PendingWrite(element, target, sourceValue, pair.SourceName, pair.TargetName));
                }
            }

            int successfulUpdateCount = 0;
            string? transactionFailure = null;
            if (writes.Count > 0)
            {
                var worksetDialogHandler = new WorksetCheckoutDialogHandler(log.Add);
                uiApplication.DialogBoxShowing += worksetDialogHandler.HandleDialogBoxShowing;
                try
                {
                    using (var transaction = new Transaction(activeDocument, "Synkroniser brannalarmparametere"))
                    {
                        try
                        {
                            TransactionStatus startStatus = transaction.Start();
                            if (startStatus != TransactionStatus.Started)
                            {
                                transactionFailure = "Transaksjonen startet ikke (" + startStatus + ")";
                            }
                            else
                            {
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
                                    if (commitStatus == TransactionStatus.Committed)
                                    {
                                        successfulUpdateCount = writes.Count;
                                    }
                                    else
                                    {
                                        transactionFailure = "Transaksjonen ble ikke committed (" + commitStatus + "); " + TryRollback(transaction);
                                    }
                                }
                                catch (Autodesk.Revit.Exceptions.RegenerationFailedException exception)
                                {
                                    transactionFailure = "Fatal regenereringsfeil: " + exception.Message + "; " + TryRollback(transaction);
                                }
                                catch (Exception exception)
                                {
                                    transactionFailure = exception.Message + "; " + TryRollback(transaction);
                                }
                            }
                        }
                        catch (Exception exception)
                        {
                            transactionFailure = "Transaksjonen kunne ikke startes: " + exception.Message + "; " + TryRollback(transaction);
                        }
                    }
                }
                finally { uiApplication.DialogBoxShowing -= worksetDialogHandler.HandleDialogBoxShowing; }

                if (transactionFailure is not null)
                {
                    log.Add("TRANSAKSJONSFEIL: " + transactionFailure);
                    foreach (PendingWrite write in writes)
                    {
                        AddError(errorsByElement, write.Element.Id.Value, write.TargetName + ": synkronisering ikke bekreftet; " + transactionFailure);
                    }
                }
            }

            var reportRows = new List<string[]>();
            foreach (KeyValuePair<long, List<string>> error in errorsByElement.OrderBy(item => item.Key))
            {
                reportContextByElement.TryGetValue(error.Key, out string[]? context);
                reportRows.Add(new[]
                {
                    error.Key.ToString(CultureInfo.InvariantCulture),
                    context is not null && context.Length > 0 ? context[0] : string.Empty,
                    context is not null && context.Length > 1 ? context[1] : string.Empty,
                    string.Join("; ", error.Value)
                });
            }

            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
            string reportPath = Path.Combine(OutputDirectory, "Regel 9 avvik " + timestamp + ".xlsx");
            string summary = string.Format(
                CultureInfo.InvariantCulture,
                "Regel 9 v{0}: Fire Alarm Devices kontrollert {1}; feilende elementer {2}; synkroniseringer fullført {3}; kildeparametere mangler {4}; målparametere mangler {5}; tvetydige parametere {6}; feil lagringstype {7}; skrivebeskyttede avvik {8}; tomme sekvenskilder {9}.",
                ScriptVersion,
                elements.Count,
                reportRows.Count,
                successfulUpdateCount,
                missingSourceCount,
                missingTargetCount,
                ambiguousParameterCount,
                unsupportedStorageCount,
                readOnlyCount,
                emptySequenceSourceCount);

            try
            {
                Directory.CreateDirectory(OutputDirectory);
                WriteExcelReport(reportPath, reportRows);
            }
            catch (Exception exception)
            {
                log.Insert(1, summary);
                log.Add("RAPPORTFEIL: " + exception.Message);
                return SaveAndReturn(log, "Regel 9 v" + ScriptVersion + ": Excel-rapport kunne ikke opprettes. " + exception.Message);
            }

            log.Insert(1, summary);
            log.Add("Excel-rapport: " + reportPath);
            return SaveAndReturn(log, summary + " Excel-rapport: " + reportPath);
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

        private static bool IsPlaceholderSequenceFamily(string familyName)
        {
            return familyName.StartsWith(ExtinguishingSystemFamilyPrefix, StringComparison.OrdinalIgnoreCase)
                || familyName.StartsWith(InertGasFamilyPrefix, StringComparison.OrdinalIgnoreCase)
                || IsGaseousExtinguishingFamily(familyName);
        }

        private static bool IsGaseousExtinguishingFamily(string familyName)
        {
            return familyName.IndexOf(GaseousExtinguishingFamilyToken, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void PlanFixedValueWrite(
            Element element,
            string parameterName,
            string requiredValue,
            bool isSourceParameter,
            List<PendingWrite> writes,
            Dictionary<long, List<string>> errorsByElement,
            ref int missingSourceCount,
            ref int missingTargetCount,
            ref int ambiguousParameterCount,
            ref int unsupportedStorageCount,
            ref int readOnlyCount)
        {
            bool found = TryGetSingleParameter(element, parameterName, out Parameter? parameter, out string issue);
            if (!found || parameter is null)
            {
                if (issue == "parameter mangler")
                {
                    if (isSourceParameter) missingSourceCount++;
                    else missingTargetCount++;
                }
                else
                {
                    ambiguousParameterCount++;
                }
                AddError(errorsByElement, element.Id.Value, parameterName + ": " + issue);
                return;
            }
            if (parameter.StorageType != StorageType.String)
            {
                unsupportedStorageCount++;
                AddError(errorsByElement, element.Id.Value, parameterName + ": parameteren må ha lagringstypen String");
                return;
            }
            if (string.Equals(parameter.AsString() ?? string.Empty, requiredValue, StringComparison.Ordinal))
            {
                return;
            }
            if (parameter.IsReadOnly)
            {
                readOnlyCount++;
                AddError(errorsByElement, element.Id.Value, parameterName + ": verdien avviker, men parameteren er skrivebeskyttet");
                return;
            }
            writes.Add(new PendingWrite(element, parameter, requiredValue, "familiekrav", parameterName));
        }

        private static bool IsEmptySequence(string? value)
        {
            return string.IsNullOrWhiteSpace(value)
                || string.Equals(value.Trim(), "--", StringComparison.Ordinal);
        }

        private static void AddError(Dictionary<long, List<string>> errorsByElement, long elementId, string message)
        {
            if (!errorsByElement.TryGetValue(elementId, out List<string>? errors))
            {
                errors = new List<string>();
                errorsByElement.Add(elementId, errors);
            }
            if (!errors.Any(error => string.Equals(error, message, StringComparison.Ordinal)))
            {
                errors.Add(message);
            }
        }

        private static string GetContextValue(Element element, string parameterName)
        {
            IList<Parameter> parameters = element.GetParameters(parameterName);
            if (parameters.Count != 1 || !parameters[0].HasValue)
            {
                return string.Empty;
            }
            Parameter parameter = parameters[0];
            return (parameter.StorageType == StorageType.String ? parameter.AsString() : parameter.AsValueString()) ?? string.Empty;
        }

        private static string TryRollback(Transaction transaction)
        {
            try
            {
                TransactionStatus status = transaction.GetStatus();
                if (status != TransactionStatus.Started)
                {
                    return "rollback ikke utført; transaksjonsstatus " + status;
                }
                TransactionStatus rollbackStatus = transaction.RollBack();
                return rollbackStatus == TransactionStatus.RolledBack
                    ? "rollback bekreftet"
                    : "rollback ikke bekreftet; status " + rollbackStatus;
            }
            catch (Exception exception)
            {
                return "rollback feilet: " + exception.Message;
            }
        }

        private static void WriteExcelReport(string path, List<string[]> rows)
        {
            var sheet = new StringBuilder();
            sheet.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>")
                .Append("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">")
                .Append("<cols><col min=\"1\" max=\"1\" width=\"16\" customWidth=\"1\"/>")
                .Append("<col min=\"2\" max=\"2\" width=\"25\" customWidth=\"1\"/>")
                .Append("<col min=\"3\" max=\"3\" width=\"40\" customWidth=\"1\"/>")
                .Append("<col min=\"4\" max=\"4\" width=\"65\" customWidth=\"1\"/></cols><sheetData>");
            var allRows = new List<string[]> { ExcelHeaders };
            allRows.AddRange(rows);
            for (int rowIndex = 0; rowIndex < allRows.Count; rowIndex++)
            {
                int rowNumber = rowIndex + 1;
                sheet.Append("<row r=\"").Append(rowNumber.ToString(CultureInfo.InvariantCulture)).Append("\">");
                for (int columnIndex = 0; columnIndex < ExcelHeaders.Length; columnIndex++)
                {
                    string cellReference = ((char)('A' + columnIndex)).ToString() + rowNumber.ToString(CultureInfo.InvariantCulture);
                    string value = columnIndex < allRows[rowIndex].Length ? allRows[rowIndex][columnIndex] ?? string.Empty : string.Empty;
                    sheet.Append("<c r=\"").Append(cellReference).Append("\" t=\"inlineStr\"><is><t xml:space=\"preserve\">")
                        .Append(XmlEscape(RemoveInvalidXmlCharacters(value)))
                        .Append("</t></is></c>");
                }
                sheet.Append("</row>");
            }
            sheet.Append("</sheetData><autoFilter ref=\"A1:D")
                .Append(allRows.Count.ToString(CultureInfo.InvariantCulture))
                .Append("\"/></worksheet>");

            WriteZipPackage(path, new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["[Content_Types].xml"] = "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/><Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/></Types>",
                ["_rels/.rels"] = "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>",
                ["xl/workbook.xml"] = "<?xml version=\"1.0\" encoding=\"UTF-8\"?><workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets><sheet name=\"Avvik\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>",
                ["xl/_rels/workbook.xml.rels"] = "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/></Relationships>",
                ["xl/worksheets/sheet1.xml"] = sheet.ToString()
            });
        }

        private static void WriteZipPackage(string path, Dictionary<string, string> entries)
        {
            Assembly compression = Assembly.Load("System.IO.Compression");
            Type archiveType = compression.GetType("System.IO.Compression.ZipArchive", true)!;
            Type modeType = compression.GetType("System.IO.Compression.ZipArchiveMode", true)!;
            ConstructorInfo constructor = archiveType.GetConstructor(new[] { typeof(Stream), modeType, typeof(bool) })
                ?? throw new InvalidOperationException("Fant ikke ZipArchive-konstruktøren.");
            MethodInfo createEntry = archiveType.GetMethod("CreateEntry", new[] { typeof(string) })
                ?? throw new InvalidOperationException("Fant ikke ZipArchive.CreateEntry.");
            using var fileStream = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
            object archive = constructor.Invoke(new[] { fileStream, Enum.Parse(modeType, "Create"), (object)true });
            try
            {
                foreach (KeyValuePair<string, string> entry in entries)
                {
                    object zipEntry = createEntry.Invoke(archive, new object[] { entry.Key })!;
                    MethodInfo open = zipEntry.GetType().GetMethod("Open", Type.EmptyTypes)
                        ?? throw new InvalidOperationException("Fant ikke ZipArchiveEntry.Open.");
                    using var entryStream = (Stream)open.Invoke(zipEntry, null)!;
                    using var writer = new StreamWriter(entryStream, new UTF8Encoding(false));
                    writer.Write(entry.Value);
                }
            }
            finally
            {
                ((IDisposable)archive).Dispose();
            }
        }

        private static string RemoveInvalidXmlCharacters(string value)
        {
            var filtered = new StringBuilder(value.Length);
            for (int index = 0; index < value.Length; index++)
            {
                char character = value[index];
                if (char.IsHighSurrogate(character) && index + 1 < value.Length && char.IsLowSurrogate(value[index + 1]))
                {
                    filtered.Append(character).Append(value[++index]);
                }
                else if (!char.IsSurrogate(character)
                    && (character == '\t' || character == '\n' || character == '\r'
                        || (character >= 0x20 && character <= 0xD7FF)
                        || (character >= 0xE000 && character <= 0xFFFD)))
                {
                    filtered.Append(character);
                }
            }
            return filtered.ToString();
        }

        private static string XmlEscape(string value)
        {
            return value.Replace("&", "&amp;", StringComparison.Ordinal)
                .Replace("<", "&lt;", StringComparison.Ordinal)
                .Replace(">", "&gt;", StringComparison.Ordinal)
                .Replace("\"", "&quot;", StringComparison.Ordinal)
                .Replace("'", "&apos;", StringComparison.Ordinal);
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