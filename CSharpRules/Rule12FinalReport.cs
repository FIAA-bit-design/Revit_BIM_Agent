#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace CW.Assistant.Generated
{
    internal sealed class GeneratedAction
    {
        private const string ScriptVersion = "0.0.3";
        private const string OutputDirectory = @"D:\Revit\Python\Revit_BIM_Agent\logs\csv";
        private static readonly string[] ParameterNames =
        {
            "FOB_Leveransepakke",
            "PGF_Mengdetype",
            "FOB_Merkestreng"
        };
        private static readonly HashSet<long> CenterLineCategoryIds = new HashSet<long>(
            Enum.GetValues(typeof(BuiltInCategory))
                .Cast<BuiltInCategory>()
                .Where(category => category.ToString().EndsWith("CenterLine", StringComparison.Ordinal))
                .Select(category => new ElementId(category).Value));

        private sealed class SnapshotRow
        {
            internal string Package { get; }
            internal string QuantityType { get; }
            internal string MarkString { get; }
            internal long ElementId { get; }

            internal SnapshotRow(string package, string quantityType, string markString, long elementId)
            {
                Package = package;
                QuantityType = quantityType;
                MarkString = markString;
                ElementId = elementId;
            }
        }

        public string Execute(UIApplication uiApplication, Document? activeDocument)
        {
            if (activeDocument is null)
            {
                return "FEIL: Ingen aktiv Revit-modell. Regel 12 stoppet før elementlesing.";
            }

            var rows = new List<SnapshotRow>();
            var missingCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            var absentCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (string name in ParameterNames)
            {
                missingCounts.Add(name, 0);
                absentCounts.Add(name, 0);
            }

            int totalInstances = 0;
            foreach (Element element in new FilteredElementCollector(activeDocument)
                .WhereElementIsNotElementType()
                .ToElements())
            {
                if (IsCenterLine(element))
                {
                    continue;
                }

                totalInstances++;
                var parameters = ParameterNames.Select(element.LookupParameter).ToArray();
                if (parameters.All(parameter => parameter is null))
                {
                    foreach (string name in ParameterNames)
                    {
                        absentCounts[name]++;
                    }
                    continue;
                }

                var values = new string[ParameterNames.Length];
                for (int index = 0; index < ParameterNames.Length; index++)
                {
                    Parameter? parameter = parameters[index];
                    if (parameter is null)
                    {
                        absentCounts[ParameterNames[index]]++;
                        values[index] = string.Empty;
                        continue;
                    }

                    values[index] = GetParameterValue(parameter);
                    if (IsMissingValue(values[index]))
                    {
                        missingCounts[ParameterNames[index]]++;
                    }
                }

                rows.Add(new SnapshotRow(values[0], values[1], values[2], element.Id.Value));
            }

            rows.Sort((left, right) => left.ElementId.CompareTo(right.ElementId));
            string reportPath = GetUniqueReportPath();
            try
            {
                Directory.CreateDirectory(OutputDirectory);
                File.WriteAllText(reportPath, BuildReport(activeDocument, totalInstances, rows, missingCounts, absentCounts), new UTF8Encoding(false));
            }
            catch (Exception exception)
            {
                return "Regel 12 v" + ScriptVersion + ": rapporten kunne ikke skrives: " + exception.Message;
            }

            return string.Format(
                CultureInfo.InvariantCulture,
                "Regel 12 v{0}: skrivebeskyttet snapshot; instanser undersøkt {1}; elementrader {2}; manglende verdier Leveransepakke/Mengdetype/Merkestreng {3}/{4}/{5}; parametere ikke til stede {6}/{7}/{8}; rapport {9}.",
                ScriptVersion,
                totalInstances,
                rows.Count,
                missingCounts[ParameterNames[0]],
                missingCounts[ParameterNames[1]],
                missingCounts[ParameterNames[2]],
                absentCounts[ParameterNames[0]],
                absentCounts[ParameterNames[1]],
                absentCounts[ParameterNames[2]],
                reportPath);
        }

        private static string BuildReport(
            Document document,
            int totalInstances,
            List<SnapshotRow> rows,
            Dictionary<string, int> missingCounts,
            Dictionary<string, int> absentCounts)
        {
            var output = new StringBuilder();
            AppendCsvRow(output, "Rapport", "Felles BIM-kontroll logg v" + ScriptVersion);
            AppendCsvRow(output, "Dokument", CleanCell(document.Title));
            AppendCsvRow(output, "Opprettet", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            AppendCsvRow(output, "Instanser undersøkt", totalInstances.ToString(CultureInfo.InvariantCulture));
            AppendCsvRow(output, "Elementer i rapporten", rows.Count.ToString(CultureInfo.InvariantCulture));
            for (int index = 0; index < ParameterNames.Length; index++)
            {
                string name = ParameterNames[index];
                AppendCsvRow(output, "Manglende verdier " + name + " (blank/--)", missingCounts[name].ToString(CultureInfo.InvariantCulture));
                AppendCsvRow(output, "Parameter ikke til stede " + name, absentCounts[name].ToString(CultureInfo.InvariantCulture));
            }

            output.AppendLine();
            AppendCsvRow(output, "FOB_Leveransepakke", "PGF_Mengdetype", "FOB_Merkestreng", "ElementId");
            foreach (SnapshotRow row in rows)
            {
                AppendCsvRow(output,
                    row.Package,
                    row.QuantityType,
                    row.MarkString,
                    row.ElementId.ToString(CultureInfo.InvariantCulture));
            }
            return output.ToString();
        }

        private static void AppendCsvRow(StringBuilder output, params string[] values)
        {
            output.AppendLine(string.Join(";", values.Select(value => "\"" + CleanCell(value).Replace("\"", "\"\"", StringComparison.Ordinal) + "\"")));
        }

        private static string GetParameterValue(Parameter parameter)
        {
            if (!parameter.HasValue)
            {
                return string.Empty;
            }
            string? value = parameter.StorageType == StorageType.String
                ? parameter.AsString()
                : parameter.AsValueString();
            return CleanCell(value ?? string.Empty);
        }

        private static string CleanCell(string value)
        {
            return value.Replace("\t", " ").Replace("\r", " ").Replace("\n", " ").Trim();
        }

        private static bool IsMissingValue(string value)
        {
            return string.IsNullOrWhiteSpace(value) || string.Equals(value, "--", StringComparison.Ordinal);
        }

        private static bool IsCenterLine(Element element)
        {
            long categoryId = element.Category?.Id.Value ?? long.MinValue;
            return CenterLineCategoryIds.Contains(categoryId);
        }

        private static string GetUniqueReportPath()
        {
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
            string path = Path.Combine(OutputDirectory, "Felles BIM-kontroll logg " + timestamp + ".csv");
            int suffix = 1;
            while (File.Exists(path))
            {
                path = Path.Combine(OutputDirectory, "Felles BIM-kontroll logg " + timestamp + "_" + suffix.ToString(CultureInfo.InvariantCulture) + ".csv");
                suffix++;
            }
            return path;
        }
    }
}