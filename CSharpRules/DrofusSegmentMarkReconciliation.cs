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

public sealed class DrofusSegmentMarkReconciliation
{
    private const string Model = "Autodesk Docs://Fornebubanen/U_F_BAS_FBU_RIE_XXX.rvt";
    private const string Root = @"D:\Revit\Python\Revit_BIM_Agent\logs\reports";
    private static readonly string[] Fields = { "FOB_Merkestreng", "FOB_Sekvensnummer", "FOB_Omraade" };
    private static readonly long[] ApprovedIds = { 8118212, 8118270, 8261503, 8261669, 8261762, 8261897, 8261924,
        8261934, 8261942, 8261950, 8261958, 8261968, 8262252, 8262260, 8262268, 8262278, 8262286, 8262810,
        8262818, 8262834, 8263049, 8263149, 8263172, 8263212, 8263345, 8263353, 8264685, 8264693, 8264701,
        8264709, 8264717, 8264725, 8264735, 8265055, 10197063, 10217219, 10217348, 13010530, 13010643 };
    private static string Stem => Path.Combine(Root, "BAS dRofus segmentmerke 20261016_1035");

    public string Run(UIApplication ui, Document? doc, CancellationToken token, bool apply)
    {
        token.ThrowIfCancellationRequested();
        if (doc is null || doc.PathName != Model || ui.ActiveUIDocument?.Document.PathName != Model
            || doc.ActiveView.UniqueId != "5e8f81e4-fcff-4378-8a08-70d893e9ab6c-0110f8e2")
            throw new InvalidOperationException("STOPP: feil BAS-modell/view.");
        string[][] manifest = File.ReadAllLines(Path.Combine(Root, "BAS dRofus segmentmerke omfang 20261016_1035.tsv"),
            Encoding.UTF8).Skip(1).Select(line => line.Split('\t')).ToArray();
        if (manifest.Length != 39 || manifest.Any(row => row.Length != 12)
            || manifest.Select(row => row[1]).Distinct(StringComparer.Ordinal).Count() != 39
            || !manifest.Select(row => long.Parse(row[0], CultureInfo.InvariantCulture)).OrderBy(id => id)
                .SequenceEqual(ApprovedIds.OrderBy(id => id))
            || manifest.Any(row => row[2] != "8261587" || row[3] != "851e8c46-9070-439c-a667-6fbb876395f4-007e0fd3"
                || row[4] != "P14-460-KAF-001" || row[5] != "001" || row[6] != "P14"
                || row[7] != "P14-460-KAF-006" || row[8] != "006" || row[9] != "P14"
                || row[10] != "0" || row[11] != "808983" || row[1] == row[3]))
            throw new InvalidOperationException("Feil godkjent 39-segmentsomfang.");
        Element[] all = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_CableTray)
            .WhereElementIsNotElementType().Where(element => Text(element, "FOB_Entreprise") == "K5B").ToArray();
        var visible = new FilteredElementCollector(doc, doc.ActiveView.Id).OfCategory(BuiltInCategory.OST_CableTray)
            .WhereElementIsNotElementType().ToElementIds().Select(id => id.Value).ToHashSet();
        Element[] representatives = all.Where(element => visible.Contains(element.Id.Value)).ToArray();
        if (representatives.Length != 1134 || representatives.Any(element => Integer(element, "drofus_occurrence_id") <= 0)
            || representatives.GroupBy(element => Integer(element, "drofus_occurrence_id")).Any(group => group.Count() > 1)
            || representatives.Where(element => !string.IsNullOrWhiteSpace(Text(element, Fields[0])))
                .GroupBy(element => Text(element, Fields[0]), StringComparer.Ordinal).Any(group => group.Count() > 1))
            throw new InvalidOperationException("Representantantall/occurrence/merkeunikhet avviker.");
        var targets = manifest.ToDictionary(row => row[1], StringComparer.Ordinal);
        foreach (string[] row in manifest)
        {
            Element element = doc.GetElement(row[1]) ?? throw new InvalidOperationException("Mangler segment.");
            Element owner = doc.GetElement(row[3]) ?? throw new InvalidOperationException("Mangler representant.");
            if (!all.Any(candidate => candidate.Id == element.Id) || element.Id.Value.ToString() != row[0]
                || owner.Id.Value.ToString() != row[2] || visible.Contains(element.Id.Value) || !visible.Contains(owner.Id.Value)
                || Integer(element, "drofus_occurrence_id").ToString(CultureInfo.InvariantCulture) != row[10]
                || Integer(owner, "drofus_occurrence_id").ToString(CultureInfo.InvariantCulture) != row[11]
                || Text(owner, "FOB_Entreprise") != "K5B")
                throw new InvalidOperationException("Element/representant/occurrence endret: " + row[0]);
            foreach (Element candidate in new[] { element, owner })
                if (doc.GetElement(candidate.GetTypeId()).Name.IndexOf("Strømskinne", StringComparison.OrdinalIgnoreCase) >= 0)
                    throw new InvalidOperationException("Strømskinne utenfor omfang.");
            for (int index = 0; index < Fields.Length; index++)
            {
                if (Text(owner, Fields[index]) != row[index + 7]
                    || Text(element, Fields[index]) != row[index + (apply ? 4 : 7)])
                    throw new InvalidOperationException("Forventet feltverdi endret: " + row[0] + " " + Fields[index]);
                if (apply && Parameter(element, Fields[index], StorageType.String).IsReadOnly)
                    throw new InvalidOperationException("Skrivebeskyttet felt.");
            }
            if (apply && doc.IsWorkshared && WorksharingUtils.GetCheckoutStatus(doc, element.Id) == CheckoutStatus.OwnedByOtherUser)
                throw new InvalidOperationException("Segment eies av annen bruker: " + row[0]);
        }
        if (all.Any(element => Text(element, Fields[0]) == "P14-460-KAF-006"
            && element.Id.Value != 8261587 && !targets.ContainsKey(element.UniqueId)))
            throw new InvalidOperationException("Målmerke finnes utenfor dokumentert strekk.");
        string[][] before = apply ? Snapshot(all, visible) : File.ReadAllLines(Stem + " for.tsv", Encoding.UTF8)
            .Skip(1).Select(line => line.Split('\t')).ToArray();
        var log = new List<string> { DateTimeOffset.Now.ToString("O"), Model,
            "39 dokumenterte segmenter til P14-460-KAF-006. Bare merke/sekvens/område. Tre tomme representanter bevares.",
            "Ingen lagring, synk, referansegodkjenning eller kopiering av occurrence-ID." };
        if (apply)
        {
            if (File.Exists(Stem + " for.tsv")) throw new InvalidOperationException("Før-snapshot finnes allerede; verifiser før nytt forsøk.");
            Write(Stem + " for.tsv", before);
            void Dialog(object? sender, DialogBoxShowingEventArgs args)
            {
                if (args is not TaskDialogShowingEventArgs task) return;
                string message = task.Message ?? "";
                string normal = new string(message.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
                if (normal.Contains("tryingtocheckoutalargenumberofelements"))
                    log.Add("WORKSHARING: " + message.Substring(0, Math.Min(500, message.Length)).Replace('\n', ' ')
                        + "; godtatt=" + args.OverrideResult((int)TaskDialogResult.CommandLink1));
                else if (normal.Contains("checkout") && normal.Contains("workset"))
                    log.Add("WORKSHARING: uavklart dialog, ikke overstyrt: " + message.Substring(0, Math.Min(500, message.Length)));
            }
            using var group = new TransactionGroup(doc, "dRofus-merke til dokumenterte kabelbrosegmenter");
            if (group.Start() != TransactionStatus.Started) throw new InvalidOperationException("Kunne ikke starte gruppe.");
            ui.DialogBoxShowing += Dialog;
            try
            {
                using (var transaction = new Transaction(doc, "39 segmenter følger dRofus-representanten"))
                {
                    if (transaction.Start() != TransactionStatus.Started) throw new InvalidOperationException("Transaksjon feilet.");
                    transaction.SetFailureHandlingOptions(transaction.GetFailureHandlingOptions()
                        .SetForcedModalHandling(true).SetClearAfterRollback(true).SetFailuresPreprocessor(new RejectFailures(log)));
                    int writes = 0;
                    foreach (string[] row in manifest)
                    {
                        token.ThrowIfCancellationRequested();
                        Element element = doc.GetElement(row[1]);
                        for (int index = 0; index < Fields.Length; index++)
                            if (row[index + 4] != row[index + 7])
                            {
                                if (!Parameter(element, Fields[index], StorageType.String).Set(row[index + 7]))
                                    throw new InvalidOperationException("Felt ble ikke satt.");
                                writes++;
                            }
                    }
                    TransactionStatus status = transaction.Commit();
                    log.Add("Transaksjon=" + status + "; segmenter=39; felt=" + writes);
                    if (status != TransactionStatus.Committed) throw new InvalidOperationException("Commit ikke bekreftet.");
                }
                token.ThrowIfCancellationRequested();
                Compare(before, Snapshot(all, visible), targets);
                TransactionStatus statusGroup = group.Assimilate();
                log.Add("Gruppe=" + statusGroup + "; alle K5B-kabelbroer kontrollert, type/pin/kurve/dimensjoner/ID-er bevart.");
                if (statusGroup != TransactionStatus.Committed) throw new InvalidOperationException("Gruppecommit feilet.");
            }
            finally
            {
                ui.DialogBoxShowing -= Dialog;
                if (group.GetStatus() == TransactionStatus.Started)
                {
                    TransactionStatus status = group.RollBack();
                    log.Add("Tilbakeføring=" + status);
                    File.WriteAllLines(Stem + " tilbakefort.txt", log, new UTF8Encoding(false));
                    if (status != TransactionStatus.RolledBack) throw new InvalidOperationException("Rollback ikke bekreftet.");
                }
            }
        }
        string[][] after = Snapshot(all, visible);
        Compare(before, after, targets);
        string output = apply ? Stem + " etter" : Path.Combine(Root, "BAS dRofus segmentmerke etterkontroll " + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff"));
        Write(output + ".tsv", after);
        log.Add("VERIFISERT: alle 39 har representantens merke/sekvens/område; øvrige verdier og segmenter uendret.");
        File.WriteAllLines(output + ".txt", log, new UTF8Encoding(false));
        return string.Join(Environment.NewLine, log) + Environment.NewLine + output;
    }

    private static void Compare(string[][] before, string[][] after, Dictionary<string, string[]> targets)
    {
        var original = before.ToDictionary(row => row[1], StringComparer.Ordinal);
        if (original.Count != after.Length || after.Count(row => targets.ContainsKey(row[1])) != 39)
            throw new InvalidOperationException("Elementutvalg endret.");
        foreach (string[] row in after)
        {
            if (!original.TryGetValue(row[1], out var old)) throw new InvalidOperationException("Nytt element.");
            for (int index = 0; index < row.Length; index++)
            {
                string expected = targets.TryGetValue(row[1], out var target) && index >= 4 && index <= 6 ? target[index + 3] : old[index];
                if (row[index] != expected) throw new InvalidOperationException("Uventet etterverdi: " + row[0] + " kolonne " + index);
            }
        }
    }
    private static string[][] Snapshot(Element[] elements, HashSet<long> visible) => elements.OrderBy(element => element.Id.Value).Select(element =>
    {
        Curve curve = ((LocationCurve)element.Location).Curve;
        return new[] { element.Id.Value.ToString(), element.UniqueId, visible.Contains(element.Id.Value).ToString(),
            element.GetTypeId().Value.ToString(), Text(element, Fields[0]), Text(element, Fields[1]), Text(element, Fields[2]),
            Integer(element, "drofus_occurrence_id").ToString(), Integer(element.Document.GetElement(element.GetTypeId()), "dRofus_ItemID").ToString(),
            Text(element, "FOB_Leveransepakke"), element.Pinned.ToString(), Point(curve.GetEndPoint(0)), Point(curve.GetEndPoint(1)),
            curve.Length.ToString("R", CultureInfo.InvariantCulture),
            element.get_Parameter(BuiltInParameter.RBS_CABLETRAY_WIDTH_PARAM).AsDouble().ToString("R", CultureInfo.InvariantCulture),
            element.get_Parameter(BuiltInParameter.RBS_CABLETRAY_HEIGHT_PARAM).AsDouble().ToString("R", CultureInfo.InvariantCulture) };
    }).ToArray();
    private static string Point(XYZ point) => string.Join(",", new[] { point.X, point.Y, point.Z }.Select(value => value.ToString("R", CultureInfo.InvariantCulture)));
    private static Parameter Parameter(Element element, string name, StorageType type)
    {
        var parameters = element.GetParameters(name);
        if (parameters.Count != 1 || parameters[0].StorageType != type) throw new InvalidOperationException("Ugyldig parameter: " + name);
        return parameters[0];
    }
    private static string Text(Element element, string name) => Parameter(element, name, StorageType.String).AsString() ?? "";
    private static int Integer(Element element, string name) => Parameter(element, name, StorageType.Integer).AsInteger();
    private static void Write(string path, string[][] rows)
    {
        if (rows.Any(row => row.Length != 16 || row.Any(value => value.IndexOfAny(new[] { '\t', '\r', '\n' }) >= 0)))
            throw new InvalidDataException("Ugyldig TSV.");
        File.WriteAllLines(path, new[] { "ElementId\tUniqueId\tVisible\tTypeId\tMerkestreng\tSekvens\tOmraade\tOccurrenceId\tItemId\tPackage\tPinned\tStart\tEnd\tLength\tWidth\tHeight" }
            .Concat(rows.Select(row => string.Join("\t", row))), new UTF8Encoding(false));
    }
    private sealed class RejectFailures : IFailuresPreprocessor
    {
        private readonly List<string> log;
        internal RejectFailures(List<string> log) { this.log = log; }
        public FailureProcessingResult PreprocessFailures(FailuresAccessor accessor)
        {
            var failures = accessor.GetFailureMessages();
            foreach (var failure in failures) log.Add("REVIT: " + failure.GetSeverity() + ": " + failure.GetDescriptionText());
            return failures.Count == 0 ? FailureProcessingResult.Continue : FailureProcessingResult.ProceedWithRollBack;
        }
    }
}
