using System.IO.Compression;
using System.Xml.Linq;
using CW.Assistant.Generated;

int tests = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new Exception("FAIL: " + name);
    tests++;
}
void Reject(Action action, string name)
{
    try { action(); }
    catch (InvalidDataException) { tests++; return; }
    throw new Exception("FAIL: accepted " + name);
}
DrofusQueuedMember Member(long id, double length, int occurrence = 0, string mark = "", string sequence = "") => new() {
    ElementId = id, UniqueId = "uid-" + id, LengthFeet = length, OccurrenceId = occurrence,
    Mark = mark, Sequence = sequence, WidthMm = 300, HeightMm = 60, State = "snapshot",
    TypeName = "Kabelstige 300 mm x 60 mm" };
DrofusRouteQueue Queue() => new() { Routes = new() { new() {
    Route = "BAS-1", RepresentativeUniqueId = "uid-1", RegisteredAt = DateTime.Now,
    Members = new() { Member(1, 3, 101, "MERKE-001", "001"), Member(2, 9, 102, "MERKE-002", "002") } } } };

Check(DrofusRouteQueueStore.Choose(new[] { Member(1, 1, 1, "X", "001"), Member(2, 10) }).Member.ElementId == 1,
    "unique synced representative wins");
Check(DrofusRouteQueueStore.Choose(Queue().Routes[0].Members).Member.ElementId == 2, "multiple occurrences use longest");
Check(DrofusRouteQueueStore.Choose(new[] { Member(3, 5), Member(2, 5) }).Member.ElementId == 2, "tie uses lowest id");
Check(DrofusRouteQueueStore.Choose(new[] { Member(1, 1, 1, "X", "Error: bad"), Member(2, 9) }).Member.ElementId == 2,
    "invalid sequence is not candidate");
Check(DrofusRouteQueueStore.Choose(new[] { Member(1, 1, 1, "X", "000"), Member(2, 9) }).Member.ElementId == 2,
    "zero sequence is not candidate");
Reject(() => DrofusRouteQueueStore.Choose(new[] { Member(1, 1) }), "single segment");
var queue = Queue();
DrofusRouteQueueStore.Validate(queue);
queue.Routes[0].Members.Add(Member(1, 4));
Reject(() => DrofusRouteQueueStore.Validate(queue), "overlap");
queue = Queue();
queue.Routes[0].RepresentativeUniqueId = "missing";
Reject(() => DrofusRouteQueueStore.Validate(queue), "missing representative");
queue = Queue();
queue.ModelPath = "another model";
Reject(() => DrofusRouteQueueStore.Validate(queue), "wrong model");
queue = Queue();
queue.Routes[0].Status = "SuccessMaybe";
Reject(() => DrofusRouteQueueStore.Validate(queue), "unknown status");
queue = Queue();
queue.Routes[0].Members[0].WidthMm = double.NaN;
Reject(() => DrofusRouteQueueStore.Validate(queue), "invalid width");
string folder = Path.Combine(Path.GetTempPath(), "DrofusQueueTests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(folder);
try
{
    string path = Path.Combine(folder, "queue.json");
    Check(DrofusRouteQueueStore.Load(path).Routes.Count == 0, "new queue");
    queue = Queue();
    DrofusRouteQueueStore.Save(path, queue, folder);
    Check(DrofusRouteQueueStore.Load(path).Routes[0].Members[1].OccurrenceId == 102, "identity roundtrip");
    queue.Routes[0].Status = "Applied";
    DrofusRouteQueueStore.Save(path, queue, folder);
    Check(Directory.GetFiles(folder, "*before*.json").Length == 1, "backup before replacement");
    Check(DrofusRouteQueueStore.Load(path).Routes[0].Status == "Applied", "status roundtrip");
    using (var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
    {
        try { DrofusRouteQueueStore.Save(path, queue, folder); throw new Exception("Locked file accepted"); }
        catch (IOException) { tests++; }
    }
    Check(Directory.GetFiles(folder, "*.tmp").Length == 0, "failed persistence leaves no temp file");
    string xlsx = args.Length == 1 ? Path.GetFullPath(args[0]) : Path.Combine(folder, "report.xlsx");
    queue.Routes[0].Members[1].Mark = "=FORMULA < & æøå";
    DrofusRouteQueueReport.Write(queue, path, xlsx);
    using var zip = ZipFile.OpenRead(xlsx);
    using var sheet = zip.GetEntry("xl/worksheets/sheet2.xml")!.Open();
    var xml = XDocument.Load(sheet);
    XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    Check(xml.Descendants(ns + "row").Count() == 3, "exactly one row per member");
    Check(xml.Descendants(ns + "c").All(c => (string?)c.Attribute("t") != "inlineStr" || c.Element(ns + "is") != null),
        "no invalid empty inline strings");
    Check(xml.Descendants(ns + "t").Any(t => t.Value == "=FORMULA < & æøå")
        && !xml.Descendants(ns + "f").Any(), "strings preserved without formula injection");
    string formatPath = Path.Combine(Path.GetDirectoryName(xlsx)!, Path.GetFileNameWithoutExtension(xlsx) + "-format.xlsx");
    ExcelListReportWriter.Write(formatPath, new[]
    {
        new ExcelListReportWriter.WorksheetData("Romdata", new[] { "ElementId", "Romnavn", "Detalj" }, new()
        {
            new[] { "001", "", "=FORMULA < & æøå" },
            new[] { "002" },
            new[] { "003", "\u0001", "æøå" }
        }),
        new ExcelListReportWriter.WorksheetData("Unntak", new[] { "ElementId" }, new())
    });
    using var formatZip = ZipFile.OpenRead(formatPath);
    XDocument ReadXml(string entry)
    {
        using var stream = formatZip.GetEntry(entry)!.Open();
        return XDocument.Load(stream);
    }
    var workbookXml = ReadXml("xl/workbook.xml");
    var formatXml = ReadXml("xl/worksheets/sheet1.xml");
    Check(workbookXml.Descendants(ns + "workbookView").Count() == 1, "explicit workbook view survives openpyxl roundtrip");
    Check((string?)formatXml.Root!.Element(ns + "dimension")?.Attribute("ref") == "A1:C4", "explicit worksheet dimensions");
    Check((string?)formatXml.Descendants(ns + "sheetView").Single().Attribute("workbookViewId") == "0"
        && (string?)formatXml.Descendants(ns + "pane").Single().Attribute("state") == "frozen", "frozen header references valid view");
    foreach (string reference in new[] { "B2", "B3", "C3", "B4" })
    {
        XElement cell = formatXml.Descendants(ns + "c").Single(cell => (string?)cell.Attribute("r") == reference);
        Check(cell.Attribute("t") is null && cell.Element(ns + "is") is null, "true blank cell " + reference);
    }
    Check((string?)ReadXml("xl/worksheets/sheet2.xml").Root!.Element(ns + "dimension")?.Attribute("ref") == "A1:A1",
        "header-only worksheet has valid dimensions");
    Console.WriteLine("REPORT: " + xlsx);
    Console.WriteLine("FORMAT REPORT: " + formatPath);
}
finally { Directory.Delete(folder, true); }
Console.WriteLine("PASS: " + tests + " queue / representative / persistence / Excel checks");
