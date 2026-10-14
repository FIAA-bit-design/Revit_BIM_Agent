#nullable enable
using System;
using System.Collections.Generic;
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

            return "Passed: 12 revision-value cases and 3 report-shape regression cases.";
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
