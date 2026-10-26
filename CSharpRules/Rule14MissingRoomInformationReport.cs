#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Threading;
using Autodesk.Revit.DB;
using System.Xml.Linq;

namespace CW.Assistant.Generated;

internal sealed class Rule14MissingRoomInformationReport
{
    internal const string Version = "0.0.4";
    internal const string BasModelTitle = "U_F_BAS_FBU_RIE_XXX";
    internal const string BasModelPath = "Autodesk Docs://Fornebubanen/U_F_BAS_FBU_RIE_XXX.rvt";
    internal const string StaModelTitle = "U_F_STA_FBU_RIE_XXX";
    internal const string StaModelPath = "Autodesk Docs://Fornebubanen/U_F_STA_FBU_RIE_XXX.rvt";

    internal static readonly string[] RoomParameterNames = { "Romnavn", "dRofus_RoomfunctionNo" };
    internal static readonly string[] Headers =
    {
        "ElementId", "Revit-kategori", "Familienavn", "Type", "FOB_Entreprise",
        "FOB_Leveransepakke", "FOB_Merkestreng", "Romnavn", "dRofus_RoomfunctionNo", "Avvik"
    };
    internal static readonly string[] ExceptionHeaders = Headers.Append("Unntaksregel").ToArray();

    private const string MissingMarker = "--";
    private const string IfcExcludeParameterName = "MC Exclude From IFC View";
    private const string TemporaryDeliveryWorkset = "RIE Temp Varelevering";
    private const string SpaceRoomConduitTypeName = "Trekkerør 25 mm K5B";
    private const string SpaceRoomElbowTypeName = "Trekkerør bend 25 mm K5B";

    private static readonly HashSet<long> ElectricalCategoryIds = new(
        new[]
        {
            BuiltInCategory.OST_CableTray,
            BuiltInCategory.OST_CableTrayFitting,
            BuiltInCategory.OST_Conduit,
            BuiltInCategory.OST_ConduitFitting,
            BuiltInCategory.OST_DataDevices,
            BuiltInCategory.OST_FireAlarmDevices,
            BuiltInCategory.OST_ElectricalEquipment,
            BuiltInCategory.OST_ElectricalFixtures,
            BuiltInCategory.OST_LightingFixtures,
            BuiltInCategory.OST_LightingDevices,
            BuiltInCategory.OST_CommunicationDevices,
            BuiltInCategory.OST_SecurityDevices
        }.Select(category => new ElementId(category).Value));

    private static readonly HashSet<long> CenterLineCategoryIds = new(
        Enum.GetValues(typeof(BuiltInCategory)).Cast<BuiltInCategory>()
            .Where(category => category.ToString().EndsWith("CenterLine", StringComparison.Ordinal))
            .Select(category => new ElementId(category).Value));

    internal string Execute(Document document, string workspaceRoot, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        cancellationToken.ThrowIfCancellationRequested();
        EnsureSupportedModel(document);
        string root = ResolveRoot(workspaceRoot);
        string reportDirectory = Path.Combine(root, "logs", "reports");
        Directory.CreateDirectory(reportDirectory);
        List<Rule14ExceptionRule> exceptionRules = Rule14ExceptionRules.ReadWorkbookExceptions(
            Path.Combine(reportDirectory, Rule14ExceptionRules.WorkbookFileName));

        int scopedCount = 0;
        var rows = new List<string[]>();
        var exceptionRows = new List<string[]>();
        List<Element> elements = new FilteredElementCollector(document)
            .WhereElementIsNotElementType()
            .ToElements()
            .OrderBy(element => element.Id.Value)
            .ToList();

        foreach (Element element in elements)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsTarget(element)) continue;
            scopedCount++;

            var issues = new List<string>();
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            bool requiresPlaceholder = RequiresRoomPlaceholder(element);
            foreach (string parameterName in RoomParameterNames)
            {
                IList<Parameter> parameters = element.GetParameters(parameterName);
                string value = string.Join(" | ", parameters.Select(GetParameterText));
                values[parameterName] = value;

                string? issue = Rule14RoomValueValidator.FindParameterIssue(parameters.Count,
                    parameters.Count == 1 ? parameters[0].StorageType.ToString() : null,
                    parameters.Count == 1 ? parameters[0].AsString() : null,
                    requiresPlaceholder);
                if (issue is not null) issues.Add(parameterName + ": " + issue);
            }

            if (issues.Count == 0) continue;
            string familyName = GetFamilyName(element);
            string[] row = new[]
            {
                element.Id.Value.ToString(CultureInfo.InvariantCulture),
                element.Category?.Name ?? string.Empty,
                familyName,
                GetTypeName(element),
                GetInstanceParameterText(element, "FOB_Entreprise"),
                GetInstanceParameterText(element, "FOB_Leveransepakke"),
                GetInstanceParameterText(element, "FOB_Merkestreng"),
                values["Romnavn"],
                values["dRofus_RoomfunctionNo"],
                string.Join("; ", issues)
            };
            bool isConduit = IsCategory(element, BuiltInCategory.OST_Conduit);
            string? sizeValue = GetSingleStringInstanceParameter(element, "Size");
            Rule14ExceptionRule? exceptionRule = exceptionRules.FirstOrDefault(rule =>
                rule.MatchesElement(familyName, isConduit, GetTypeName(element), sizeValue));
            if (exceptionRule is null)
                rows.Add(row);
            else
                exceptionRows.Add(row.Append(exceptionRule.Description).ToArray());
        }

        cancellationToken.ThrowIfCancellationRequested();
        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
        string reportPath = Path.Combine(reportDirectory, "Mangler_rominformasjon " + timestamp + ".xlsx");
        ExcelListReportWriter.Write(reportPath, new[]
        {
            new ExcelListReportWriter.WorksheetData("Mangler rominfo", Headers, rows),
            new ExcelListReportWriter.WorksheetData("Unntak", ExceptionHeaders, exceptionRows)
        });

        return string.Format(CultureInfo.InvariantCulture,
            "Regel 14 v{0}: elementer i utvalget {1}; mangler eller ugyldig rominformasjon {2}; unntatt etter arbeidsboken {3}; rapport: {4}",
            Version, scopedCount, rows.Count, exceptionRows.Count, reportPath);
    }

    private static bool IsTarget(Element element)
    {
        long categoryId = element.Category?.Id.Value ?? long.MinValue;
        if (!ElectricalCategoryIds.Contains(categoryId) || CenterLineCategoryIds.Contains(categoryId)) return false;
        if (IsTemporaryDeliveryException(element) || IsExcludedFromIfc(element)) return false;

        IList<Parameter> parameters = element.GetParameters("FOB_Entreprise");
        if (parameters.Count != 1 || parameters[0].StorageType != StorageType.String) return false;
        string enterprise = parameters[0].AsString() ?? string.Empty;
        return string.Equals(enterprise, "K5B", StringComparison.Ordinal)
            || (string.IsNullOrWhiteSpace(enterprise) && !parameters[0].IsReadOnly);
    }

    private static bool RequiresRoomPlaceholder(Element element)
    {
        if (UsesConduitSpaceRoom(element)) return false;
        if (IsCategory(element, BuiltInCategory.OST_Conduit)
            || IsCategory(element, BuiltInCategory.OST_ConduitFitting)) return true;

        IList<Parameter> parameters = element.GetParameters("FOB_Eksistensstatus");
        return parameters.Count == 1
            && parameters[0].StorageType == StorageType.String
            && string.Equals(parameters[0].AsString(), "Infoelement", StringComparison.Ordinal);
    }

    private static bool UsesConduitSpaceRoom(Element element)
    {
        if (IsCategory(element, BuiltInCategory.OST_Conduit))
            return string.Equals(GetTypeElement(element)?.Name, SpaceRoomConduitTypeName, StringComparison.Ordinal);
        if (!IsCategory(element, BuiltInCategory.OST_ConduitFitting) || element is not FamilyInstance fitting)
            return false;

        Parameter? part = fitting.Symbol.Family.get_Parameter(BuiltInParameter.FAMILY_CONTENT_PART_TYPE);
        if (part is null || part.StorageType != StorageType.Integer || part.AsInteger() != (int)PartType.Elbow)
            return false;
        return string.Equals(fitting.Symbol.Name, SpaceRoomElbowTypeName, StringComparison.Ordinal)
            || GetConnectedConduits(fitting).Any(source =>
                string.Equals(GetTypeElement(source)?.Name, SpaceRoomConduitTypeName, StringComparison.Ordinal));
    }

    private static List<Element> GetConnectedConduits(FamilyInstance fitting)
    {
        var connected = new Dictionary<long, Element>();
        if (fitting.MEPModel?.ConnectorManager is null) return connected.Values.ToList();
        foreach (Connector connector in fitting.MEPModel.ConnectorManager.Connectors)
        {
            if (connector.ConnectorType != ConnectorType.End) continue;
            foreach (Connector reference in connector.AllRefs)
            {
                Element source = reference.Owner;
                if (reference.ConnectorType == ConnectorType.End
                    && IsCategory(source, BuiltInCategory.OST_Conduit)
                    && connector.IsConnectedTo(reference)
                    && reference.IsConnectedTo(connector))
                    connected[source.Id.Value] = source;
            }
        }
        return connected.Values.ToList();
    }

    private static bool IsTemporaryDeliveryException(Element element) =>
        string.Equals(element.Document.Title, BasModelTitle, StringComparison.OrdinalIgnoreCase)
        && string.Equals(element.Document.GetWorksetTable().GetWorkset(element.WorksetId)?.Name,
            TemporaryDeliveryWorkset, StringComparison.OrdinalIgnoreCase);

    private static bool IsExcludedFromIfc(Element element)
    {
        IList<Parameter> parameters = element.GetParameters(IfcExcludeParameterName);
        return parameters.Count == 1
            && parameters[0].StorageType == StorageType.Integer
            && parameters[0].HasValue
            && parameters[0].AsInteger() != 0;
    }

    private static bool IsCategory(Element element, BuiltInCategory category) =>
        element.Category is not null && element.Category.Id.Value == (long)category;

    private static ElementType? GetTypeElement(Element element) =>
        element.Document.GetElement(element.GetTypeId()) as ElementType;

    private static string GetFamilyName(Element element) => element is FamilyInstance instance
        ? instance.Symbol.Family?.Name ?? string.Empty
        : GetTypeElement(element)?.FamilyName ?? string.Empty;

    private static string GetTypeName(Element element) => GetTypeElement(element)?.Name ?? string.Empty;

    private static string GetInstanceParameterText(Element element, string name)
    {
        IList<Parameter> parameters = element.GetParameters(name);
        return parameters.Count == 1 ? GetParameterText(parameters[0]) : string.Empty;
    }

    private static string? GetSingleStringInstanceParameter(Element element, string name)
    {
        IList<Parameter> parameters = element.GetParameters(name);
        return parameters.Count == 1 && parameters[0].StorageType == StorageType.String
            ? parameters[0].AsString()
            : null;
    }

    private static string GetParameterText(Parameter parameter)
    {
        string? value = parameter.StorageType == StorageType.String ? parameter.AsString() : parameter.AsValueString();
        return value ?? string.Empty;
    }

    private static void EnsureSupportedModel(Document document)
    {
        bool isBas = string.Equals(document.Title, BasModelTitle, StringComparison.Ordinal)
            && string.Equals(document.PathName, BasModelPath, StringComparison.Ordinal);
        bool isSta = string.Equals(document.Title, StaModelTitle, StringComparison.Ordinal)
            && string.Equals(document.PathName, StaModelPath, StringComparison.Ordinal);
        if (!isBas && !isSta)
            throw new InvalidOperationException("Regel 14 støtter bare de verifiserte BAS- og STA-modellene.");
    }

    private static string ResolveRoot(string workspaceRoot)
    {
        if (workspaceRoot is null) throw new ArgumentNullException(nameof(workspaceRoot));
        if (workspaceRoot.Length > 1024) throw new ArgumentException("Arbeidsområderoten er for lang.");
        string root;
        if (workspaceRoot.Length == 0)
            root = Path.GetDirectoryName(typeof(Rule14MissingRoomInformationReport).Assembly.Location)
                ?? throw new InvalidOperationException("Runnerens plassering er ukjent; oppgi arbeidsområderot.");
        else
        {
            if (!Path.IsPathFullyQualified(workspaceRoot))
                throw new ArgumentException("Arbeidsområderoten må være en absolutt katalogbane.");
            root = Path.GetFullPath(workspaceRoot);
        }
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException("Fant ikke arbeidsområderoten: " + root);
        return root;
    }
}