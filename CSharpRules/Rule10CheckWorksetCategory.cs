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
        private const string ScriptVersion = "0.0.4";
        private const string WorkbookPath = @"D:\Revit\Python\Revit_BIM_Agent\config\Workset liste.xlsx";
        private const string OutputDirectory = @"D:\Revit\Python\Revit_BIM_Agent\logs\csv";
        private const string LogPath = @"D:\Revit\Python\Revit_BIM_Agent\logs\history\Rule10_WorksetCategory.log";
        private const string MainNamespace = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        private const string RelationshipNamespace = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        private const string PackageRelationshipNamespace = "http://schemas.openxmlformats.org/package/2006/relationships";

        private sealed class ExcelMapping
        {
            internal string CategoryLabel { get; }
            internal string ExpectedWorkset { get; }
            internal string Source { get; }

            internal ExcelMapping(string categoryLabel, string expectedWorkset, string source)
            {
                CategoryLabel = categoryLabel;
                ExpectedWorkset = expectedWorkset;
                Source = source;
            }
        }

        private sealed class ExpectedCategory
        {
            internal BuiltInCategory Category { get; }
            internal string CategoryLabel { get; }
            internal string ExpectedWorkset { get; }
            internal string Source { get; }

            internal ExpectedCategory(BuiltInCategory category, ExcelMapping mapping)
            {
                Category = category;
                CategoryLabel = mapping.CategoryLabel;
                ExpectedWorkset = mapping.ExpectedWorkset;
                Source = mapping.Source;
            }
        }

        private sealed class PendingChange
        {
            internal Element Element { get; }
            internal Parameter Parameter { get; }
            internal ExpectedCategory Category { get; }
            internal Workset TargetWorkset { get; }
            internal string CurrentWorkset { get; }

            internal PendingChange(Element element, Parameter parameter, ExpectedCategory category, Workset targetWorkset, string currentWorkset)
            {
                Element = element;
                Parameter = parameter;
                Category = category;
                TargetWorkset = targetWorkset;
                CurrentWorkset = currentWorkset;
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

        private static readonly Dictionary<string, BuiltInCategory[]> CategoryDefinitions = new Dictionary<string, BuiltInCategory[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["Cable tray with fittings"] = new[] { BuiltInCategory.OST_CableTray, BuiltInCategory.OST_CableTrayFitting },
            ["Conduits with fittings"] = new[] { BuiltInCategory.OST_Conduit, BuiltInCategory.OST_ConduitFitting },
            ["Data devices"] = new[] { BuiltInCategory.OST_DataDevices },
            ["Fire alarm devices"] = new[] { BuiltInCategory.OST_FireAlarmDevices },
            ["Electrical Equipment"] = new[] { BuiltInCategory.OST_ElectricalEquipment },
            ["Electrical Fixtures"] = new[] { BuiltInCategory.OST_ElectricalFixtures },
            ["Lightning Fixtures"] = new[] { BuiltInCategory.OST_LightingFixtures },
            ["Lightning Devices"] = new[] { BuiltInCategory.OST_LightingDevices },
            ["Spaces"] = new[] { BuiltInCategory.OST_MEPSpaces },
            ["Specialty Equipment"] = new[] { BuiltInCategory.OST_SpecialityEquipment },
            ["Communication Devices"] = new[] { BuiltInCategory.OST_CommunicationDevices },
            ["Security Devices"] = new[] { BuiltInCategory.OST_SecurityDevices }
        };

        public string Execute(UIApplication uiApplication, Document? activeDocument)
        {
            if (activeDocument is null)
            {
                return "FEIL: Ingen aktiv Revit-modell. Regel 10 stoppet før elementlesing.";
            }
            if (!activeDocument.IsWorkshared)
            {
                return "BLOKKERT: Aktiv modell er ikke workshared; workset-kontroll kan ikke utføres.";
            }

            var log = new List<string>
            {
                string.Format(CultureInfo.InvariantCulture, "=== Regel 10 v{0} | {1:O} | {2} ===", ScriptVersion, DateTime.Now, activeDocument.Title),
                "Elementer flyttes til forventet workset fra Excel når målarbeidssettet finnes i modellen.",
                "Arbeidsbok: " + WorkbookPath
            };
            List<ExcelMapping> mappings;
            try
            {
                mappings = ReadWorkbook(WorkbookPath);
            }
            catch (Exception exception)
            {
                log.Add("BLOKKERING: Excel-listen kunne ikke leses: " + exception.Message);
                return SaveAndReturn(log, "Regel 10 stoppet: Excel-listen kunne ikke leses.");
            }

            var blockers = new List<string>();
            var validationRows = new List<string[]>();
            var expected = new Dictionary<long, ExpectedCategory>();
            var conflictingCategories = new HashSet<long>();
            foreach (ExcelMapping mapping in mappings)
            {
                if (!CategoryDefinitions.TryGetValue(mapping.CategoryLabel, out BuiltInCategory[]? categories))
                {
                    string message = "Ukjent kategorietikett i " + mapping.Source + ": " + mapping.CategoryLabel;
                    blockers.Add(message);
                    validationRows.Add(ReportRow("Ukjent kategorietikett", "", "", mapping.CategoryLabel, mapping.ExpectedWorkset, "", message));
                    continue;
                }

                foreach (BuiltInCategory category in categories)
                {
                    long categoryId = new ElementId(category).Value;
                    if (conflictingCategories.Contains(categoryId)) continue;
                    if (expected.TryGetValue(categoryId, out ExpectedCategory? prior))
                    {
                        if (!string.Equals(prior.ExpectedWorkset, mapping.ExpectedWorkset, StringComparison.OrdinalIgnoreCase))
                        {
                            string message = "Motstridende workset-mapping for " + category + " i " + prior.Source + " og " + mapping.Source + ".";
                            blockers.Add(message);
                            validationRows.Add(ReportRow("Motstridende Excel-mapping", "", category.ToString(), mapping.CategoryLabel, mapping.ExpectedWorkset, prior.ExpectedWorkset, message));
                            expected.Remove(categoryId);
                            conflictingCategories.Add(categoryId);
                        }
                    }
                    else
                    {
                        expected.Add(categoryId, new ExpectedCategory(category, mapping));
                    }
                }
            }

            if (expected.Count == 0)
            {
                blockers.Add("Ingen gyldige kategorier kunne kobles fra Excel-listen.");
            }

            Dictionary<string, Workset> modelWorksets = new FilteredWorksetCollector(activeDocument)
                .OfKind(WorksetKind.UserWorkset)
                .ToWorksets()
                .ToDictionary(workset => workset.Name, workset => workset, StringComparer.OrdinalIgnoreCase);
            var targetWorksets = new Dictionary<long, Workset>();
            var missingWorksetCategories = new HashSet<long>();
            foreach (KeyValuePair<long, ExpectedCategory> pair in expected)
            {
                if (!modelWorksets.TryGetValue(pair.Value.ExpectedWorkset, out Workset? targetWorkset))
                {
                    string message = "Workset finnes ikke i modellen: " + pair.Value.ExpectedWorkset + " (" + pair.Value.CategoryLabel + ").";
                    blockers.Add(message);
                    validationRows.Add(ReportRow("Forventet workset mangler", "", pair.Value.Category.ToString(), pair.Value.CategoryLabel, pair.Value.ExpectedWorkset, "", message));
                    missingWorksetCategories.Add(pair.Key);
                }
                else
                {
                    targetWorksets.Add(pair.Key, targetWorkset);
                }
            }

            var instanceCounts = expected.Keys.ToDictionary(categoryId => categoryId, _ => 0);
            var changedCounts = expected.Keys.ToDictionary(categoryId => categoryId, _ => 0);
            var unresolvedCounts = expected.Keys.ToDictionary(categoryId => categoryId, _ => 0);
            var pendingChanges = new List<PendingChange>();
            var unresolvedRows = new List<string[]>();
            int checkedCount = 0;
            foreach (Element element in new FilteredElementCollector(activeDocument)
                .WhereElementIsNotElementType()
                .ToElements()
                .OrderBy(item => item.Id.Value))
            {
                long categoryId = element.Category?.Id.Value ?? long.MinValue;
                if (!expected.TryGetValue(categoryId, out ExpectedCategory? category)) continue;
                instanceCounts[categoryId]++;
                checkedCount++;
                string actualWorkset;
                try
                {
                    actualWorkset = activeDocument.GetWorksetTable().GetWorkset(element.WorksetId).Name;
                }
                catch (Exception exception)
                {
                    actualWorkset = "";
                    blockers.Add("Kunne ikke lese workset for ElementId " + element.Id.Value.ToString(CultureInfo.InvariantCulture) + ": " + exception.Message);
                }

                if (missingWorksetCategories.Contains(categoryId))
                {
                    unresolvedCounts[categoryId]++;
                    unresolvedRows.Add(ReportRow("Workset-avvik", element.Id.Value.ToString(CultureInfo.InvariantCulture), element.Category?.Name ?? category.Category.ToString(), "", category.CategoryLabel + " (" + category.Source + ") | " + category.ExpectedWorkset, actualWorkset, "Målarbeidssettet finnes ikke i modellen; elementet ble ikke flyttet."));
                    continue;
                }
                if (string.Equals(actualWorkset, category.ExpectedWorkset, StringComparison.OrdinalIgnoreCase)) continue;

                Parameter? worksetParameter = element.get_Parameter(BuiltInParameter.ELEM_PARTITION_PARAM);
                if (worksetParameter is null || worksetParameter.StorageType != StorageType.Integer || worksetParameter.IsReadOnly)
                {
                    string reason = worksetParameter is null ? "Workset-parameteren mangler." : worksetParameter.StorageType != StorageType.Integer ? "Workset-parameteren har feil lagringstype." : "Workset-parameteren er skrivebeskyttet.";
                    blockers.Add("ElementId " + element.Id.Value.ToString(CultureInfo.InvariantCulture) + ": " + reason);
                    unresolvedCounts[categoryId]++;
                    unresolvedRows.Add(ReportRow("Workset-avvik", element.Id.Value.ToString(CultureInfo.InvariantCulture), element.Category?.Name ?? category.Category.ToString(), "", category.CategoryLabel + " (" + category.Source + ") | " + category.ExpectedWorkset, actualWorkset, reason));
                    continue;
                }

                string familyName = element is FamilyInstance familyInstance
                    ? familyInstance.Symbol?.Family?.Name ?? ""
                    : "";
                pendingChanges.Add(new PendingChange(element, worksetParameter, category, targetWorksets[categoryId], actualWorkset));
            }

            var movedRows = new List<string[]>();
            if (pendingChanges.Count > 0)
            {
                var dialogHandler = new WorksetCheckoutDialogHandler(log);
                uiApplication.DialogBoxShowing += dialogHandler.HandleDialogBoxShowing;
                Transaction? transaction = null;
                var completedChanges = new List<PendingChange>();
                var failedChanges = new List<Tuple<PendingChange, string>>();
                try
                {
                    transaction = new Transaction(activeDocument, "Regel 10: rett workset etter Excel-listen");
                    if (transaction.Start() != TransactionStatus.Started)
                    {
                        throw new InvalidOperationException("Revit startet ikke transaksjonen for workset-retting.");
                    }

                    foreach (PendingChange change in pendingChanges)
                    {
                        try
                        {
                            if (!change.Parameter.Set(change.TargetWorkset.Id.IntegerValue)
                                || change.Parameter.AsInteger() != change.TargetWorkset.Id.IntegerValue)
                            {
                                throw new InvalidOperationException("Revit bekreftet ikke den nye workset-verdien.");
                            }
                            completedChanges.Add(change);
                        }
                        catch (Exception exception)
                        {
                            failedChanges.Add(Tuple.Create(change, exception.Message));
                        }
                    }

                    TransactionStatus commitStatus = transaction.Commit();
                    if (commitStatus != TransactionStatus.Committed)
                    {
                        throw new InvalidOperationException("Revit fullførte ikke workset-transaksjonen: " + commitStatus);
                    }

                    foreach (PendingChange change in completedChanges)
                    {
                        long categoryId = new ElementId(change.Category.Category).Value;
                        changedCounts[categoryId]++;
                        movedRows.Add(ReportRow("Flyttet", change.Element.Id.Value.ToString(CultureInfo.InvariantCulture), change.Element.Category?.Name ?? change.Category.Category.ToString(), GetFamilyName(change.Element), change.Category.CategoryLabel + " (" + change.Category.Source + ") | " + change.Category.ExpectedWorkset, change.Category.ExpectedWorkset, "Flyttet fra " + change.CurrentWorkset + " til forventet workset."));
                    }
                    foreach (Tuple<PendingChange, string> failure in failedChanges)
                    {
                        long categoryId = new ElementId(failure.Item1.Category.Category).Value;
                        unresolvedCounts[categoryId]++;
                        string message = "ElementId " + failure.Item1.Element.Id.Value.ToString(CultureInfo.InvariantCulture) + " kunne ikke flyttes: " + failure.Item2;
                        blockers.Add(message);
                        unresolvedRows.Add(ReportRow("Workset-avvik", failure.Item1.Element.Id.Value.ToString(CultureInfo.InvariantCulture), failure.Item1.Element.Category?.Name ?? failure.Item1.Category.Category.ToString(), GetFamilyName(failure.Item1.Element), failure.Item1.Category.CategoryLabel + " (" + failure.Item1.Category.Source + ") | " + failure.Item1.Category.ExpectedWorkset, failure.Item1.CurrentWorkset, failure.Item2));
                    }
                }
                catch (Exception exception)
                {
                    if (transaction is not null && transaction.GetStatus() == TransactionStatus.Started)
                    {
                        transaction.RollBack();
                    }
                    completedChanges.Clear();
                    failedChanges.Clear();
                    blockers.Add("Workset-transaksjonen ble rullet tilbake: " + exception.Message);
                    foreach (PendingChange change in pendingChanges)
                    {
                        long categoryId = new ElementId(change.Category.Category).Value;
                        unresolvedCounts[categoryId]++;
                        unresolvedRows.Add(ReportRow("Workset-avvik", change.Element.Id.Value.ToString(CultureInfo.InvariantCulture), change.Element.Category?.Name ?? change.Category.Category.ToString(), GetFamilyName(change.Element), change.Category.CategoryLabel + " (" + change.Category.Source + ") | " + change.Category.ExpectedWorkset, change.CurrentWorkset, "Transaksjonen ble rullet tilbake: " + exception.Message));
                    }
                }
                finally
                {
                    transaction?.Dispose();
                    uiApplication.DialogBoxShowing -= dialogHandler.HandleDialogBoxShowing;
                }

                if (dialogHandler.HandledCount > 0)
                {
                    log.Add("WORKSHARING: automatisk håndterte Check Out Worksets-dialoger " + dialogHandler.HandledCount.ToString(CultureInfo.InvariantCulture) + ".");
                }
            }

            int changedCount = changedCounts.Values.Sum();
            int unresolvedCount = unresolvedCounts.Values.Sum();
            var reportRows = new List<string[]>();
            foreach (KeyValuePair<long, ExpectedCategory> pair in expected.OrderBy(item => item.Value.CategoryLabel, StringComparer.OrdinalIgnoreCase).ThenBy(item => item.Value.Category))
            {
                reportRows.Add(ReportRow(
                    "Kategorisammendrag",
                    "",
                    pair.Value.Category.ToString(),
                    "",
                    pair.Value.CategoryLabel + " | " + pair.Value.ExpectedWorkset,
                    "",
                        string.Format(CultureInfo.InvariantCulture, "Elementer kontrollert: {0}; flyttet: {1}; uløste avvik: {2}.", instanceCounts[pair.Key], changedCounts[pair.Key], unresolvedCounts[pair.Key])));
            }
            reportRows.AddRange(validationRows);
                    reportRows.AddRange(movedRows);
                    reportRows.AddRange(unresolvedRows);

            string reportPath;
            try
            {
                Directory.CreateDirectory(OutputDirectory);
                reportPath = GetUniqueReportPath();
                WriteCsvReport(reportPath, reportRows);
            }
            catch (Exception exception)
            {
                log.Add("RAPPORTFEIL: " + exception.Message);
                return SaveAndReturn(log, "Regel 10 kunne ikke opprette rapport: " + exception.Message);
            }

            log.Add(string.Format(CultureInfo.InvariantCulture, "Mappinger lest: {0}; kategorier kontrollert: {1}; elementer kontrollert: {2}; flyttet: {3}; uløste avvik: {4}; blokkeringer: {5}.", mappings.Count, expected.Count, checkedCount, changedCount, unresolvedCount, blockers.Count));
            log.Add("Rapport: " + reportPath);
            foreach (string blocker in blockers.Distinct(StringComparer.Ordinal)) log.Add("BLOKKERING: " + blocker);
            string status = blockers.Count > 0 ? changedCount > 0 ? "DELVIS UTFØRT" : "BLOKKERT" : unresolvedCount > 0 ? "AVVIK" : "OK";
            return SaveAndReturn(log, string.Format(CultureInfo.InvariantCulture,
                "Regel 10 v{0}: {1}; kategorier {2}; elementer {3}; flyttet {4}; uløste avvik {5}; blokkeringer {6}; rapport {7}.",
                ScriptVersion, status, expected.Count, checkedCount, changedCount, unresolvedCount, blockers.Count, reportPath));
        }

        private static string GetFamilyName(Element element)
        {
            return element is FamilyInstance instance ? instance.Symbol?.Family?.Name ?? "" : "";
        }

        private static List<ExcelMapping> ReadWorkbook(string path)
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
            var targets = GetDescendants(relationships, "Relationship", PackageRelationshipNamespace)
                .ToDictionary(node => GetXmlAttribute(node, "Id"), node => GetXmlAttribute(node, "Target"), StringComparer.Ordinal);
            var mappings = new List<ExcelMapping>();
            foreach (object sheet in GetDescendants(workbook, "sheet", MainNamespace))
            {
                string sheetName = GetXmlAttribute(sheet, "name");
                string relationshipId = GetXmlAttribute(sheet, "id", RelationshipNamespace);
                if (!targets.TryGetValue(relationshipId, out string? target) || string.IsNullOrEmpty(target))
                {
                    throw new InvalidDataException("Fant ingen worksheet-relasjon for fanen '" + sheetName + "'.");
                }
                string partName = ResolvePartName("xl", target);
                if (!entries.TryGetValue(partName, out string? sheetXml))
                {
                    throw new InvalidDataException("Fant ikke worksheet-delen for fanen '" + sheetName + "'.");
                }

                List<object> rows = GetDescendants(LoadXml(sheetXml), "row", MainNamespace).ToList();
                int categoryColumn = -1;
                int worksetColumn = -1;
                int headerRow = -1;
                for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
                {
                    Dictionary<int, string> cells = ReadCells(rows[rowIndex], sharedStrings);
                    foreach (KeyValuePair<int, string> cell in cells)
                    {
                        string header = cell.Value.Trim();
                        if (string.Equals(header, "Type elementer", StringComparison.OrdinalIgnoreCase)) categoryColumn = cell.Key;
                        if (string.Equals(header, "Workset", StringComparison.OrdinalIgnoreCase)) worksetColumn = cell.Key;
                    }
                    if (categoryColumn >= 0 && worksetColumn >= 0)
                    {
                        headerRow = rowIndex;
                        break;
                    }
                }
                if (headerRow < 0) continue;

                for (int rowIndex = headerRow + 1; rowIndex < rows.Count; rowIndex++)
                {
                    Dictionary<int, string> cells = ReadCells(rows[rowIndex], sharedStrings);
                    string label = cells.TryGetValue(categoryColumn, out string? categoryValue) ? categoryValue.Trim() : "";
                    string workset = cells.TryGetValue(worksetColumn, out string? worksetValue) ? worksetValue.Trim() : "";
                    if (label.Length == 0 && workset.Length == 0) continue;
                    string rowNumber = GetXmlAttribute(rows[rowIndex], "r");
                    string source = sheetName + "!" + (rowNumber.Length == 0 ? (rowIndex + 1).ToString(CultureInfo.InvariantCulture) : rowNumber);
                    if (label.Length == 0 || workset.Length == 0)
                    {
                        throw new InvalidDataException("Mangler Type elementer eller Workset i " + source + ".");
                    }
                    mappings.Add(new ExcelMapping(label, workset, source));
                }
            }
            if (mappings.Count == 0) throw new InvalidDataException("Fant ingen rader under kolonnene Type elementer og Workset.");
            return mappings;
        }

        private static Dictionary<int, string> ReadCells(object row, List<string> sharedStrings)
        {
            var cells = new Dictionary<int, string>();
            foreach (object cell in GetChildElements(row, "c", MainNamespace))
            {
                int column = ColumnIndex(GetXmlAttribute(cell, "r"));
                if (column < 0) continue;
                string type = GetXmlAttribute(cell, "t");
                string value;
                if (type == "inlineStr")
                {
                    value = string.Concat(GetDescendants(cell, "t", MainNamespace).Select(GetXmlInnerText));
                }
                else
                {
                    object? valueNode = GetChildElement(cell, "v", MainNamespace);
                    value = valueNode is null ? "" : GetXmlInnerText(valueNode);
                    if (type == "s")
                    {
                        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int sharedIndex)
                            || sharedIndex < 0 || sharedIndex >= sharedStrings.Count)
                        {
                            throw new InvalidDataException("Ugyldig shared-string-referanse i arbeidsboken.");
                        }
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
            return GetDescendants(LoadXml(xml), "si", MainNamespace)
                .Select(item => string.Concat(GetDescendants(item, "t", MainNamespace).Select(GetXmlInnerText)))
                .ToList();
        }

        private static Dictionary<string, string> ReadZipEntries(string path)
        {
            if (!File.Exists(path)) throw new FileNotFoundException("Fant ikke workset-listen.", path);
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
                    string name = (string)(entry.GetType().GetProperty("FullName")?.GetValue(entry) ?? "");
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
            foreach (object? child in children) if (child is not null) yield return child;
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
            return string.Equals(nodeType.GetProperty("NodeType")?.GetValue(node)?.ToString(), "Element", StringComparison.Ordinal)
                && string.Equals(nodeType.GetProperty("LocalName")?.GetValue(node) as string, localName, StringComparison.Ordinal)
                && string.Equals(nodeType.GetProperty("NamespaceURI")?.GetValue(node) as string, namespaceUri, StringComparison.Ordinal);
        }

        private static string GetXmlAttribute(object element, string localName, string namespaceUri = "")
        {
            MethodInfo getAttribute = element.GetType().GetMethod("GetAttribute", new[] { typeof(string), typeof(string) })
                ?? throw new InvalidOperationException("Fant ikke XmlElement.GetAttribute.");
            return getAttribute.Invoke(element, new object[] { localName, namespaceUri }) as string ?? "";
        }

        private static string GetXmlInnerText(object node)
        {
            return node.GetType().GetProperty("InnerText")?.GetValue(node) as string ?? "";
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

        private static string[] ReportRow(string result, string elementId, string category, string family, string expected, string actual, string comment)
        {
            return new[] { result, elementId, category, family, expected, actual, comment };
        }

        private static string GetUniqueReportPath()
        {
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
            string path = Path.Combine(OutputDirectory, "Workset kategori kontroll " + timestamp + ".csv");
            int suffix = 1;
            while (File.Exists(path))
            {
                path = Path.Combine(OutputDirectory, "Workset kategori kontroll " + timestamp + "_" + suffix.ToString(CultureInfo.InvariantCulture) + ".csv");
                suffix++;
            }
            return path;
        }

        private static void WriteCsvReport(string path, List<string[]> rows)
        {
            var output = new StringBuilder();
            output.AppendLine("Resultat;ElementId;Revit-kategori;Familie;Forventet workset;Faktisk workset;Kommentar");
            foreach (string[] row in rows)
            {
                output.AppendLine(string.Join(";", row.Select(EscapeCsv)));
            }
            File.WriteAllText(path, output.ToString(), new UTF8Encoding(true));
        }

        private static string EscapeCsv(string value)
        {
            return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
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