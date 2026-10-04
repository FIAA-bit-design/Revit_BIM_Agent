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
        private const string ScriptVersion = "0.0.3";
        private const string FamilyName = "InfoNode";
        private const string HostIdParameterName = "InfoNode_hostID";
        private const string SubsParameterName = "InfoNode_subs";
        private const double PortClearanceFeet = 50.0 / 304.8;
        private const string OutputDirectory = @"D:\Revit\Python\Revit_BIM_Agent\logs\reports";
        private const string LogPath = @"D:\Revit\Python\Revit_BIM_Agent\logs\history\Rule08_InfoNodeCheck.log";
        private static readonly string[] ExcelHeaders =
        {
            "Funn", "ElementId", "InfoNode_hostID", "InfoNode_subs", "Andre på samme sted", "Kommentar"
        };

        private sealed class MoveCandidate
        {
            internal FamilyInstance Fire { get; }
            internal FamilyInstance Climate { get; }
            internal XYZ Delta { get; }

            internal MoveCandidate(FamilyInstance fire, FamilyInstance climate, XYZ delta)
            {
                Fire = fire;
                Climate = climate;
                Delta = delta;
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
                return "FEIL: Ingen aktiv Revit-modell. Regel 8 stoppet før elementlesing.";
            }

            var log = new List<string>
            {
                string.Format(CultureInfo.InvariantCulture, "=== Regel 8 v{0} | {1:O} | {2} ===", ScriptVersion, DateTime.Now, activeDocument.Title),
                "Formål: Kontroller InfoNode-hostID/subs og flytt kvalifisert Klimaport under Brannport."
            };
            var rows = new List<string[]>();
            List<FamilyInstance> instances = new FilteredElementCollector(activeDocument)
                .OfCategory(BuiltInCategory.OST_SpecialityEquipment)
                .OfClass(typeof(FamilyInstance))
                .WhereElementIsNotElementType()
                .Cast<FamilyInstance>()
                .Where(instance => string.Equals(GetFamilyName(instance), FamilyName, StringComparison.Ordinal))
                .OrderBy(instance => instance.Id.Value)
                .ToList();
            var byId = instances.ToDictionary(instance => instance.Id.Value);
            var hostIds = new Dictionary<string, List<FamilyInstance>>(StringComparer.Ordinal);
            var missingHostIds = new List<FamilyInstance>();

            foreach (FamilyInstance instance in instances)
            {
                string? value = GetParameterValue(instance, HostIdParameterName);
                if (string.IsNullOrEmpty(value))
                {
                    missingHostIds.Add(instance);
                    rows.Add(new[] { "Tom HostID", IdText(instance), string.Empty, GetParameterValue(instance, SubsParameterName) ?? string.Empty, string.Empty, string.Empty });
                }
                else
                {
                    if (!hostIds.TryGetValue(value, out List<FamilyInstance>? group))
                    {
                        group = new List<FamilyInstance>();
                        hostIds.Add(value, group);
                    }
                    group.Add(instance);
                }
            }

            List<KeyValuePair<string, List<FamilyInstance>>> duplicateHostIds = hostIds
                .Where(pair => pair.Value.Count > 1)
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .ToList();
            foreach (KeyValuePair<string, List<FamilyInstance>> duplicate in duplicateHostIds)
            {
                foreach (FamilyInstance instance in duplicate.Value)
                {
                    string others = IdList(duplicate.Value.Where(item => item.Id.Value != instance.Id.Value));
                    rows.Add(new[] { "Duplisert HostID", IdText(instance), duplicate.Key, GetParameterValue(instance, SubsParameterName) ?? string.Empty, string.Empty, "Delt med ElementId " + others });
                }
            }

            log.Add("Dokument: " + activeDocument.Title);
            log.Add(string.Format(CultureInfo.InvariantCulture, "Familie: {0} (Specialty Equipment), antall instanser: {1}", FamilyName, instances.Count));
            log.Add(string.Format(CultureInfo.InvariantCulture, "Manglende eller tom InfoNode_hostID: {0}", missingHostIds.Count));
            log.Add(string.Format(CultureInfo.InvariantCulture, "ID-verdier brukt av flere instanser: {0}", duplicateHostIds.Count));

            List<FailureMessage> warnings = activeDocument.GetWarnings()
                .Where(warning => warning.GetFailureDefinitionId().Equals(BuiltInFailures.OverlapFailures.DuplicateInstances))
                .ToList();
            var conflicts = new List<List<FamilyInstance>>();
            var missingSubs = new HashSet<long>();
            var portPairs = new List<Tuple<FamilyInstance, FamilyInstance>>();
            var seenPairs = new HashSet<string>(StringComparer.Ordinal);
            int relevantWarnings = 0;

            foreach (FailureMessage warning in warnings)
            {
                List<FamilyInstance> members = warning.GetFailingElements()
                    .Where(id => byId.ContainsKey(id.Value))
                    .Select(id => byId[id.Value])
                    .ToList();
                if (members.Count < 2)
                {
                    continue;
                }
                relevantWarnings++;
                var values = new Dictionary<string, List<FamilyInstance>>(StringComparer.Ordinal);
                foreach (FamilyInstance instance in members)
                {
                    string? value = GetParameterValue(instance, SubsParameterName);
                    if (string.IsNullOrEmpty(value))
                    {
                        missingSubs.Add(instance.Id.Value);
                    }
                    else
                    {
                        if (!values.TryGetValue(value, out List<FamilyInstance>? group))
                        {
                            group = new List<FamilyInstance>();
                            values.Add(value, group);
                        }
                        group.Add(instance);
                    }
                }

                if (values.Count > 1)
                {
                    conflicts.Add(members);
                    if (members.Count == 2)
                    {
                        FamilyInstance? fire = members.FirstOrDefault(item => PortKind(item) == "brann");
                        FamilyInstance? climate = members.FirstOrDefault(item => PortKind(item) == "klima");
                        string pairKey = string.Join(":", members.Select(item => item.Id.Value).OrderBy(id => id));
                        if (fire is not null && climate is not null && seenPairs.Add(pairKey))
                        {
                            portPairs.Add(Tuple.Create(fire, climate));
                        }
                    }

                    foreach (FamilyInstance instance in members)
                    {
                        List<FamilyInstance> others = members.Where(item => item.Id.Value != instance.Id.Value).ToList();
                        string subValue = GetParameterValue(instance, SubsParameterName) ?? string.Empty;
                        string finding = subValue.Length == 0 ? "Tom InfoNode_subs" : "Ulike InfoNode_subs";
                        rows.Add(new[] { finding, IdText(instance), GetParameterValue(instance, HostIdParameterName) ?? string.Empty, subValue, IdList(others), string.Empty });
                    }
                }
                else
                {
                    foreach (FamilyInstance instance in members.Where(item => missingSubs.Contains(item.Id.Value)))
                    {
                        List<FamilyInstance> others = members.Where(item => item.Id.Value != instance.Id.Value).ToList();
                        rows.Add(new[] { "Tom InfoNode_subs", IdText(instance), GetParameterValue(instance, HostIdParameterName) ?? string.Empty, string.Empty, IdList(others), string.Empty });
                    }
                }
            }

            log.Add(string.Format(CultureInfo.InvariantCulture, "Relevante DuplicateInstances-advarsler med minst to InfoNodes: {0}", relevantWarnings));
            log.Add(string.Format(CultureInfo.InvariantCulture, "Advarsler med ulike utfylte InfoNode_subs: {0}", conflicts.Count));
            log.Add(string.Format(CultureInfo.InvariantCulture, "Instanser med tom eller manglende InfoNode_subs i disse advarslene: {0}", missingSubs.Count));

            List<Tuple<FamilyInstance, FamilyInstance>> moved;
            List<Tuple<FamilyInstance, FamilyInstance, string>> skipped;
            var worksetDialogHandler = new WorksetCheckoutDialogHandler(log);
            uiApplication.DialogBoxShowing += worksetDialogHandler.HandleDialogBoxShowing;
            try
            {
                try
                {
                    MovePortPairs(activeDocument, portPairs, out moved, out skipped);
                }
                catch (Exception exception)
                {
                    log.Add("FLYTTEFEIL: " + exception.Message);
                    SaveAndReturn(log);
                    throw;
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

            var movedStatus = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (Tuple<FamilyInstance, FamilyInstance> pair in moved)
            {
                movedStatus[IdText(pair.Item1)] = "Brannport (referanse)";
                movedStatus[IdText(pair.Item2)] = "Flyttet Klimaport";
                log.Add(string.Format(CultureInfo.InvariantCulture, "FLYTTET Brannport {0}: Klimaport {1} under med 50 mm klaring.", pair.Item1.Id.Value, pair.Item2.Id.Value));
            }
            foreach (Tuple<FamilyInstance, FamilyInstance, string> pair in skipped)
            {
                log.Add(string.Format(CultureInfo.InvariantCulture, "IKKE FLYTTET Klimaport {0} fra Brannport {1}: {2}.", pair.Item2.Id.Value, pair.Item1.Id.Value, pair.Item3));
            }

            for (int index = 0; index < rows.Count; index++)
            {
                string[] row = rows[index];
                if (row[0] == "Ulike InfoNode_subs" && movedStatus.TryGetValue(row[1], out string? status))
                {
                    rows[index] = new[] { status, row[1], row[2], row[3], row[4], row[5] };
                }
            }

            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
            string reportPath = Path.Combine(OutputDirectory, "Slette duplikater infonoder funn " + timestamp + ".xlsx");
            try
            {
                Directory.CreateDirectory(OutputDirectory);
                WriteExcelReport(reportPath, rows);
            }
            catch (Exception exception)
            {
                log.Add("RAPPORTFEIL: " + exception.Message);
                SaveAndReturn(log);
                return string.Format(CultureInfo.InvariantCulture, "Regel 8 v{0}: kontroll og eventuelle flyttinger fullført, men Excel-rapport feilet: {1}.", ScriptVersion, exception.Message);
            }

            log.Insert(1, string.Format(
                CultureInfo.InvariantCulture,
                "InfoNodes {0}; manglende HostID {1}; dupliserte HostID-verdier {2}; relevante advarsler {3}; subs-konflikter {4}; tomme subs {5}; flyttet par {6}; hoppet over par {7}; Excel-rader {8}.",
                instances.Count, missingHostIds.Count, duplicateHostIds.Count, relevantWarnings, conflicts.Count, missingSubs.Count, moved.Count, skipped.Count, rows.Count));
            log.Add("Excel-rapport: " + reportPath);
            string logPath = SaveAndReturn(log);
            return string.Format(
                CultureInfo.InvariantCulture,
                "Regel 8 v{0}: InfoNodes {1}; manglende HostID {2}; dupliserte HostID-verdier {3}; subs-konflikter {4}; tomme subs {5}; flyttet par {6}; hoppet over par {7}; rapport {8}; logg {9}.",
                ScriptVersion, instances.Count, missingHostIds.Count, duplicateHostIds.Count, conflicts.Count, missingSubs.Count, moved.Count, skipped.Count, reportPath, logPath);
        }

        private static void MovePortPairs(
            Document document,
            List<Tuple<FamilyInstance, FamilyInstance>> pairs,
            out List<Tuple<FamilyInstance, FamilyInstance>> moved,
            out List<Tuple<FamilyInstance, FamilyInstance, string>> skipped)
        {
            var ready = new List<MoveCandidate>();
            skipped = new List<Tuple<FamilyInstance, FamilyInstance, string>>();
            foreach (Tuple<FamilyInstance, FamilyInstance> pair in pairs)
            {
                FamilyInstance fire = pair.Item1;
                FamilyInstance climate = pair.Item2;
                LocationPoint? fireLocation = fire.Location as LocationPoint;
                LocationPoint? climateLocation = climate.Location as LocationPoint;
                BoundingBoxXYZ? fireBounds = fire.get_BoundingBox(null);
                BoundingBoxXYZ? climateBounds = climate.get_BoundingBox(null);
                if (fireLocation is null || climateLocation is null || fireBounds is null || climateBounds is null
                    || climate.Pinned || climate.GroupId.Value != -1 || climate.Host is not null)
                {
                    skipped.Add(Tuple.Create(fire, climate, "låst, gruppert, vertsbundet eller uten punktplassering"));
                    continue;
                }
                if (fireLocation.Point.DistanceTo(climateLocation.Point) * 304.8 > 2.0)
                {
                    skipped.Add(Tuple.Create(fire, climate, "står ikke lenger på samme sted"));
                    continue;
                }

                double targetY = fireBounds.Min.Y - PortClearanceFeet - (climateBounds.Max.Y - climateLocation.Point.Y);
                XYZ delta = new XYZ(
                    fireLocation.Point.X - climateLocation.Point.X,
                    targetY - climateLocation.Point.Y,
                    fireLocation.Point.Z - climateLocation.Point.Z);
                if (delta.Y >= 0.0)
                {
                    skipped.Add(Tuple.Create(fire, climate, "flytting ville ikke gått nedover i plan"));
                    continue;
                }
                ready.Add(new MoveCandidate(fire, climate, delta));
            }

            moved = new List<Tuple<FamilyInstance, FamilyInstance>>();
            if (ready.Count == 0)
            {
                return;
            }

            using (var transaction = new Transaction(document, "Flytt InfoNode Klimaport under Brannport"))
            {
                TransactionStatus startStatus = transaction.Start();
                if (startStatus != TransactionStatus.Started)
                {
                    throw new InvalidOperationException("Flyttetransaksjonen startet ikke: " + startStatus);
                }
                try
                {
                    foreach (MoveCandidate candidate in ready)
                    {
                        ElementTransformUtils.MoveElement(document, candidate.Climate.Id, candidate.Delta);
                    }
                    document.Regenerate();
                    foreach (MoveCandidate candidate in ready)
                    {
                        BoundingBoxXYZ? fireBounds = candidate.Fire.get_BoundingBox(null);
                        BoundingBoxXYZ? climateBounds = candidate.Climate.get_BoundingBox(null);
                        LocationPoint? fireLocation = candidate.Fire.Location as LocationPoint;
                        LocationPoint? climateLocation = candidate.Climate.Location as LocationPoint;
                        if (fireBounds is null || climateBounds is null || fireLocation is null || climateLocation is null)
                        {
                            throw new InvalidOperationException("Etterkontroll mangler plassering eller bounding box for Klimaport ElementId " + candidate.Climate.Id.Value.ToString(CultureInfo.InvariantCulture));
                        }

                        double gapMm = (fireBounds.Min.Y - climateBounds.Max.Y) * 304.8;
                        if (Math.Abs(gapMm - 50.0) > 1.0
                            || Math.Abs(fireLocation.Point.X - climateLocation.Point.X) * 304.8 > 1.0
                            || Math.Abs(fireLocation.Point.Z - climateLocation.Point.Z) * 304.8 > 1.0)
                        {
                            throw new InvalidOperationException("Etterkontroll feilet for Klimaport ElementId " + candidate.Climate.Id.Value.ToString(CultureInfo.InvariantCulture));
                        }
                    }

                    TransactionStatus commitStatus = transaction.Commit();
                    if (commitStatus != TransactionStatus.Committed)
                    {
                        throw new InvalidOperationException("Revit fullførte ikke flyttetransaksjonen: " + commitStatus);
                    }
                }
                catch
                {
                    if (transaction.GetStatus() == TransactionStatus.Started)
                    {
                        transaction.RollBack();
                    }
                    throw;
                }
            }

            moved.AddRange(ready.Select(candidate => Tuple.Create(candidate.Fire, candidate.Climate)));
        }

        private static void WriteExcelReport(string path, List<string[]> rows)
        {
            var sheet = new StringBuilder();
            sheet.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>")
                .Append("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">")
                .Append("<cols><col min=\"1\" max=\"1\" width=\"25\" customWidth=\"1\"/>")
                .Append("<col min=\"2\" max=\"2\" width=\"20\" customWidth=\"1\"/>")
                .Append("<col min=\"3\" max=\"4\" width=\"32\" customWidth=\"1\"/>")
                .Append("<col min=\"5\" max=\"5\" width=\"30\" customWidth=\"1\"/>")
                .Append("<col min=\"6\" max=\"6\" width=\"38\" customWidth=\"1\"/></cols><sheetData>");
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
            sheet.Append("</sheetData><autoFilter ref=\"A1:F")
                .Append(allRows.Count.ToString(CultureInfo.InvariantCulture))
                .Append("\"/></worksheet>");

            WriteZipPackage(path, new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["[Content_Types].xml"] = "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/><Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/></Types>",
                ["_rels/.rels"] = "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>",
                ["xl/workbook.xml"] = "<?xml version=\"1.0\" encoding=\"UTF-8\"?><workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets><sheet name=\"Funn\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>",
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

        private static string? GetParameterValue(Element element, string name)
        {
            Parameter? parameter = element.LookupParameter(name);
            if (parameter is null)
            {
                return null;
            }
            if (!parameter.HasValue)
            {
                return string.Empty;
            }
            string? value = parameter.StorageType == StorageType.String ? parameter.AsString() : parameter.AsValueString();
            return value?.Trim() ?? string.Empty;
        }

        private static string? PortKind(FamilyInstance instance)
        {
            string value = (GetParameterValue(instance, SubsParameterName) ?? string.Empty).ToLowerInvariant();
            if (value.Contains("klimaport") && !value.Contains("brannport")) return "klima";
            if (value.Contains("brannport") && !value.Contains("klimaport")) return "brann";
            return null;
        }

        private static string GetFamilyName(FamilyInstance instance)
        {
            try { return instance.Symbol?.Family?.Name ?? string.Empty; }
            catch { return string.Empty; }
        }

        private static string IdText(Element element) => element.Id.Value.ToString(CultureInfo.InvariantCulture);

        private static string IdList(IEnumerable<FamilyInstance> instances)
        {
            return string.Join(", ", instances.OrderBy(instance => instance.Id.Value).Select(IdText));
        }

        private static string RemoveInvalidXmlCharacters(string value)
        {
            var filtered = new StringBuilder(value.Length);
            foreach (char character in value)
            {
                if (character == '\t' || character == '\n' || character == '\r' || character >= 0x20)
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

        private static string SaveAndReturn(List<string> log)
        {
            File.AppendAllText(LogPath, string.Join(Environment.NewLine, log) + Environment.NewLine, new UTF8Encoding(false));
            return LogPath;
        }
    }
}