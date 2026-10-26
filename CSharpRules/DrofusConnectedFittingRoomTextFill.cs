#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;

namespace CW.Assistant.Generated;

public sealed class DrofusConnectedFittingRoomTextFill
{
    private const string Model = "Autodesk Docs://Fornebubanen/U_F_BAS_FBU_RIE_XXX.rvt";
    private const string Root = @"D:\Revit\Python\Revit_BIM_Agent\logs\reports";
    private static readonly HashSet<string> Parts = new(StringComparer.Ordinal)
    {
        "Elbow", "ChannelCableTrayElbow", "ChannelCableTrayVerticalElbow",
        "LadderCableTrayElbow", "LadderCableTrayVerticalElbow",
        "Cross", "ChannelCableTrayCross", "LadderCableTrayCross",
        "Tee", "ChannelCableTrayTee", "LadderCableTrayTee"
    };

    private sealed record Plan(Element Target, Element[] Sources, string BeforeName, string BeforeNumber,
        string AfterName, string AfterNumber, string Status, string Reason, long? SourceId,
        int BeforeRoomId, int AfterRoomId);

    public string Run(UIApplication ui, Document? doc, CancellationToken token, bool apply, string targetUniqueIds = "")
    {
        if (doc is null || doc.PathName != Model || ui.ActiveUIDocument?.Document.PathName != Model)
            throw new InvalidOperationException("Feil aktiv BAS-modell.");
        token.ThrowIfCancellationRequested();
        string[] requested = targetUniqueIds.Split(new[] { ',', ';', '\r', '\n' },
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var scope = requested.ToHashSet(StringComparer.Ordinal);
        if (targetUniqueIds.Length > 0 && scope.Count == 0)
            throw new InvalidOperationException("Angitt målutvalg må inneholde minst én UniqueId; tomt tolket utvalg avvises.");
        if (scope.Count != requested.Length)
            throw new InvalidOperationException("Duplisert UniqueId i målutvalget.");
        var plans = new List<Plan>();
        var excluded = new List<Plan>();
        foreach (Element fitting in new FilteredElementCollector(doc)
            .OfCategory(BuiltInCategory.OST_CableTrayFitting).WhereElementIsNotElementType()
            .Where(element => Text(element, "FOB_Entreprise") == "K5B").OrderBy(element => element.Id.Value))
        {
            token.ThrowIfCancellationRequested();
            if (scope.Count > 0 && !scope.Contains(fitting.UniqueId)) continue;
            if (!IsBendOrCross(fitting)) continue;
            if (IsBusbar(fitting))
            {
                string name = Text(fitting, "Romnavn"), number = Text(fitting, "dRofus_RoomfunctionNo");
                excluded.Add(new(fitting, Array.Empty<Element>(), name, number, name, number, "EXCLUDED",
                    "Strømskinne: unntatt fra denne romtekstkontrollen; modellverdier bevares.", null,
                    RoomId(fitting), RoomId(fitting)));
                continue;
            }
            plans.Add(CreatePlan(doc, fitting));
        }
        if (scope.Count > 0 && plans.Count + excluded.Count != scope.Count)
            throw new InvalidOperationException("Målutvalget inneholder manglende element, feil entreprise/kategori eller ikke støttet deltype.");

        Directory.CreateDirectory(Root);
        string stem = Path.Combine(Root, "BAS fittings rominfo "
            + DateTimeOffset.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture));
        var headers = new[] { "Status", "ElementId", "UniqueId", "FOB_Leveransepakke", "Familienavn", "Type",
            "dRofus_RoomID", "Romnavn før", "Romnavn etter", "Romnummer før", "Romnummer etter",
            "KildeElementId", "Tilkoblede rettstrekk", "Kildeverdier", "Årsak", "RoomID før", "RoomID etter" };
        List<string[]> Rows(bool after, IEnumerable<Plan>? entries = null) => (entries ?? plans).Select(plan => new[]
        {
            after && apply && plan.Status == "ELIGIBLE" ? "VERIFIED" : plan.Status,
            plan.Target.Id.Value.ToString(CultureInfo.InvariantCulture), plan.Target.UniqueId,
            Text(plan.Target, "FOB_Leveransepakke"), (plan.Target as FamilyInstance)?.Symbol.Family.Name ?? "",
            doc.GetElement(plan.Target.GetTypeId())?.Name ?? "", Text(plan.Target, "dRofus_RoomID"),
            plan.BeforeName, after ? Text(plan.Target, "Romnavn") : plan.AfterName,
            plan.BeforeNumber, after ? Text(plan.Target, "dRofus_RoomfunctionNo") : plan.AfterNumber,
            plan.SourceId?.ToString(CultureInfo.InvariantCulture) ?? "",
            string.Join(",", plan.Sources.Select(source => source.Id.Value)),
            string.Join(" | ", plan.Sources.Select(source => source.Id.Value + ": RoomID="
                + Text(source, "dRofus_RoomID") + "; nummer=" + Text(source, "dRofus_RoomfunctionNo")
                + "; navn=" + Text(source, "Romnavn"))), plan.Reason,
            plan.BeforeRoomId.ToString(CultureInfo.InvariantCulture),
            (after ? RoomId(plan.Target) : plan.AfterRoomId).ToString(CultureInfo.InvariantCulture)
        }).ToList();
        File.WriteAllLines(stem + " skriveplan.tsv", new[] { string.Join("\t", headers) }
            .Concat(Rows(false).Select(row => string.Join("\t", row.Select(Clean)))), new UTF8Encoding(false));

        var log = new List<string>
        {
            DateTimeOffset.Now.ToString("o"), "Modell=" + Model,
            "Omfang=BAS K5B-kabelbrobend/kryss/T-stykker, også skjulte; bare manglende rominformasjon.",
            "Målutvalg=" + (scope.Count == 0 ? "alle støttede fittings" : string.Join(",", requested)),
            "Unntak=strømskinner etter familie-/typenavn; utelatt=" + excluded.Count
                + "; ElementId=" + string.Join(",", excluded.Select(plan => plan.Target.Id.Value)),
            "Positive RoomID-er og alle occurrence-ID-er bevares. Bare T-stykker kan få manglende RoomID fra entydig kilde.",
            "Modus=" + (apply ? "utføring" : "forhåndsvisning")
        };
        Plan[] writes = plans.Where(plan => plan.Status == "ELIGIBLE").ToArray();
        bool committed = false;
        if (apply && writes.Length > 0)
        {
            var allowed = writes.ToDictionary(plan => plan.Target.Id.Value,
                plan => new HashSet<string>(new[]
                {
                    plan.BeforeName != plan.AfterName ? "Romnavn" : "",
                    plan.BeforeNumber != plan.AfterNumber ? "dRofus_RoomfunctionNo" : "",
                    plan.BeforeRoomId != plan.AfterRoomId ? "dRofus_RoomID" : ""
                }, StringComparer.Ordinal));
            var observed = plans.Select(plan => plan.Target).Concat(plans.SelectMany(plan => plan.Sources))
                .DistinctBy(element => element.Id.Value).ToArray();
            var before = observed.ToDictionary(element => element.Id.Value,
                element => Fingerprint(element, allowed.GetValueOrDefault(element.Id.Value)));
            var elementIds = new FilteredElementCollector(doc).WherePasses(new LogicalOrFilter(
                new ElementIsElementTypeFilter(), new ElementIsElementTypeFilter(true)))
                .ToElementIds().Select(id => id.Value).ToHashSet();
            var unexpectedChanges = new HashSet<long>();
            void Changed(object? sender, Autodesk.Revit.DB.Events.DocumentChangedEventArgs args)
            {
                if (!args.GetDocument().Equals(doc)) return;
                foreach (ElementId id in args.GetModifiedElementIds())
                    if (!before.ContainsKey(id.Value)) unexpectedChanges.Add(id.Value);
                foreach (ElementId id in args.GetAddedElementIds().Concat(args.GetDeletedElementIds()))
                    unexpectedChanges.Add(id.Value);
            }
            void Dialog(object? sender, DialogBoxShowingEventArgs args)
            {
                if (args is not TaskDialogShowingEventArgs task) return;
                string message = task.Message ?? "";
                string normalized = new string(message.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
                if (normalized.Contains("tryingtocheckoutalargenumberofelements"))
                    log.Add("WORKSHARING: " + Clean(message[..Math.Min(message.Length, 500)])
                        + "; valgt=" + args.OverrideResult((int)TaskDialogResult.CommandLink1));
                else if (normalized.Contains("checkout") && normalized.Contains("workset"))
                    log.Add("Uavklart checkout-dialog, ikke overstyrt: " + Clean(message[..Math.Min(message.Length, 500)]));
            }
            using var group = new TransactionGroup(doc, "Rominfo på K5B kabelbrofittings");
            if (group.Start() != TransactionStatus.Started)
                throw new InvalidOperationException("Transaksjonsgruppen kunne ikke startes.");
            ui.DialogBoxShowing += Dialog;
            ui.Application.DocumentChanged += Changed;
            try
            {
                foreach (Plan[] batch in writes.Chunk(100))
                {
                    token.ThrowIfCancellationRequested();
                    using var transaction = new Transaction(doc, "Fyll manglende rominfo på kabelbrofittings");
                    if (transaction.Start() != TransactionStatus.Started)
                        throw new InvalidOperationException("Transaksjonen kunne ikke startes.");
                    transaction.SetFailureHandlingOptions(transaction.GetFailureHandlingOptions()
                        .SetForcedModalHandling(true).SetClearAfterRollback(true)
                        .SetFailuresPreprocessor(new RejectFailures(log)));
                    foreach (Plan plan in batch)
                    {
                        token.ThrowIfCancellationRequested();
                        Plan fresh = CreatePlan(doc, plan.Target);
                        if (fresh.Status != "ELIGIBLE" || fresh.AfterName != plan.AfterName
                            || fresh.AfterNumber != plan.AfterNumber || fresh.BeforeName != plan.BeforeName
                            || fresh.BeforeNumber != plan.BeforeNumber || fresh.SourceId != plan.SourceId
                            || fresh.BeforeRoomId != plan.BeforeRoomId || fresh.AfterRoomId != plan.AfterRoomId)
                            throw new InvalidOperationException("Skrivegrunnlag endret: " + plan.Target.Id.Value);
                        if (plan.BeforeRoomId != plan.AfterRoomId
                            && !Parameter(plan.Target, "dRofus_RoomID")!.Set(plan.AfterRoomId))
                            throw new InvalidOperationException("RoomID kunne ikke skrives: " + plan.Target.Id.Value);
                        if (plan.BeforeName != plan.AfterName && !Parameter(plan.Target, "Romnavn")!.Set(plan.AfterName))
                            throw new InvalidOperationException("Romnavn kunne ikke skrives: " + plan.Target.Id.Value);
                        if (plan.BeforeNumber != plan.AfterNumber
                            && !Parameter(plan.Target, "dRofus_RoomfunctionNo")!.Set(plan.AfterNumber))
                            throw new InvalidOperationException("Romnummer kunne ikke skrives: " + plan.Target.Id.Value);
                    }
                    TransactionStatus status = transaction.Commit();
                    log.Add("Batch: " + batch.Length + "; status=" + status);
                    if (status != TransactionStatus.Committed)
                        throw new InvalidOperationException("Batch-commit ikke bekreftet.");
                    foreach (Plan plan in batch) Verify(plan, true);
                }
                token.ThrowIfCancellationRequested();
                foreach (Plan plan in plans) Verify(plan, true);
                foreach (Element element in observed)
                    if (before[element.Id.Value] != Fingerprint(element, allowed.GetValueOrDefault(element.Id.Value)))
                        throw new InvalidOperationException("Utilsiktet parameter-/geometri-/connectorendring: " + element.Id.Value);
                if (!elementIds.SetEquals(new FilteredElementCollector(doc).WherePasses(new LogicalOrFilter(
                    new ElementIsElementTypeFilter(), new ElementIsElementTypeFilter(true)))
                    .ToElementIds().Select(id => id.Value)))
                    throw new InvalidOperationException("Elementutvalget er endret.");
                if (unexpectedChanges.Count > 0)
                    throw new InvalidOperationException("Følgeendringer utenfor mål/kilder: " + string.Join(",", unexpectedChanges.OrderBy(id => id)));
                TransactionStatus statusGroup = group.Assimilate();
                log.Add("Transaksjonsgruppe=" + statusGroup);
                if (statusGroup != TransactionStatus.Committed)
                    throw new InvalidOperationException("Gruppe-commit ikke bekreftet.");
                committed = true;
            }
            catch (Exception exception)
            {
                log.Add("FEIL: " + exception.Message);
                if (group.GetStatus() == TransactionStatus.Started)
                {
                    TransactionStatus status = group.RollBack();
                    log.Add("Rollback=" + status);
                    if (status != TransactionStatus.RolledBack)
                    {
                        File.WriteAllLines(stem + ".txt", log, new UTF8Encoding(false));
                        throw new InvalidOperationException("Rollback ikke bekreftet.", exception);
                    }
                }
                File.WriteAllLines(stem + ".txt", log, new UTF8Encoding(false));
                throw;
            }
            finally
            {
                ui.DialogBoxShowing -= Dialog;
                ui.Application.DocumentChanged -= Changed;
            }
        }
        try
        {
            foreach (Plan plan in plans.Concat(excluded)) Verify(plan, apply);
            List<string[]> rows = Rows(true);
            List<string[]> exceptionRows = Rows(true, excluded);
            ExcelListReportWriter.Write(stem + ".xlsx", new[]
            {
                new ExcelListReportWriter.WorksheetData("Kabelbrofittings", headers, rows),
                new ExcelListReportWriter.WorksheetData("Unntak", headers, exceptionRows)
            });
            File.WriteAllLines(stem + ".tsv", new[] { string.Join("\t", headers) }
                .Concat(rows.Concat(exceptionRows).Select(row => string.Join("\t", row.Select(Clean)))), new UTF8Encoding(false));
            log.Add("Bend/kryss/T-stykker=" + plans.Count + "; " + (apply ? "rettet=" : "planlagt=") + writes.Length
                + "; allerede komplett=" + plans.Count(plan => plan.Status == "COMPLETE")
                + "; blokkert=" + plans.Count(plan => plan.Status == "BLOCKED"));
            log.Add("Excel=" + stem + ".xlsx");
            File.WriteAllLines(stem + ".txt", log, new UTF8Encoding(false));
        }
        catch (Exception exception) when (committed)
        {
            return "Rule12Runner FEIL ETTER COMMIT: Modellendring er committed og IKKE tilbakeført. Feil etter commit: "
                + exception.Message + Environment.NewLine + string.Join(Environment.NewLine, log)
                + Environment.NewLine + "Skriveplan=" + stem + " skriveplan.tsv";
        }
        return "Rule12Runner: " + string.Join(Environment.NewLine, log);
    }

    private static Plan CreatePlan(Document doc, Element fitting)
    {
        string name = Text(fitting, "Romnavn"), number = Text(fitting, "dRofus_RoomfunctionNo");
        Element[] sources = Sources(fitting);
        int beforeRoomId = RoomId(fitting);
        bool isTee = GetPartType(fitting) is "Tee" or "ChannelCableTrayTee" or "LadderCableTrayTee";
        Plan Block(string reason) => new(fitting, sources, name, number, name, number, "BLOCKED", reason, null,
            beforeRoomId, beforeRoomId);
        if (!Missing(name) && !Missing(number) && (!isTee || beforeRoomId > 0))
            return new(fitting, sources, name, number, name, number, "COMPLETE", "Eksisterende rominfo bevares.", null,
                beforeRoomId, beforeRoomId);
        Parameter? nameParameter = Parameter(fitting, "Romnavn"), numberParameter = Parameter(fitting, "dRofus_RoomfunctionNo");
        if (nameParameter?.StorageType != StorageType.String || numberParameter?.StorageType != StorageType.String)
            return Block("Romtekstparameter mangler, er duplisert eller har feil datatype.");
        Parameter? room = Parameter(fitting, "dRofus_RoomID");
        if (room?.StorageType != StorageType.Integer)
            return Block("RoomID-parameter mangler, er duplisert eller er ikke Integer.");
        int targetRoomId = beforeRoomId;
        if (targetRoomId < 0 && isTee)
            return Block("Negativ eksisterende RoomID krever avklaring; bare 0 regnes som manglende Tee-RoomID.");
        if (targetRoomId <= 0)
        {
            if (!isTee) return Block("Mangler positiv fitting-RoomID; bare T-stykker får manglende RoomID her.");
            int[] positiveIds = sources.Select(RoomId).Where(value => value > 0).Distinct().ToArray();
            if (positiveIds.Length != 1)
                return Block("T-stykket mangler RoomID; krever nøyaktig én positiv RoomID blant tilkoblede rettstrekk.");
            targetRoomId = positiveIds[0];
        }
        Element[] matching = sources.Where(source => Parameter(source, "dRofus_RoomID") is
            { StorageType: StorageType.Integer } sourceRoom && sourceRoom.AsInteger() == targetRoomId).ToArray();
        string[] names = matching.Select(source => Text(source, "Romnavn")).Where(value => !Missing(value)).Distinct().ToArray();
        string[] numbers = matching.Select(source => Text(source, "dRofus_RoomfunctionNo")).Where(value => !Missing(value)).Distinct().ToArray();
        if (names.Length > 1 || numbers.Length > 1)
            return Block("Tilkoblede kilder med samme RoomID har motstridende romtekst.");
        Element? source = matching.FirstOrDefault(element => !Missing(Text(element, "Romnavn"))
            && !Missing(Text(element, "dRofus_RoomfunctionNo")));
        if (source is null)
            return Block("Ingen direkte tilkoblet rett K5B-kabelbro med samme RoomID og komplett rominformasjon.");
        string sourceName = Text(source, "Romnavn"), sourceNumber = Text(source, "dRofus_RoomfunctionNo");
        if ((!Missing(name) && name != sourceName) || (!Missing(number) && number != sourceNumber))
            return Block("Eksisterende romtekst samsvarer ikke med kilde; verdiene bevares.");
        if ((Missing(name) && nameParameter.IsReadOnly) || (Missing(number) && numberParameter.IsReadOnly)
            || (beforeRoomId != targetRoomId && room.IsReadOnly))
            return Block("Manglende rominformasjon er skrivebeskyttet.");
        if (doc.IsWorkshared && WorksharingUtils.GetCheckoutStatus(doc, fitting.Id) == CheckoutStatus.OwnedByOtherUser)
            return Block("Fittinget eies av en annen bruker.");
        return new(fitting, sources, name, number, Missing(name) ? sourceName : name,
            Missing(number) ? sourceNumber : number, "ELIGIBLE",
            "Entydig komplett romkilde; bare manglende felt fylles. Positiv RoomID og occurrence-ID bevares.",
            source.Id.Value, beforeRoomId, targetRoomId);
    }

    private static void Verify(Plan plan, bool apply)
    {
        bool written = apply && plan.Status == "ELIGIBLE";
        if (Text(plan.Target, "Romnavn") != (written ? plan.AfterName : plan.BeforeName)
            || Text(plan.Target, "dRofus_RoomfunctionNo") != (written ? plan.AfterNumber : plan.BeforeNumber)
            || RoomId(plan.Target) != (written ? plan.AfterRoomId : plan.BeforeRoomId))
            throw new InvalidOperationException("Etterkontroll av rominfo feilet: " + plan.Target.Id.Value);
    }

    private static Element[] Sources(Element fitting)
    {
        var sources = new Dictionary<long, Element>();
        if (fitting is not FamilyInstance instance || instance.MEPModel?.ConnectorManager is null)
            return Array.Empty<Element>();
        foreach (Connector connector in instance.MEPModel.ConnectorManager.Connectors)
        {
            if (connector.ConnectorType != ConnectorType.End) continue;
            foreach (Connector connected in connector.AllRefs)
            {
                Element owner = connected.Owner;
                if (owner.Id.Value == fitting.Id.Value || connected.ConnectorType != ConnectorType.End
                    || !connector.IsConnectedTo(connected) || !connected.IsConnectedTo(connector)
                    || owner.Category?.Id.Value != (long)BuiltInCategory.OST_CableTray
                    || IsBusbar(owner)
                    || Text(owner, "FOB_Entreprise") != "K5B"
                    || owner.Location is not LocationCurve curve || curve.Curve is not Line) continue;
                sources[owner.Id.Value] = owner;
            }
        }
        return sources.Values.OrderBy(source => source.Id.Value).ToArray();
    }

    private static bool IsBusbar(Element element)
    {
        ElementType? type = element.Document.GetElement(element.GetTypeId()) as ElementType;
        return (type?.FamilyName ?? "").IndexOf("Strømskinne", StringComparison.OrdinalIgnoreCase) >= 0
            || (type?.Name ?? "").IndexOf("Strømskinne", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool IsBendOrCross(Element fitting)
        => Parts.Contains(GetPartType(fitting));

    private static string GetPartType(Element fitting)
    {
        Parameter? parameter = (fitting as FamilyInstance)?.Symbol.Family
            .get_Parameter(BuiltInParameter.FAMILY_CONTENT_PART_TYPE);
        return parameter?.StorageType == StorageType.Integer
            ? Enum.GetName(typeof(PartType), parameter.AsInteger()) ?? "" : "";
    }

    private static int RoomId(Element element)
        => Parameter(element, "dRofus_RoomID") is { StorageType: StorageType.Integer } parameter ? parameter.AsInteger() : 0;

    private static Parameter? Parameter(Element element, string name)
    {
        var parameters = element.GetParameters(name);
        return parameters.Count == 1 ? parameters[0] : null;
    }

    private static string Text(Element element, string name)
    {
        Parameter? parameter = Parameter(element, name);
        return parameter?.StorageType switch
        {
            StorageType.String => parameter.AsString() ?? "",
            StorageType.Integer => parameter.AsInteger().ToString(CultureInfo.InvariantCulture),
            _ => ""
        };
    }

    private static bool Missing(string value) => string.IsNullOrWhiteSpace(value) || value.Trim() == "--";
    private static string Clean(string value) => value.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
    private static string Point(XYZ point) => string.Join(",", new[] { point.X, point.Y, point.Z }
        .Select(value => value.ToString("R", CultureInfo.InvariantCulture)));

    private static string Fingerprint(Element element, HashSet<string>? allowed)
    {
        var values = new List<string> { element.UniqueId, element.GetTypeId().Value.ToString() };
        foreach (Parameter parameter in element.Parameters.Cast<Parameter>().OrderBy(parameter => parameter.Id.Value))
        {
            if (parameter.Id.Value == (long)BuiltInParameter.EDITED_BY) continue;
            if (allowed?.Contains(parameter.Definition.Name) == true) continue;
            string value = parameter.StorageType switch
            {
                StorageType.String => parameter.AsString() ?? "",
                StorageType.Integer => parameter.AsInteger().ToString(CultureInfo.InvariantCulture),
                StorageType.Double => parameter.AsDouble().ToString("R", CultureInfo.InvariantCulture),
                StorageType.ElementId => parameter.AsElementId().Value.ToString(CultureInfo.InvariantCulture),
                _ => ""
            };
            values.Add(parameter.Id.Value + "=" + value);
        }
        if (element.Location is LocationPoint location) values.Add(Point(location.Point) + ";" + location.Rotation.ToString("R", CultureInfo.InvariantCulture));
        if (element.Location is LocationCurve curve) values.Add(string.Join(";", curve.Curve.Tessellate().Select(Point)));
        ConnectorManager? manager = (element as FamilyInstance)?.MEPModel?.ConnectorManager
            ?? (element as Autodesk.Revit.DB.Electrical.CableTray)?.ConnectorManager;
        if (manager is not null)
            foreach (Connector connector in manager.Connectors.Cast<Connector>().OrderBy(connector => connector.Id))
            {
                if (connector.ConnectorType != ConnectorType.End) continue;
                values.Add(connector.Id + ":" + Point(connector.Origin) + ":" + Point(connector.CoordinateSystem.BasisZ)
                    + ":" + string.Join(",", connector.AllRefs.Cast<Connector>()
                        .Where(other => other.Owner.Id.Value != element.Id.Value && connector.IsConnectedTo(other)
                            && other.IsConnectedTo(connector))
                        .Select(other => other.Owner.Id.Value + "/" + other.Id).OrderBy(value => value)));
            }
        return string.Join("\n", values.Select(value => value.Length + ":" + value));
    }

    private sealed class RejectFailures(List<string> log) : IFailuresPreprocessor
    {
        public FailureProcessingResult PreprocessFailures(FailuresAccessor accessor)
        {
            var messages = accessor.GetFailureMessages();
            foreach (var message in messages) log.Add("REVIT: " + message.GetSeverity() + ": " + message.GetDescriptionText());
            return messages.Count == 0 ? FailureProcessingResult.Continue : FailureProcessingResult.ProceedWithRollBack;
        }
    }
}