#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;

namespace CW.Assistant.Generated;

internal sealed class Rule13CablingEquipmentReport
{
    internal const string ModelPath = "Autodesk Docs://Fornebubanen/U_F_BAS_FBU_RIE_XXX.rvt";
    internal const string StationModelPath = "Autodesk Docs://Fornebubanen/U_F_STA_FBU_RIE_XXX.rvt";
    internal static string ModelCode(string path) => path == ModelPath ? "BAS"
        : path == StationModelPath ? "STA" : throw new InvalidOperationException("Bare eksakt BAS- eller STA-modellsti støttes.");
    internal const string Enterprise = "K5B";
    internal const string GroundingEnterprise = "K5A";
    internal const string GroundingDraftingView = "Utskrift 450 Detaljtegning jordskinner";

    internal static string[] ParseJrpMarks(string text) => Regex.Matches(text ?? "",
            @"(?<![\p{L}\p{N}_-])[A-Z0-9]+(?:-[A-Z0-9]+)+(?![\p{L}\p{N}_-])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
        .Select(match => match.Value)
        .Where(mark => mark.Split('-').Skip(1).SkipLast(1).Contains("JRP", StringComparer.OrdinalIgnoreCase))
        .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    internal static string[] ParseJosMarks(string text) => Regex.Matches(text ?? "",
        @"(?<![\p{L}\p{N}_-])BYG200-450-JOS-00[1-6](?![\p{L}\p{N}_-])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
        .Select(match => match.Value).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    internal static bool IsGroundingCandidate(bool isFamilyInstance, bool isElectricalFixture,
        string family, string enterprise, int? ifcValue, string groundingClass = "JRP") =>
        isFamilyInstance && isElectricalFixture
        && (groundingClass == "JRP" ? family.StartsWith("Jordbolt", StringComparison.OrdinalIgnoreCase)
            : groundingClass == "JOS" && family.StartsWith("Jordspyd", StringComparison.OrdinalIgnoreCase))
        && string.Equals(enterprise.Trim(), GroundingEnterprise, StringComparison.Ordinal) && !IsIfcExcluded(ifcValue);

    private static BuiltInCategory[] Categories => new[]
    {
        BuiltInCategory.OST_ElectricalEquipment, BuiltInCategory.OST_ElectricalFixtures,
        BuiltInCategory.OST_LightingFixtures, BuiltInCategory.OST_LightingDevices,
        BuiltInCategory.OST_DataDevices, BuiltInCategory.OST_FireAlarmDevices,
        BuiltInCategory.OST_CommunicationDevices, BuiltInCategory.OST_SecurityDevices
    };
    internal static readonly string[] HiddenEquipmentHeaders = { "UniqueId", "Revit-kategori", "Mark", "MC Exclude From IFC View" };
    internal static readonly string[] Headers =
    {
        "ElementId", "UniqueId", "Revit-kategori", "Familienavn", "Type", "Mark",
        "FOB_Merkestreng", "FOB_Leveransepakke", "MC Exclude From IFC View",
        "Load Name i kurs", "Nivå", "Kablet", "BetjenerMerke", "BetjenerNavn"
    };
    internal static readonly string[] EquipmentListHeaders =
    {
        "ElementId", "Familienavn", "Type", "FOB_Merkestreng", "FOB_Leveransepakke",
        "Nivå", "BetjenerMerke", "BetjenerNavn"
    };
    internal static readonly string[] GroundingListHeaders =
    {
        "ElementId", "UniqueId", "Revit-kategori", "Familienavn", "Type", "Mark",
        "FOB_Merkestreng", "FOB_Leveransepakke", "Nivå", "TextNote ElementId", "DraftingView", "Grunnlag"
    };
    private static readonly string[] GroundingDetailHeaders =
        { "FOB_Entreprise", "TextNote ElementId", "DraftingView", "Grunnlag" };
    private static readonly string[] GroundingSourceHeaders = Headers.Concat(GroundingDetailHeaders).ToArray();
    internal static readonly Dictionary<string, double> EquipmentListColumnWidths = new(StringComparer.Ordinal)
    {
        ["ElementId"] = 11.5703125,
        ["Familienavn"] = 59.5703125,
        ["Type"] = 13,
        ["FOB_Merkestreng"] = 24.28515625,
        ["FOB_Leveransepakke"] = 21.7109375,
        ["Nivå"] = 7.140625,
        ["BetjenerMerke"] = 23.28515625,
        ["BetjenerNavn"] = 74.140625
    };
    internal static readonly Dictionary<string, double> GroundingListColumnWidths = new(StringComparer.Ordinal)
    {
        ["ElementId"] = 11.5703125,
        ["UniqueId"] = 45,
        ["Revit-kategori"] = 16.140625,
        ["Familienavn"] = 21.5703125,
        ["Type"] = 13,
        ["Mark"] = 7.42578125,
        ["FOB_Merkestreng"] = 19.42578125,
        ["FOB_Leveransepakke"] = 21.7109375,
        ["Nivå"] = 6.7109375,
        ["TextNote ElementId"] = 19.85546875,
        ["DraftingView"] = 32,
        ["Grunnlag"] = 98
    };

    internal static List<string[]> ProjectRows(string[] sourceHeaders, string[] targetHeaders, IEnumerable<string[]> rows)
    {
        int[] indexes = targetHeaders.Select(header => Array.IndexOf(sourceHeaders, header)).ToArray();
        if (indexes.Any(index => index < 0))
            throw new InvalidOperationException("Målkolonne mangler i kildelisten.");
        return rows.Select(row => indexes.Select(index => index < row.Length ? row[index] : "").ToArray()).ToList();
    }

    internal static string ReportFileName(string modelCode) =>
        "Utstyr_til_kabling_K5B_" + modelCode + "_FBU.xlsx";

    internal sealed class Configuration
    {
        public int SchemaVersion { get; set; }
        public string ModelPath { get; set; } = "";
        public string Enterprise { get; set; } = "";
        public string GroundingEnterprise { get; set; } = "";
        public string SourceWorkbook { get; set; } = "";
        public string SourceSheet { get; set; } = "";
        public string Match { get; set; } = "";
        public string[] NamePrefixes { get; set; } = Array.Empty<string>();
        public string[] Families { get; set; } = Array.Empty<string>();
    }

    internal static Configuration ReadConfiguration(string path)
    {
        var config = JsonSerializer.Deserialize<Configuration>(File.ReadAllText(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("Tom unntakskonfigurasjon.");
        if (config.SchemaVersion != 1 || config.ModelPath != ModelPath || config.Enterprise != Enterprise
            || config.GroundingEnterprise != GroundingEnterprise
            || config.Match != "familyExactIgnoreCaseTrim" || string.IsNullOrWhiteSpace(config.SourceWorkbook)
            || string.IsNullOrWhiteSpace(config.SourceSheet) || config.Families is null
            || config.Families.Length == 0 || config.Families.Any(string.IsNullOrWhiteSpace)
            || config.Families.Select(name => name.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != config.Families.Length)
            throw new InvalidDataException("Ugyldig konfigurasjon: krever schemaVersion=1, BAS, K5B, groundingEnterprise=K5A, eksakt familiematch og unike, ikke-tomme familier.");
        if (config.NamePrefixes is null || config.NamePrefixes.Any(string.IsNullOrWhiteSpace)
            || config.NamePrefixes.Select(prefix => prefix.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != config.NamePrefixes.Length)
            throw new InvalidDataException("Navneprefikser må være unike og ikke-tomme.");
        return config;
    }

    internal static string ResolveRoot(string workspaceRoot)
    {
        if (workspaceRoot is null) throw new ArgumentNullException(nameof(workspaceRoot));
        if (workspaceRoot.Length > 1024) throw new ArgumentException("Arbeidsområderoten er for lang.");
        string root;
        if (workspaceRoot.Length == 0)
            root = Path.GetDirectoryName(typeof(Rule13CablingEquipmentReport).Assembly.Location)
                ?? throw new InvalidOperationException("Runnerens plassering er ukjent; oppgi WorkspaceRoot.");
        else
        {
            if (!Path.IsPathFullyQualified(workspaceRoot)) throw new ArgumentException("WorkspaceRoot må være en absolutt katalogbane.");
            root = Path.GetFullPath(workspaceRoot);
        }
        if (!File.Exists(Path.Combine(root, "config", "CablingEquipmentExceptions.json")))
            throw new FileNotFoundException("Fant ikke config/CablingEquipmentExceptions.json under valgt rot: " + root);
        return root;
    }

    internal static bool IsExcluded(string family, IEnumerable<string> exceptions) =>
        exceptions.Any(name => string.Equals(family.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase));

    internal static string? MatchNamePrefix(string family, string type, IEnumerable<string> prefixes) =>
        prefixes.FirstOrDefault(prefix => family.Trim().StartsWith(prefix.Trim(), StringComparison.OrdinalIgnoreCase)
            || type.Trim().StartsWith(prefix.Trim(), StringComparison.OrdinalIgnoreCase));

    internal static string[] ServedValues(string family, string type, Func<string, string> readInstance) =>
        MatchNamePrefix(family, type, new[] { "Koblingspunkt" }) is null
            ? new[] { "", "" }
            : new[] { readInstance("PGF_RIE_BetjenerMerke"), readInstance("PGF_RIE_BetjenerNavn") };

    internal static bool IsIfcExcluded(int? value) => value.HasValue && value.Value != 0;

    internal static bool IsBox(string family, string type) =>
        new[] { family, type }.Any(name => name.Contains("boks", StringComparison.OrdinalIgnoreCase)
            || name.Contains("box", StringComparison.OrdinalIgnoreCase));

    internal static Dictionary<string, long[]> MatchGroundingIds(IEnumerable<string> marks,
        IEnumerable<(long Id, string Mark)> candidates)
    {
        var byMark = candidates.ToLookup(candidate => candidate.Mark.Trim(), StringComparer.OrdinalIgnoreCase);
        return marks.Select(mark => mark.Trim()).Distinct(StringComparer.OrdinalIgnoreCase)
            .ToDictionary(mark => mark, mark => byMark[mark].Select(candidate => candidate.Id)
                .Distinct().OrderBy(id => id).ToArray(), StringComparer.OrdinalIgnoreCase);
    }

    private sealed record GroundingSource(string Mark, string TextIds, string Class);

    private static Dictionary<long, GroundingSource> ReadGroundingTargets(Document document,
        string groundingClass, CancellationToken cancellationToken, out List<string[]> unresolved)
    {
        using var views = new FilteredElementCollector(document);
        var matchingViews = views.OfClass(typeof(View)).Cast<View>()
            .Where(view => string.Equals(view.Name, GroundingDraftingView, StringComparison.Ordinal)).ToArray();
        if (matchingViews.Length != 1 || matchingViews[0].ViewType != ViewType.DraftingView)
            throw new InvalidOperationException("Krever ett entydig DraftingView: " + GroundingDraftingView);
        View draftingView = matchingViews[0];
        var sources = new Dictionary<string, HashSet<long>>(StringComparer.OrdinalIgnoreCase);
        using (var notes = new FilteredElementCollector(document, draftingView.Id))
        {
            foreach (TextNote note in notes.OfClass(typeof(TextNote)).Cast<TextNote>()
                         .Where(note => note.OwnerViewId == draftingView.Id))
            {
                cancellationToken.ThrowIfCancellationRequested();
                foreach (string mark in groundingClass == "JOS" ? ParseJosMarks(note.Text) : ParseJrpMarks(note.Text))
                {
                    if (!sources.TryGetValue(mark, out var textIds))
                        sources.Add(mark, textIds = new HashSet<long>());
                    textIds.Add(note.Id.Value);
                }
            }
        }
        var candidates = new List<(long Id, string Mark)>();
        using (var fixtures = new FilteredElementCollector(document))
        {
            foreach (FamilyInstance instance in fixtures.OfCategory(BuiltInCategory.OST_ElectricalFixtures)
                         .OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                Parameter? ifc = instance.LookupParameter("MC Exclude From IFC View");
                int? ifcValue = ifc is { HasValue: true, StorageType: StorageType.Integer } ? ifc.AsInteger() : null;
                if (IsGroundingCandidate(true, true, instance.Symbol?.Family?.Name ?? "",
                    Text(instance.LookupParameter("FOB_Entreprise")), ifcValue, groundingClass))
                    candidates.Add((instance.Id.Value, Text(instance.LookupParameter("FOB_Merkestreng"))));
            }
        }
        var targets = new Dictionary<long, GroundingSource>();
        unresolved = new List<string[]>();
        foreach (var match in MatchGroundingIds(sources.Keys, candidates).OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            string textIds = string.Join("; ", sources[match.Key].OrderBy(id => id)
                .Select(id => id.ToString(CultureInfo.InvariantCulture)));
            if (match.Value.Length == 1)
                targets.Add(match.Value[0], new GroundingSource(match.Key, textIds, groundingClass));
            else
                unresolved.Add(new[]
                {
                    match.Key, textIds, GroundingDraftingView,
                    match.Value.Length == 0 ? $"Ingen gyldig K5A {groundingClass}-instans" : $"Flere gyldige K5A {groundingClass}-instanser; ingen valgt",
                    match.Value.Length.ToString(CultureInfo.InvariantCulture),
                    string.Join("; ", match.Value.Select(id => id.ToString(CultureInfo.InvariantCulture)))
                });
        }
        return targets;
    }

    internal string Execute(Document document, string workspaceRoot, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string modelCode = ModelCode(document.PathName);
        string root = ResolveRoot(workspaceRoot);
        string configPath = Path.Combine(root, "config", "CablingEquipmentExceptions.json");
        Configuration config = ReadConfiguration(configPath);
        DateTimeOffset exportedAt = DateTimeOffset.Now;
        var rows = new List<string[]>();
        var groundingRows = new List<string[]>();
        var josRows = new List<string[]>();
        var groundingTargets = ReadGroundingTargets(document, "JRP", cancellationToken, out var unresolvedGrounding);
        var unresolvedJos = new List<string[]>();
        var josTargets = modelCode == "BAS"
            ? ReadGroundingTargets(document, "JOS", cancellationToken, out unresolvedJos)
            : new Dictionary<long, GroundingSource>();
        foreach (var target in josTargets) groundingTargets.Add(target.Key, target.Value);
        var familyCounts = config.Families.ToDictionary(name => name.Trim(), _ => 0, StringComparer.OrdinalIgnoreCase);
        var prefixCounts = config.NamePrefixes.ToDictionary(prefix => prefix.Trim(), _ => 0, StringComparer.OrdinalIgnoreCase);
        int candidates = 0;
        int ifcExcluded = 0;
        int boxesExcluded = 0;
        using var collector = new FilteredElementCollector(document);
        foreach (Element element in collector.WhereElementIsNotElementType()
                     .WherePasses(new ElementMulticategoryFilter(Categories)).OrderBy(element => element.Id.Value))
        {
            cancellationToken.ThrowIfCancellationRequested();
            bool isGrounding = groundingTargets.TryGetValue(element.Id.Value, out var groundingSource);
            string actualEnterprise = Text(element.LookupParameter("FOB_Entreprise"));
            bool isK5B = string.Equals(actualEnterprise.Trim(), Enterprise, StringComparison.Ordinal);
            if (!isK5B && !isGrounding) continue;
            if (isK5B) candidates++;
            FamilySymbol? symbol = (element as FamilyInstance)?.Symbol;
            string family = symbol?.Family?.Name ?? "";
            if (isK5B && IsBox(family, symbol?.Name ?? ""))
            {
                boxesExcluded++;
                continue;
            }
            if (isK5B && IsExcluded(family, config.Families))
            {
                familyCounts[family.Trim()]++;
                continue;
            }
            string? matchedPrefix = isK5B ? MatchNamePrefix(family, symbol?.Name ?? "", config.NamePrefixes) : null;
            if (matchedPrefix is not null)
            {
                prefixCounts[matchedPrefix.Trim()]++;
                continue;
            }
            Parameter? ifcParameter = element.LookupParameter("MC Exclude From IFC View");
            int? ifcValue = ifcParameter is { HasValue: true, StorageType: StorageType.Integer } ? ifcParameter.AsInteger() : null;
            if (IsIfcExcluded(ifcValue))
            {
                if (isK5B) ifcExcluded++;
                continue;
            }
            string loads = "";
            if (element is FamilyInstance instance && instance.MEPModel is not null)
            {
                ISet<ElectricalSystem>? circuits = instance.MEPModel.GetElectricalSystems();
                if (circuits is not null)
                    loads = string.Join("; ", circuits.Select(circuit => Text(circuit.LookupParameter("Load Name")).Trim())
                        .Where(value => value.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(value => value, StringComparer.OrdinalIgnoreCase));
            }
            var row = new[]
            {
                element.Id.Value.ToString(CultureInfo.InvariantCulture), element.UniqueId,
                element.Category?.Name ?? "", family, symbol?.Name ?? "",
                Text(element.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)),
                Text(element.LookupParameter("FOB_Merkestreng")), Text(element.LookupParameter("FOB_Leveransepakke")),
                Text(ifcParameter), loads, document.GetElement(element.LevelId)?.Name ?? "", ""
            }.Concat(ServedValues(family, symbol?.Name ?? "", name => Text(element.LookupParameter(name)))).ToArray();
            rows.Add(row);
            if (isGrounding)
                (groundingSource!.Class == "JOS" ? josRows : groundingRows).Add(row.Concat(new[]
                {
                    actualEnterprise, groundingSource!.TextIds, GroundingDraftingView,
                    "TextNote-merke " + groundingSource.Mark + "; eksakt FOB_Merkestreng; unik K5A " + (groundingSource.Class == "JOS" ? "Jordspyd" : "Jordbolt") + " i ElectricalFixtures; IFC-filter"
                }).ToArray());
        }
        var exclusions = familyCounts.Select(pair => new[] { pair.Key, pair.Value.ToString(CultureInfo.InvariantCulture) }).ToList();
        exclusions.AddRange(prefixCounts.Select(pair => new[] { "Prefiks familie/type: " + pair.Key, pair.Value.ToString(CultureInfo.InvariantCulture) }));
        var scope = new List<string[]>
        {
            new[] { "Runner", "Rule13Runner v0.0.7; lesende utility utenfor standard BIM-sekvens" },
            new[] { "Modellsti", document.PathName }, new[] { "Modellnavn", document.Title },
            new[] { "Eksporttid", exportedAt.ToString("O", CultureInfo.InvariantCulture) },
            new[] { "Entreprise", "Blandet K5B og avgrenset K5A JRP/JOS; instansparameter FOB_Entreprise, trimmet eksakt; ingen typefallback" },
            new[] { "Kategorier", string.Join("; ", Categories.Select(category => category.ToString())) },
            new[] { "Omfang", "Utstyr K5B inneholder K5B-utvalget og entydige K5A JRP-jordbolter/JOS-jordspyd fra angitt drafting-view; ingen lenker, rom eller nærhets-/3D-filter" },
            new[] { "K5A JRP-kilde", GroundingDraftingView + "; kun view-eide TextNote; hele JRP-merker, eksakt skiftleiefri instans FOB_Merkestreng; ingen Mark-fallback" },
            new[] { "K5A JRP-rader", groundingRows.Count.ToString(CultureInfo.InvariantCulture) },
            new[] { "Uavklarte JRP-merker", unresolvedGrounding.Count.ToString(CultureInfo.InvariantCulture) },
            new[] { "K5A JOS-kilde", modelCode == "BAS"
                ? GroundingDraftingView + "; kun view-eide TextNote; seks bekreftede helstrengsmerker BYG200-450-JOS-001 til 006; eksakt skiftleiefri instans FOB_Merkestreng; ingen Mark-fallback"
                : "FBU: jordspyd inngår ikke i brukerens omfang; ingen JOS-tillegg." },
            new[] { "K5A JOS-rader", josRows.Count.ToString(CultureInfo.InvariantCulture) },
            new[] { "Uavklarte JOS-merker", unresolvedJos.Count.ToString(CultureInfo.InvariantCulture) },
            new[] { "K5B-utvalg", "Alle instanser i dokumentet, uavhengig av aktivt view; eksisterende K5B-filtre beholdes" },
            new[] { "Familiematch", config.Match + "; familieunntak telles før IFC-filter" },
            new[] { "Navneprefikser", "Familie eller type begynner med, trimmet og skiftleiefritt: " + string.Join("; ", config.NamePrefixes) },
            new[] { "Boksfilter", "Familie eller type inneholder boks/box, skiftleiefritt; telles før familieunntak" },
            new[] { "Infoelementfilter", "Opprinnelig snapshot hadde et ekstra status-/navnefilter. Definisjonen er ukjent og gjenskapes ikke her; utvalget er ikke identisk med snapshotet." },
            new[] { "Konfigurasjon", configPath },
            new[] { "Unntaksgrunnlag", "Samme brukerbekreftede familieunntak for BAS og STA; config.modelPath angir opprinnelig BAS-kilde, ikke aktiv målmodell." },
            new[] { "Snapshot-kilde (kun referanse)", config.SourceWorkbook + " | " + config.SourceSheet },
            new[] { "IFC-avgrensning", "Instansparameter MC Exclude From IFC View: ikke-null integer ulik 0 utelates fra NY modellrapport" },
            new[] { "Opprinnelig snapshot", "Inkluderte IFC-ekskluderte elementer. Separat snapshot-filtrering bevares; denne eksporten leser eller endrer ikke snapshotet." },
            new[] { "Kablet", "Tomt i ny eksport. Eksisterende Kablet-kilder og manuelle notater importeres eller erstattes aldri." },
            new[] { "BetjenerMerke / BetjenerNavn", "Bare familie/type med prefiks Koblingspunkt, trimmet og skiftleiefritt: instansparametere PGF_RIE_BetjenerMerke / PGF_RIE_BetjenerNavn; ingen typefallback. Andre elementer får tomme celler." },
            new[] { "K5B-kandidater", candidates.ToString(CultureInfo.InvariantCulture) },
            new[] { "Boksunntak", boxesExcluded.ToString(CultureInfo.InvariantCulture) },
            new[] { "Familieunntak", familyCounts.Values.Sum().ToString(CultureInfo.InvariantCulture) },
            new[] { "Navneprefiksunntak", prefixCounts.Values.Sum().ToString(CultureInfo.InvariantCulture) },
            new[] { "IFC-unntak etter familiefilter", ifcExcluded.ToString(CultureInfo.InvariantCulture) },
            new[] { "Eksporterte rader", rows.Count.ToString(CultureInfo.InvariantCulture) }
        };
        cancellationToken.ThrowIfCancellationRequested();
        string output = Path.Combine(root, "logs", "reports");
        Directory.CreateDirectory(output);
        string path = Path.Combine(output, ReportFileName(modelCode));
        var equipmentRows = ProjectRows(Headers, EquipmentListHeaders, rows);
        var jrpRows = ProjectRows(GroundingSourceHeaders, GroundingListHeaders, groundingRows);
        var josListRows = ProjectRows(GroundingSourceHeaders, GroundingListHeaders, josRows);
        ExcelListReportWriter.Write(path, new[]
        {
            new ExcelListReportWriter.WorksheetData("Omfang", new[] { "Filter", "Verdi" }, scope),
            new ExcelListReportWriter.WorksheetData("Utstyr K5B", EquipmentListHeaders, equipmentRows, columnWidths: EquipmentListColumnWidths),
            new ExcelListReportWriter.WorksheetData("K5A JRP", GroundingListHeaders, jrpRows, columnWidths: GroundingListColumnWidths),
            new ExcelListReportWriter.WorksheetData("Uavklart JRP", new[]
                { "JRP-merke", "TextNote ElementId", "DraftingView", "Årsak", "Gyldige treff", "ElementId" }, unresolvedGrounding),
            new ExcelListReportWriter.WorksheetData("K5A JOS", GroundingListHeaders, josListRows, columnWidths: GroundingListColumnWidths),
            new ExcelListReportWriter.WorksheetData("Uavklart JOS", new[]
                { "JOS-merke", "TextNote ElementId", "DraftingView", "Årsak", "Gyldige treff", "ElementId" }, unresolvedJos),
            new ExcelListReportWriter.WorksheetData("Unntak", new[] { "Familienavn / navneprefiks", "Antall utelatt" }, exclusions),
        }, overwriteExisting: true);
        return path;
    }

    private static string Text(Parameter? parameter)
    {
        if (parameter is null || !parameter.HasValue) return "";
        return parameter.StorageType switch
        {
            StorageType.String => parameter.AsString() ?? "",
            StorageType.Integer => parameter.AsInteger().ToString(CultureInfo.InvariantCulture),
            _ => parameter.AsValueString() ?? ""
        };
    }
}
