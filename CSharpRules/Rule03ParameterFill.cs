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
        private const string ScriptVersion = "0.0.5";
        private const string PackageParameter = "FOB_Leveransepakke";
        private const string MengdetypeParameter = "PGF_Mengdetype";
        private const string RevisionWorkbookPath = @"D:\Revit\Python\Revit_BIM_Agent\config\Revisjonsliste.xlsx";
        private const string LogPath = @"D:\Revit\Python\Revit_BIM_Agent\logs\history\Rule03_ParameterFill.log";
        private const string ParameterListPath = @"D:\Revit\Python\Revit_BIM_Agent\rie-bim-agent\parameter-rules\parameter-list.md";
        private static readonly string[] RevisionParameterNames = { "FOB_Revisjonsdato", "PGF_Revisjonsign", "PGF_Revisjonsindeks" };
        private static readonly string[] TypeControlledParameterNames = { "FOB_Funksjonskode", "FOB_Merkesystem", "FOB_System" };

        private sealed class PendingWrite
        {
            internal Element Owner { get; }
            internal Parameter Parameter { get; }
            internal string Value { get; }
            internal string Reason { get; }
            internal bool IsRevisionCheck { get; }

            internal PendingWrite(Element owner, Parameter parameter, string value, string reason, bool isRevisionCheck)
            {
                Owner = owner;
                Parameter = parameter;
                Value = value;
                Reason = reason;
                IsRevisionCheck = isRevisionCheck;
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

        public string Execute(UIApplication uiApplication, Document? activeDocument)
        {
            if (activeDocument is null)
            {
                return "FEIL: Ingen aktiv Revit-modell. Regel 3 stoppet før elementlesing.";
            }

            var log = new List<string>
            {
                string.Format(CultureInfo.InvariantCulture, "=== Regel 3 v{0} | {1:O} | {2} ===", ScriptVersion, DateTime.Now, activeDocument.Title),
                "Dokument: " + activeDocument.Title
            };
            List<string> parameterNames;
            try
            {
                parameterNames = LoadParameterNames();
            }
            catch (Exception exception)
            {
                log.Add("BLOKKERING: Kunne ikke lese parametertabellen: " + exception.Message);
                return SaveAndReturn(log, "Regel 3 stoppet: parameterlisten kunne ikke leses.");
            }

            Dictionary<string, Dictionary<string, string>> revisions;
            try
            {
                revisions = ReadRevisionWorkbook(RevisionWorkbookPath);
                log.Add(string.Format(CultureInfo.InvariantCulture, "Revisjonsliste lest: {0} faner.", revisions.Count));
            }
            catch (Exception exception)
            {
                revisions = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
                log.Add("BLOKKERING: Revisjonslisten kunne ikke leses; revisjonsfeltene blir ikke endret: " + exception.Message);
            }

            List<Element> instances = new FilteredElementCollector(activeDocument)
                .WhereElementIsNotElementType()
                .ToElements()
                .Where(element => element is not null)
                .OrderBy(element => element.Id.Value)
                .ToList();
            var writes = new List<PendingWrite>();
            var scheduled = new HashSet<string>(StringComparer.Ordinal);
            var absentCounts = parameterNames.ToDictionary(name => name, _ => 0, StringComparer.Ordinal);
            int readOnlyCount = 0;
            int unresolvedCount = 0;
            int preservedCount = 0;
            int revisionChecked = 0;
            int revisionBlocked = 0;
            var processedTypes = new HashSet<long>();

            foreach (Element instance in instances)
            {
                Element? typeElement = GetTypeElement(activeDocument, instance);
                if (typeElement is not null && processedTypes.Add(typeElement.Id.Value))
                {
                    ProcessTypeParameters(instance, typeElement, parameterNames, writes, scheduled, absentCounts, log, ref readOnlyCount, ref unresolvedCount);
                }

                ProcessRevisionParameters(instance, parameterNames, revisions, writes, scheduled, log, ref revisionChecked, ref revisionBlocked, ref readOnlyCount, ref unresolvedCount);

                foreach (string name in parameterNames)
                {
                    if (TypeControlledParameterNames.Contains(name, StringComparer.Ordinal)
                        || RevisionParameterNames.Contains(name, StringComparer.Ordinal)
                        || string.Equals(name, PackageParameter, StringComparison.Ordinal)
                        || string.Equals(name, "PGF_RIE_Alarmsystemer", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (!TryGetSingleParameter(instance, name, out Parameter? parameter, out string issue))
                    {
                        if (parameter is null && issue == "mangler")
                        {
                            absentCounts[name]++;
                        }
                        else
                        {
                            unresolvedCount++;
                            log.Add(FormatIssue(instance, name, issue));
                        }
                        continue;
                    }

                    if (parameter is null)
                    {
                        continue;
                    }
                    bool missingMengdelistepost = string.Equals(name, "FOB_Mengdelistepost", StringComparison.Ordinal)
                        && string.Equals(GetParameterText(parameter).Trim(), "--", StringComparison.Ordinal);
                    if (!IsParameterEmpty(parameter) && !missingMengdelistepost)
                    {
                        preservedCount++;
                        continue;
                    }

                    string? value = ResolveValue(activeDocument, instance, typeElement, name, log, ref unresolvedCount);
                    if (value is null)
                    {
                        continue;
                    }
                    ScheduleWrite(instance, parameter, value, "tom parameter", writes, scheduled, log, ref readOnlyCount, ref unresolvedCount);
                }
            }

            int updated = 0;
            int failedWrites = 0;
            int successfulRevisionWrites = 0;
            int failedRevisionWrites = 0;
            int scheduledRevisionWrites = writes.Count(write => write.IsRevisionCheck);
            if (writes.Count > 0)
            {
                var worksetDialogHandler = new WorksetCheckoutDialogHandler(log);
                uiApplication.DialogBoxShowing += worksetDialogHandler.HandleDialogBoxShowing;
                try
                {
                    using (var transaction = new Transaction(activeDocument, "Regel 3 - fyll og kontroller parametere"))
                    {
                        TransactionStatus startStatus = transaction.Start();
                        if (startStatus != TransactionStatus.Started)
                        {
                            revisionBlocked += scheduledRevisionWrites;
                            unresolvedCount += scheduledRevisionWrites;
                            log.Add("TRANSAKSJONSFEIL: transaksjonen startet ikke (" + startStatus + ").");
                            return SaveAndReturn(log, "Regel 3: ingen verdier ble skrevet fordi transaksjonen ikke startet.");
                        }

                        try
                        {
                            foreach (PendingWrite write in writes)
                            {
                                if (TrySetParameter(write.Parameter, write.Value))
                                {
                                    updated++;
                                    if (write.IsRevisionCheck) successfulRevisionWrites++;
                                    log.Add(string.Format(CultureInfo.InvariantCulture, "OPPDATERT ElementId {0}, {1} = '{2}' ({3}).", write.Owner.Id.Value, write.Parameter.Definition.Name, EscapeLog(write.Value), write.Reason));
                                }
                                else
                                {
                                    failedWrites++;
                                    if (write.IsRevisionCheck) failedRevisionWrites++;
                                    else unresolvedCount++;
                                    log.Add(FormatIssue(write.Owner, write.Parameter.Definition.Name, "Parameter.SetValueString/Set returnerte false"));
                                }
                            }

                            TransactionStatus commitStatus = transaction.Commit();
                            if (commitStatus != TransactionStatus.Committed)
                            {
                                throw new InvalidOperationException("Transaksjonen ble ikke committed: " + commitStatus);
                            }

                            revisionChecked += successfulRevisionWrites;
                            revisionBlocked += failedRevisionWrites;
                            unresolvedCount += failedRevisionWrites;
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
                            revisionBlocked += scheduledRevisionWrites;
                            unresolvedCount += scheduledRevisionWrites;
                            log.Add("TRANSAKSJONSFEIL: " + exception);
                            string result = rollbackStatus == TransactionStatus.RolledBack
                                ? "Regel 3 ble rullet tilbake; ingen endringer ble lagret. " + exception.Message
                                : "Regel 3 feilet, men rollback er ikke bekreftet (" + rollbackStatus + "); kontroller modellstatus før retry. " + exception.Message;
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
                    log.Add(string.Format(CultureInfo.InvariantCulture, "WORKSHARING: automatisk håndterte Check Out Worksets-dialoger {0}.", worksetDialogHandler.HandledCount));
                }
            }

            foreach (KeyValuePair<string, int> absent in absentCounts.Where(pair => pair.Value > 0).OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                log.Add(string.Format(CultureInfo.InvariantCulture, "PARAMETER MANGLER: {0} instanser uten '{1}'.", absent.Value, absent.Key));
            }

            log.Add(string.Format(CultureInfo.InvariantCulture,
                "Oppsummering: instanser {0}; parameternavn {1}; oppdatert {2}; eksisterende verdier bevart {3}; revisjonsfelt kontrollert {4}; revisjonspakker blokkert {5}; skrivebeskyttet {6}; uavklart {7}; skrivefeil {8}.",
                instances.Count, parameterNames.Count, updated, preservedCount, revisionChecked, revisionBlocked, readOnlyCount, unresolvedCount, failedWrites));
            return SaveAndReturn(log, string.Format(CultureInfo.InvariantCulture,
                "Regel 3 v{0}: oppdatert {1}; eksisterende verdier bevart {2}; revisjonsfelt kontrollert {3}; blokkeringer {4}; logg: {5}.",
                ScriptVersion, updated, preservedCount, revisionChecked, revisionBlocked + unresolvedCount + failedWrites, LogPath));
        }

        private static List<string> LoadParameterNames()
        {
            string path = ParameterListPath;
            if (!File.Exists(path))
            {
                throw new FileNotFoundException("Fant ikke parameterlisten.", path);
            }

            var names = new List<string>();
            bool inTable = false;
            foreach (string line in File.ReadAllLines(path, Encoding.UTF8))
            {
                string trimmed = line.Trim();
                if (!inTable)
                {
                    if (trimmed.Equals("# Parameterliste", StringComparison.Ordinal)
                        || trimmed.StartsWith("## Regel 3:", StringComparison.Ordinal)) inTable = true;
                    continue;
                }
                if (trimmed.StartsWith("## Generelle regler", StringComparison.Ordinal)
                    || trimmed.StartsWith("## Regel ", StringComparison.Ordinal)) break;
                if (!trimmed.StartsWith("|", StringComparison.Ordinal)) continue;
                int first = trimmed.IndexOf('`');
                int last = first < 0 ? -1 : trimmed.IndexOf('`', first + 1);
                if (first >= 0 && last > first + 1)
                {
                    string name = trimmed.Substring(first + 1, last - first - 1);
                    if (!names.Contains(name, StringComparer.Ordinal)) names.Add(name);
                }
            }
            if (names.Count == 0) throw new InvalidDataException("Fant ingen parameternavn i tabellen under regel 3.");
            return names;
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
                string value = cells.TryGetValue(valueColumn, out string? cellValue) ? cellValue.Trim() : string.Empty;
                occurrences[parameterName.Trim()] = occurrences.TryGetValue(parameterName.Trim(), out int count) ? count + 1 : 1;
                approved[parameterName.Trim()] = NormalizeApprovedValue(parameterName.Trim(), value);
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
                string reference = GetXmlAttribute(cell, "r");
                int column = ColumnIndex(reference);
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

        private static void ProcessTypeParameters(Element instance, Element typeElement, List<string> parameterNames,
            List<PendingWrite> writes, HashSet<string> scheduled, Dictionary<string, int> absentCounts, List<string> log,
            ref int readOnlyCount, ref int unresolvedCount)
        {
            foreach (string name in TypeControlledParameterNames)
            {
                if (!parameterNames.Contains(name, StringComparer.Ordinal)) continue;
                if (!TryGetSingleParameter(typeElement, name, out Parameter? parameter, out string issue))
                {
                    if (parameter is null && issue == "mangler") absentCounts[name]++;
                    else { unresolvedCount++; log.Add(FormatIssue(typeElement, name, issue)); }
                    continue;
                }
                if (parameter is null || !IsParameterEmpty(parameter)) continue;
                string value = ResolveTypeValue(instance, name);
                ScheduleWrite(typeElement, parameter, value, "typeparameterregel", writes, scheduled, log, ref readOnlyCount, ref unresolvedCount);
            }
        }

        private static string ResolveTypeValue(Element instance, string name)
        {
            bool cableTray = IsCategory(instance, BuiltInCategory.OST_CableTray) || IsCategory(instance, BuiltInCategory.OST_CableTrayFitting);
            bool conduit = IsCategory(instance, BuiltInCategory.OST_Conduit) || IsCategory(instance, BuiltInCategory.OST_ConduitFitting);
            if (name == "FOB_Funksjonskode")
            {
                if (cableTray) return "KAF";
                if (conduit) return "TRR";
            }
            if (name == "FOB_Merkesystem" && (cableTray || conduit)) return "SPV";
            if (name == "FOB_System" && (cableTray || conduit)) return "460";
            return "--";
        }

        private static void ProcessRevisionParameters(Element instance, List<string> parameterNames,
            Dictionary<string, Dictionary<string, string>> revisions, List<PendingWrite> writes, HashSet<string> scheduled,
            List<string> log, ref int revisionChecked, ref int revisionBlocked, ref int readOnlyCount, ref int unresolvedCount)
        {
            if (!parameterNames.Contains(PackageParameter, StringComparer.Ordinal)) return;
            if (!TryGetSingleParameter(instance, PackageParameter, out Parameter? packageParameter, out string packageIssue)
                || packageParameter is null)
            {
                return;
            }
            string package = GetParameterText(packageParameter);
            if (string.IsNullOrWhiteSpace(package) || package == "--") return;
            if (!revisions.TryGetValue(package, out Dictionary<string, string>? approved)) return;

            foreach (string name in RevisionParameterNames)
            {
                if (!parameterNames.Contains(name, StringComparer.Ordinal)) continue;
                if (!TryGetSingleParameter(instance, name, out Parameter? parameter, out string issue) || parameter is null)
                {
                    revisionBlocked++;
                    unresolvedCount++;
                    log.Add(FormatIssue(instance, name, issue));
                    continue;
                }
                string approvedValue = approved[name];
                string currentValue = GetParameterText(parameter);
                if (string.Equals(currentValue, approvedValue, StringComparison.Ordinal))
                {
                    revisionChecked++;
                    continue;
                }
                if (parameter.IsReadOnly)
                {
                    revisionBlocked++;
                    readOnlyCount++;
                    unresolvedCount++;
                    log.Add(FormatIssue(instance, name, "avviker fra godkjent verdi, men parameteren er skrivebeskyttet"));
                    continue;
                }
                ScheduleWrite(instance, parameter, approvedValue, "godkjent revisjon for pakke '" + EscapeLog(package) + "'", writes, scheduled, log, ref readOnlyCount, ref unresolvedCount, isRevisionCheck: true);
            }
        }

        private static string? ResolveValue(Document document, Element instance, Element? typeElement, string name, List<string> log, ref int unresolvedCount)
        {
            switch (name)
            {
                case "FOB_Eksistensstatus": return "Ny";
                case "FOB_Entreprise": return "K5B";
                case "FOB_Status": return "S4";
                case "PGF_Statussign": return DateTime.Today.ToString("yyyy.MM.dd", CultureInfo.InvariantCulture);
                case "FOB_Mengdeenhet": return IsStraightForingsvei(instance) ? "m" : "stk";
                case "FOB_Mengde": return IsStraightForingsvei(instance) ? null : "1";
                case "FOB_Mengdelistepost":
                    if (!IsForingsveiFitting(instance) || !IsBendFitting(instance, typeElement)) return "--";
                    if (TryGetBendMengdelistepost(document, instance, out string post, out string sourceInfo)) return post;
                    unresolvedCount++;
                    log.Add(FormatIssue(instance, name, sourceInfo));
                    return null;
                case MengdetypeParameter:
                    if (TryGetMengdetype(document, instance, typeElement, out string? text, out string reason)) return text;
                    unresolvedCount++;
                    log.Add(FormatIssue(instance, name, reason));
                    return null;
                default: return "--";
            }
        }

        private static bool IsBendFitting(Element instance, Element? typeElement)
        {
            Parameter? partTypeParameter = (instance as FamilyInstance)?.Symbol.Family.get_Parameter(BuiltInParameter.FAMILY_CONTENT_PART_TYPE)
                ?? typeElement?.get_Parameter(BuiltInParameter.FAMILY_CONTENT_PART_TYPE)
                ?? instance.get_Parameter(BuiltInParameter.FAMILY_CONTENT_PART_TYPE);
            if (partTypeParameter is null || !partTypeParameter.HasValue || partTypeParameter.StorageType != StorageType.Integer)
            {
                return false;
            }

            string? partType = Enum.GetName(typeof(PartType), partTypeParameter.AsInteger());
            string[] bendPartTypes =
            {
                "Elbow", "ChannelCableTrayElbow", "ChannelCableTrayVerticalElbow",
                "LadderCableTrayElbow", "LadderCableTrayVerticalElbow"
            };
            return partType is not null && bendPartTypes.Contains(partType, StringComparer.Ordinal);
        }

        private static bool TryGetBendMengdelistepost(Document document, Element fitting, out string value, out string sourceInfo)
        {
            value = string.Empty;
            sourceInfo = "fant ingen direkte tilkoblet rett føringsvei med gyldig FOB_Mengdelistepost";
            if (fitting is not FamilyInstance familyInstance || familyInstance.MEPModel?.ConnectorManager is null)
            {
                sourceInfo = "fitting mangler tilgjengelige MEP-koblinger";
                return false;
            }
            if (!HasK5BEnterprise(fitting, allowBlank: true))
            {
                sourceInfo = "målentreprise er utenfor K5B eller kan ikke avklares";
                return false;
            }

            var sources = new Dictionary<long, Element>();
            foreach (Connector connector in familyInstance.MEPModel.ConnectorManager.Connectors)
            {
                if (connector.ConnectorType != ConnectorType.End) continue;
                foreach (Connector connected in connector.AllRefs)
                {
                    Element owner = connected.Owner;
                    if (owner.Id.Value == fitting.Id.Value || connected.ConnectorType != ConnectorType.End
                        || !connector.IsConnectedTo(connected) || !IsStraightForingsvei(owner)
                        || !IsMatchingForingsveiCategory(fitting, owner)
                        || !HasK5BEnterprise(owner, allowBlank: false)
                        || owner.Location is not LocationCurve locationCurve || locationCurve.Curve is not Line)
                    {
                        continue;
                    }
                    sources[owner.Id.Value] = owner;
                }
            }

            var values = new Dictionary<string, List<long>>(StringComparer.Ordinal);
            foreach (Element source in sources.Values)
            {
                if (!TryGetSingleParameter(source, "FOB_Mengdelistepost", out Parameter? parameter, out _)
                    || parameter is null || parameter.StorageType != StorageType.String)
                {
                    continue;
                }
                string sourceValue = GetParameterText(parameter).Trim();
                if (string.IsNullOrWhiteSpace(sourceValue) || string.Equals(sourceValue, "--", StringComparison.Ordinal)) continue;
                if (!values.TryGetValue(sourceValue, out List<long>? sourceIds))
                {
                    sourceIds = new List<long>();
                    values.Add(sourceValue, sourceIds);
                }
                sourceIds.Add(source.Id.Value);
            }

            if (values.Count != 1)
            {
                sourceInfo = values.Count == 0
                    ? "ingen direkte tilkoblet rett føringsvei har en gyldig FOB_Mengdelistepost"
                    : "tilkoblede rettføringer har ulike FOB_Mengdelistepost-verdier: "
                        + string.Join("; ", values.Select(pair => pair.Key + " fra ElementId " + string.Join(",", pair.Value)));
                return false;
            }

            KeyValuePair<string, List<long>> match = values.Single();
            value = match.Key;
            sourceInfo = "direkte tilkoblet rettstrekk ElementId " + string.Join(",", match.Value);
            return true;
        }

        private static bool IsMatchingForingsveiCategory(Element fitting, Element straight)
        {
            return (IsCategory(fitting, BuiltInCategory.OST_CableTrayFitting) && IsCategory(straight, BuiltInCategory.OST_CableTray))
                || (IsCategory(fitting, BuiltInCategory.OST_ConduitFitting) && IsCategory(straight, BuiltInCategory.OST_Conduit));
        }

        private static bool HasK5BEnterprise(Element element, bool allowBlank)
        {
            if (!TryGetSingleParameter(element, "FOB_Entreprise", out Parameter? parameter, out _)
                || parameter is null || parameter.StorageType != StorageType.String)
            {
                return false;
            }
            string value = parameter.AsString() ?? string.Empty;
            return string.Equals(value, "K5B", StringComparison.Ordinal)
                || (allowBlank && string.IsNullOrWhiteSpace(value));
        }

        private static bool TryGetMengdetype(Document document, Element instance, Element? typeElement, out string? value, out string reason)
        {
            value = null;
            reason = "Description mangler eller er tom";
            if (IsStraightForingsvei(instance))
            {
                if (!TryGetDescription(typeElement, out string description)) return false;
                Parameter? sizeParameter = GetFirstParameter(instance, "Size") ?? GetFirstParameter(typeElement, "Size");
                string size = sizeParameter is null ? string.Empty : GetParameterText(sizeParameter).Trim();
                if (string.IsNullOrWhiteSpace(size))
                {
                    reason = "Size mangler for rett føringsvei";
                    return false;
                }
                value = description + " " + size;
                return true;
            }

            if (IsForingsveiFitting(instance))
            {
                if (instance is not FamilyInstance familyInstance || familyInstance.MEPModel?.ConnectorManager is null)
                {
                    reason = "fitting mangler tilgjengelige MEP-koblinger";
                    return false;
                }
                var descriptions = new HashSet<string>(StringComparer.Ordinal);
                foreach (Connector connector in familyInstance.MEPModel.ConnectorManager.Connectors)
                {
                    foreach (Connector connected in connector.AllRefs)
                    {
                        Element owner = connected.Owner;
                        if (owner.Id.Value == instance.Id.Value || !IsStraightForingsvei(owner)) continue;
                        Element? ownerType = GetTypeElement(document, owner);
                        if (TryGetDescription(ownerType, out string description))
                        {
                            Parameter? sizeParameter = GetFirstParameter(owner, "Size") ?? GetFirstParameter(ownerType, "Size");
                            string size = sizeParameter is null ? string.Empty : GetParameterText(sizeParameter).Trim();
                            if (!string.IsNullOrWhiteSpace(size)) descriptions.Add(description + " " + size);
                        }
                    }
                }
                if (descriptions.Count == 1)
                {
                    value = descriptions.Single();
                    return true;
                }
                reason = descriptions.Count == 0 ? "fant ingen tilkoblet rett føringsvei med Description og Size" : "tilkoblede føringsveier har ulike Description/Size-verdier";
                return false;
            }

            if (TryGetDescription(typeElement, out string typeDescription))
            {
                value = typeDescription;
                return true;
            }
            return false;
        }

        private static bool TryGetDescription(Element? typeElement, out string description)
        {
            description = string.Empty;
            Parameter? parameter = GetFirstParameter(typeElement, "Description");
            if (parameter is null) return false;
            description = GetParameterText(parameter).Trim();
            return !string.IsNullOrWhiteSpace(description);
        }

        private static void ScheduleWrite(Element owner, Parameter parameter, string value, string reason,
            List<PendingWrite> writes, HashSet<string> scheduled, List<string> log, ref int readOnlyCount, ref int unresolvedCount,
            bool isRevisionCheck = false)
        {
            if (parameter.IsReadOnly)
            {
                readOnlyCount++;
                unresolvedCount++;
                log.Add(FormatIssue(owner, parameter.Definition.Name, "parameteren er skrivebeskyttet"));
                return;
            }
            string key = owner.Id.Value.ToString(CultureInfo.InvariantCulture) + "|" + parameter.Definition.Name;
            if (scheduled.Add(key)) writes.Add(new PendingWrite(owner, parameter, value, reason, isRevisionCheck));
        }

        private static bool TrySetParameter(Parameter parameter, string value)
        {
            if (parameter.StorageType == StorageType.String) return parameter.Set(value);
            return parameter.SetValueString(value);
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

        private static Parameter? GetFirstParameter(Element? element, string name)
        {
            return element?.GetParameters(name).Cast<Parameter>().FirstOrDefault();
        }

        private static Element? GetTypeElement(Document document, Element instance)
        {
            ElementId typeId = instance.GetTypeId();
            return typeId.Value < 0 ? null : document.GetElement(typeId);
        }

        private static bool IsParameterEmpty(Parameter parameter)
        {
            if (!parameter.HasValue) return true;
            if (parameter.StorageType == StorageType.String) return string.IsNullOrWhiteSpace(parameter.AsString());
            return false;
        }

        private static string GetParameterText(Parameter parameter)
        {
            if (!parameter.HasValue) return string.Empty;
            return parameter.StorageType == StorageType.String
                ? parameter.AsString() ?? string.Empty
                : parameter.AsValueString() ?? string.Empty;
        }

        private static bool IsStraightForingsvei(Element element)
        {
            return IsCategory(element, BuiltInCategory.OST_Conduit) || IsCategory(element, BuiltInCategory.OST_CableTray);
        }

        private static bool IsForingsveiFitting(Element element)
        {
            return IsCategory(element, BuiltInCategory.OST_ConduitFitting) || IsCategory(element, BuiltInCategory.OST_CableTrayFitting);
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