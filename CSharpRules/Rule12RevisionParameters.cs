#nullable enable
using System;
using System.Collections;
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
        private const string ScriptVersion = "0.0.1";
        private const string PackageParameter = "FOB_Leveransepakke";
        private const string RevisionWorkbookPath = @"D:\Revit\Python\Revit_BIM_Agent\config\Revisjonsliste.xlsx";
        private const string LogPath = @"D:\Revit\Python\Revit_BIM_Agent\logs\history\Rule12_RevisionParameters.log";
        private static readonly string[] RevisionParameterNames = { "FOB_Revisjonsdato", "PGF_Revisjonsign", "PGF_Revisjonsindeks" };
        private static readonly HashSet<long> CenterLineCategoryIds = new HashSet<long>(
            Enum.GetValues(typeof(BuiltInCategory))
                .Cast<BuiltInCategory>()
                .Where(category => category.ToString().EndsWith("CenterLine", StringComparison.Ordinal))
                .Select(category => new ElementId(category).Value));
        private static readonly HashSet<long> ElectricalCategoryIds = new HashSet<long>(
            new[]
            {
                BuiltInCategory.OST_CableTray,
                BuiltInCategory.OST_CableTrayFitting,
                BuiltInCategory.OST_Conduit,
                BuiltInCategory.OST_ConduitFitting,
                BuiltInCategory.OST_DataDevices,
                BuiltInCategory.OST_FireAlarmDevices,
                BuiltInCategory.OST_ElectricalEquipment,
                BuiltInCategory.OST_ElectricalFixtures,
                BuiltInCategory.OST_LightingFixtures,
                BuiltInCategory.OST_LightingDevices,
                BuiltInCategory.OST_CommunicationDevices,
                BuiltInCategory.OST_SecurityDevices
            }
            .Select(category => new ElementId(category).Value));

        private sealed class PendingWrite
        {
            internal Element Owner { get; }
            internal Parameter Parameter { get; }
            internal string Value { get; }
            internal string Package { get; }

            internal PendingWrite(Element owner, Parameter parameter, string value, string package)
            {
                Owner = owner;
                Parameter = parameter;
                Value = value;
                Package = package;
            }
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
                if (eventArgs is not TaskDialogShowingEventArgs taskDialog) return;

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

        public string Execute(UIApplication uiApplication, Document? activeDocument)
        {
            if (activeDocument is null)
            {
                return "FEIL: Ingen aktiv Revit-modell. Regel 12 stoppet før elementlesing.";
            }

            var log = new List<string>
            {
                string.Format(CultureInfo.InvariantCulture, "=== Regel 12 v{0} | {1:O} | {2} ===", ScriptVersion, DateTime.Now, activeDocument.Title),
                "Dokument: " + activeDocument.Title
            };

            Dictionary<string, Dictionary<string, string>> revisions;
            try
            {
                revisions = ReadRevisionWorkbook(RevisionWorkbookPath);
                log.Add(string.Format(CultureInfo.InvariantCulture, "Revisjonsliste lest: {0} faner.", revisions.Count));
            }
            catch (Exception exception)
            {
                log.Add("BLOKKERING: Revisjonslisten kunne ikke leses; ingen revisjonsfelt blir endret: " + exception.Message);
                return SaveAndReturn(log, "Regel 12 stoppet: revisjonslisten kunne ikke leses.");
            }

            List<Element> instances = new FilteredElementCollector(activeDocument)
                .WhereElementIsNotElementType()
                .ToElements()
                .Where(element => element is not null && !IsCenterLine(element) && IsElectricalDisciplineElement(element))
                .OrderBy(element => element.Id.Value)
                .ToList();
            var writes = new List<PendingWrite>();
            var scheduled = new HashSet<string>(StringComparer.Ordinal);
            int matchedCount = 0;
            int blockedCount = 0;
            int readOnlyCount = 0;
            int unresolvedCount = 0;
            int updatedCount = 0;
            int failedWrites = 0;

            foreach (Element instance in instances)
            {
                if (!TryGetSingleParameter(instance, PackageParameter, out Parameter? packageParameter, out _) || packageParameter is null)
                {
                    continue;
                }

                string package = GetParameterText(packageParameter);
                if (string.IsNullOrWhiteSpace(package) || package == "--") continue;
                if (!revisions.TryGetValue(package, out Dictionary<string, string>? approved)) continue;

                foreach (string name in RevisionParameterNames)
                {
                    if (!TryGetSingleParameter(instance, name, out Parameter? parameter, out string issue) || parameter is null)
                    {
                        blockedCount++;
                        unresolvedCount++;
                        log.Add(FormatIssue(instance, name, issue));
                        continue;
                    }

                    string approvedValue = approved[name];
                    string currentValue = GetParameterText(parameter);
                    if (string.Equals(currentValue, approvedValue, StringComparison.Ordinal))
                    {
                        matchedCount++;
                        continue;
                    }
                    if (parameter.IsReadOnly)
                    {
                        blockedCount++;
                        readOnlyCount++;
                        unresolvedCount++;
                        log.Add(FormatIssue(instance, name, "avviker fra godkjent verdi, men parameteren er skrivebeskyttet"));
                        continue;
                    }

                    string key = instance.Id.Value.ToString(CultureInfo.InvariantCulture) + "|" + name;
                    if (scheduled.Add(key)) writes.Add(new PendingWrite(instance, parameter, approvedValue, package));
                }
            }

            if (writes.Count > 0)
            {
                var worksetDialogHandler = new WorksetCheckoutDialogHandler(log);
                uiApplication.DialogBoxShowing += worksetDialogHandler.HandleDialogBoxShowing;
                try
                {
                    using (var transaction = new Transaction(activeDocument, "Regel 12 - kontroller revisjonsparametere"))
                    {
                        TransactionStatus startStatus = transaction.Start();
                        if (startStatus != TransactionStatus.Started)
                        {
                            blockedCount += writes.Count;
                            unresolvedCount += writes.Count;
                            log.Add("TRANSAKSJONSFEIL: transaksjonen startet ikke (" + startStatus + ").");
                            return SaveAndReturn(log, "Regel 12: ingen revisjonsverdier ble skrevet fordi transaksjonen ikke startet.");
                        }

                        try
                        {
                            int successfulWrites = 0;
                            foreach (PendingWrite write in writes)
                            {
                                if (TrySetParameter(write.Parameter, write.Value))
                                {
                                    successfulWrites++;
                                    log.Add(string.Format(CultureInfo.InvariantCulture,
                                        "OPPDATERT ElementId {0}, pakke '{1}', {2} = '{3}' (godkjent revisjonsverdi).",
                                        write.Owner.Id.Value, EscapeLog(write.Package), write.Parameter.Definition.Name, EscapeLog(write.Value)));
                                }
                                else
                                {
                                    failedWrites++;
                                    log.Add(FormatIssue(write.Owner, write.Parameter.Definition.Name, "Parameter.SetValueString/Set returnerte false"));
                                }
                            }

                            TransactionStatus commitStatus = transaction.Commit();
                            if (commitStatus != TransactionStatus.Committed)
                            {
                                throw new InvalidOperationException("Transaksjonen ble ikke committed: " + commitStatus);
                            }
                            updatedCount = successfulWrites;
                            blockedCount += failedWrites;
                            unresolvedCount += failedWrites;
                        }
                        catch (Exception exception)
                        {
                            TransactionStatus rollbackStatus = transaction.GetStatus();
                            if (rollbackStatus == TransactionStatus.Started)
                            {
                                try { rollbackStatus = transaction.RollBack(); }
                                catch (Exception rollbackException)
                                {
                                    log.Add("ROLLBACKFEIL: " + rollbackException);
                                    rollbackStatus = transaction.GetStatus();
                                }
                            }
                            blockedCount += writes.Count;
                            unresolvedCount += writes.Count;
                            log.Add("TRANSAKSJONSFEIL: " + exception);
                            string result = rollbackStatus == TransactionStatus.RolledBack
                                ? "Regel 12 ble rullet tilbake; ingen endringer ble lagret. " + exception.Message
                                : "Regel 12 feilet, men rollback er ikke bekreftet (" + rollbackStatus + "); kontroller modellstatus før retry. " + exception.Message;
                            return SaveAndReturn(log, result);
                        }
                    }
                }
                finally
                {
                    uiApplication.DialogBoxShowing -= worksetDialogHandler.HandleDialogBoxShowing;
                }

                if (worksetDialogHandler.HandledCount > 0)
                {
                    log.Add(string.Format(CultureInfo.InvariantCulture,
                        "WORKSHARING: automatisk håndterte Check Out Worksets-dialoger {0}.", worksetDialogHandler.HandledCount));
                }
            }

            log.Add(string.Format(CultureInfo.InvariantCulture,
                "Oppsummering: elektriske instanser {0}; revisjonsverdier samsvarte {1}; oppdatert {2}; blokkeringer {3}; skrivebeskyttet {4}; uavklart {5}; skrivefeil {6}.",
                instances.Count, matchedCount, updatedCount, blockedCount, readOnlyCount, unresolvedCount, failedWrites));
            return SaveAndReturn(log, string.Format(CultureInfo.InvariantCulture,
                "Regel 12 v{0}: revisjonsverdier samsvarte {1}; oppdatert {2}; blokkeringer {3}; logg: {4}.",
                ScriptVersion, matchedCount + updatedCount, updatedCount, blockedCount, LogPath));
        }

        private static Dictionary<string, Dictionary<string, string>> ReadRevisionWorkbook(string path)
        {
            Dictionary<string, string> entries = ReadZipEntries(path);
            if (!entries.TryGetValue("xl/workbook.xml", out string? workbookXml)
                || !entries.TryGetValue("xl/_rels/workbook.xml.rels", out string? relationshipsXml))
            {
                throw new InvalidDataException("Arbeidsboken mangler workbook.xml eller workbook.xml.rels.");
            }

            List<string> sharedStrings = ReadSharedStrings(entries);
            object workbook = LoadXml(workbookXml);
            object relationships = LoadXml(relationshipsXml);
            const string mainNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            const string relationshipNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
            const string packageRelationshipNs = "http://schemas.openxmlformats.org/package/2006/relationships";
            var targets = GetDescendants(relationships, "Relationship", packageRelationshipNs)
                .ToDictionary(node => GetXmlAttribute(node, "Id"), node => GetXmlAttribute(node, "Target"), StringComparer.Ordinal);
            var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);

            foreach (object sheet in GetDescendants(workbook, "sheet", mainNs))
            {
                string sheetName = GetXmlAttribute(sheet, "name");
                string relationId = GetXmlAttribute(sheet, "id", relationshipNs);
                if (!targets.TryGetValue(relationId, out string? target) || string.IsNullOrEmpty(target))
                {
                    throw new InvalidDataException("Fant ingen worksheet-relasjon for fanen '" + sheetName + "'.");
                }
                string partName = ResolvePartName("xl", target);
                if (!entries.TryGetValue(partName, out string? sheetXml))
                {
                    throw new InvalidDataException("Fant ikke worksheet-delen for fanen '" + sheetName + "'.");
                }
                result.Add(sheetName, ReadRevisionSheet(sheetXml, sharedStrings));
            }
            if (result.Count == 0)
            {
                throw new InvalidDataException("Revisjonslisten inneholder ingen leveransepakker.");
            }
            return result;
        }

        private static Dictionary<string, string> ReadRevisionSheet(string xml, List<string> sharedStrings)
        {
            object document = LoadXml(xml);
            const string ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            List<object> rows = GetDescendants(document, "row", ns).ToList();
            int parameterColumn = -1;
            int valueColumn = -1;
            int headerRowIndex = -1;
            for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
            {
                Dictionary<int, string> cells = ReadCells(rows[rowIndex], sharedStrings, ns);
                foreach (KeyValuePair<int, string> cell in cells)
                {
                    string header = cell.Value.Trim();
                    if (string.Equals(header, "Parameternavn", StringComparison.Ordinal)) parameterColumn = cell.Key;
                    if (string.Equals(header, "Verdi", StringComparison.Ordinal)) valueColumn = cell.Key;
                }
                if (parameterColumn >= 0 && valueColumn >= 0)
                {
                    headerRowIndex = rowIndex;
                    break;
                }
            }
            if (headerRowIndex < 0) throw new InvalidDataException("Fant ikke kolonneoverskriftene Parameternavn og Verdi.");

            var approved = new Dictionary<string, string>(StringComparer.Ordinal);
            var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int rowIndex = headerRowIndex + 1; rowIndex < rows.Count; rowIndex++)
            {
                Dictionary<int, string> cells = ReadCells(rows[rowIndex], sharedStrings, ns);
                if (!cells.TryGetValue(parameterColumn, out string? parameterName)) continue;
                if (!RevisionParameterNames.Contains(parameterName.Trim(), StringComparer.Ordinal)) continue;
                string name = parameterName.Trim();
                string value = cells.TryGetValue(valueColumn, out string? cellValue) ? cellValue.Trim() : string.Empty;
                occurrences[name] = occurrences.TryGetValue(name, out int count) ? count + 1 : 1;
                approved[name] = NormalizeApprovedValue(name, value);
            }
            foreach (string requiredName in RevisionParameterNames)
            {
                if (!occurrences.TryGetValue(requiredName, out int count) || count != 1)
                {
                    throw new InvalidDataException("Parameternavnet '" + requiredName + "' mangler eller forekommer flere ganger.");
                }
                if (string.IsNullOrWhiteSpace(approved[requiredName]))
                {
                    throw new InvalidDataException("Godkjent verdi for '" + requiredName + "' er tom.");
                }
            }
            return approved;
        }

        private static Dictionary<int, string> ReadCells(object row, List<string> sharedStrings, string ns)
        {
            var cells = new Dictionary<int, string>();
            foreach (object cell in GetChildElements(row, "c", ns))
            {
                int column = ColumnIndex(GetXmlAttribute(cell, "r"));
                if (column < 0) continue;
                string type = GetXmlAttribute(cell, "t");
                string value;
                if (type == "inlineStr")
                {
                    value = string.Concat(GetDescendants(cell, "t", ns).Select(GetXmlInnerText));
                }
                else
                {
                    object? valueNode = GetChildElement(cell, "v", ns);
                    value = valueNode is null ? string.Empty : GetXmlInnerText(valueNode);
                    if (type == "s" && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int sharedIndex)
                        && sharedIndex >= 0 && sharedIndex < sharedStrings.Count)
                    {
                        value = sharedStrings[sharedIndex];
                    }
                }
                cells[column] = value;
            }
            return cells;
        }

        private static List<string> ReadSharedStrings(Dictionary<string, string> entries)
        {
            if (!entries.TryGetValue("xl/sharedStrings.xml", out string? xml)) return new List<string>();
            const string ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            object document = LoadXml(xml);
            return GetDescendants(document, "si", ns)
                .Select(item => string.Concat(GetDescendants(item, "t", ns).Select(GetXmlInnerText)))
                .ToList();
        }

        private static Dictionary<string, string> ReadZipEntries(string path)
        {
            if (!File.Exists(path)) throw new FileNotFoundException("Fant ikke revisjonslisten.", path);
            Assembly compression = Assembly.Load("System.IO.Compression");
            Type archiveType = compression.GetType("System.IO.Compression.ZipArchive", true)!;
            Type modeType = compression.GetType("System.IO.Compression.ZipArchiveMode", true)!;
            ConstructorInfo constructor = archiveType.GetConstructor(new[] { typeof(Stream), modeType, typeof(bool) })
                ?? throw new InvalidOperationException("Fant ikke ZipArchive-konstruktøren.");
            PropertyInfo entriesProperty = archiveType.GetProperty("Entries")
                ?? throw new InvalidOperationException("Fant ikke ZipArchive.Entries.");
            using var fileStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            object archive = constructor.Invoke(new[] { fileStream, Enum.Parse(modeType, "Read"), (object)false });
            var entries = new Dictionary<string, string>(StringComparer.Ordinal);
            try
            {
                foreach (object entry in (IEnumerable)entriesProperty.GetValue(archive)!)
                {
                    string name = (string)(entry.GetType().GetProperty("FullName")?.GetValue(entry) ?? string.Empty);
                    MethodInfo open = entry.GetType().GetMethod("Open", Type.EmptyTypes)
                        ?? throw new InvalidOperationException("Fant ikke ZipArchiveEntry.Open.");
                    using var stream = (Stream)open.Invoke(entry, null)!;
                    using var reader = new StreamReader(stream, Encoding.UTF8, true);
                    entries[name] = reader.ReadToEnd();
                }
            }
            finally
            {
                ((IDisposable)archive).Dispose();
            }
            return entries;
        }

        private static object LoadXml(string xml)
        {
            Assembly xmlAssembly = Assembly.Load("System.Xml.ReaderWriter");
            Type documentType = xmlAssembly.GetType("System.Xml.XmlDocument", true)!;
            object document = Activator.CreateInstance(documentType)!;
            documentType.GetProperty("XmlResolver")?.SetValue(document, null);
            MethodInfo loadXml = documentType.GetMethod("LoadXml", new[] { typeof(string) })
                ?? throw new InvalidOperationException("Fant ikke XmlDocument.LoadXml.");
            loadXml.Invoke(document, new object[] { xml });
            return document;
        }

        private static IEnumerable<object> GetXmlChildNodes(object node)
        {
            object? childNodes = node.GetType().GetProperty("ChildNodes")?.GetValue(node);
            if (childNodes is not IEnumerable children) yield break;
            foreach (object? child in children)
            {
                if (child is not null) yield return child;
            }
        }

        private static IEnumerable<object> GetDescendants(object node, string localName, string namespaceUri)
        {
            foreach (object child in GetXmlChildNodes(node))
            {
                if (IsXmlElement(child, localName, namespaceUri)) yield return child;
                foreach (object descendant in GetDescendants(child, localName, namespaceUri)) yield return descendant;
            }
        }

        private static IEnumerable<object> GetChildElements(object node, string localName, string namespaceUri)
        {
            return GetXmlChildNodes(node).Where(child => IsXmlElement(child, localName, namespaceUri));
        }

        private static object? GetChildElement(object node, string localName, string namespaceUri)
        {
            return GetChildElements(node, localName, namespaceUri).FirstOrDefault();
        }

        private static bool IsXmlElement(object node, string localName, string namespaceUri)
        {
            Type nodeType = node.GetType();
            string nodeKind = nodeType.GetProperty("NodeType")?.GetValue(node)?.ToString() ?? string.Empty;
            string nodeName = nodeType.GetProperty("LocalName")?.GetValue(node) as string ?? string.Empty;
            string nodeNamespace = nodeType.GetProperty("NamespaceURI")?.GetValue(node) as string ?? string.Empty;
            return string.Equals(nodeKind, "Element", StringComparison.Ordinal)
                && string.Equals(nodeName, localName, StringComparison.Ordinal)
                && string.Equals(nodeNamespace, namespaceUri, StringComparison.Ordinal);
        }

        private static string GetXmlAttribute(object element, string localName, string namespaceUri = "")
        {
            MethodInfo getAttribute = element.GetType().GetMethod("GetAttribute", new[] { typeof(string), typeof(string) })
                ?? throw new InvalidOperationException("Fant ikke XmlElement.GetAttribute.");
            return getAttribute.Invoke(element, new object[] { localName, namespaceUri }) as string ?? string.Empty;
        }

        private static string GetXmlInnerText(object node)
        {
            return node.GetType().GetProperty("InnerText")?.GetValue(node) as string ?? string.Empty;
        }

        private static string ResolvePartName(string basePath, string target)
        {
            var parts = new List<string>();
            string combined = target.StartsWith("/", StringComparison.Ordinal) ? target.Substring(1) : basePath + "/" + target;
            foreach (string part in combined.Replace('\\', '/').Split('/'))
            {
                if (part.Length == 0 || part == ".") continue;
                if (part == "..")
                {
                    if (parts.Count > 0) parts.RemoveAt(parts.Count - 1);
                }
                else parts.Add(part);
            }
            return string.Join("/", parts);
        }

        private static int ColumnIndex(string cellReference)
        {
            int value = 0;
            int letters = 0;
            foreach (char character in cellReference)
            {
                if (!char.IsLetter(character)) break;
                value = value * 26 + char.ToUpperInvariant(character) - 'A' + 1;
                letters++;
            }
            return letters == 0 ? -1 : value - 1;
        }

        private static string NormalizeApprovedValue(string parameterName, string value)
        {
            if (parameterName == "FOB_Revisjonsdato"
                && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double serial)
                && serial > 0 && serial < 2958466)
            {
                try { return DateTime.FromOADate(serial).ToString("yyyy.MM.dd", CultureInfo.InvariantCulture); }
                catch { return value; }
            }
            return value;
        }

        private static bool TryGetSingleParameter(Element element, string name, out Parameter? parameter, out string issue)
        {
            IList<Parameter> parameters = element.GetParameters(name);
            if (parameters.Count == 0)
            {
                parameter = null;
                issue = "mangler";
                return false;
            }
            if (parameters.Count > 1)
            {
                parameter = null;
                issue = "flere parametere med samme navn";
                return false;
            }
            parameter = parameters[0];
            issue = string.Empty;
            return true;
        }

        private static string GetParameterText(Parameter parameter)
        {
            if (!parameter.HasValue) return string.Empty;
            return parameter.StorageType == StorageType.String
                ? parameter.AsString() ?? string.Empty
                : parameter.AsValueString() ?? string.Empty;
        }

        private static bool TrySetParameter(Parameter parameter, string value)
        {
            return parameter.StorageType == StorageType.String ? parameter.Set(value) : parameter.SetValueString(value);
        }

        private static bool IsCenterLine(Element element)
        {
            long categoryId = element.Category?.Id.Value ?? long.MinValue;
            return CenterLineCategoryIds.Contains(categoryId);
        }

        private static bool IsElectricalDisciplineElement(Element element)
        {
            long categoryId = element.Category?.Id.Value ?? long.MinValue;
            return ElectricalCategoryIds.Contains(categoryId);
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