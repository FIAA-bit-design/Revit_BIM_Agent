using CW.Assistant.Generated;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

int checks = 0;
void Check(string? actual, string? expected, string message)
{
    checks++;
    if (!string.Equals(actual, expected, StringComparison.Ordinal))
        throw new InvalidOperationException(message + "; fikk '" + actual + "'.");
}
void CheckCondition(bool condition, string message)
{
    checks++;
    if (!condition) throw new InvalidOperationException(message);
}

Check(Rule14RoomValueValidator.FindParameterIssue(0, null, null, false),
    "parameter mangler", "Manglende romparameter rapporteres.");
Check(Rule14RoomValueValidator.FindParameterIssue(2, null, "A | B", false),
    "dupliserte instansparametere (2)", "Dupliserte romparametere rapporteres.");
Check(Rule14RoomValueValidator.FindParameterIssue(1, "Integer", "1", false),
    "feil lagringstype Integer", "Ikke-tekstparameter rapporteres.");
Check(Rule14RoomValueValidator.FindParameterIssue(1, "String", null, false),
    "blank verdi", "Tom verdi rapporteres.");
Check(Rule14RoomValueValidator.FindParameterIssue(1, "String", "  ", false),
    "blank verdi", "Whitespace rapporteres.");
Check(Rule14RoomValueValidator.FindParameterIssue(1, "String", " -- ", false),
    "verdien '--' er ikke gyldig", "Plassholder rapporteres for vanlige elementer.");
Check(Rule14RoomValueValidator.FindParameterIssue(1, "String", "Rom 12", false),
    null, "Gyldig romtekst beholdes.");
Check(Rule14RoomValueValidator.FindParameterIssue(1, "String", "--", true),
    null, "Eksakt plassholder godtas der den kreves.");
Check(Rule14RoomValueValidator.FindParameterIssue(1, "String", " -- ", true),
    "krever nøyaktig '--'", "Plassholderkravet er eksakt.");
Check(Rule14RoomValueValidator.FindParameterIssue(1, "String", "Rom 12", true),
    "krever nøyaktig '--'", "Infoelement/trekkerør med romtekst rapporteres.");
CheckCondition(!Rule14RoomValueValidator.RequiresConduitPlaceholder("K5B", "ø=50 mm"),
    "K5B-trekkerør på nøyaktig 50 mm bruker romverdier.");
CheckCondition(!Rule14RoomValueValidator.RequiresConduitPlaceholder("K5B", "ø=110 mm"),
    "K5B-trekkerør over 50 mm bruker romverdier.");
CheckCondition(!Rule14RoomValueValidator.RequiresConduitPlaceholder("K5B", "Ø=125mm"),
    "Størrelseslesingen er skiftleifri og godtar tett mm-format.");
CheckCondition(!Rule14RoomValueValidator.RequiresConduitPlaceholder("K5B", "50,5 mm"),
    "Desimalkomma godtas for størrelser over terskelen.");
CheckCondition(Rule14RoomValueValidator.RequiresConduitPlaceholder("K5B", "ø=49 mm"),
    "K5B-trekkerør under 50 mm beholder plassholderregelen.");
CheckCondition(Rule14RoomValueValidator.RequiresConduitPlaceholder("K5A", "ø=110 mm"),
    "50 mm-unntaket gjelder ikke andre entrepriser.");
CheckCondition(Rule14RoomValueValidator.RequiresConduitPlaceholder("K5B", "ukjent størrelse"),
    "Ukjent størrelse bruker fortsatt plassholderregelen.");
bool largeK5BConduitRequiresPlaceholder = Rule14RoomValueValidator.RequiresConduitPlaceholder("K5B", "ø=110 mm");
Check(Rule14RoomValueValidator.FindParameterIssue(1, "String", "Gjennomkjøringsspor P1", largeK5BConduitRequiresPlaceholder),
    null, "Reell romtekst godtas for K5B-trekkerør fra 50 mm.");
Check(Rule14RoomValueValidator.FindParameterIssue(1, "String", "--", largeK5BConduitRequiresPlaceholder),
    "verdien '--' er ikke gyldig", "Plassholder avvises for K5B-trekkerør fra 50 mm.");

Rule14ExceptionRule slokkeRule = Rule14ExceptionRule.Parse("Alle familier som begynner med Slokke");
Check(slokkeRule.FamilyPrefix, "Slokke", "Familieprefikset leses fra unntaksregelen.");
CheckCondition(slokkeRule.MatchesFamily(" Slokkeanlegg detektor "), "Familieprefiks matcher trimmet navn skiftleifritt.");
CheckCondition(!slokkeRule.MatchesFamily("Utstyr Slokkeanlegg"), "Familieprefiks matcher bare fra starten av familienavnet.");
Check(Rule14ExceptionRule.Parse("Alle familier som begynner med Inertgass").FamilyPrefix,
    "Inertgass", "Inertgass legges til som familieprefiks.");
Rule14ExceptionRule strømskinneTypeRule = Rule14ExceptionRule.Parse("Alle typenavn som begynner med Strømskinne");
Check(strømskinneTypeRule.TypePrefix, "Strømskinne", "Strømskinne legges til som typenavnprefiks.");
CheckCondition(strømskinneTypeRule.MatchesElement("Cable Tray with Fittings", false, false,
    "Strømskinne 2500A 200 mm x 350 mm", null), "Strømskinne-unntak matcher typenavn når familienavnet er annerledes.");
CheckCondition(!strømskinneTypeRule.MatchesElement("Strømskinne", false, false, "Kabelbro rett", null),
    "Strømskinne-typeunntaket matcher ikke bare familienavnet.");
Rule14ExceptionRule aspirationRule = Rule14ExceptionRule.Parse("Alle familier og typenavn som begynner med Aspirasjonrør");
Check(aspirationRule.FamilyPrefix, "Aspirasjonrør", "Aspirasjonrør-regelen matcher familienavn.");
Check(aspirationRule.TypePrefix, "Aspirasjonrør", "Aspirasjonrør-regelen matcher typenavn.");
CheckCondition(aspirationRule.MatchesElement("M_Conduit Elbow - without Fittings - RNC", false, true,
    "Aspirasjonrør bend", "ø=25 mmxø=25 mm"), "Aspirasjonrør-bend fanges via typenavn.");
CheckCondition(aspirationRule.MatchesElement("Aspirasjonrør sensor", false, false,
    "Standard", null), "Aspirasjonrør-familie fanges via familienavn.");
Rule14ExceptionRule conduitBendRule = Rule14ExceptionRule.Parse(Rule14ExceptionRule.ConduitBendTwentyFiveMmDescription);
CheckCondition(conduitBendRule.MatchesElement("M_Conduit Elbow - without Fittings - RNC", false, true,
    "Trekkerør bend 25 mm K5B", "ø=25 mmxø=25 mm"), "25 mm-trekkerørsbend fanges fra type og Size.");
CheckCondition(!conduitBendRule.MatchesElement("M_Conduit Elbow - without Fittings - RNC", false, true,
    "Trekkerør bend 16 mm K5B", "ø=16 mmxø=16 mm"), "16 mm-bend fanges ikke som 25 mm.");
CheckCondition(!conduitBendRule.MatchesElement("Other family", false, true,
    "Standard elbow 25 mm", "ø=25 mmxø=25 mm"), "Andre 25 mm-fittings fanges ikke som trekkerørsbend.");
Rule14ExceptionRule conduitRule = Rule14ExceptionRule.Parse(Rule14ExceptionRule.ConduitTwentyFiveMmDescription);
CheckCondition(conduitRule.MatchesElement("Trekkerør", true, false, "Trekkerør 25 mm K5B", null),
    "Conduit-typenavn med mellomrom gjenkjennes som 25mm.");
CheckCondition(conduitRule.MatchesElement("Annet", true, false, "Annet", "ø=25 mm"),
    "Conduit Size-parameter med 25 mm gjenkjennes.");
CheckCondition(!conduitRule.MatchesElement("Annet", true, false, "Annet", "ø=32 mm"),
    "Andre Conduit-størrelser gjenkjennes ikke.");
CheckCondition(!conduitRule.MatchesElement("Annet", false, false, "25 mm utstyr", "ø=25 mm"),
    "25 mm-regelen gjelder bare Conduit.");
CheckCondition(!conduitRule.MatchesElement("Annet", true, false, "125 mm K5B", "ø=125 mm"),
    "25 mm-regelen matcher ikke 125 mm.");
try
{
    Rule14ExceptionRule.Parse("Slokke-familier");
    throw new InvalidOperationException("Ukjent unntaksformat skulle vært avvist.");
}
catch (InvalidDataException)
{
    checks++;
}

string tempDirectory = Path.Combine(Path.GetTempPath(), "Rule14Exceptions_" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(tempDirectory);
try
{
    Check(Rule14ExceptionRules.GetWorkbookPath(tempDirectory),
        Path.Combine(tempDirectory, "config", "Mangler_rominformasjon.xlsx"),
        "Rule 14 henter kildearbeidsboken fra config-mappen.");
    string workbookPath = Path.Combine(tempDirectory, Rule14ExceptionRules.WorkbookFileName);
    using (var file = File.Create(workbookPath))
    using (var archive = new ZipArchive(file, ZipArchiveMode.Create))
    {
        WriteEntry(archive, "xl/workbook.xml", "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets><sheet name=\"Unntak\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>");
        WriteEntry(archive, "xl/_rels/workbook.xml.rels", "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"worksheet\" Target=\"worksheets/sheet1.xml\"/></Relationships>");
        WriteEntry(archive, "xl/worksheets/sheet1.xml", "<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData><row r=\"1\"><c r=\"A1\" t=\"inlineStr\"><is><t>Alle familier som begynner med Gulvboks</t></is></c></row><row r=\"2\"><c r=\"A2\" t=\"inlineStr\"><is><t>Alle familier som begynner med Strømskinne</t></is></c></row></sheetData></worksheet>");
    }

    List<Rule14ExceptionRule> rules = Rule14ExceptionRules.ReadWorkbookExceptions(workbookPath);
    Check(rules.Count.ToString(), "2", "Unntaksfanen leses fra XLSX.");
    Check(rules[1].FamilyPrefix, "Strømskinne", "Norske tegn bevares fra XLSX.");

    string reportPath = Path.Combine(tempDirectory, "rule14-layout.xlsx");
    string[] reportHeaders = { "Avvik", "ElementId", "Revit-kategori" };
    ExcelListReportWriter.Write(reportPath, new[]
    {
        new ExcelListReportWriter.WorksheetData("Mangler rominfo", reportHeaders,
            new List<string[]> { new[] { "blank verdi", "42", "Elektrisk utstyr" } },
            columnWidths: new Dictionary<string, double>
            {
                ["Avvik"] = 48,
                ["ElementId"] = 14,
                ["Revit-kategori"] = 24
            })
    });
    using (var reportFile = File.OpenRead(reportPath))
    using (var reportArchive = new ZipArchive(reportFile, ZipArchiveMode.Read))
    using (Stream worksheetStream = reportArchive.GetEntry("xl/worksheets/sheet1.xml")!.Open())
    {
        XNamespace spreadsheet = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XDocument worksheet = XDocument.Load(worksheetStream);
        string[] actualHeaders = worksheet.Descendants(spreadsheet + "row").First()
            .Elements(spreadsheet + "c")
            .Select(cell => (string?)cell.Element(spreadsheet + "is")?.Element(spreadsheet + "t") ?? string.Empty)
            .ToArray();
        Check(string.Join("|", actualHeaders), "Avvik|ElementId|Revit-kategori",
            "Rapportens handlingskolonner står først.");
        string[] actualWidths = worksheet.Descendants(spreadsheet + "col")
            .Select(column => (string?)column.Attribute("width") ?? string.Empty)
            .ToArray();
        Check(string.Join("|", actualWidths), "48|14|24", "Rapportens kolonnebredder følger layouten.");
        Check((string?)worksheet.Descendants(spreadsheet + "autoFilter").Single().Attribute("ref"),
            "A1:C2", "Rapportfilteret dekker overskrifter og data.");
    }
}
finally
{
    Directory.Delete(tempDirectory, recursive: true);
}

Console.WriteLine("Rule14Runner: " + checks + " fokuserte kontroller bestått; ingen Revit-modell kjørt.");

static void WriteEntry(ZipArchive archive, string path, string content)
{
    using Stream stream = archive.CreateEntry(path).Open();
    using var writer = new StreamWriter(stream, Encoding.UTF8);
    writer.Write(content);
}