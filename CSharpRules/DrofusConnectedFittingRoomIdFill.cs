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

public sealed class DrofusConnectedFittingRoomIdFill
{
    private const string Model = "Autodesk Docs://Fornebubanen/U_F_BAS_FBU_RIE_XXX.rvt";
    private const string ReportRoot = @"D:\Revit\Python\Revit_BIM_Agent\logs\reports";
    private sealed record Source(Element Element, int RoomId);
    private sealed record Candidate(Element Fitting, string Family, string Type, string PartType,
        int PreviousRoomId, int? TargetRoomId, List<Source> Sources, string Status, string Reason);

    public string Run(UIApplication ui, Document? doc, CancellationToken token, bool apply)
    {
        if (doc is null || doc.PathName != Model || ui.ActiveUIDocument?.Document.PathName != Model)
            throw new InvalidOperationException("Feil aktiv BAS-modell.");
        token.ThrowIfCancellationRequested();

        var fittings = new FilteredElementCollector(doc)
            .OfCategory(BuiltInCategory.OST_CableTrayFitting)
            .WhereElementIsNotElementType()
            .Where(element => HasEnterprise(element, "K5B"))
            .ToArray();
        var candidates = new List<Candidate>();
        foreach (Element fitting in fittings)
        {
            token.ThrowIfCancellationRequested();
            Element? type = doc.GetElement(fitting.GetTypeId());
            string family = (fitting as FamilyInstance)?.Symbol?.Family?.Name ?? "";
            string typeName = type?.Name ?? "";
            string partType = GetPartType(fitting, type);
            if (!IsBendOrCross(partType)) continue;

            Parameter roomIdParameter = RequireRoomIdParameter(fitting);
            int previousRoomId = roomIdParameter.AsInteger();
            List<Source> sources = GetConnectedStraightSources(fitting);
            if (sources.Count == 0)
            {
                candidates.Add(new Candidate(fitting, family, typeName, partType, previousRoomId, null, sources,
                    "BLOCKED", "Ingen direkte tilkoblet rett K5B-kabelbro med linjegeometri."));
                continue;
            }

            int[] sourceRoomIds = sources.Select(source => source.RoomId).Where(roomId => roomId > 0).Distinct().ToArray();
            if (sourceRoomIds.Length > 1)
            {
                candidates.Add(new Candidate(fitting, family, typeName, partType, previousRoomId, null, sources,
                    previousRoomId > 0 ? "PRESERVED_CONFLICT" : "BLOCKED",
                    "Tilkoblede rettstrekk har ulike positive RoomID-er; "
                    + (previousRoomId > 0 ? "eksisterende fitting-RoomID bevares etter avtalt konfliktregel."
                        : "mangler gyldig eksisterende fitting-RoomID.")));
                continue;
            }

            if (sourceRoomIds.Length == 0)
            {
                candidates.Add(new Candidate(fitting, family, typeName, partType, previousRoomId, null, sources,
                    "BLOCKED", "Ingen tilkoblet rettstrekk har positiv RoomID."));
                continue;
            }

            int targetRoomId = sourceRoomIds[0];
            if (sources.Any(source => source.RoomId > 0 && source.RoomId != targetRoomId))
                throw new InvalidOperationException("Kildeverdiene endret seg under planlegging for " + fitting.Id.Value);
            if (doc.IsWorkshared && WorksharingUtils.GetCheckoutStatus(doc, fitting.Id) == CheckoutStatus.OwnedByOtherUser)
            {
                candidates.Add(new Candidate(fitting, family, typeName, partType, previousRoomId, targetRoomId, sources,
                    "BLOCKED", "Fittinget eies av en annen bruker."));
                continue;
            }
            if (roomIdParameter.IsReadOnly && previousRoomId != targetRoomId)
            {
                candidates.Add(new Candidate(fitting, family, typeName, partType, previousRoomId, targetRoomId, sources,
                    "BLOCKED", "dRofus_RoomID på fittinget er skrivebeskyttet."));
                continue;
            }

            string status = previousRoomId == targetRoomId ? "ALREADY_CORRECT" : "ELIGIBLE";
            candidates.Add(new Candidate(fitting, family, typeName, partType, previousRoomId, targetRoomId, sources,
                status, sources.Any(source => source.RoomId == 0)
                    ? "Entydig positiv kilde-ID; øvrige tilkoblede kildeverdier er tomme/0."
                    : "Entydig positiv RoomID fra direkte tilkoblet rettstrekk."));
        }

        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
        string reportPath = Path.Combine(ReportRoot, "BAS bend kryss RoomID " + timestamp + ".tsv");
        if (File.Exists(reportPath)) throw new InvalidOperationException("Rapport finnes allerede: " + reportPath);
        var log = new List<string>
        {
            DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture),
            "Modell=" + Model,
            "Omfang=K5B OST_CableTrayFitting; bare bend/kryss; kilde=direkte tilkoblet rett OST_CableTray i K5B.",
            "Skriver bare dRofus_RoomID. Romnummer, Romnavn, occurrence-ID, geometri, lagring og synk endres ikke.",
            "Modus=" + (apply ? "utføring" : "forhåndsvisning"),
            "Bend/kryss=" + candidates.Count.ToString(CultureInfo.InvariantCulture)
                + "; planlagt=" + candidates.Count(candidate => candidate.Status == "ELIGIBLE").ToString(CultureInfo.InvariantCulture)
                + "; allerede riktig=" + candidates.Count(candidate => candidate.Status == "ALREADY_CORRECT").ToString(CultureInfo.InvariantCulture)
                + "; blokkerte=" + candidates.Count(candidate => candidate.Status == "BLOCKED").ToString(CultureInfo.InvariantCulture)
                + "; motstridende kilder, eksisterende ID bevart="
                + candidates.Count(candidate => candidate.Status == "PRESERVED_CONFLICT").ToString(CultureInfo.InvariantCulture)
        };

        if (apply && candidates.Any(candidate => candidate.Status == "ELIGIBLE"))
        {
            void Dialog(object? sender, DialogBoxShowingEventArgs args)
            {
                if (args is not TaskDialogShowingEventArgs task) return;
                string message = task.Message ?? "";
                string normalized = new string(message.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
                if (normalized.Contains("tryingtocheckoutalargenumberofelements"))
                    log.Add("WORKSHARING: " + message[..Math.Min(500, message.Length)].Replace('\n', ' ')
                        + "; valgt=" + args.OverrideResult((int)TaskDialogResult.CommandLink1));
                else if (normalized.Contains("checkout") && normalized.Contains("workset"))
                    log.Add("Uavklart checkout-dialog, ikke overstyrt: " + message[..Math.Min(500, message.Length)]);
            }

            using var group = new TransactionGroup(doc, "RoomID på K5B kabelbrobend og kryss");
            if (group.Start() != TransactionStatus.Started)
                throw new InvalidOperationException("Transaksjonsgruppe kunne ikke startes.");
            ui.DialogBoxShowing += Dialog;
            try
            {
                Candidate[] writes = candidates.Where(candidate => candidate.Status == "ELIGIBLE").ToArray();
                const int batchSize = 100;
                for (int start = 0; start < writes.Length; start += batchSize)
                {
                    token.ThrowIfCancellationRequested();
                    Candidate[] batch = writes.Skip(start).Take(batchSize).ToArray();
                    using var transaction = new Transaction(doc, "Fyll RoomID på kabelbrobend/kryss "
                        + (start + 1).ToString(CultureInfo.InvariantCulture) + "-"
                        + (start + batch.Length).ToString(CultureInfo.InvariantCulture));
                    if (transaction.Start() != TransactionStatus.Started)
                        throw new InvalidOperationException("Transaksjon kunne ikke startes.");
                    transaction.SetFailureHandlingOptions(transaction.GetFailureHandlingOptions()
                        .SetForcedModalHandling(true).SetClearAfterRollback(true).SetFailuresPreprocessor(new Reject(log)));
                    foreach (Candidate candidate in batch)
                    {
                        token.ThrowIfCancellationRequested();
                        Parameter parameter = RequireRoomIdParameter(candidate.Fitting);
                        if (parameter.AsInteger() != candidate.PreviousRoomId)
                            throw new InvalidOperationException("Fittingets RoomID endret siden planlegging: " + candidate.Fitting.Id.Value);
                        foreach (Source source in candidate.Sources)
                        {
                            if (RequireRoomIdParameter(source.Element).AsInteger() != source.RoomId)
                                throw new InvalidOperationException("Kildens RoomID endret siden planlegging: " + source.Element.Id.Value);
                        }
                        if (!parameter.Set(candidate.TargetRoomId!.Value))
                            throw new InvalidOperationException("Parameter.Set feilet på " + candidate.Fitting.Id.Value);
                    }
                    TransactionStatus batchStatus = transaction.Commit();
                    log.Add("Batch " + (start / batchSize + 1).ToString(CultureInfo.InvariantCulture)
                        + ": mål=" + batch.Length.ToString(CultureInfo.InvariantCulture) + "; status=" + batchStatus);
                    if (batchStatus != TransactionStatus.Committed)
                        throw new InvalidOperationException("Batch-commit ikke bekreftet.");
                    foreach (Candidate candidate in batch)
                        if (RequireRoomIdParameter(candidate.Fitting).AsInteger() != candidate.TargetRoomId)
                            throw new InvalidOperationException("Batch-etterkontroll feilet på " + candidate.Fitting.Id.Value);
                }
                TransactionStatus groupStatus = group.Assimilate();
                log.Add("Gruppe=" + groupStatus);
                if (groupStatus != TransactionStatus.Committed)
                    throw new InvalidOperationException("Transaksjonsgruppecommit ikke bekreftet.");
            }
            catch
            {
                if (group.GetStatus() == TransactionStatus.Started)
                {
                    TransactionStatus rollback = group.RollBack();
                    log.Add("Rollback=" + rollback);
                    if (rollback != TransactionStatus.RolledBack)
                        throw new InvalidOperationException("Rollback av transaksjonsgruppe ikke bekreftet.");
                }
                throw;
            }
            finally
            {
                ui.DialogBoxShowing -= Dialog;
            }
        }

        List<string> reportRows = new()
        {
            "Status\tElementId\tUniqueId\tLeveransepakke\tFamilie\tType\tPartType\tRoomIDFør\tRoomIDEtter\tKildestrekkElementId\tKildeRoomID-er\tÅrsak"
        };
        foreach (Candidate candidate in candidates)
        {
            int currentRoomId = RequireRoomIdParameter(candidate.Fitting).AsInteger();
            string status = apply && candidate.Status == "ELIGIBLE" ? "VERIFIED" : candidate.Status;
            if (apply && candidate.Status == "ELIGIBLE" && currentRoomId != candidate.TargetRoomId)
                throw new InvalidOperationException("Etterkontroll feilet på " + candidate.Fitting.Id.Value);
            if (apply && candidate.Status != "ELIGIBLE" && currentRoomId != candidate.PreviousRoomId)
                throw new InvalidOperationException("Uplanlagt endring på fitting " + candidate.Fitting.Id.Value);
            reportRows.Add(string.Join("\t", status, candidate.Fitting.Id.Value, candidate.Fitting.UniqueId,
                Clean(GetText(candidate.Fitting, "FOB_Leveransepakke")), Clean(candidate.Family), Clean(candidate.Type),
                candidate.PartType, candidate.PreviousRoomId.ToString(CultureInfo.InvariantCulture),
                currentRoomId.ToString(CultureInfo.InvariantCulture),
                string.Join(",", candidate.Sources.Select(source => source.Element.Id.Value).Distinct().OrderBy(id => id)),
                string.Join(",", candidate.Sources.Select(source => source.RoomId.ToString(CultureInfo.InvariantCulture)).Distinct().OrderBy(id => id)),
                Clean(candidate.Reason)));
        }
        File.WriteAllLines(reportPath, reportRows, new UTF8Encoding(false));
        log.Add("Rapport=" + reportPath);
        File.WriteAllLines(Path.ChangeExtension(reportPath, ".txt"), log, new UTF8Encoding(false));
        return string.Join(Environment.NewLine, log) + Environment.NewLine + "Elementrapporter=" + reportPath;
    }

    private static List<Source> GetConnectedStraightSources(Element fitting)
    {
        if (fitting is not FamilyInstance familyInstance || familyInstance.MEPModel?.ConnectorManager is null)
            throw new InvalidOperationException("Fitting mangler MEP ConnectorManager: " + fitting.Id.Value);
        var sources = new Dictionary<long, Source>();
        foreach (Connector connector in familyInstance.MEPModel.ConnectorManager.Connectors)
        {
            if (connector.ConnectorType != ConnectorType.End) continue;
            foreach (Connector connected in connector.AllRefs)
            {
                Element owner = connected.Owner;
                if (owner.Id.Value == fitting.Id.Value || connected.ConnectorType != ConnectorType.End
                    || !connector.IsConnectedTo(connected) || !connected.IsConnectedTo(connector)
                    || owner.Category?.Id.Value != (long)BuiltInCategory.OST_CableTray
                    || !HasEnterprise(owner, "K5B")
                    || owner.Location is not LocationCurve locationCurve || locationCurve.Curve is not Line)
                    continue;
                Parameter roomId = RequireRoomIdParameter(owner);
                sources[owner.Id.Value] = new Source(owner, roomId.AsInteger());
            }
        }
        return sources.Values.OrderBy(source => source.Element.Id.Value).ToList();
    }

    private static bool IsBendOrCross(string partType) => partType is
        "Elbow" or "ChannelCableTrayElbow" or "ChannelCableTrayVerticalElbow"
        or "LadderCableTrayElbow" or "LadderCableTrayVerticalElbow"
        or "Cross" or "ChannelCableTrayCross" or "LadderCableTrayCross";

    private static string GetPartType(Element fitting, Element? type)
    {
        Parameter? parameter = (fitting as FamilyInstance)?.Symbol?.Family.get_Parameter(BuiltInParameter.FAMILY_CONTENT_PART_TYPE)
            ?? type?.get_Parameter(BuiltInParameter.FAMILY_CONTENT_PART_TYPE)
            ?? fitting.get_Parameter(BuiltInParameter.FAMILY_CONTENT_PART_TYPE);
        return parameter is { StorageType: StorageType.Integer, HasValue: true }
            ? Enum.GetName(typeof(PartType), parameter.AsInteger()) ?? ""
            : "";
    }

    private static Parameter RequireRoomIdParameter(Element element)
    {
        var parameters = element.GetParameters("dRofus_RoomID");
        if (parameters.Count != 1 || parameters[0].StorageType != StorageType.Integer)
            throw new InvalidOperationException("dRofus_RoomID mangler, er duplisert eller er ikke Integer på " + element.Id.Value);
        return parameters[0];
    }

    private static bool HasEnterprise(Element element, string enterprise)
    {
        var parameters = element.GetParameters("FOB_Entreprise");
        return parameters.Count == 1 && parameters[0].StorageType == StorageType.String
            && string.Equals(parameters[0].AsString(), enterprise, StringComparison.Ordinal);
    }

    private static string GetText(Element element, string name)
    {
        var parameters = element.GetParameters(name);
        if (parameters.Count != 1 || parameters[0].StorageType != StorageType.String)
            throw new InvalidOperationException("Ugyldig tekstparameter " + name + " på " + element.Id.Value);
        return parameters[0].AsString() ?? "";
    }

    private static string Clean(string value) => value.Replace("\t", " ").Replace("\r", " ").Replace("\n", " ");

    private sealed class Reject : IFailuresPreprocessor
    {
        private readonly List<string> log;
        internal Reject(List<string> log) => this.log = log;
        public FailureProcessingResult PreprocessFailures(FailuresAccessor accessor)
        {
            var messages = accessor.GetFailureMessages();
            foreach (var message in messages)
                log.Add("REVIT: " + message.GetSeverity() + ": " + message.GetDescriptionText());
            return messages.Count == 0 ? FailureProcessingResult.Continue : FailureProcessingResult.ProceedWithRollBack;
        }
    }
}

internal sealed class GeneratedAction
{
    public string Execute(UIApplication uiApplication, Document? activeDocument, CancellationToken cancellationToken) =>
        new DrofusConnectedFittingRoomIdFill().Run(uiApplication, activeDocument, cancellationToken, apply: false);
}
