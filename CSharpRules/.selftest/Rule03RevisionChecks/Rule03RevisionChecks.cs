#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace CW.Assistant.Generated
{
    public static class Rule03RevisionChecks
    {
        public static string Run()
        {
            MethodInfo missing = GetMethod("IsMissingRevisionValue");
            var cases = new (string Value, bool Missing)[]
            {
                ("", true), (" ", true), ("\t\r\n", true), ("--", true), (" -- ", true),
                ("2026.10.15", false), ("FIAA", false), ("03B", false),
                ("any filled text", false), ("-", false), ("---", false), (" 03B ", false)
            };
            foreach (var test in cases)
            {
                bool actual = (bool)missing.Invoke(null, new object[] { test.Value })!;
                Require(actual == test.Missing, "Incorrect completeness result for '" + test.Value + "'.");
            }

            MethodInfo report = GetMethod("ToExcelReportRow");
            var context = new Dictionary<long, (string FamilyName, string MarkString)>
            {
                [123] = ("Family", "Mark")
            };
            string[] row = (string[])report.Invoke(null, new object[]
            {
                "REVISJONSAVVIK ElementId 123, pakke 'LPE''161', FOB_Revisjonsdato = ' -- ': verdien '--' er ikke gyldig.",
                context
            })!;
            Require(row.SequenceEqual(new[]
            {
                "Revisjonsavvik", "123", "Family", "Mark", "FOB_Revisjonsdato", " -- ",
                "Pakke 'LPE'161'; verdien '--' er ikke gyldig"
            }), "Revision report columns or escaped values are incorrect.");

            row = (string[])report.Invoke(null, new object[]
            {
                "REVISJONSAVVIK ElementId 123, pakke '', PGF_Revisjonsign = '': mangler.",
                context
            })!;
            Require(row.Length == 7 && row[0] == "Revisjonsavvik" && row[4] == "PGF_Revisjonsign"
                && row[5] == "" && row[6] == "Pakke ''; mangler", "Missing instance parameter is not reported.");

            row = (string[])report.Invoke(null, new object[]
            {
                "OPPDATERT ElementId 123, FOB_Status = 'S4' (statusregel).", context
            })!;
            Require(row.Length == 7 && row[0] == "Oppdatert" && row[4] == "FOB_Status"
                && row[5] == "S4", "Existing update report behavior changed.");

            MethodInfo approval = GetMethod("RequireApprovedDrofusMetadata");
            var metadata = new Dictionary<string, string>
            {
                ["SchemaVersion"] = "1", ["ModelPath"] = "model", ["Status"] = "Approved"
            };
            approval.Invoke(null, new object[] { metadata, "model" });
            foreach (var invalid in new[] { ("Status", "Pending"), ("SchemaVersion", "2"), ("ModelPath", "other") })
            {
                var candidate = new Dictionary<string, string>(metadata) { [invalid.Item1] = invalid.Item2 };
                bool blocked = false;
                try { approval.Invoke(null, new object[] { candidate, "model" }); }
                catch (TargetInvocationException exception) when (exception.InnerException is InvalidDataException) { blocked = true; }
                Require(blocked, "Unsafe dRofus metadata was accepted: " + invalid.Item1);
            }
            MethodInfo sheetReader = GetMethod("ReadDrofusSheet");
            var referenceMetadata = (List<Dictionary<string, string>>)sheetReader.Invoke(null, new object[] { "Metadata" })!;
            Require(referenceMetadata.Any(r => r["Key"] == "SchemaVersion" && r["Value"] == "1"), "Reference metadata schema was not read.");
            foreach (string sheetName in new[] { "Forekomster", "Segmenter", "Uavklart" })
            {
                var referenceRows = (List<Dictionary<string, string>>)sheetReader.Invoke(null, new object[] { sheetName })!;
                Require(referenceRows.All(r => r.ContainsKey("UniqueId") && r.ContainsKey("ExpectedOccurrenceId")), "Reference headers were not read: " + sheetName);
            }
            return "Passed: 12 revision-value cases, 3 report-shape cases, 4 reference-approval cases and 4 Excel-sheet checks.";
        }

        private static MethodInfo GetMethod(string name)
        {
            return typeof(GeneratedAction).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)
                ?? throw new InvalidOperationException("Missing method " + name + ".");
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
