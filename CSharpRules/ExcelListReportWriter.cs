#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;

namespace CW.Assistant.Generated
{
    internal static class ExcelListReportWriter
    {
        internal sealed class WorksheetData
        {
            internal string Name { get; }
            internal string[] Headers { get; }
            internal List<string[]> Rows { get; }
            internal HashSet<string> HiddenHeaders { get; }
            internal IReadOnlyDictionary<string, double> ColumnWidths { get; }

            internal WorksheetData(string name, string[] headers, List<string[]> rows, string[]? hiddenHeaders = null,
                IReadOnlyDictionary<string, double>? columnWidths = null)
            {
                Name = name;
                Headers = headers;
                Rows = rows;
                HiddenHeaders = new HashSet<string>(hiddenHeaders ?? Array.Empty<string>(), StringComparer.Ordinal);
                ColumnWidths = columnWidths ?? new Dictionary<string, double>(StringComparer.Ordinal);
            }
        }

        internal static void Write(string path, string sheetName, string[] headers, List<string[]> rows)
        {
            Write(path, new[] { new WorksheetData(sheetName, headers, rows) });
        }

        internal static void Write(string path, IReadOnlyList<WorksheetData> worksheets, bool overwriteExisting = false)
        {
            if (worksheets.Count == 0) throw new ArgumentException("Rapporten må ha minst ett ark.", nameof(worksheets));
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (WorksheetData worksheet in worksheets)
            {
                if (string.IsNullOrWhiteSpace(worksheet.Name) || worksheet.Name.Length > 31
                    || worksheet.Name.IndexOfAny(new[] { '[', ']', ':', '*', '?', '/', '\\' }) >= 0)
                {
                    throw new ArgumentException("Ugyldig Excel-arknavn: " + worksheet.Name, nameof(worksheets));
                }
                if (!names.Add(worksheet.Name)) throw new ArgumentException("Duplikat ark i Excel-rapporten: " + worksheet.Name, nameof(worksheets));
                if (worksheet.Headers.Length == 0) throw new ArgumentException("Rapportarket må ha minst én kolonne.", nameof(worksheets));
            }

            var entries = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["[Content_Types].xml"] = BuildContentTypes(worksheets.Count),
                ["_rels/.rels"] = "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>",
                ["xl/workbook.xml"] = BuildWorkbook(worksheets),
                ["xl/_rels/workbook.xml.rels"] = BuildWorkbookRelationships(worksheets.Count)
            };
            for (int index = 0; index < worksheets.Count; index++)
            {
                entries["xl/worksheets/sheet" + (index + 1).ToString(CultureInfo.InvariantCulture) + ".xml"] = BuildWorksheet(worksheets[index]);
            }
            WriteZipPackage(path, entries, overwriteExisting);
        }

        private static string BuildWorksheet(WorksheetData worksheet)
        {
            var sheet = new StringBuilder();
            string range = "A1:" + ColumnName(worksheet.Headers.Length)
                + (worksheet.Rows.Count + 1).ToString(CultureInfo.InvariantCulture);
            sheet.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>")
                .Append("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">")
                .Append("<dimension ref=\"").Append(range).Append("\"/>")
                .Append("<sheetViews><sheetView workbookViewId=\"0\"><pane ySplit=\"1\" topLeftCell=\"A2\" activePane=\"bottomLeft\" state=\"frozen\"/>")
                .Append("<selection pane=\"bottomLeft\" activeCell=\"A2\" sqref=\"A2\"/></sheetView></sheetViews><cols>");
            for (int columnIndex = 0; columnIndex < worksheet.Headers.Length; columnIndex++)
            {
                int columnNumber = columnIndex + 1;
                string header = worksheet.Headers[columnIndex];
                string width = worksheet.ColumnWidths.TryGetValue(header, out double customWidth)
                    ? customWidth.ToString("0.########", CultureInfo.InvariantCulture)
                    : (header switch
                {
                    "Type" => 22,
                    "ElementId" => 16,
                    "Familienavn" => 32,
                    "FOB_Merkestreng" => 32,
                    "FOB_Leveransepakke" => 25,
                    "Revit-kategori" => 28,
                    "Parameter" => 32,
                    "Verdi" => 32,
                    "Detalj" => 90,
                    "Avvik" => 55,
                    _ => 32
                }).ToString(CultureInfo.InvariantCulture);
                sheet.Append("<col min=\"").Append(columnNumber.ToString(CultureInfo.InvariantCulture))
                    .Append("\" max=\"").Append(columnNumber.ToString(CultureInfo.InvariantCulture))
                    .Append("\" width=\"").Append(width)
                    .Append("\" customWidth=\"1\"");
                if (worksheet.HiddenHeaders.Contains(worksheet.Headers[columnIndex])) sheet.Append(" hidden=\"1\"");
                sheet.Append("/>");
            }
            sheet.Append("</cols><sheetData>");
            var allRows = new List<string[]> { worksheet.Headers };
            allRows.AddRange(worksheet.Rows);
            for (int rowIndex = 0; rowIndex < allRows.Count; rowIndex++)
            {
                int rowNumber = rowIndex + 1;
                sheet.Append("<row r=\"").Append(rowNumber.ToString(CultureInfo.InvariantCulture)).Append("\">");
                for (int columnIndex = 0; columnIndex < worksheet.Headers.Length; columnIndex++)
                {
                    string cellReference = ColumnName(columnIndex + 1) + rowNumber.ToString(CultureInfo.InvariantCulture);
                    string value = columnIndex < allRows[rowIndex].Length ? allRows[rowIndex][columnIndex] ?? string.Empty : string.Empty;
                    value = RemoveInvalidXmlCharacters(value);
                    if (value.Length == 0)
                    {
                        sheet.Append("<c r=\"").Append(cellReference).Append("\"/>");
                        continue;
                    }
                    sheet.Append("<c r=\"").Append(cellReference).Append("\" t=\"inlineStr\"><is><t xml:space=\"preserve\">")
                        .Append(XmlEscape(value))
                        .Append("</t></is></c>");
                }
                sheet.Append("</row>");
            }
            sheet.Append("</sheetData><autoFilter ref=\"")
                .Append(range)
                .Append("\"/></worksheet>");
            return sheet.ToString();
        }

        private static string BuildContentTypes(int worksheetCount)
        {
            var xml = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>");
            for (int index = 1; index <= worksheetCount; index++)
            {
                xml.Append("<Override PartName=\"/xl/worksheets/sheet").Append(index.ToString(CultureInfo.InvariantCulture))
                    .Append(".xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>");
            }
            return xml.Append("</Types>").ToString();
        }

        private static string BuildWorkbook(IReadOnlyList<WorksheetData> worksheets)
        {
            var xml = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\"?><workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><bookViews><workbookView/></bookViews><sheets>");
            for (int index = 0; index < worksheets.Count; index++)
            {
                int number = index + 1;
                xml.Append("<sheet name=\"").Append(XmlEscape(worksheets[index].Name)).Append("\" sheetId=\"")
                    .Append(number.ToString(CultureInfo.InvariantCulture)).Append("\" r:id=\"rId")
                    .Append(number.ToString(CultureInfo.InvariantCulture)).Append("\"/>");
            }
            return xml.Append("</sheets></workbook>").ToString();
        }

        private static string BuildWorkbookRelationships(int worksheetCount)
        {
            var xml = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">");
            for (int index = 1; index <= worksheetCount; index++)
            {
                xml.Append("<Relationship Id=\"rId").Append(index.ToString(CultureInfo.InvariantCulture))
                    .Append("\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet")
                    .Append(index.ToString(CultureInfo.InvariantCulture)).Append(".xml\"/>");
            }
            return xml.Append("</Relationships>").ToString();
        }

        private static string ColumnName(int columnNumber)
        {
            var name = new StringBuilder();
            while (columnNumber > 0)
            {
                int remainder = (columnNumber - 1) % 26;
                name.Insert(0, (char)('A' + remainder));
                columnNumber = (columnNumber - 1) / 26;
            }
            return name.ToString();
        }

        private static void WriteZipPackage(string path, Dictionary<string, string> entries, bool overwriteExisting)
        {
            if (!overwriteExisting)
            {
                WriteZipPackageFile(path, entries, FileMode.CreateNew);
                return;
            }

            string temporaryPath = Path.GetFullPath(path) + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                WriteZipPackageFile(temporaryPath, entries, FileMode.CreateNew);
                if (File.Exists(path)) File.Replace(temporaryPath, path, null);
                else File.Move(temporaryPath, path);
            }
            finally
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }
        }

        private static void WriteZipPackageFile(string path, Dictionary<string, string> entries, FileMode fileMode)
        {
            Assembly compression = Assembly.Load("System.IO.Compression");
            Type archiveType = compression.GetType("System.IO.Compression.ZipArchive", true)!;
            Type modeType = compression.GetType("System.IO.Compression.ZipArchiveMode", true)!;
            ConstructorInfo constructor = archiveType.GetConstructor(new[] { typeof(Stream), modeType, typeof(bool) })
                ?? throw new InvalidOperationException("Fant ikke ZipArchive-konstruktøren.");
            MethodInfo createEntry = archiveType.GetMethod("CreateEntry", new[] { typeof(string) })
                ?? throw new InvalidOperationException("Fant ikke ZipArchive.CreateEntry.");
            using var fileStream = new FileStream(path, fileMode, FileAccess.ReadWrite, FileShare.None);
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
    }
}