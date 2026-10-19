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

public sealed class DrofusRouteFieldFill
{
    private const string Model = "Autodesk Docs://Fornebubanen/U_F_BAS_FBU_RIE_XXX.rvt";
    private const string Root = @"D:\Revit\Python\Revit_BIM_Agent\logs\reports";
    private const string Prefix = "BAS dRofus strekk utfylling 20261016_1133";
    private static readonly string[] Fields = { "FOB_Merkestreng", "FOB_Sekvensnummer", "FOB_Omraade",
        "drofus_occurrence_id", "dRofus_RoomID", "dRofus_RoomfunctionNo", "Romnavn", "FOB_Leveransepakke" };

    public string Run(UIApplication ui, Document? doc, CancellationToken token, bool apply)
    {
        if (doc is null || doc.PathName != Model || ui.ActiveUIDocument?.Document.PathName != Model)
            throw new InvalidOperationException("Feil aktiv BAS-modell.");
        token.ThrowIfCancellationRequested();
        View view = doc.GetElement("5e8f81e4-fcff-4378-8a08-70d893e9ab6c-0110f8e2") as View
            ?? throw new InvalidOperationException("Mangler dRofus-view.");
        var visible = new FilteredElementCollector(doc, view.Id).OfCategory(BuiltInCategory.OST_CableTray)
            .WhereElementIsNotElementType().ToElementIds().Select(id => id.Value).ToHashSet();
        Element[] all = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_CableTray).WhereElementIsNotElementType()
            .Where(element => Value(element, "FOB_Entreprise") == "K5B"
                && doc.GetElement(element.GetTypeId()).Name.IndexOf("Strømskinne", StringComparison.OrdinalIgnoreCase) < 0).ToArray();
        if (all.Length != 2071) throw new InvalidOperationException("K5B-utvalg endret.");
        var elements = all.ToDictionary(element => element.UniqueId, StringComparer.Ordinal);
        var owners = all.Where(element => visible.Contains(element.Id.Value)).ToArray();
        if (owners.Length != 1134 || owners.Any(owner => int.Parse(Value(owner, "drofus_occurrence_id")) <= 0)
            || owners.GroupBy(owner => Value(owner, "drofus_occurrence_id")).Any(group => group.Count() > 1)
            || owners.Where(owner => !string.IsNullOrWhiteSpace(Value(owner, Fields[0])))
                .GroupBy(owner => Value(owner, Fields[0]), StringComparer.Ordinal).Any(group => group.Count() > 1))
            throw new InvalidOperationException("Representantunikhet avviker.");
        var plan = File.ReadAllLines(Path.Combine(Root, "BAS dRofus strekk utfyllingsplan 20261016_1133.tsv"), Encoding.UTF8)
            .Skip(1).Select(line => line.Split('\t')).ToArray();
        if (plan.Length != 2693 || plan.Any(row => row.Length != 12) || plan.Select(row => row[1]).Distinct().Count() != 935
            || plan.Select(row => row[1] + "|" + row[4]).Distinct().Count() != plan.Length)
            throw new InvalidOperationException("Godkjent plan avviker.");
        var expected = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string[] row in plan)
        {
            token.ThrowIfCancellationRequested();
            Element target = elements[row[1]], source = elements[row[3]];
            // Older plans label this value Rombetegnelse; the Revit target is always Romnavn.
            string field = string.Equals(row[4], "dRofus_Rombetegnelse", StringComparison.Ordinal)
                ? "Romnavn"
                : row[4];
            int index = Array.IndexOf(Fields, field);
            if (index < 0 || index == 3 || index == 7 || row[1] == row[3]
                || target.Id.Value.ToString() != row[0] || source.Id.Value.ToString() != row[2]
                || visible.Contains(target.Id.Value) || (index < 4 && !visible.Contains(source.Id.Value))
                || Value(target, "drofus_occurrence_id") != row[7] || row[7] != "0"
                || Value(source, "drofus_occurrence_id") != row[8] || int.Parse(row[8]) <= 0
                || Value(source, Fields[0]) != row[9] || Value(source, Fields[1]) != row[10]
                || Value(source, "dRofus_RoomID") != row[11] || Value(source, field) != row[6]
                || Value(target, field) != (apply ? row[5] : row[6]) || IsMissingRoomText(row[6])
                || row[6].StartsWith("Error:", StringComparison.Ordinal))
                throw new InvalidOperationException("Kilde/mål avviker: " + row[0] + " " + field);
            if (index < 3 && (string.IsNullOrWhiteSpace(row[9]) || string.IsNullOrWhiteSpace(row[10]) || row[10].StartsWith("Error:")))
                throw new InvalidOperationException("Ingen gyldig dRofus-merkekilde.");
            if (index >= 4 && (int.Parse(row[11]) <= 0
                || IsMissingRoomText(Value(source, "dRofus_RoomfunctionNo"))
                || IsMissingRoomText(Value(source, "Romnavn"))
                || !RoomIdMatchesOrMissing(Value(target, "dRofus_RoomID"), row[11])
                || !(IsMissingRoomText(row[5]) || (index == 4 && IsMissingRoomId(row[5])))))
                throw new InvalidOperationException("Romkilden er ugyldig eller eksisterende målverdi ville bli overskrevet.");
            if (apply && (Parameter(target, field).IsReadOnly
                || (doc.IsWorkshared && WorksharingUtils.GetCheckoutStatus(doc, target.Id) == CheckoutStatus.OwnedByOtherUser)))
                throw new InvalidOperationException("Skrivebeskyttet/eid mål: " + row[0]);
            expected.Add(row[1] + "|" + (index + 4), row[6]);
        }
        string stem = Path.Combine(Root, Prefix);
        var log = new List<string> { DateTimeOffset.Now.ToString("O"), Model,
            "Bare dokumenterte dRofus-kilder. Ingen nye merker, occurrence-kopiering, geometri, lagring eller synk." };
        string[][] before = apply ? Snapshot(all, visible) : Read(stem + " for.tsv");
        if (apply)
        {
            if (File.Exists(stem + " for.tsv")) throw new InvalidOperationException("Før-snapshot finnes; ikke kjør blind retry.");
            Write(stem + " for.tsv", before);
            void Dialog(object? sender, DialogBoxShowingEventArgs args)
            {
                if (args is not TaskDialogShowingEventArgs task) return;
                string message = task.Message ?? "";
                string normalized = new string(message.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
                if (normalized.Contains("tryingtocheckoutalargenumberofelements"))
                    log.Add("WORKSHARING: " + message.Substring(0, Math.Min(500, message.Length)).Replace('\n', ' ')
                        + "; godtatt=" + args.OverrideResult((int)TaskDialogResult.CommandLink1));
                else if (normalized.Contains("checkout") && normalized.Contains("workset"))
                    log.Add("Uavklart checkout-dialog, ikke overstyrt: " + message.Substring(0, Math.Min(500, message.Length)));
            }
            using var group = new TransactionGroup(doc, "dRofus-felter på dokumenterte K5B-strekk");
            if (group.Start() != TransactionStatus.Started) throw new InvalidOperationException("Gruppe kunne ikke startes.");
            ui.DialogBoxShowing += Dialog;
            try
            {
                using (var transaction = new Transaction(doc, "Utfyll merke, sekvens og manglende rominfo"))
                {
                    if (transaction.Start() != TransactionStatus.Started) throw new InvalidOperationException("Start feilet.");
                    transaction.SetFailureHandlingOptions(transaction.GetFailureHandlingOptions().SetForcedModalHandling(true)
                        .SetClearAfterRollback(true).SetFailuresPreprocessor(new Reject(log)));
                    foreach (string[] row in plan)
                    {
                        token.ThrowIfCancellationRequested();
                        Parameter parameter = Parameter(elements[row[1]], row[4]);
                        bool set = parameter.StorageType == StorageType.Integer
                            ? parameter.Set(int.Parse(row[6], CultureInfo.InvariantCulture)) : parameter.Set(row[6]);
                        if (!set) throw new InvalidOperationException("Set feilet: " + row[0]);
                    }
                    TransactionStatus status = transaction.Commit();
                    log.Add("Transaksjon=" + status);
                    if (status != TransactionStatus.Committed) throw new InvalidOperationException("Commit ikke bekreftet.");
                }
                token.ThrowIfCancellationRequested();
                Compare(before, Snapshot(all, visible), expected);
                TransactionStatus statusGroup = group.Assimilate();
                log.Add("Gruppe=" + statusGroup);
                if (statusGroup != TransactionStatus.Committed) throw new InvalidOperationException("Gruppecommit feilet.");
            }
            finally
            {
                ui.DialogBoxShowing -= Dialog;
                if (group.GetStatus() == TransactionStatus.Started)
                {
                    TransactionStatus rollback = group.RollBack();
                    log.Add("Rollback=" + rollback);
                    File.WriteAllLines(stem + " feil.txt", log, new UTF8Encoding(false));
                    if (rollback != TransactionStatus.RolledBack) throw new InvalidOperationException("Rollback feilet.");
                }
            }
        }
        string[][] after = Snapshot(all, visible);
        Compare(before, after, expected);
        string output = apply ? stem + " etter" : Path.Combine(Root, "BAS dRofus strekk etterkontroll " + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff"));
        Write(output + ".tsv", after);
        log.Add("VERIFISERT: 2693 felt på 935 segmenter. Alle 2071 sammenlignet; øvrige kontrollerte felt/type/pin/geometri/ID-er uendret.");
        File.WriteAllLines(output + ".txt", log, new UTF8Encoding(false));
        return string.Join(Environment.NewLine, log) + Environment.NewLine + output;
    }
    private static void Compare(string[][] before, string[][] after, Dictionary<string, string> expected)
    {
        var old = before.ToDictionary(row => row[1]);
        if (old.Count != 2071 || after.Length != old.Count) throw new InvalidOperationException("Utvalg endret.");
        foreach (string[] row in after)
            for (int i = 0; i < row.Length; i++)
                if (row[i] != (expected.TryGetValue(row[1] + "|" + i, out var value) ? value : old[row[1]][i]))
                    throw new InvalidOperationException("Uventet etterverdi " + row[0] + " kolonne " + i);
    }
    private static string[][] Snapshot(Element[] all, HashSet<long> visible) => all.OrderBy(element => element.Id.Value).Select(element =>
    {
        Curve curve = ((LocationCurve)element.Location).Curve;
        return new[] { element.Id.Value.ToString(), element.UniqueId, visible.Contains(element.Id.Value).ToString(),
            element.GetTypeId().Value.ToString() }.Concat(Fields.Select(field => Value(element, field))).Concat(new[] {
                element.Pinned.ToString(), Point(curve.GetEndPoint(0)), Point(curve.GetEndPoint(1)), F(curve.Length),
                F(element.get_Parameter(BuiltInParameter.RBS_CABLETRAY_WIDTH_PARAM).AsDouble()),
                F(element.get_Parameter(BuiltInParameter.RBS_CABLETRAY_HEIGHT_PARAM).AsDouble()),
                Value(element.Document.GetElement(element.GetTypeId()), "dRofus_ItemID") }).ToArray();
    }).ToArray();
    private static string F(double value) => value.ToString("R", CultureInfo.InvariantCulture);
    private static bool IsMissingRoomText(string value) => string.IsNullOrWhiteSpace(value) || value.Trim() == "--";
    private static bool IsMissingRoomId(string value) =>
        string.IsNullOrWhiteSpace(value) || value.Trim() is "--" or "0";
    private static bool RoomIdMatchesOrMissing(string targetRoomId, string sourceRoomId)
    {
        if (IsMissingRoomId(targetRoomId)) return true;
        return int.TryParse(targetRoomId, NumberStyles.Integer, CultureInfo.InvariantCulture, out int targetId)
            && int.TryParse(sourceRoomId, NumberStyles.Integer, CultureInfo.InvariantCulture, out int sourceId)
            && targetId > 0 && targetId == sourceId;
    }
    private static string Point(XYZ point) => string.Join(",", new[] { point.X, point.Y, point.Z }.Select(F));
    private static Parameter Parameter(Element element, string name)
    {
        var parameters = element.GetParameters(name);
        if (parameters.Count != 1) throw new InvalidOperationException("Ugyldig parameter " + name);
        if (parameters[0].StorageType != StorageType.String && parameters[0].StorageType != StorageType.Integer)
            throw new InvalidOperationException("Uventet lagringstype " + name);
        return parameters[0];
    }
    private static string Value(Element element, string name)
    {
        Parameter parameter = Parameter(element, name);
        return parameter.StorageType == StorageType.String ? parameter.AsString() ?? "" : parameter.AsInteger().ToString(CultureInfo.InvariantCulture);
    }
    private static string[][] Read(string path) => File.ReadAllLines(path, Encoding.UTF8).Skip(1).Select(line => line.Split('\t')).ToArray();
    private static void Write(string path, string[][] rows)
    {
        if (rows.Any(row => row.Length != 19 || row.Any(value => value.IndexOfAny(new[] { '\t', '\r', '\n' }) >= 0)))
            throw new InvalidOperationException("Ugyldig TSV.");
        File.WriteAllLines(path, new[] { string.Join("\t", new[] { "ElementId", "UniqueId", "Visible", "TypeId" }.Concat(Fields)
            .Concat(new[] { "Pinned", "Start", "End", "Length", "Width", "Height", "ItemId" })) }
            .Concat(rows.Select(row => string.Join("\t", row))), new UTF8Encoding(false));
    }
    private sealed class Reject : IFailuresPreprocessor
    {
        private readonly List<string> log;
        internal Reject(List<string> log) { this.log = log; }
        public FailureProcessingResult PreprocessFailures(FailuresAccessor accessor)
        {
            var messages = accessor.GetFailureMessages();
            foreach (var message in messages) log.Add("REVIT: " + message.GetSeverity() + ": " + message.GetDescriptionText());
            return messages.Count == 0 ? FailureProcessingResult.Continue : FailureProcessingResult.ProceedWithRollBack;
        }
    }
}
