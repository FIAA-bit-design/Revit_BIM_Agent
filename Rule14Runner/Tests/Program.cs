using CW.Assistant.Generated;
using System.IO.Compression;
using System.Text;

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

Rule14ExceptionRule slokkeRule = Rule14ExceptionRule.Parse("Alle familier som begynner med Slokke");
Check(slokkeRule.FamilyPrefix, "Slokke", "Familieprefikset leses fra unntaksregelen.");
CheckCondition(slokkeRule.MatchesFamily(" Slokkeanlegg detektor "), "Familieprefiks matcher trimmet navn skiftleifritt.");
CheckCondition(!slokkeRule.MatchesFamily("Utstyr Slokkeanlegg"), "Familieprefiks matcher bare fra starten av familienavnet.");
Check(Rule14ExceptionRule.Parse("Alle familier som begynner med Inertgass").FamilyPrefix,
    "Inertgass", "Inertgass legges til som familieprefiks.");
Rule14ExceptionRule strømskinneTypeRule = Rule14ExceptionRule.Parse("Alle typenavn som begynner med Strømskinne");
Check(strømskinneTypeRule.TypePrefix, "Strømskinne", "Strømskinne legges til som typenavnprefiks.");
CheckCondition(strømskinneTypeRule.MatchesElement("Cable Tray with Fittings", false,
    "Strømskinne 2500A 200 mm x 350 mm", null), "Strømskinne-unntak matcher typenavn når familienavnet er annerledes.");
CheckCondition(!strømskinneTypeRule.MatchesElement("Strømskinne", false, "Kabelbro rett", null),
    "Strømskinne-typeunntaket matcher ikke bare familienavnet.");
Rule14ExceptionRule conduitRule = Rule14ExceptionRule.Parse(Rule14ExceptionRule.ConduitTwentyFiveMmDescription);
CheckCondition(conduitRule.MatchesElement("Trekkerør", true, "Trekkerør 25 mm K5B", null),
    "Conduit-typenavn med mellomrom gjenkjennes som 25mm.");
CheckCondition(conduitRule.MatchesElement("Annet", true, "Annet", "ø=25 mm"),
    "Conduit Size-parameter med 25 mm gjenkjennes.");
CheckCondition(!conduitRule.MatchesElement("Annet", true, "Annet", "ø=32 mm"),
    "Andre Conduit-størrelser gjenkjennes ikke.");
CheckCondition(!conduitRule.MatchesElement("Annet", false, "25 mm utstyr", "ø=25 mm"),
    "25 mm-regelen gjelder bare Conduit.");
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