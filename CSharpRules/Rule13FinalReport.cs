#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace CW.Assistant.Generated
{
    internal sealed class GeneratedAction
    {
        private const string ScriptVersion = "0.0.15";
        private const string OutputDirectory = @"D:\Revit\Python\Revit_BIM_Agent\logs\csv";
        private const string ReportOutputDirectory = @"D:\Revit\Python\Revit_BIM_Agent\logs\reports";
        private const string RuleSourceDirectory = @"D:\Revit\Python\Revit_BIM_Agent\CSharpRules";
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
                return "FEIL: Ingen aktiv Revit-modell. Regel 13 stoppet før elementlesing.";
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
                    foreach (string name in ParameterNames) absentCounts[name]++;
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
                    if (IsMissingValue(values[index])) missingCounts[ParameterNames[index]]++;
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
                return "Regel 13 v" + ScriptVersion + ": rapporten kunne ikke skrives: " + exception.Message;
            }

            string excelReportPath = string.Empty;
            string excelReportError = string.Empty;
            try
            {
                Directory.CreateDirectory(ReportOutputDirectory);
                List<string[]> sourceFollowupRows = BuildFollowupRows(activeDocument);
                List<string[]> followupRows = FilterFollowupsToK5B(
                    activeDocument, sourceFollowupRows,
                    out int filteredOut, out int unscopedRows,
                    out int k5bForingsveiCount, out int k5bUnionCount,
                    out int k5bRunFittingCount, out int k5bAspirationExcludedCount,
                    out int k5bStrømskinneBendExcludedCount,
                    out int k5bMissingLengthCount);
                List<string[]> overviewRows = BuildK5BOverviewRows(
                    followupRows, filteredOut, unscopedRows,
                    k5bForingsveiCount, k5bUnionCount, k5bRunFittingCount,
                    k5bAspirationExcludedCount, k5bStrømskinneBendExcludedCount, k5bMissingLengthCount);
                List<string[]> ruleVersionRows = BuildRuleVersionRows();
                excelReportPath = GetUniqueExcelReportPath();
                WriteFollowupExcelReport(excelReportPath, overviewRows, followupRows, ruleVersionRows);
            }
            catch (Exception exception)
            {
                excelReportError = " Excel-oppfølgingsrapporten kunne ikke skrives: " + exception.Message;
            }

            return string.Format(
                CultureInfo.InvariantCulture,
                "Regel 13 v{0}: skrivebeskyttet snapshot; instanser undersøkt {1}; elementrader {2}; manglende verdier Leveransepakke/Mengdetype/Merkestreng {3}/{4}/{5}; parametere ikke til stede {6}/{7}/{8}; CSV {9}; Excel-oppfølging {10}.{11}",
                ScriptVersion,
                totalInstances,
                rows.Count,
                missingCounts[ParameterNames[0]],
                missingCounts[ParameterNames[1]],
                missingCounts[ParameterNames[2]],
                absentCounts[ParameterNames[0]],
                absentCounts[ParameterNames[1]],
                absentCounts[ParameterNames[2]],
                reportPath,
                excelReportPath.Length == 0 ? "ikke opprettet" : excelReportPath,
                excelReportError);
        }

        private static List<string[]> BuildOverviewRows(
            int totalInstances,
            int reportRows,
            Dictionary<string, int> missingCounts,
            Dictionary<string, int> absentCounts,
            int followupCount)
        {
            var rows = new List<string[]>
            {
                new[] { "Regel", "Kjøringsstatus", "Kildeversjon", "Sist kjørt versjon", "Siste loggtid", "Kontrollresultat", "Oppfølgingspunkter", "Kilde" }
            };

            for (int ruleNumber = 1; ruleNumber <= 12; ruleNumber++)
            {
                string fileName = GetRuleLogFileName(ruleNumber);
                List<string> lines = fileName.Length == 0
                    ? new List<string>()
                    : ReadLatestRuleLog(ruleNumber, fileName);
                string sourceVersion = GetRuleSourceVersion(ruleNumber);
                string runVersion = GetRuleLogVersion(lines);
                int issueCount = CountRuleIssues(ruleNumber, lines);
                string status = GetRuleRunStatus(ruleNumber, lines, sourceVersion, runVersion, issueCount);
                string summary = GetRuleSummary(ruleNumber, lines);
                if (summary.Length == 0)
                {
                    summary = lines.Count == 0
                        ? "Ingen varig kjøringslogg tilgjengelig."
                        : "Kjøringslogg finnes, men ingen standardisert oppsummeringslinje ble funnet.";
                }

                rows.Add(new[]
                {
                    "Regel " + ruleNumber.ToString(CultureInfo.InvariantCulture),
                    status,
                    sourceVersion,
                    runVersion,
                    GetRuleLogTimestamp(lines),
                    summary,
                    issueCount.ToString(CultureInfo.InvariantCulture),
                    fileName.Length == 0 ? "Ingen historikkfil" : Path.Combine("logs", "history", fileName)
                });
            }

            rows.Add(new[]
            {
                "Regel 13",
                "Snapshot opprettet",
                ScriptVersion,
                ScriptVersion,
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                string.Format(CultureInfo.InvariantCulture,
                    "Instanser undersøkt {0}; elementrader i CSV-snapshot {1}; oppfølgingsrader i Excel {2}; manglende Leveransepakke/Mengdetype/Merkestreng {3}/{4}/{5}; parametere ikke til stede {6}/{7}/{8}.",
                    totalInstances, reportRows, followupCount,
                    missingCounts[ParameterNames[0]], missingCounts[ParameterNames[1]], missingCounts[ParameterNames[2]],
                    absentCounts[ParameterNames[0]], absentCounts[ParameterNames[1]], absentCounts[ParameterNames[2]]),
                followupCount.ToString(CultureInfo.InvariantCulture),
                Path.Combine("logs", "csv")
            });
            rows.Add(new[]
            {
                "Datagrunnlag",
                "Siste kjøringslogger",
                string.Empty,
                string.Empty,
                string.Empty,
                "Excel-arket Manuell oppfølging inneholder bare loggførte avvik og aggregerte funn uten ElementId. Full modelliste ligger separat i CSV-snapshotet.",
                string.Empty,
                Path.Combine("logs", "history")
            });
            return rows;
        }

        private static string GetRuleLogTimestamp(List<string> lines)
        {
            string? header = lines.FirstOrDefault(line => line.StartsWith("=== Regel ", StringComparison.Ordinal));
            if (header is null) return string.Empty;
            Match isoTimestamp = Regex.Match(header, "[0-9]{4}-[0-9]{2}-[0-9]{2}T[^ |]+", RegexOptions.CultureInvariant);
            if (isoTimestamp.Success) return isoTimestamp.Value;
            int firstSeparator = header.IndexOf('|');
            if (firstSeparator < 0) return string.Empty;
            int secondSeparator = header.IndexOf('|', firstSeparator + 1);
            if (secondSeparator < 0) return string.Empty;
            return header.Substring(firstSeparator + 1, secondSeparator - firstSeparator - 1).Trim();
        }

        private static List<string[]> BuildFollowupRows(Document document)
        {
            var rows = new List<string[]>
            {
                new[] { "Regel", "ElementId", "Parameter / område", "Funn", "Anbefalt sjekk", "Kilde" }
            };

            for (int ruleNumber = 2; ruleNumber <= 12; ruleNumber++)
            {
                string fileName = GetRuleLogFileName(ruleNumber);
                if (fileName.Length == 0) continue;
                List<string> lines = ReadLatestRuleLog(ruleNumber, fileName);
                foreach (string line in lines)
                {
                    if (line.StartsWith("UAVKLART ", StringComparison.Ordinal))
                    {
                        if (ruleNumber == 7 && TryGetIssueElementId(line, out long elementId))
                        {
                            Element? element = document.GetElement(new ElementId(elementId));
                            if (IsExactK5B(element) && IsAspirationElement(element)) continue;
                        }
                        AddUnresolvedLogRow(rows, ruleNumber, fileName, line);
                    }
                }

                if (ruleNumber == 4 && lines.Count == 0)
                {
                    AddFollowupRow(rows, 4, string.Empty, "RIS-lenke",
                        "Ingen Regel 4-kjøringslogg finnes for denne kontrollen.",
                        "Avklar om RIS-lenken var relevant og lastet. Hvis regelen skal kjøres, last U_F_BAS_FBU_RIS_XXX.rvt og kjør Regel 4.", fileName);
                }
                else if (ruleNumber == 4)
                {
                    foreach (string line in lines.Where(item => item.StartsWith("STOPPET:", StringComparison.Ordinal)
                        || item.StartsWith("BLOKKERING:", StringComparison.Ordinal)))
                    {
                        AddFollowupRow(rows, 4, string.Empty, "RIS-lenke", line,
                            "Avklar lenkestatus og kjør regelen på nytt bare dersom lenken er nødvendig.", fileName);
                    }
                }

                if (ruleNumber == 5)
                {
                    AddCountFollowup(rows, 5, fileName, lines,
                        "SUMMARY: Connector devices mangler markør/rom/familie: ",
                        "Datagrunnlag for Load Name", "Kontroller markør, romnummer, romnavn og familie. Loggen har ikke ElementId per funn.");
                }
                else if (ruleNumber == 7)
                {
                    AddRegexCountFollowup(rows, 7, fileName, lines,
                        new Regex("mangler lengde ([0-9]+)", RegexOptions.CultureInvariant),
                        "FOB_Mengde / lengde", "Identifiser elementene og avklar lengdegrunnlaget. Regel 7-loggen har ikke ElementId for disse funnene.");
                }
                else if (ruleNumber == 8)
                {
                    AddInfoNodeFollowup(rows, fileName, lines);
                }
                else if (ruleNumber == 9)
                {
                    AddRegexCountFollowup(rows, 9, fileName, lines,
                        new Regex("feilende elementer ([0-9]+)", RegexOptions.CultureInvariant),
                        "Brannalarmparametere", "Se Regel 9-avviksrapporten for ElementId og avvik.");
                }
                else if (ruleNumber == 11)
                {
                    AddWorksetFollowup(rows, fileName, lines);
                }
            }

            return rows;
        }

        private static List<string[]> FilterFollowupsToK5B(
            Document document,
            List<string[]> sourceRows,
            out int filteredOut,
            out int unscopedRows,
            out int k5bForingsveiCount,
            out int k5bUnionCount,
            out int k5bRunFittingCount,
            out int k5bAspirationExcludedCount,
            out int k5bStrømskinneBendExcludedCount,
            out int k5bMissingLengthCount)
        {
            var rows = new List<string[]>
            {
                new[] { "Regel", "ElementId", "FOB_Entreprise", "Parameter", "Funn", "Anbefalt sjekk", "Kilde" }
            };
            filteredOut = 0;
            unscopedRows = 0;

            foreach (string[] row in sourceRows.Skip(1))
            {
                if (row.Length < 2) continue;
                if (row[0] == "Regel 7" && string.IsNullOrWhiteSpace(row[1]))
                {
                    continue;
                }
                if (!long.TryParse(row[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out long elementId))
                {
                    unscopedRows++;
                    continue;
                }

                if (!IsExactK5B(document.GetElement(new ElementId(elementId))))
                {
                    filteredOut++;
                    continue;
                }

                rows.Add(new[]
                {
                    row[0],
                    row[1],
                    "K5B",
                    CleanCell(row.Length > 2 ? row[2] : string.Empty),
                    CleanCell(row.Length > 3 ? row[3] : string.Empty),
                    CleanCell(row.Length > 4 ? row[4] : string.Empty),
                    CleanCell(row.Length > 5 ? row[5] : string.Empty)
                });
            }

            k5bForingsveiCount = 0;
            k5bUnionCount = 0;
            k5bRunFittingCount = 0;
            k5bAspirationExcludedCount = 0;
            k5bStrømskinneBendExcludedCount = 0;
            k5bMissingLengthCount = 0;
            BuiltInCategory[] foringsveiCategories =
            {
                BuiltInCategory.OST_Conduit,
                BuiltInCategory.OST_CableTray,
                BuiltInCategory.OST_ConduitFitting,
                BuiltInCategory.OST_CableTrayFitting
            };
            foreach (BuiltInCategory category in foringsveiCategories)
            {
                foreach (Element element in new FilteredElementCollector(document)
                    .OfCategory(category)
                    .WhereElementIsNotElementType())
                {
                    if (!IsExactK5B(element)) continue;
                    if (IsAspirationElement(element))
                    {
                        k5bAspirationExcludedCount++;
                        continue;
                    }
                    if (IsStrømskinneBend(element))
                    {
                        k5bStrømskinneBendExcludedCount++;
                        continue;
                    }
                    if (IsCableTrayUnion(element))
                    {
                        k5bUnionCount++;
                        continue;
                    }
                    if (IsCableTrayRunLengthFitting(element))
                    {
                        k5bRunFittingCount++;
                        continue;
                    }
                    k5bForingsveiCount++;
                    bool fitting = category == BuiltInCategory.OST_ConduitFitting
                        || category == BuiltInCategory.OST_CableTrayFitting;
                    if (GetForingsveiLengthMillimeters(element, fitting) is not null) continue;

                    k5bMissingLengthCount++;
                    rows.Add(new[]
                    {
                        "Regel 7",
                        element.Id.Value.ToString(CultureInfo.InvariantCulture),
                        "K5B",
                        "FOB_Mengde",
                        "K5B-element mangler lesbar lengde i føringsveikontrollen.",
                        "Identifiser elementet og avklar lengdegrunnlaget.",
                        Path.Combine("logs", "history", "Rule07_QuantityForingsways.log")
                    });
                }
            }
            return rows;
        }

        private static List<string[]> BuildK5BOverviewRows(
            List<string[]> followupRows,
            int filteredOut,
            int unscopedRows,
            int k5bForingsveiCount,
            int k5bUnionCount,
            int k5bRunFittingCount,
            int k5bAspirationExcludedCount,
            int k5bStrømskinneBendExcludedCount,
            int k5bMissingLengthCount)
        {
            int rule2Rows = followupRows.Count(row => row.Length > 0 && row[0] == "Regel 2");
            int rule3Rows = followupRows.Count(row => row.Length > 0 && row[0] == "Regel 3");
            List<string> rule2Log = ReadLatestRuleLog(2, GetRuleLogFileName(2));
            List<string> rule3Log = ReadLatestRuleLog(3, GetRuleLogFileName(3));
            int rule2Logged = rule2Log.Count(line => line.StartsWith("UAVKLART ", StringComparison.Ordinal));
            int rule3Logged = rule3Log.Count(line => line.StartsWith("UAVKLART ", StringComparison.Ordinal));
            var rows = new List<string[]>
            {
                new[] { "Kontroll", "Avgrensning", "Resultat", "Antall", "Kommentar" },
                new[] { "Entreprisefilter", "FOB_Entreprise er én String-instansparameter lik K5B", "Eksakt treff", "K5B", "Andre, blanke, manglende og ugyldige entrepriser er filtrert ut." },
                new[] { "Regel 2", "Kun ElementId verifisert som K5B i aktiv modell", "Uavklarte leveransepakker", rule2Rows.ToString(CultureInfo.InvariantCulture), string.Format(CultureInfo.InvariantCulture, "K5B-funn {0} av {1} loggførte; resterende elementrader filtrert bort.", rule2Rows, rule2Logged) },
                new[] { "Regel 3", "Kun ElementId verifisert som K5B i aktiv modell", "Uavklarte parameterfunn", rule3Rows.ToString(CultureInfo.InvariantCulture), string.Format(CultureInfo.InvariantCulture, "K5B-funn {0} av {1} loggførte; resterende elementrader filtrert bort.", rule3Rows, rule3Logged) },
                new[] { "Regel 7", "Eksakt K5B-målsett kontrollert på nytt i aktiv modell", "Kontrollert / Aspirasjon / strømskinne / Union / T-kryss / mangler lengde", string.Format(CultureInfo.InvariantCulture, "{0} / {1} / {2} / {3} / {4} / {5}", k5bForingsveiCount, k5bAspirationExcludedCount, k5bStrømskinneBendExcludedCount, k5bUnionCount, k5bRunFittingCount, k5bMissingLengthCount), "Aspirasjon og strømskinne-bend utelates; Union utelates fra lengdekravet. T/kryss får mengde 1 stk." },
                new[] { "Andre entrepriser", "FOB_Entreprise ikke eksakt K5B", "Elementrader filtrert bort", filteredOut.ToString(CultureInfo.InvariantCulture), "Ingen av disse ElementId-ene vises i oppfølgingsarket." },
                new[] { "Uten enterpriselement", "Funn uten verifiserbar ElementId-avgrensning", "Aggregerte funn utelatt", unscopedRows.ToString(CultureInfo.InvariantCulture), "Ikke-elementspesifikke funn er ikke tatt med i K5B-lista." },
                new[] { "Omfang", "K5B-only", "Elementrader i oppfølgingsarket", (rule2Rows + rule3Rows + k5bMissingLengthCount).ToString(CultureInfo.InvariantCulture), "Full modelliste ligger separat i CSV-snapshotet og er ikke del av denne Excel-lista." }
            };
            return rows;
        }

        private static string GetRuleSourceVersion(int ruleNumber)
        {
            string fileName = GetRuleSourceFileName(ruleNumber);
            if (fileName.Length == 0) return string.Empty;
            string path = Path.Combine(RuleSourceDirectory, fileName);
            if (!File.Exists(path)) return "Kilde mangler";
            Match match = Regex.Match(File.ReadAllText(path),
                "private\\s+const\\s+string\\s+ScriptVersion\\s*=\\s*\"([^\"]+)\"",
                RegexOptions.CultureInvariant);
            return match.Success ? match.Groups[1].Value : "Ikke versjonert";
        }

        private static string GetRuleLogVersion(List<string> lines)
        {
            string? header = lines.FirstOrDefault(line => line.StartsWith("=== Regel ", StringComparison.Ordinal));
            if (header is null) return string.Empty;
            Match match = Regex.Match(header, "\\bv([0-9]+(?:\\.[0-9]+)+)(?:\\s|\\|)", RegexOptions.CultureInvariant);
            return match.Success ? match.Groups[1].Value : string.Empty;
        }

        private static string GetRuleRunStatus(int ruleNumber, List<string> lines, string sourceVersion,
            string logVersion, int issueCount)
        {
            if (ruleNumber == 1) return "Ingen per-regel logg";
            if (sourceVersion.Length == 0) return "Kildeversjon mangler";
            if (sourceVersion == "Kilde mangler") return sourceVersion;
            if (lines.Count == 0 || logVersion.Length == 0) return "Kildeversjon ikke kjørt";
            if (!string.Equals(sourceVersion, logVersion, StringComparison.Ordinal)) return "Kilde oppdatert - ny kjøring mangler";
            string? header = lines.FirstOrDefault(line => line.StartsWith("=== Regel ", StringComparison.Ordinal));
            if (header is not null && header.IndexOf("målrettet", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Delkjøring - full regelkjøring ikke bekreftet";
            return issueCount > 0 ? "Gjeldende versjon kjørt - avvik" : "Gjeldende versjon kjørt";
        }

        private static string GetRuleSourceFileName(int ruleNumber)
        {
            return ruleNumber switch
            {
                1 => "Rule01SynchronizeElementId.cs",
                2 => "Rule02FillMissingPackage.cs",
                3 => "Rule03ParameterFill.cs",
                4 => "Rule04CopyAlarmParentData.cs",
                5 => "Rule05LoadNameToDataCircuits.cs",
                6 => "Rule06CleanMekConnectionPoints.cs",
                7 => "Rule07QuantityForingsways.cs",
                8 => "Rule08CheckInfoNodes.cs",
                9 => "Rule09SyncAlarmSystemMark.cs",
                10 => "Rule10MagicadSystemFromConnectedStraight.cs",
                11 => "Rule11CheckWorksetCategory.cs",
                12 => "Rule12RevisionParameters.cs",
                _ => string.Empty
            };
        }

        private static List<string[]> BuildRuleVersionRows()
        {
            var rows = new List<string[]>
            {
                new[] { "Regel", "Kildeversjon", "Sist kjørt versjon", "Siste loggtid", "Kjøringsstatus", "Kilde", "Siste kjøringssammendrag" }
            };
            for (int ruleNumber = 1; ruleNumber <= 12; ruleNumber++)
            {
                string sourceFile = GetRuleSourceFileName(ruleNumber);
                string sourceVersion = GetRuleSourceVersion(ruleNumber);
                string logFile = GetRuleLogFileName(ruleNumber);
                List<string> lines = logFile.Length == 0 ? new List<string>() : ReadLatestRuleLog(ruleNumber, logFile);
                string logVersion = GetRuleLogVersion(lines);
                rows.Add(new[]
                {
                    "Regel " + ruleNumber.ToString(CultureInfo.InvariantCulture),
                    sourceVersion,
                    logVersion,
                    GetRuleLogTimestamp(lines),
                    GetRuleRunStatus(ruleNumber, lines, sourceVersion, logVersion, CountRuleIssues(ruleNumber, lines)),
                    sourceFile.Length == 0 ? "Ingen historikkfil" : Path.Combine("CSharpRules", sourceFile),
                    CleanCell(GetRuleSummary(ruleNumber, lines))
                });
            }
            rows.Add(new[]
            {
                "Regel 13", ScriptVersion, ScriptVersion,
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                "Rapport generert", Path.Combine("CSharpRules", "Rule13FinalReport.cs"),
                "K5B-avgrenset Excel-rapport og full CSV-snapshot opprettet."
            });
            return rows;
        }

        private static string GetCableTrayPartTypeName(FamilyInstance familyInstance)
        {
            Parameter? parameter = familyInstance.Symbol?.Family?.get_Parameter(BuiltInParameter.FAMILY_CONTENT_PART_TYPE)
                ?? familyInstance.Symbol?.get_Parameter(BuiltInParameter.FAMILY_CONTENT_PART_TYPE)
                ?? familyInstance.get_Parameter(BuiltInParameter.FAMILY_CONTENT_PART_TYPE);
            return parameter is not null && parameter.HasValue && parameter.StorageType == StorageType.Integer
                ? Enum.GetName(typeof(PartType), parameter.AsInteger()) ?? string.Empty
                : string.Empty;
        }

        private static bool IsExactK5B(Element? element)
        {
            if (element is null) return false;
            IList<Parameter> parameters = element.GetParameters("FOB_Entreprise");
            return parameters.Count == 1
                && parameters[0].StorageType == StorageType.String
                && string.Equals(parameters[0].AsString(), "K5B", StringComparison.Ordinal);
        }

        private static bool IsAspirationElement(Element? element)
        {
            if (element is not FamilyInstance instance) return false;
            string familyName = instance.Symbol?.Family?.Name ?? string.Empty;
            string typeName = instance.Symbol?.Name ?? string.Empty;
            string instanceName = element.Name ?? string.Empty;
            return ContainsAspirationToken(familyName)
                || ContainsAspirationToken(typeName)
                || ContainsAspirationToken(instanceName);
        }

        private static bool ContainsAspirationToken(string value)
        {
            return value.IndexOf("aspirasjon", StringComparison.OrdinalIgnoreCase) >= 0
                || value.IndexOf("aspiration", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool TryGetIssueElementId(string line, out long elementId)
        {
            const string prefix = "UAVKLART ElementId ";
            elementId = 0;
            if (!line.StartsWith(prefix, StringComparison.Ordinal)) return false;
            int separator = line.IndexOfAny(new[] { ',', ':' }, prefix.Length);
            return separator > prefix.Length
                && long.TryParse(line.Substring(prefix.Length, separator - prefix.Length),
                    NumberStyles.Integer, CultureInfo.InvariantCulture, out elementId);
        }

        private static bool IsCableTrayUnion(Element element)
        {
            if (element.Category?.Id.Value != new ElementId(BuiltInCategory.OST_CableTrayFitting).Value
                || element is not FamilyInstance familyInstance)
            {
                return false;
            }

            Parameter? partTypeParameter = familyInstance.Symbol?.Family?.get_Parameter(BuiltInParameter.FAMILY_CONTENT_PART_TYPE)
                ?? familyInstance.Symbol?.get_Parameter(BuiltInParameter.FAMILY_CONTENT_PART_TYPE)
                ?? element.get_Parameter(BuiltInParameter.FAMILY_CONTENT_PART_TYPE);
            if (partTypeParameter is null || !partTypeParameter.HasValue
                || partTypeParameter.StorageType != StorageType.Integer)
            {
                return false;
            }

            string? partType = Enum.GetName(typeof(PartType), partTypeParameter.AsInteger());
            return partType is "Union" or "ChannelCableTrayUnion" or "LadderCableTrayUnion";
        }

        private static bool IsStrømskinneBend(Element element)
        {
            if (element.Category?.Id.Value != new ElementId(BuiltInCategory.OST_CableTrayFitting).Value
                || element is not FamilyInstance familyInstance)
            {
                return false;
            }
            string familyName = familyInstance.Symbol?.Family?.Name ?? string.Empty;
            string typeName = familyInstance.Symbol?.Name ?? string.Empty;
            return (familyName.IndexOf("strømskinne", StringComparison.OrdinalIgnoreCase) >= 0
                    || typeName.IndexOf("strømskinne", StringComparison.OrdinalIgnoreCase) >= 0)
                && GetCableTrayPartTypeName(familyInstance).IndexOf("Elbow", StringComparison.Ordinal) >= 0;
        }

        private static bool IsCableTrayRunLengthFitting(Element element)
        {
            if (element.Category?.Id.Value != new ElementId(BuiltInCategory.OST_CableTrayFitting).Value
                || element is not FamilyInstance familyInstance)
            {
                return false;
            }

            Parameter? partTypeParameter = familyInstance.Symbol?.Family?.get_Parameter(BuiltInParameter.FAMILY_CONTENT_PART_TYPE)
                ?? familyInstance.Symbol?.get_Parameter(BuiltInParameter.FAMILY_CONTENT_PART_TYPE)
                ?? element.get_Parameter(BuiltInParameter.FAMILY_CONTENT_PART_TYPE);
            if (partTypeParameter is null || !partTypeParameter.HasValue
                || partTypeParameter.StorageType != StorageType.Integer)
            {
                return false;
            }

            string? partType = Enum.GetName(typeof(PartType), partTypeParameter.AsInteger());
            return partType is "Tee" or "Cross" or "ChannelCableTrayTee" or "ChannelCableTrayCross"
                or "LadderCableTrayTee" or "LadderCableTrayCross";
        }

        private static double? GetForingsveiLengthMillimeters(Element element, bool fitting)
        {
            if (element.Id.Value is 17760381L or 17760457L
                && element.Category?.Id.Value == new ElementId(BuiltInCategory.OST_ConduitFitting).Value)
            {
                IList<Parameter> conduitLengthParameters = element.GetParameters("Conduit Length");
                if (conduitLengthParameters.Count == 1)
                {
                    double? conduitLength = GetLengthParameterMillimeters(conduitLengthParameters[0]);
                    if (conduitLength is not null && conduitLength.Value > 0.0) return conduitLength;
                }
            }
            if (!fitting)
            {
                double? curveLength = GetLengthParameterMillimeters(element.get_Parameter(BuiltInParameter.CURVE_ELEM_LENGTH));
                if (curveLength is not null) return curveLength;
            }
            foreach (string parameterName in new[] { "Arc Length", "PGF_Length_Bend", "Length 3" })
            {
                double? parameterLength = GetLengthParameterMillimeters(element.LookupParameter(parameterName));
                if (parameterLength is not null && parameterLength.Value > 0.0) return parameterLength;
            }
            if (element.Location is LocationCurve locationCurve && locationCurve.Curve is not null)
            {
                return UnitUtils.ConvertFromInternalUnits(locationCurve.Curve.Length, UnitTypeId.Millimeters);
            }
            return null;
        }

        private static double? GetLengthParameterMillimeters(Parameter? parameter)
        {
            if (parameter is null || parameter.StorageType != StorageType.Double || !parameter.HasValue) return null;
            return UnitUtils.ConvertFromInternalUnits(parameter.AsDouble(), UnitTypeId.Millimeters);
        }

        private static void AddUnresolvedLogRow(List<string[]> rows, int ruleNumber, string fileName, string line)
        {
            Match match = Regex.Match(line,
                "^UAVKLART ElementId ([0-9]+)(?:, ([^:]+): |: )(.*)$",
                RegexOptions.CultureInvariant);
            if (!match.Success)
            {
                AddFollowupRow(rows, ruleNumber, string.Empty, "Uavklart", line,
                    "Undersøk funnet i aktiv modell før eventuell retting.", fileName);
                return;
            }

            string elementId = match.Groups[1].Value;
            string parameterName = match.Groups[2].Success
                ? match.Groups[2].Value.Trim()
                : ruleNumber == 2 ? "FOB_Leveransepakke" : "Parameter ikke angitt";
            string reason = match.Groups[3].Value.Trim();
            string action = GetRecommendedAction(reason, parameterName);
            AddFollowupRow(rows, ruleNumber, elementId, parameterName, reason, action, fileName);
        }

        private static string GetRecommendedAction(string reason, string parameterName)
        {
            if (reason.IndexOf("streng pakke-majoritet", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Avklar korrekt leveransepakke fra prosjektgrunnlaget; ikke gjett ut fra nærhet.";
            if (reason.IndexOf("ingen kandidat innen 3000 mm", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Finn og kontroller leveransepakken manuelt.";
            if (reason.IndexOf("utenfor K5B eller kan ikke avklares", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Kontroller FOB_Entreprise. Eksplisitt annen entreprise er utenfor omfang; blank/uklar entreprise må avklares.";
            if (reason.IndexOf("flere parametere med samme navn", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Avklar dupliserte parametere før en verdi fylles eller korrigeres.";
            if (reason.IndexOf("ingen direkte tilkoblet rett føringsvei", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Kontroller tilgjengelig tilkoblet rettstrekk og kildeverdien; ikke opprett forbindelse automatisk.";
            if (reason.IndexOf("ulike FOB_Mengdelistepost-verdier", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Avklar hvilken fysisk tilkoblet kildeverdi som er faglig riktig; ikke gjett.";
            return "Undersøk funnet i aktiv modell og avklar korrekt verdi etter fagregelen.";
        }

        private static void AddCountFollowup(List<string[]> rows, int ruleNumber, string fileName,
            List<string> lines, string prefix, string area, string action)
        {
            string? line = lines.FirstOrDefault(item => item.StartsWith(prefix, StringComparison.Ordinal));
            if (line is null) return;
            string countText = line.Substring(prefix.Length).Trim();
            if (!int.TryParse(countText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int count) || count <= 0) return;
            AddFollowupRow(rows, ruleNumber, string.Empty, area, countText + " funn. " + line, action, fileName);
        }

        private static void AddRegexCountFollowup(List<string[]> rows, int ruleNumber, string fileName,
            List<string> lines, Regex pattern, string area, string action)
        {
            foreach (string line in lines)
            {
                Match match = pattern.Match(line);
                if (!match.Success || !int.TryParse(match.Groups[1].Value, NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out int count) || count <= 0) continue;
                AddFollowupRow(rows, ruleNumber, string.Empty, area,
                    count.ToString(CultureInfo.InvariantCulture) + " funn. " + line, action, fileName);
                return;
            }
        }

        private static void AddInfoNodeFollowup(List<string[]> rows, string fileName, List<string> lines)
        {
            string? summary = lines.FirstOrDefault(line => line.StartsWith("InfoNodes ", StringComparison.Ordinal));
            if (summary is null) return;
            Match match = Regex.Match(summary,
                "manglende HostID ([0-9]+); dupliserte HostID-verdier ([0-9]+);.*subs-konflikter ([0-9]+); tomme subs ([0-9]+)",
                RegexOptions.CultureInvariant);
            if (!match.Success) return;
            int findings = match.Groups.Cast<System.Text.RegularExpressions.Group>().Skip(1)
                .Sum(group => int.TryParse(group.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : 0);
            if (findings == 0) return;
            AddFollowupRow(rows, 8, string.Empty, "InfoNode HostID/subs", summary,
                "Se den tidsstemplede InfoNode-Excel-rapporten for elementdetaljer.", fileName);
        }

        private static void AddWorksetFollowup(List<string[]> rows, string fileName, List<string> lines)
        {
            string? summary = lines.FirstOrDefault(line => line.StartsWith("Mappinger lest:", StringComparison.Ordinal));
            if (summary is null) return;
            Match match = Regex.Match(summary, "uløste avvik: ([0-9]+); blokkeringer: ([0-9]+)", RegexOptions.CultureInvariant);
            if (!match.Success || match.Groups.Cast<System.Text.RegularExpressions.Group>().Skip(1)
                .All(group => int.TryParse(group.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) && value == 0)) return;
            AddFollowupRow(rows, 11, string.Empty, "Workset", summary,
                "Se Regel 11-CSV-rapporten for ElementId og forventet/faktisk workset.", fileName);
        }

        private static void AddFollowupRow(List<string[]> rows, int ruleNumber, string elementId,
            string parameter, string finding, string action, string fileName)
        {
            rows.Add(new[]
            {
                "Regel " + ruleNumber.ToString(CultureInfo.InvariantCulture),
                elementId,
                CleanCell(parameter),
                CleanCell(finding),
                CleanCell(action),
                Path.Combine("logs", "history", fileName)
            });
        }

        private static int CountRuleIssues(int ruleNumber, List<string> lines)
        {
            if (ruleNumber == 4 && lines.Count == 0) return 1;
            if (ruleNumber == 8) return GetInfoNodeIssueCount(lines);
            if (ruleNumber == 9)
            {
                return GetCountFromRegex(lines,
                    new Regex("feilende elementer ([0-9]+)", RegexOptions.CultureInvariant));
            }
            if (ruleNumber == 11) return GetWorksetIssueCount(lines);

            int count = lines.Count(line => line.StartsWith("UAVKLART ", StringComparison.Ordinal)
                || line.StartsWith("BLOKKERING:", StringComparison.Ordinal)
                || line.StartsWith("STOPPET:", StringComparison.Ordinal));
            if (ruleNumber == 5)
                count += GetCountFromPrefix(lines, "SUMMARY: Connector devices mangler markør/rom/familie: ");
            if (ruleNumber == 7)
                count += GetCountFromRegex(lines, new Regex("mangler lengde ([0-9]+)", RegexOptions.CultureInvariant));
            return count;
        }

        private static int GetInfoNodeIssueCount(List<string> lines)
        {
            string? summary = lines.FirstOrDefault(line => line.StartsWith("InfoNodes ", StringComparison.Ordinal));
            if (summary is null) return 0;
            Match match = Regex.Match(summary,
                "manglende HostID ([0-9]+); dupliserte HostID-verdier ([0-9]+);.*subs-konflikter ([0-9]+); tomme subs ([0-9]+)",
                RegexOptions.CultureInvariant);
            return match.Success
                ? match.Groups.Cast<System.Text.RegularExpressions.Group>().Skip(1)
                    .Sum(group => int.TryParse(group.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : 0)
                : 0;
        }

        private static int GetWorksetIssueCount(List<string> lines)
        {
            string? summary = lines.FirstOrDefault(line => line.StartsWith("Mappinger lest:", StringComparison.Ordinal));
            if (summary is null) return 0;
            Match match = Regex.Match(summary, "uløste avvik: ([0-9]+); blokkeringer: ([0-9]+)", RegexOptions.CultureInvariant);
            return match.Success
                ? match.Groups.Cast<System.Text.RegularExpressions.Group>().Skip(1)
                    .Sum(group => int.TryParse(group.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : 0)
                : 0;
        }

        private static int GetCountFromPrefix(List<string> lines, string prefix)
        {
            string? line = lines.FirstOrDefault(item => item.StartsWith(prefix, StringComparison.Ordinal));
            return line is not null && int.TryParse(line.Substring(prefix.Length).Trim(), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out int count) ? count : 0;
        }

        private static int GetCountFromRegex(List<string> lines, Regex pattern)
        {
            foreach (string line in lines)
            {
                Match match = pattern.Match(line);
                if (match.Success && int.TryParse(match.Groups[1].Value, NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out int count)) return count;
            }
            return 0;
        }

        private static string GetRuleSummary(int ruleNumber, List<string> lines)
        {
            IEnumerable<string> summaries = ruleNumber switch
            {
                2 => lines.Where(line => line.StartsWith("Instanser ", StringComparison.Ordinal)),
                3 => lines.Where(line => line.StartsWith("Oppsummering:", StringComparison.Ordinal)),
                4 => lines.Where(line => line.StartsWith("STOPPET:", StringComparison.Ordinal)
                    || line.StartsWith("Regel 4 v", StringComparison.Ordinal)),
                5 => lines.Where(line => line.StartsWith("SUMMARY: Spredenett-parents i modell:", StringComparison.Ordinal)
                    || line.StartsWith("SUMMARY: Connector devices mangler markør/rom/familie:", StringComparison.Ordinal)
                    || line.StartsWith("SUMMARY: Resultat:", StringComparison.Ordinal)),
                6 => lines.Where(line => line.StartsWith("Regel 6 v", StringComparison.Ordinal)),
                7 => lines.Where(line => line.StartsWith("K5B-målsett:", StringComparison.Ordinal)
                    || line.StartsWith("Oppdatert ", StringComparison.Ordinal)),
                8 => lines.Where(line => line.StartsWith("InfoNodes ", StringComparison.Ordinal)),
                9 => lines.Where(line => line.StartsWith("Regel 9 v", StringComparison.Ordinal)),
                10 => lines.Where(line => line.StartsWith("Oppsummering:", StringComparison.Ordinal)),
                11 => lines.Where(line => line.StartsWith("Mappinger lest:", StringComparison.Ordinal)),
                12 => lines.Where(line => line.StartsWith("Oppsummering:", StringComparison.Ordinal)),
                _ => Enumerable.Empty<string>()
            };
            return string.Join(" | ", summaries.Select(CleanCell));
        }

        private static string GetRuleLogFileName(int ruleNumber)
        {
            return ruleNumber switch
            {
                1 => string.Empty,
                2 => "Rule02_FOB_Leveransepakke.log",
                3 => "Rule03_ParameterFill.log",
                4 => "Rule04_CopyAlarmParentData.log",
                5 => "Rule05_LoadNameToDataCircuits.log",
                6 => "Rule06_CleanMekConnectionPoints.log",
                7 => "Rule07_QuantityForingsways.log",
                8 => "Rule08_InfoNodeCheck.log",
                9 => "Rule09_SyncAlarmSystemMark.log",
                10 => "Rule10_MagicadSystemFromConnectedStraight.log",
                11 => "Rule11_WorksetCategory.log",
                12 => "Rule12_RevisionParameters.log",
                _ => string.Empty
            };
        }

        private static List<string> ReadLatestRuleLog(int ruleNumber, string fileName)
        {
            string path = Path.Combine(ReportOutputDirectory, "..", "history", fileName);
            if (!File.Exists(path)) return new List<string>();
            string[] lines = File.ReadAllLines(path);
            string header = "=== Regel " + ruleNumber.ToString(CultureInfo.InvariantCulture) + " ";
            int start = Array.FindLastIndex(lines, line => line.StartsWith(header, StringComparison.Ordinal));
            if (start < 0) return new List<string>();
            int end = Array.FindIndex(lines, start + 1, line => line.StartsWith("=== Regel ", StringComparison.Ordinal));
            if (end < 0) end = lines.Length;
            return lines.Skip(start).Take(end - start).ToList();
        }

        private static string GetUniqueExcelReportPath()
        {
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
            string path = Path.Combine(ReportOutputDirectory, "Felles BIM-kontroll oppfølging K5B " + timestamp + ".xlsx");
            int suffix = 1;
            while (File.Exists(path))
            {
                path = Path.Combine(ReportOutputDirectory,
                    "Felles BIM-kontroll oppfølging K5B " + timestamp + "_" + suffix.ToString(CultureInfo.InvariantCulture) + ".xlsx");
                suffix++;
            }
            return path;
        }

        private static void WriteFollowupExcelReport(
            string path,
            List<string[]> overviewRows,
            List<string[]> followupRows,
            List<string[]> ruleVersionRows)
        {
            WriteZipPackage(path, new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["[Content_Types].xml"] = "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/><Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/><Override PartName=\"/xl/worksheets/sheet2.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/><Override PartName=\"/xl/worksheets/sheet3.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/><Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/></Types>",
                ["_rels/.rels"] = "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>",
                ["xl/workbook.xml"] = "<?xml version=\"1.0\" encoding=\"UTF-8\"?><workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets><sheet name=\"Oversikt K5B\" sheetId=\"1\" r:id=\"rId1\"/><sheet name=\"Manuell oppfølging\" sheetId=\"2\" r:id=\"rId2\"/><sheet name=\"Regelversjoner\" sheetId=\"3\" r:id=\"rId3\"/></sheets></workbook>",
                ["xl/_rels/workbook.xml.rels"] = "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/><Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet2.xml\"/><Relationship Id=\"rId3\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet3.xml\"/><Relationship Id=\"rId4\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/></Relationships>",
                ["xl/styles.xml"] = "<?xml version=\"1.0\" encoding=\"UTF-8\"?><styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><fonts count=\"2\"><font><sz val=\"11\"/><name val=\"Calibri\"/></font><font><b/><color rgb=\"FFFFFFFF\"/><sz val=\"11\"/><name val=\"Calibri\"/></font></fonts><fills count=\"3\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill><fill><patternFill patternType=\"solid\"><fgColor rgb=\"FF1F4E78\"/><bgColor indexed=\"64\"/></patternFill></fill></fills><borders count=\"1\"><border><left/><right/><top/><bottom/><diagonal/></border></borders><cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs><cellXfs count=\"2\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/><xf numFmtId=\"0\" fontId=\"1\" fillId=\"2\" borderId=\"0\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\" applyAlignment=\"1\"><alignment vertical=\"center\" wrapText=\"1\"/></xf></cellXfs><cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles></styleSheet>",
                ["xl/worksheets/sheet1.xml"] = BuildWorksheetXml(overviewRows, new[] { 18.0, 24.0, 28.0, 70.0, 18.0, 44.0 }),
                ["xl/worksheets/sheet2.xml"] = BuildWorksheetXml(followupRows, new[] { 18.0, 16.0, 30.0, 68.0, 78.0, 42.0 }),
                ["xl/worksheets/sheet3.xml"] = BuildWorksheetXml(ruleVersionRows, new[] { 16.0, 18.0, 22.0, 30.0, 42.0, 44.0, 70.0 })
            });
        }

        private static string BuildWorksheetXml(List<string[]> rows, double[] columnWidths)
        {
            using var output = new MemoryStream();
            var settings = new XmlWriterSettings { Encoding = new UTF8Encoding(false), OmitXmlDeclaration = false };
            using (XmlWriter writer = XmlWriter.Create(output, settings))
            {
                writer.WriteStartDocument();
                writer.WriteStartElement("worksheet", "http://schemas.openxmlformats.org/spreadsheetml/2006/main");
                string lastColumn = ExcelColumnName(columnWidths.Length);
                writer.WriteStartElement("dimension");
                writer.WriteAttributeString("ref", "A1" + lastColumn + rows.Count.ToString(CultureInfo.InvariantCulture));
                writer.WriteEndElement();
                writer.WriteStartElement("sheetViews");
                writer.WriteStartElement("sheetView");
                writer.WriteAttributeString("workbookViewId", "0");
                writer.WriteStartElement("pane");
                writer.WriteAttributeString("ySplit", "1");
                writer.WriteAttributeString("topLeftCell", "A2");
                writer.WriteAttributeString("activePane", "bottomLeft");
                writer.WriteAttributeString("state", "frozen");
                writer.WriteEndElement();
                writer.WriteEndElement();
                writer.WriteEndElement();
                writer.WriteStartElement("sheetFormatPr");
                writer.WriteAttributeString("defaultRowHeight", "18");
                writer.WriteEndElement();
                writer.WriteStartElement("cols");
                for (int column = 0; column < columnWidths.Length; column++)
                {
                    writer.WriteStartElement("col");
                    writer.WriteAttributeString("min", (column + 1).ToString(CultureInfo.InvariantCulture));
                    writer.WriteAttributeString("max", (column + 1).ToString(CultureInfo.InvariantCulture));
                    writer.WriteAttributeString("width", columnWidths[column].ToString(CultureInfo.InvariantCulture));
                    writer.WriteAttributeString("customWidth", "1");
                    writer.WriteEndElement();
                }
                writer.WriteEndElement();
                writer.WriteStartElement("sheetData");
                for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
                {
                    writer.WriteStartElement("row");
                    writer.WriteAttributeString("r", (rowIndex + 1).ToString(CultureInfo.InvariantCulture));
                    for (int column = 0; column < columnWidths.Length; column++)
                    {
                        string cellReference = ExcelColumnName(column + 1) + (rowIndex + 1).ToString(CultureInfo.InvariantCulture);
                        string value = column < rows[rowIndex].Length ? rows[rowIndex][column] ?? string.Empty : string.Empty;
                        writer.WriteStartElement("c");
                        writer.WriteAttributeString("r", cellReference);
                        writer.WriteAttributeString("t", "inlineStr");
                        if (rowIndex == 0) writer.WriteAttributeString("s", "1");
                        writer.WriteStartElement("is");
                        writer.WriteStartElement("t");
                        writer.WriteAttributeString("xml", "space", "http://www.w3.org/XML/1998/namespace", "preserve");
                        writer.WriteString(value);
                        writer.WriteEndElement();
                        writer.WriteEndElement();
                        writer.WriteEndElement();
                    }
                    writer.WriteEndElement();
                }
                writer.WriteEndElement();
                writer.WriteStartElement("autoFilter");
                writer.WriteAttributeString("ref", "A1" + lastColumn + rows.Count.ToString(CultureInfo.InvariantCulture));
                writer.WriteEndElement();
                writer.WriteEndElement();
                writer.WriteEndDocument();
            }
            return Encoding.UTF8.GetString(output.ToArray());
        }

        private static string ExcelColumnName(int column)
        {
            var name = new StringBuilder();
            while (column > 0)
            {
                int remainder = (column - 1) % 26;
                name.Insert(0, (char)('A' + remainder));
                column = (column - 1) / 26;
            }
            return name.ToString();
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