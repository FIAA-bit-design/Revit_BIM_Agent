using System.IO.Compression;
using System.Text.Json;
using System.Xml.Linq;
using CW.Assistant.Generated;

int checks = 0;
void Check(bool condition, string message)
{
    checks++;
    if (!condition) throw new InvalidOperationException(message);
}
void Reject<TException>(Action action, string message) where TException : Exception
{
    try { action(); }
    catch (TException) { checks++; return; }
    throw new InvalidOperationException(message);
}

string temp = Path.Combine(Path.GetTempPath(), "Rule13RunnerTests_" + Guid.NewGuid().ToString("N"));
if (args.Contains("--jrp"))
{
    Check(Rule13CablingEquipmentReport.ModelCode(Rule13CablingEquipmentReport.ModelPath) == "BAS", "BAS støttes fortsatt.");
    Check(Rule13CablingEquipmentReport.ModelCode(Rule13CablingEquipmentReport.StationModelPath) == "STA", "Eksakt stasjonsmodellsti gir STA-kode.");
    Reject<InvalidOperationException>(() => Rule13CablingEquipmentReport.ModelCode("Autodesk Docs://Fornebubanen/U_F_STA_BES_RIE_XXX.rvt"), "Andre modeller avvises.");
    var marks = Rule13CablingEquipmentReport.ParseJrpMarks("  BYG200-450-JRP-055 \nbyg200-450-jrp-055; 2FD-450-JRP-007\nBYG200-450-JOS-001 BYG200-450-RJB-001 BYG200-450-XJRP-055");
    Check(marks.SequenceEqual(new[] { "BYG200-450-JRP-055", "2FD-450-JRP-007" }), "Hele JRP-merker og duplikatnoter; ikke JOS/RJB/substring.");
    Check(!marks.Contains("450-JRP-055", StringComparer.OrdinalIgnoreCase), "Ikke delstreng.");
    Check(Rule13CablingEquipmentReport.ParseJrpMarks("_BYG200-450-JRP-055 BYG200-450-JRP-055_ -BYG200-450-JRP-055").Length == 0, "Ikke del av større token.");
    Check(Rule13CablingEquipmentReport.ParseJrpMarks("JRP-055 BYG200-450-JRP").Length == 0, "JRP krever segment på begge sider.");
    Check(Rule13CablingEquipmentReport.IsGroundingCandidate(true, true, "jordbolt M10", " K5A ", 0), "Gyldig K5A Jordbolt.");
    Check(!Rule13CablingEquipmentReport.IsGroundingCandidate(true, true, "Jordbolt", "K5B", 0), "Kun K5A.");
    Check(!Rule13CablingEquipmentReport.IsGroundingCandidate(true, true, "Jordbolt", "k5a", 0), "Eksakt entreprise.");
    Check(!Rule13CablingEquipmentReport.IsGroundingCandidate(false, true, "Jordbolt", "K5A", 0), "Kun FamilyInstance.");
    Check(!Rule13CablingEquipmentReport.IsGroundingCandidate(true, false, "Jordbolt", "K5A", 0), "Kun ElectricalFixtures.");
    Check(!Rule13CablingEquipmentReport.IsGroundingCandidate(true, true, "Annen Jordbolt", "K5A", 0), "Kun familieprefiks.");
    Check(!Rule13CablingEquipmentReport.IsGroundingCandidate(true, true, "Jordbolt", "K5A", 1), "IFC utelates.");
    var matches = Rule13CablingEquipmentReport.MatchGroundingIds(new[]
        { "BYG200-450-JRP-055", " byg200-450-jrp-055 ", "2FD-450-JRP-007", "2FD-450-JRP-008" },
        new (long Id, string Mark)[]
        {
            (5000000000, " byg200-450-jrp-055 "), (5000000000, "BYG200-450-JRP-055"),
            (2, "2FD-450-JRP-008"), (3, "2FD-450-JRP-008"),
            (4, "Ekstra-BYG200-450-JRP-055"), (5, "2FD-450-JRP-007-ekstra")
        });
    Check(matches.Count == 3, "Duplikatnoter gir ett merke.");
    Check(matches["BYG200-450-JRP-055"].SequenceEqual(new long[] { 5000000000 }), "Eksakt skiftleiefri trimmet match, unike 64-bit-ID-er.");
    Check(matches["2FD-450-JRP-007"].Length == 0, "Manglende merke er uavklart; ikke substring.");
    Check(matches["2FD-450-JRP-008"].SequenceEqual(new long[] { 2, 3 }), "Alle gyldige treff bevares som flertydige; ikke første.");
    var groundingConfig = Rule13CablingEquipmentReport.ReadConfiguration(Path.Combine(AppContext.BaseDirectory, "config", "CablingEquipmentExceptions.json"));
    Check(groundingConfig.GroundingEnterprise == "K5A", "Konfigurasjon låser K5A-tillegget.");
    Check(Rule13CablingEquipmentReport.GroundingDraftingView == "Utskrift 450 Detaljtegning jordskinner", "Eksakt drafting-view.");
    Check(Rule13CablingEquipmentReport.Headers.SequenceEqual(new[] { "ElementId", "UniqueId", "Revit-kategori", "Familienavn", "Type", "Mark", "FOB_Merkestreng", "FOB_Leveransepakke", "MC Exclude From IFC View", "Load Name i kurs", "Nivå", "Kablet", "BetjenerMerke", "BetjenerNavn" }), "BetjenerNavn brukes som parameterkolonnenavn.");
    Check(Rule13CablingEquipmentReport.IsGroundingCandidate(true, true, "Jordbolt", "K5A", null), "Manglende IFC-verdi er ikke ekskludert.");
    Check(!Rule13CablingEquipmentReport.IsGroundingCandidate(true, true, "Jordbolt", "K5A ekstra", 0), "Ikke entrepriseprefiks.");
    Check(!Rule13CablingEquipmentReport.IsGroundingCandidate(true, true, "Jordbolt", "K5A", -1), "Negativ IFC-verdi utelates.");
    var confirmedJos = Enumerable.Range(1, 6).Select(number => $"BYG200-450-JOS-{number:000}").ToArray();
    var josMarks = Rule13CablingEquipmentReport.ParseJosMarks(string.Join("; ", confirmedJos)
        + " byg200-450-jos-001 BYG200-450-JOS-007 BYG201-450-JOS-001 BYG200-450-RJB-001 BYG200-450-JRP-055");
    Check(josMarks.SequenceEqual(confirmedJos), "Kun seks bekreftede JOS-merker; ikke JOS007, andre bygg, RJB eller JRP.");
    Check(Rule13CablingEquipmentReport.ParseJosMarks(" byg200-450-jos-006 ").Single() == "byg200-450-jos-006", "JOS skiftleiefritt og trimmet.");
    foreach (string invalid in new[] { "_BYG200-450-JOS-001", "BYG200-450-JOS-001_", "-BYG200-450-JOS-001", "BYG200-450-JOS-001-ekstra", "Ekstra-BYG200-450-JOS-001", "BYG200-450-JOS-0010", "450-JOS-001", "BYG200-450-JOS-01" })
        Check(Rule13CablingEquipmentReport.ParseJosMarks(invalid).Length == 0, "JOS krever bekreftet helstreng: " + invalid);
    Check(Rule13CablingEquipmentReport.IsGroundingCandidate(true, true, "jordspyd 3m", " K5A ", 0, "JOS"), "Gyldig K5A Jordspyd.");
    Check(Rule13CablingEquipmentReport.IsGroundingCandidate(true, true, "Jordspyd", "K5A", null, "JOS"), "JOS uten IFC-verdi beholdes.");
    Check(!Rule13CablingEquipmentReport.IsGroundingCandidate(true, true, "Jordbolt", "K5A", 0, "JOS"), "JOS kan ikke matche Jordbolt.");
    Check(!Rule13CablingEquipmentReport.IsGroundingCandidate(true, true, "Jordspyd", "K5A", 0), "JRP kan ikke matche Jordspyd.");
    Check(!Rule13CablingEquipmentReport.IsGroundingCandidate(false, true, "Jordspyd", "K5A", 0, "JOS"), "JOS krever FamilyInstance.");
    Check(!Rule13CablingEquipmentReport.IsGroundingCandidate(true, false, "Jordspyd", "K5A", 0, "JOS"), "JOS krever ElectricalFixtures.");
    Check(!Rule13CablingEquipmentReport.IsGroundingCandidate(true, true, "Annen Jordspyd", "K5A", 0, "JOS"), "JOS krever familieprefiks.");
    foreach (string enterprise in new[] { "K5B", "k5a", "K5A ekstra", "" })
        Check(!Rule13CablingEquipmentReport.IsGroundingCandidate(true, true, "Jordspyd", enterprise, 0, "JOS"), "JOS krever eksakt K5A: " + enterprise);
    foreach (int ifc in new[] { -1, 1, 2 })
        Check(!Rule13CablingEquipmentReport.IsGroundingCandidate(true, true, "Jordspyd", "K5A", ifc, "JOS"), "JOS IFC integer nonzero utelates.");
    var josMatches = Rule13CablingEquipmentReport.MatchGroundingIds(josMarks,
        new (long Id, string Mark)[]
        {
            (5000000001, " byg200-450-jos-001 "), (5000000001, "BYG200-450-JOS-001"),
            (12, "BYG200-450-JOS-003"), (13, "BYG200-450-JOS-003"),
            (14, "Ekstra-BYG200-450-JOS-002"), (15, "BYG200-450-JOS-002-ekstra"), (16, ""), (17, "BYG200-450-JOS-007")
        });
    Check(josMatches.Count == 6, "Seks bekreftede JOS-merker; ingen nye målmerker.");
    Check(josMatches[confirmedJos[0]].SequenceEqual(new long[] { 5000000001 }), "JOS eksakt skiftleiefri FOB-match og unik 64-bit-ID.");
    Check(josMatches[confirmedJos[1]].Length == 0, "JOS nulltreff er uavklart; ingen substring.");
    Check(josMatches[confirmedJos[2]].SequenceEqual(new long[] { 12, 13 }), "JOS flertreff er uavklart; ingen første valgt.");
    Check(josMatches.Where(pair => pair.Value.Length == 1).Select(pair => pair.Value[0]).Distinct().Count() == 1, "Bare entydige JOS-mål uten duplikater.");
    Console.WriteLine($"Rule13Runner v0.0.7 JRP/JOS: {checks} fokuserte kontroller bestått; ingen modell eller Excel kjørt.");
    return;
}

Directory.CreateDirectory(Path.Combine(temp, "config"));
try
{
    string sourceConfig = Path.Combine(AppContext.BaseDirectory, "config", "CablingEquipmentExceptions.json");
    var config = Rule13CablingEquipmentReport.ReadConfiguration(sourceConfig);
    Check(config.Families.Length == 8, "Åtte familieunntak kreves.");
    foreach (string family in config.Families)
    {
        Check(Rule13CablingEquipmentReport.IsExcluded("  " + family.ToUpperInvariant() + "  ", config.Families), "Trim/skiftleie: " + family);
        Check(!Rule13CablingEquipmentReport.IsExcluded(family + " ekstra", config.Families), "Ikke prefiksmatch: " + family);
        Check(!Rule13CablingEquipmentReport.IsExcluded("Ekstra " + family, config.Families), "Ikke contains: " + family);
    }
    Check(!Rule13CablingEquipmentReport.IsExcluded("", config.Families), "Tom familie er ikke unntak.");
    Check(config.NamePrefixes.SequenceEqual(new[] { "Slokkegass", "Inertgass" }), "Bekreftede navneprefikser.");
    Check(Rule13CablingEquipmentReport.MatchNamePrefix("Slokkegass sirene", "Type", config.NamePrefixes) == "Slokkegass", "Prefiks i familie utelates.");
    Check(Rule13CablingEquipmentReport.MatchNamePrefix("Familie", " inertgass ventil ", config.NamePrefixes) == "Inertgass", "Prefiks i type, trimming og skiftleie.");
    Check(Rule13CablingEquipmentReport.MatchNamePrefix("Ekstra Slokkegass", "Annen Inertgass", config.NamePrefixes) is null, "Ikke contains-match.");
    Check(Rule13CablingEquipmentReport.MatchNamePrefix("", "", config.NamePrefixes) is null, "Tomme navn beholdes.");
    Check(Rule13CablingEquipmentReport.MatchNamePrefix("Jordbolt", "Jordspyd", config.NamePrefixes) is null, "JRP/JOS påvirkes ikke.");
    Check(!Rule13CablingEquipmentReport.IsIfcExcluded(null), "Null IFC beholdes.");
    Check(!Rule13CablingEquipmentReport.IsIfcExcluded(0), "IFC=0 beholdes.");
    Check(Rule13CablingEquipmentReport.IsIfcExcluded(1), "IFC=1 utelates.");
    Check(Rule13CablingEquipmentReport.IsIfcExcluded(-1), "Negativ IFC utelates.");
    Check(Rule13CablingEquipmentReport.IsBox("Veggboks", "Type"), "Boks i familie utelates.");
    Check(Rule13CablingEquipmentReport.IsBox("Familie", "Junction BOX"), "Box i type utelates skiftleiefritt.");
    Check(!Rule13CablingEquipmentReport.IsBox("Stikkontakt", "Enkel"), "Vanlig utstyr beholdes.");

    string configPath = Path.Combine(temp, "config", "CablingEquipmentExceptions.json");
    void SaveConfig() => File.WriteAllText(configPath, JsonSerializer.Serialize(config));
    SaveConfig();
    Check(Rule13CablingEquipmentReport.ResolveRoot(temp) == temp, "Eksplisitt absolutt rot.");
    config.NamePrefixes = new[] { " " };
    SaveConfig();
    Reject<InvalidDataException>(() => Rule13CablingEquipmentReport.ReadConfiguration(configPath), "Tomt prefiks avvises.");
    config.NamePrefixes = new[] { "Slokkegass", " slokkegass " };
    SaveConfig();
    Reject<InvalidDataException>(() => Rule13CablingEquipmentReport.ReadConfiguration(configPath), "Dupliserte prefikser avvises.");
    config.NamePrefixes = new[] { "Slokkegass", "Inertgass" };
    SaveConfig();
    Check(Rule13CablingEquipmentReport.ResolveRoot("") == AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar), "Tom rot bruker DLL, ikke arbeidskatalog.");
    Reject<ArgumentException>(() => Rule13CablingEquipmentReport.ResolveRoot("relative"), "Relativ rot skal avvises.");
    Reject<ArgumentException>(() => Rule13CablingEquipmentReport.ResolveRoot(" "), "Whitespace skal avvises.");
    Reject<ArgumentException>(() => Rule13CablingEquipmentReport.ResolveRoot(new string('a', 1025)), "For lang rot skal avvises.");
    Reject<FileNotFoundException>(() => Rule13CablingEquipmentReport.ResolveRoot(Path.GetTempPath()), "Manglende konfig skal avvises.");

    config.SchemaVersion = 2;
    SaveConfig();
    Reject<InvalidDataException>(() => Rule13CablingEquipmentReport.ReadConfiguration(configPath), "Ukjent skjema avvises.");
    config.SchemaVersion = 1;
    config.ModelPath = "feil modell";
    SaveConfig();
    Reject<InvalidDataException>(() => Rule13CablingEquipmentReport.ReadConfiguration(configPath), "Feil modell avvises.");
    config.ModelPath = Rule13CablingEquipmentReport.ModelPath;
    config.Enterprise = "K2";
    SaveConfig();
    Reject<InvalidDataException>(() => Rule13CablingEquipmentReport.ReadConfiguration(configPath), "Feil entreprise avvises.");
    config.Enterprise = Rule13CablingEquipmentReport.Enterprise;
    config.Match = "contains";
    SaveConfig();
    Reject<InvalidDataException>(() => Rule13CablingEquipmentReport.ReadConfiguration(configPath), "Contains-modus avvises.");
    config.Match = "familyExactIgnoreCaseTrim";
    config.Families = config.Families.Append("Ny bekreftet familie").ToArray();
    SaveConfig();
    Check(Rule13CablingEquipmentReport.ReadConfiguration(configPath).Families.Length == 9, "Flere bekreftede familier kan vedlikeholdes.");
    config.Families[1] = " " + config.Families[0].ToUpperInvariant() + " ";
    SaveConfig();
    Reject<InvalidDataException>(() => Rule13CablingEquipmentReport.ReadConfiguration(configPath), "Duplikatfamilier avvises.");
    config.Families = Array.Empty<string>();
    SaveConfig();
    Reject<InvalidDataException>(() => Rule13CablingEquipmentReport.ReadConfiguration(configPath), "Tom unntaksliste avvises.");
    File.WriteAllText(configPath, "{bad json}");
    Reject<JsonException>(() => Rule13CablingEquipmentReport.ReadConfiguration(configPath), "Ugyldig JSON avvises.");

    Check(Rule13CablingEquipmentReport.Headers.SequenceEqual(new[] { "ElementId", "UniqueId", "Revit-kategori", "Familienavn", "Type", "Mark", "FOB_Merkestreng", "FOB_Leveransepakke", "MC Exclude From IFC View", "Load Name i kurs", "Nivå", "Kablet", "BetjenerMerke", "BetjenerNavn" }), "Eksakt intern kolonnerekkefølge.");
    Check(Rule13CablingEquipmentReport.EquipmentListHeaders.SequenceEqual(new[] { "ElementId", "Familienavn", "Type", "FOB_Merkestreng", "FOB_Leveransepakke", "Nivå", "BetjenerMerke", "BetjenerNavn" }), "Utstyrslisten følger brukerens kompakte kolonneoppsett.");
    Check(Rule13CablingEquipmentReport.GroundingListHeaders.SequenceEqual(new[] { "ElementId", "UniqueId", "Revit-kategori", "Familienavn", "Type", "Mark", "FOB_Merkestreng", "FOB_Leveransepakke", "Nivå", "TextNote ElementId", "DraftingView", "Grunnlag" }), "JRP/JOS følger detaljoppsettet.");
    var reads = new List<string>();
    string ReadInstance(string name) { reads.Add(name); return name == "PGF_RIE_BetjenerMerke" ? "FBU0-422-SML-154" : "Skilt"; }
    Check(Rule13CablingEquipmentReport.ServedValues("Koblingspunkt for skilt", "Type", ReadInstance).SequenceEqual(new[] { "FBU0-422-SML-154", "Skilt" }), "Betjenerdata fra instansparametere.");
    Check(reads.SequenceEqual(new[] { "PGF_RIE_BetjenerMerke", "PGF_RIE_BetjenerNavn" }), "Eksakte kildeparametere.");
    Check(Rule13CablingEquipmentReport.ServedValues("Familie", " koblingspunkt i tak ", ReadInstance)[1] == "Skilt", "Typeprefiks, trimming og skiftleie.");
    reads.Clear();
    Check(Rule13CablingEquipmentReport.ServedValues("Annet Koblingspunkt", "Jordbolt", ReadInstance).All(value => value == ""), "Andre elementer har tomme betjenerceller.");
    Check(reads.Count == 0, "Ingen parameterlesing for andre elementer.");
    Check(Rule13CablingEquipmentReport.ServedValues("Koblingspunkt", "Type", _ => "").All(value => value == ""), "Manglende instansverdier forblir tomme.");
    string report = Path.Combine(temp, "test.xlsx");
    string[] row = { "5000000000", "uid-1", "Electrical Fixtures", "Prøvefamilie", "Type", "=tekst", "", "K5B", "0", "Kurs A; Kurs B", "Nivå 1", "", "FBU0-422-SML-154", "Skilt" };
    string[] equipmentRow = Rule13CablingEquipmentReport.ProjectRows(
        Rule13CablingEquipmentReport.Headers, Rule13CablingEquipmentReport.EquipmentListHeaders, new[] { row }).Single();
    Check(equipmentRow.SequenceEqual(new[] { "5000000000", "Prøvefamilie", "Type", "", "K5B", "Nivå 1", "FBU0-422-SML-154", "Skilt" }), "Kolonneprojeksjon bevarer riktig BetjenerNavn-verdi.");
    Check(Rule13CablingEquipmentReport.ReportFileName("BAS") == "Utstyr_til_kabling_K5B_BAS_FBU.xlsx", "BAS-listen bruker fast filnavn uten tidsstempel.");
    Check(Rule13CablingEquipmentReport.ReportFileName("STA") == "Utstyr_til_kabling_K5B_STA_FBU.xlsx", "STA-listen bruker fast filnavn uten tidsstempel.");
    var sheets = new[]
    {
        new ExcelListReportWriter.WorksheetData("Utstyr K5B", Rule13CablingEquipmentReport.Headers, new List<string[]> { row }, Rule13CablingEquipmentReport.HiddenEquipmentHeaders),
        new ExcelListReportWriter.WorksheetData("Utstyrsliste", Rule13CablingEquipmentReport.EquipmentListHeaders,
            new List<string[]> { equipmentRow }, columnWidths: Rule13CablingEquipmentReport.EquipmentListColumnWidths),
        new ExcelListReportWriter.WorksheetData("Unntak", new[] { "Familienavn", "Antall utelatt" }, new List<string[]>()),
        new ExcelListReportWriter.WorksheetData("Omfang", new[] { "Filter", "Verdi" }, new List<string[]> { new[] { "Prøve", "Modellfri test, ikke modellrapport" } })
    };
    ExcelListReportWriter.Write(report, sheets);
    byte[] before = File.ReadAllBytes(report);
    Reject<IOException>(() => ExcelListReportWriter.Write(report, sheets), "Eksisterende rapport skal aldri overskrives.");
    Check(before.SequenceEqual(File.ReadAllBytes(report)), "Eksisterende rapport er byteidentisk.");
    using (var zip = ZipFile.OpenRead(report))
    {
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        using var workbookStream = zip.GetEntry("xl/workbook.xml")!.Open();
        var workbook = XDocument.Load(workbookStream);
        Check(workbook.Descendants(ns + "sheet").Select(sheet => (string?)sheet.Attribute("name")).SequenceEqual(new[] { "Utstyr K5B", "Utstyrsliste", "Unntak", "Omfang" }), "Fire forventede ark.");
        Check(workbook.Descendants(ns + "workbookView").Count() == 1, "Arbeidsbokvisning finnes.");
        using var stream = zip.GetEntry("xl/worksheets/sheet1.xml")!.Open();
        var sheet = XDocument.Load(stream);
        Check((string?)sheet.Root!.Element(ns + "dimension")!.Attribute("ref") == "A1:N2", "Riktig arkområde.");
        Check((string?)sheet.Root.Element(ns + "autoFilter")!.Attribute("ref") == "A1:N2", "Riktig filter.");
        Check(sheet.Descendants(ns + "col").Where(column => (string?)column.Attribute("hidden") == "1")
            .Select(column => (int)column.Attribute("min")!).SequenceEqual(new[] { 2, 3, 6, 9 }), "Bare B, C, F og I skjules.");
        Check(sheet.Descendants(ns + "c").Single(cell => (string?)cell.Attribute("r") == "B2")
            .Descendants(ns + "t").Single().Value == "uid-1", "Skjult UniqueId bevares.");
        using var otherStream = zip.GetEntry("xl/worksheets/sheet2.xml")!.Open();
        Check(!XDocument.Load(otherStream).Descendants(ns + "col").Any(column => column.Attribute("hidden") is not null), "Andre ark skjules ikke.");
        Check(sheet.Descendants(ns + "row").Count() == 2, "Én datarad.");
        var cabled = sheet.Descendants(ns + "c").Single(cell => (string?)cell.Attribute("r") == "L2");
        Check(!cabled.HasElements && cabled.Attribute("t") is null, "Kablet er ekte tom celle.");
        Check(sheet.Descendants(ns + "t").Any(text => text.Value == "5000000000"), "64-bit ElementId som tekst.");
        Check(sheet.Descendants(ns + "t").Any(text => text.Value == "Prøvefamilie"), "Norske tegn bevares.");
        Check(!sheet.Descendants(ns + "f").Any(), "Tekst blir ikke en Excel-formel.");
        using var listStream = zip.GetEntry("xl/worksheets/sheet2.xml")!.Open();
        var equipmentSheet = XDocument.Load(listStream);
        Check((string?)equipmentSheet.Root!.Element(ns + "dimension")!.Attribute("ref") == "A1:H2", "Kompakt liste har åtte kolonner.");
        Check(equipmentSheet.Descendants(ns + "t").Any(text => text.Value == "BetjenerNavn"), "Siste listekolonne heter BetjenerNavn.");
        Check(equipmentSheet.Descendants(ns + "col").Single(column => (int)column.Attribute("min")! == 8)
            .Attribute("width")?.Value == "74.140625", "Listekolonnebredder følger brukerens layout.");
    }
    string overwritePath = Path.Combine(temp, "overwrite.xlsx");
    var overwriteRows = new List<string[]> { equipmentRow.ToArray() };
    var overwriteSheets = new[]
    {
        new ExcelListReportWriter.WorksheetData("Utstyr K5B", Rule13CablingEquipmentReport.EquipmentListHeaders, overwriteRows)
    };
    ExcelListReportWriter.Write(overwritePath, overwriteSheets);
    overwriteRows[0][1] = "Oppdatert familie";
    ExcelListReportWriter.Write(overwritePath, overwriteSheets, overwriteExisting: true);
    using (var zip = ZipFile.OpenRead(overwritePath))
    using (var stream = zip.GetEntry("xl/worksheets/sheet1.xml")!.Open())
    {
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var sheet = XDocument.Load(stream);
        Check(sheet.Descendants(ns + "t")
            .Any(text => text.Value == "Oppdatert familie"), "Eksplisitt overskriving erstatter fast rapportfil.");
    }
    if (args.Length == 1) File.Copy(report, Path.GetFullPath(args[0]), false);
    Console.WriteLine($"Rule13Runner v0.0.7: {checks} kontroller bestått; ingen Revit-modell kjørt.");
}
finally
{
    Directory.Delete(temp, true);
}
