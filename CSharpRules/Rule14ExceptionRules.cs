using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace CW.Assistant.Generated;

internal sealed class Rule14ExceptionRule
{
    private const string DescriptionPrefix = "Alle familier som begynner med ";
    private const string TypeDescriptionPrefix = "Alle typenavn som begynner med ";
    private const string FamilyAndTypeDescriptionPrefix = "Alle familier og typenavn som begynner med ";
    internal const string NumberedDetailArrowTypePrefix = "3D Detaljpil nummer ";
    internal const string ConduitTwentyFiveMmDescription = "Alle typenavn for Conduit som inneholder 25mm eller har Size 25mm";
    internal const string ConduitBendTwentyFiveMmDescription = "Alle trekkerør bend som er 25 mm";

    internal string Description { get; }
    internal string FamilyPrefix { get; }
    internal string TypePrefix { get; }
    internal string RuleKey { get; }

    private Rule14ExceptionRule(string description, string familyPrefix, string typePrefix, string ruleKey)
    {
        Description = description;
        FamilyPrefix = familyPrefix;
        TypePrefix = typePrefix;
        RuleKey = ruleKey;
    }

    internal static Rule14ExceptionRule Parse(string description)
    {
        string normalized = description?.Trim() ?? string.Empty;
        if (normalized.StartsWith(FamilyAndTypeDescriptionPrefix, StringComparison.OrdinalIgnoreCase))
        {
            string prefix = normalized.Substring(FamilyAndTypeDescriptionPrefix.Length).Trim();
            if (string.IsNullOrWhiteSpace(prefix))
                throw new InvalidDataException("Unntaksregelen mangler familie-/typenavnprefiks.");
            return new Rule14ExceptionRule(normalized, prefix, prefix, "familie-type:" + prefix);
        }

        if (normalized.StartsWith(DescriptionPrefix, StringComparison.OrdinalIgnoreCase))
        {
            string familyPrefix = normalized.Substring(DescriptionPrefix.Length).Trim();
            if (string.IsNullOrWhiteSpace(familyPrefix))
                throw new InvalidDataException("Unntaksregelen mangler familienavnprefiks.");
            return new Rule14ExceptionRule(normalized, familyPrefix, string.Empty, "familie:" + familyPrefix);
        }

        if (normalized.StartsWith(TypeDescriptionPrefix, StringComparison.OrdinalIgnoreCase))
        {
            string typePrefix = normalized.Substring(TypeDescriptionPrefix.Length).Trim();
            if (string.IsNullOrWhiteSpace(typePrefix))
                throw new InvalidDataException("Unntaksregelen mangler typenavnprefiks.");
            if (string.Equals(typePrefix, NumberedDetailArrowTypePrefix.TrimEnd(), StringComparison.OrdinalIgnoreCase))
                typePrefix = NumberedDetailArrowTypePrefix;
            return new Rule14ExceptionRule(normalized, string.Empty, typePrefix, "type:" + typePrefix);
        }

        if (string.Equals(normalized, ConduitTwentyFiveMmDescription, StringComparison.OrdinalIgnoreCase))
            return new Rule14ExceptionRule(normalized, string.Empty, string.Empty, "conduit-size-25mm");

        if (string.Equals(normalized, ConduitBendTwentyFiveMmDescription, StringComparison.OrdinalIgnoreCase))
            return new Rule14ExceptionRule(normalized, string.Empty, string.Empty, "conduit-bend-size-25mm");

        throw new InvalidDataException("Ukjent format i unntaksfanen: " + normalized);
    }

    internal bool MatchesFamily(string familyName) =>
        FamilyPrefix.Length > 0
        && (familyName ?? string.Empty).Trim().StartsWith(FamilyPrefix, StringComparison.OrdinalIgnoreCase);

    internal bool MatchesElement(string familyName, bool isConduit, bool isConduitFitting, string typeName, string? sizeValue)
    {
        bool familyMatch = FamilyPrefix.Length > 0 && MatchesFamily(familyName);
        bool typeMatch = TypePrefix.Length > 0
            && (typeName ?? string.Empty).Trim().StartsWith(TypePrefix, StringComparison.OrdinalIgnoreCase);
        if (FamilyPrefix.Length > 0 || TypePrefix.Length > 0) return familyMatch || typeMatch;

        if (RuleKey == "conduit-bend-size-25mm")
        {
            bool isTrekkerorBend = (familyName ?? string.Empty).IndexOf("Trekkerør bend", StringComparison.OrdinalIgnoreCase) >= 0
                || (typeName ?? string.Empty).IndexOf("Trekkerør bend", StringComparison.OrdinalIgnoreCase) >= 0;
            return isConduitFitting && isTrekkerorBend
                && (ContainsTwentyFiveMm(typeName) || ContainsTwentyFiveMm(sizeValue));
        }

        return isConduit && (ContainsTwentyFiveMm(typeName) || ContainsTwentyFiveMm(sizeValue));
    }

    private static bool ContainsTwentyFiveMm(string? value) =>
        Regex.IsMatch(value ?? string.Empty, @"(?<!\d)25\s*mm(?!\d)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
}

internal static class Rule14ExceptionRules
{
    internal const string WorkbookFileName = "Mangler_rominformasjon.xlsx";
    private const string WorksheetName = "Unntak";
    private const string MainNamespace = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string DocumentRelationshipNamespace = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string PackageRelationshipNamespace = "http://schemas.openxmlformats.org/package/2006/relationships";

    internal static string GetWorkbookPath(string workspaceRoot) =>
        Path.Combine(workspaceRoot, "config", WorkbookFileName);

    internal static List<Rule14ExceptionRule> ReadWorkbookExceptions(string workbookPath)
    {
        if (!File.Exists(workbookPath))
            throw new FileNotFoundException("Fant ikke arbeidsboken med Regel 14-unntak: " + workbookPath, workbookPath);

        using var file = File.OpenRead(workbookPath);
        using var archive = new ZipArchive(file, ZipArchiveMode.Read);
        XDocument workbook = ReadXml(archive, "xl/workbook.xml");
        XDocument relationships = ReadXml(archive, "xl/_rels/workbook.xml.rels");
        XElement? sheet = workbook.Descendants(XName.Get("sheet", MainNamespace))
            .SingleOrDefault(element => string.Equals((string?)element.Attribute("name"), WorksheetName, StringComparison.Ordinal));
        if (sheet is null)
            throw new InvalidDataException("Arbeidsboken mangler arket '" + WorksheetName + "'.");

        string relationshipId = (string?)sheet.Attribute(XName.Get("id", DocumentRelationshipNamespace))
            ?? throw new InvalidDataException("Unntaksarket mangler relasjons-ID.");
        XElement relationship = relationships.Descendants(XName.Get("Relationship", PackageRelationshipNamespace))
            .SingleOrDefault(element => string.Equals((string?)element.Attribute("Id"), relationshipId, StringComparison.Ordinal))
            ?? throw new InvalidDataException("Fant ikke relasjonen til unntaksarket.");
        string target = (string?)relationship.Attribute("Target")
            ?? throw new InvalidDataException("Relasjonen til unntaksarket mangler mål.");
        string worksheetPath = ResolveWorksheetPath(target);
        XDocument worksheet = ReadXml(archive, worksheetPath);
        List<string> sharedStrings = ReadSharedStrings(archive);
        XNamespace ns = MainNamespace;

        var rules = new List<Rule14ExceptionRule>();
        foreach (XElement row in worksheet.Descendants(ns + "row"))
        {
            XElement? firstColumnCell = row.Elements(ns + "c")
                .FirstOrDefault(cell => GetColumnNumber((string?)cell.Attribute("r")) == 1);
            if (firstColumnCell is null) continue;
            string value = ReadCellValue(firstColumnCell, ns, sharedStrings).Trim();
            if (value.Length == 0) continue;
            rules.Add(Rule14ExceptionRule.Parse(value));
        }

        if (rules.Count == 0)
            throw new InvalidDataException("Unntaksarket inneholder ingen familieprefikser.");
        if (rules.Select(rule => rule.RuleKey).Distinct(StringComparer.OrdinalIgnoreCase).Count() != rules.Count)
            throw new InvalidDataException("Unntaksarket inneholder dupliserte regler.");
        return rules;
    }

    private static XDocument ReadXml(ZipArchive archive, string path)
    {
        ZipArchiveEntry entry = archive.GetEntry(path)
            ?? throw new InvalidDataException("Arbeidsboken mangler " + path + ".");
        using Stream stream = entry.Open();
        return XDocument.Load(stream);
    }

    private static List<string> ReadSharedStrings(ZipArchive archive)
    {
        ZipArchiveEntry? entry = archive.GetEntry("xl/sharedStrings.xml");
        if (entry is null) return new List<string>();
        XNamespace ns = MainNamespace;
        using Stream stream = entry.Open();
        XDocument document = XDocument.Load(stream);
        return document.Descendants(ns + "si")
            .Select(item => string.Concat(item.Descendants(ns + "t").Select(text => text.Value)))
            .ToList();
    }

    private static string ReadCellValue(XElement cell, XNamespace ns, List<string> sharedStrings)
    {
        string? type = (string?)cell.Attribute("t");
        if (string.Equals(type, "inlineStr", StringComparison.Ordinal))
            return string.Concat(cell.Descendants(ns + "t").Select(text => text.Value));

        string? value = cell.Element(ns + "v")?.Value;
        if (string.Equals(type, "s", StringComparison.Ordinal))
        {
            if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int index)
                || index < 0 || index >= sharedStrings.Count)
                throw new InvalidDataException("Ugyldig delt tekstindeks i unntaksarket.");
            return sharedStrings[index];
        }
        return value ?? string.Empty;
    }

    private static int GetColumnNumber(string? cellReference)
    {
        if (string.IsNullOrEmpty(cellReference)) return -1;
        int columnNumber = 0;
        foreach (char character in cellReference)
        {
            if (character is < 'A' or > 'Z' && character is < 'a' or > 'z') break;
            char normalized = char.ToUpperInvariant(character);
            columnNumber = checked(columnNumber * 26 + normalized - 'A' + 1);
        }
        return columnNumber;
    }

    private static string ResolveWorksheetPath(string target)
    {
        var segments = new List<string>();
        string normalized = target.Replace('\\', '/').TrimStart('/');
        if (!target.StartsWith("/", StringComparison.Ordinal)) segments.Add("xl");
        foreach (string segment in normalized.Split('/'))
        {
            if (segment.Length == 0 || segment == ".") continue;
            if (segment == "..")
            {
                if (segments.Count == 0) throw new InvalidDataException("Ugyldig sti til unntaksarket.");
                segments.RemoveAt(segments.Count - 1);
                continue;
            }
            segments.Add(segment);
        }
        return string.Join("/", segments);
    }
}