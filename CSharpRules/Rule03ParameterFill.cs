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
        private const string ScriptVersion = "0.0.11";
        private const string PackageParameter = "FOB_Leveransepakke";
        private const string MengdetypeParameter = "PGF_Mengdetype";
        private const string LogPath = @"D:\Revit\Python\Revit_BIM_Agent\logs\history\Rule03_ParameterFill.log";
        private const string ParameterWorkbookPath = @"D:\Revit\Python\Revit_BIM_Agent\config\Parameterliste.xlsx";
        private const string ParameterWorksheetName = "Parameterliste";
        private static readonly string[] RevisionParameterNames = { "FOB_Revisjonsdato", "PGF_Revisjonsign", "PGF_Revisjonsindeks" };
        private static readonly string[] TypeControlledParameterNames = { "FOB_Funksjonskode", "FOB_Merkesystem", "FOB_System" };
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
            internal string Reason { get; }

            internal PendingWrite(Element owner, Parameter parameter, string value, string reason)
            {
                Owner = owner;
                Parameter = parameter;
                Value = value;
                Reason = reason;
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
                log.Add("BLOKKERING: Kunne ikke lese parameterlisten fra Excel: " + exception.Message);
                return SaveAndReturn(log, "Regel 3 stoppet: parameterlisten kunne ikke leses.");
            }

            List<Element> instances = new FilteredElementCollector(activeDocument)
                .WhereElementIsNotElementType()
                .ToElements()
                .Where(element => element is not null && !IsCenterLine(element) && IsElectricalDisciplineElement(element))
                .OrderBy(element => element.Id.Value)
                .ToList();
            var writes = new List<PendingWrite>();
            var scheduled = new HashSet<string>(StringComparer.Ordinal);
            var absentCounts = parameterNames.ToDictionary(name => name, _ => 0, StringComparer.Ordinal);
            int readOnlyCount = 0;
            int unresolvedCount = 0;
            int preservedCount = 0;
            var processedTypes = new HashSet<long>();
            bool cableTrayRuleHandled = false;
            CableTrayMarkingEngine.Plan? cableTrayPlan = null;

            if (instances.Any(HasMissingCableTrayRuleValue))
            {
                cableTrayRuleHandled = true;
                try
                {
                    cableTrayPlan = CableTrayMarkingEngine.CreatePlan(activeDocument);
                    foreach (string line in cableTrayPlan.Log)
                    {
                        log.Add("KABELBROMERKING: " + line);
                        if (line.StartsWith("UAVKLART", StringComparison.Ordinal)
                            || line.StartsWith("BLOKKERING", StringComparison.Ordinal))
                        {
                            unresolvedCount++;
                        }
                    }
                    foreach (CableTrayMarkingEngine.PlannedWrite write in cableTrayPlan.Writes)
                    {
                        string key = write.Owner.Id.Value.ToString(CultureInfo.InvariantCulture) + "|" + write.Parameter.Definition.Name;
                        if (scheduled.Add(key))
                        {
                            writes.Add(new PendingWrite(write.Owner, write.Parameter, write.Value, write.Reason));
                        }
                    }
                    log.Add("KABELBROMERKING: C#-forløper planla " + cableTrayPlan.Writes.Count.ToString(CultureInfo.InvariantCulture)
                        + " endringer før generell Regel 3-utfylling.");
                }
                catch (Exception exception)
                {
                    unresolvedCount++;
                    log.Add("BLOKKERING: Kabelbroforløperen kunne ikke lage en trygg plan: " + exception);
                }
            }

            foreach (Element instance in instances)
            {
                Element? typeElement = GetTypeElement(activeDocument, instance);
                if (typeElement is not null && processedTypes.Add(typeElement.Id.Value))
                {
                    ProcessTypeParameters(instance, typeElement, parameterNames, writes, scheduled, absentCounts, log, ref readOnlyCount, ref unresolvedCount);
                }

                foreach (string name in parameterNames)
                {
                    if (cableTrayRuleHandled
                        && IsCableTrayMarkingElement(instance)
                        && (string.Equals(name, "FOB_Sekvensnummer", StringComparison.Ordinal)
                            || string.Equals(name, "FOB_Merkestreng", StringComparison.Ordinal)
                            || string.Equals(name, "FOB_Omraade", StringComparison.Ordinal)))
                    {
                        continue;
                    }

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
                    bool k5bStatus = string.Equals(name, "FOB_Status", StringComparison.Ordinal)
                        && HasExactEnterprise(instance, "K5B");
                    if (k5bStatus && parameter.StorageType != StorageType.String)
                    {
                        unresolvedCount++;
                        log.Add(FormatIssue(instance, name, "K5B-status må være en tekstparameter for å håndheve S4"));
                        continue;
                    }
                    if (k5bStatus && string.Equals(GetParameterText(parameter), "S5", StringComparison.Ordinal))
                    {
                        preservedCount++;
                        log.Add("BESKYTTET ElementId " + instance.Id.Value.ToString(CultureInfo.InvariantCulture)
                            + ": FOB_Status=S5 bevares som lås for kabelbromerking.");
                        continue;
                    }
                    bool correctK5bStatus = k5bStatus
                        && !string.Equals(GetParameterText(parameter), "S4", StringComparison.Ordinal);
                    if (!IsParameterEmpty(parameter) && !missingMengdelistepost && !correctK5bStatus)
                    {
                        preservedCount++;
                        continue;
                    }

                    string? value = ResolveValue(activeDocument, instance, typeElement, name, log, ref unresolvedCount);
                    if (value is null)
                    {
                        continue;
                    }
                    ScheduleWrite(instance, parameter, value,
                        correctK5bStatus ? "FOB_Status settes alltid til S4 for K5B" : "tom parameter",
                        writes, scheduled, log, ref readOnlyCount, ref unresolvedCount);
                }
            }

            int updated = 0;
            int failedWrites = 0;
            bool transactionCommitted = writes.Count == 0;
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
                                    log.Add(string.Format(CultureInfo.InvariantCulture, "OPPDATERT ElementId {0}, {1} = '{2}' ({3}).", write.Owner.Id.Value, write.Parameter.Definition.Name, EscapeLog(write.Value), write.Reason));
                                }
                                else
                                {
                                    failedWrites++;
                                    unresolvedCount++;
                                    log.Add(FormatIssue(write.Owner, write.Parameter.Definition.Name, "Parameter.SetValueString/Set returnerte false"));
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
                            log.Add("TRANSAKSJONSFEIL: " + exception);
                            string result = rollbackStatus == TransactionStatus.RolledBack
                                ? "Regel 3 ble rullet tilbake; ingen endringer ble lagret. " + exception.Message
                                : "Regel 3 feilet, men rollback er ikke bekreftet (" + rollbackStatus + "); kontroller modellstatus før retry. " + exception.Message;
                            return SaveAndReturn(log, result);
                        }
                    }
                    transactionCommitted = true;
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

            if (transactionCommitted && cableTrayPlan is { Triggered: true, RegistryRows.Count: > 0 })
            {
                try
                {
                    CableTrayMarkingEngine.PersistRegistryRows(cableTrayPlan.RegistryRows);
                    log.Add("KABELBROMERKING: statusregister oppdatert med " + cableTrayPlan.RegistryRows.Count.ToString(CultureInfo.InvariantCulture) + " nye/promoterte oppføringer.");
                }
                catch (Exception exception)
                {
                    unresolvedCount++;
                    log.Add("UAVKLART: statusregisteret kunne ikke oppdateres etter commit: " + exception.Message);
                }
            }

            foreach (KeyValuePair<string, int> absent in absentCounts.Where(pair => pair.Value > 0).OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                log.Add(string.Format(CultureInfo.InvariantCulture, "PARAMETER MANGLER: {0} instanser uten '{1}'.", absent.Value, absent.Key));
            }

            log.Add(string.Format(CultureInfo.InvariantCulture,
                "Oppsummering: instanser {0}; parameternavn {1}; oppdatert {2}; eksisterende verdier bevart {3}; skrivebeskyttet {4}; uavklart {5}; skrivefeil {6}.",
                instances.Count, parameterNames.Count, updated, preservedCount, readOnlyCount, unresolvedCount, failedWrites));
            return SaveAndReturn(log, string.Format(CultureInfo.InvariantCulture,
                "Regel 3 v{0}: oppdatert {1}; eksisterende verdier bevart {2}; blokkeringer {3}; logg: {4}.",
                ScriptVersion, updated, preservedCount, unresolvedCount + failedWrites, LogPath));
        }

        private static List<string> LoadParameterNames()
        {
            string path = ParameterWorkbookPath;
            if (!File.Exists(path))
            {
                throw new FileNotFoundException("Fant ikke den autoritative parameterlisten i Excel.", path);
            }

            Dictionary<string, string> entries = ReadZipEntries(path);
            if (!entries.TryGetValue("xl/workbook.xml", out string? workbookXml)
                || !entries.TryGetValue("xl/_rels/workbook.xml.rels", out string? relationshipsXml))
            {
                throw new InvalidDataException("Parameterarbeidsboken mangler workbook.xml eller workbook.xml.rels.");
            }

            List<string> sharedStrings = ReadSharedStrings(entries);
            object workbook = LoadXml(workbookXml);
            object relationships = LoadXml(relationshipsXml);
            const string mainNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            const string relationshipNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
            const string packageRelationshipNs = "http://schemas.openxmlformats.org/package/2006/relationships";
            var targets = GetDescendants(relationships, "Relationship", packageRelationshipNs)
                .ToDictionary(node => GetXmlAttribute(node, "Id"), node => GetXmlAttribute(node, "Target"), StringComparer.Ordinal);
            object? parameterSheet = GetDescendants(workbook, "sheet", mainNs)
                .FirstOrDefault(sheet => string.Equals(GetXmlAttribute(sheet, "name"), ParameterWorksheetName, StringComparison.Ordinal));
            if (parameterSheet is null)
            {
                throw new InvalidDataException("Fant ikke fanen '" + ParameterWorksheetName + "' i parameterarbeidsboken.");
            }

            string relationId = GetXmlAttribute(parameterSheet, "id", relationshipNs);
            if (!targets.TryGetValue(relationId, out string? target) || string.IsNullOrEmpty(target))
            {
                throw new InvalidDataException("Fant ingen worksheet-relasjon for parameterfanen.");
            }
            string worksheetPart = ResolvePartName("xl", target);
            if (!entries.TryGetValue(worksheetPart, out string? worksheetXml))
            {
                throw new InvalidDataException("Fant ikke regnearket for parameterfanen.");
            }

            List<object> rows = GetDescendants(LoadXml(worksheetXml), "row", mainNs).ToList();
            int parameterColumn = -1;
            int ruleColumn = -1;
            int referenceColumn = -1;
            int headerRowIndex = -1;
            for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
            {
                Dictionary<int, string> cells = ReadCells(rows[rowIndex], sharedStrings, mainNs);
                foreach (KeyValuePair<int, string> cell in cells)
                {
                    string header = cell.Value.Trim();
                    if (string.Equals(header, "Parameternavn", StringComparison.Ordinal)) parameterColumn = cell.Key;
                    if (string.Equals(header, "Fagregel / forutsetning", StringComparison.Ordinal)) ruleColumn = cell.Key;
                    if (string.Equals(header, "Regelreferanse", StringComparison.Ordinal)) referenceColumn = cell.Key;
                }
                if (parameterColumn >= 0 && ruleColumn >= 0 && referenceColumn >= 0)
                {
                    headerRowIndex = rowIndex;
                    break;
                }
            }
            if (headerRowIndex < 0)
            {
                throw new InvalidDataException("Fant ikke kolonneoverskriftene Parameternavn, Fagregel / forutsetning og Regelreferanse.");
            }

            var names = new List<string>();
            var uniqueNames = new HashSet<string>(StringComparer.Ordinal);
            for (int rowIndex = headerRowIndex + 1; rowIndex < rows.Count; rowIndex++)
            {
                Dictionary<int, string> cells = ReadCells(rows[rowIndex], sharedStrings, mainNs);
                if (!cells.Values.Any(value => !string.IsNullOrWhiteSpace(value)))
                {
                    continue;
                }
                string rowNumber = GetXmlAttribute(rows[rowIndex], "r");
                if (!cells.TryGetValue(parameterColumn, out string? rawName) || string.IsNullOrWhiteSpace(rawName))
                {
                    throw new InvalidDataException("Rad " + rowNumber + " har innhold, men mangler parameternavn.");
                }
                string name = rawName.Trim();
                if (!uniqueNames.Add(name))
                {
                    throw new InvalidDataException("Parameternavnet '" + name + "' forekommer flere ganger i arbeidsboken.");
                }
                names.Add(name);
            }
            if (names.Count == 0) throw new InvalidDataException("Fant ingen parameternavn i parameterarbeidsboken.");
            return names;
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
            if (!File.Exists(path)) throw new FileNotFoundException("Fant ikke arbeidsboken.", path);
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
            List<PendingWrite> writes, HashSet<string> scheduled, List<string> log, ref int readOnlyCount, ref int unresolvedCount)
        {
            if (parameter.IsReadOnly)
            {
                readOnlyCount++;
                unresolvedCount++;
                log.Add(FormatIssue(owner, parameter.Definition.Name, "parameteren er skrivebeskyttet"));
                return;
            }
            string key = owner.Id.Value.ToString(CultureInfo.InvariantCulture) + "|" + parameter.Definition.Name;
            if (scheduled.Add(key)) writes.Add(new PendingWrite(owner, parameter, value, reason));
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

        private static bool IsCableTrayMarkingElement(Element element)
        {
            return IsCategory(element, BuiltInCategory.OST_CableTray)
                || IsCategory(element, BuiltInCategory.OST_CableTrayFitting);
        }

        private static bool HasMissingCableTrayRuleValue(Element element)
        {
            if (!IsCableTrayMarkingElement(element)) return false;
            return new[] { "FOB_Sekvensnummer", "FOB_Merkestreng", "FOB_Omraade" }
                .Any(name => !TryGetSingleParameter(element, name, out Parameter? parameter, out string issue)
                    || parameter is null || issue == "mangler" || IsParameterEmpty(parameter)
                    || string.Equals(GetParameterText(parameter).Trim(), "--", StringComparison.Ordinal));
        }

        private static bool HasExactEnterprise(Element element, string expectedEnterprise)
        {
            return TryGetSingleParameter(element, "FOB_Entreprise", out Parameter? parameter, out _)
                && parameter is not null
                && parameter.StorageType == StorageType.String
                && string.Equals(parameter.AsString(), expectedEnterprise, StringComparison.Ordinal);
        }

        private static bool IsForingsveiFitting(Element element)
        {
            return IsCategory(element, BuiltInCategory.OST_ConduitFitting) || IsCategory(element, BuiltInCategory.OST_CableTrayFitting);
        }

        private static bool IsCategory(Element element, BuiltInCategory category)
        {
            return element.Category is not null && element.Category.Id.Value == (long)category;
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
            string reportResult;
            try
            {
                string reportDirectory = Path.Combine(Path.GetDirectoryName(LogPath)!, "..", "reports");
                Directory.CreateDirectory(reportDirectory);
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
                string reportPath = Path.Combine(reportDirectory, "Rule03_ParameterFill " + timestamp + ".txt");
                File.WriteAllLines(reportPath, lines, new UTF8Encoding(false));
                reportResult = "Rapport: " + reportPath;
            }
            catch (Exception exception) { reportResult = "Rapport kunne ikke skrives: " + exception.Message; }
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                File.AppendAllLines(LogPath, lines, new UTF8Encoding(false));
                return result + " " + reportResult;
            }
            catch (Exception exception)
            {
                return result + " " + reportResult + " Logg kunne ikke skrives: " + exception.Message;
            }
        }
    }
}