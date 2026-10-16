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

public sealed class GeneratedAction
{
    private const string Model = "Autodesk Docs://Fornebubanen/U_F_BAS_FBU_RIE_XXX.rvt";
    private const string ViewUid = "5e8f81e4-fcff-4378-8a08-70d893e9ab6c-0110f8e2";
    private const string DirectoryPath = @"D:\Revit\Python\Revit_BIM_Agent\logs\reports";
    private static readonly long[] Ids = { 12262924, 12351754, 12351756, 12351758, 12351760 };
    private static readonly string[] Uids = {
        "3de28406-8391-46ef-a35b-32becf36f03e-00bb1e0c",
        "5f883497-5a39-4b7c-b120-6b279b0b6c8e-00bb2d2b",
        "5f883497-5a39-4b7c-b120-6b279b0b6c8e-00bb2df1",
        "5f883497-5a39-4b7c-b120-6b279b0b6c8e-00bb2f89",
        "5f883497-5a39-4b7c-b120-6b279b0b6c8e-00bb3013" };

    public string Execute(UIApplication ui, Document? doc, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (doc is null || doc.PathName != Model || ui.ActiveUIDocument.Document.PathName != Model
            || doc.ActiveView.UniqueId != ViewUid)
            throw new InvalidOperationException("Feil modell eller aktivt view.");
        View view = doc.ActiveView;
        Element[] targets = Uids.Select(uid => doc.GetElement(uid)
            ?? throw new InvalidOperationException("Mangler bekreftet element.")).ToArray();
        int[] occurrences = { 809076, 809079, 809080, 809081, 809082 };
        for (int i = 0; i < targets.Length; i++)
        {
            Element e = targets[i];
            if (e.Id.Value != Ids[i] || e.Category.Id.Value != (long)BuiltInCategory.OST_CableTray
                || e.LookupParameter("FOB_Entreprise")?.AsString() != "K5B"
                || e.GetTypeId().Value != 3730779 || e.IsHidden(view) || !e.CanBeHidden(view)
                || e.LookupParameter("drofus_occurrence_id")?.AsInteger() != occurrences[i])
                throw new InvalidOperationException("Endret grunnlag: " + Ids[i]);
        }
        if (targets[0].LookupParameter("FOB_Merkestreng")?.AsString() != "BYG201-460-KAF-326"
            || targets.Skip(1).Any(e => !string.IsNullOrEmpty(e.LookupParameter("FOB_Merkestreng")?.AsString())))
            throw new InvalidOperationException("Merkekilden er endret.");
        if (doc.IsWorkshared && WorksharingUtils.GetCheckoutStatus(doc, view.Id) == CheckoutStatus.OwnedByOtherUser)
            throw new InvalidOperationException("Viewet eies av en annen bruker.");
        var hide = targets.Skip(1).Select(e => e.Id).ToList();
        string before = Snapshot(doc, view, new HashSet<long>(), ct);
        string expected = Snapshot(doc, view, hide.Select(id => id.Value).ToHashSet(), ct);
        long[] elementIds = AllElementIds(doc);
        string stem = Path.Combine(DirectoryPath, "BAS bekreftet 600mm strekk view " + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff"));
        var log = new List<string> { Model, "View=" + view.Name + " | " + view.UniqueId,
            "Brukerbekreftet strekk: " + string.Join(",", Ids),
            "Behold=12262924 | BYG201-460-KAF-326 | occurrence=809076",
            "Skjul=" + string.Join(",", hide.Select(id => id.Value)),
            "300 mm 14157827 utelatt. Ingen parameterendring eller sletting av dRofus-forekomster." };
        File.WriteAllText(stem + " for.tsv", before, new UTF8Encoding(false));
        EventHandler<DialogBoxShowingEventArgs> handler = (_, args) =>
        {
            if (args is not TaskDialogShowingEventArgs dialog) return;
            string text = new string(dialog.Message.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
            if (text.Contains("tryingtocheckoutalargenumberofelements"))
                log.Add("Checkout-dialog accepted=" + dialog.OverrideResult((int)TaskDialogResult.CommandLink1));
            else if (text.Contains("checkout") && text.Contains("workset"))
                log.Add("Uavklart checkout-dialog: " + dialog.Message);
        };
        using var group = new TransactionGroup(doc, "Ett brukerbekreftet kabelbrostrekk i dRofus-view");
        if (group.Start() != TransactionStatus.Started) throw new InvalidOperationException("Group start failed.");
        ui.DialogBoxShowing += handler;
        try
        {
            using var transaction = new Transaction(doc, "Skjul fire bekreftede strekksegmenter");
            if (transaction.Start() != TransactionStatus.Started) throw new InvalidOperationException("Start failed.");
            transaction.SetFailureHandlingOptions(transaction.GetFailureHandlingOptions()
                .SetFailuresPreprocessor(new RejectFailures(log)).SetClearAfterRollback(true));
            ct.ThrowIfCancellationRequested();
            view.HideElements(hide);
            var status = transaction.Commit();
            log.Add("Transaction=" + status);
            if (status != TransactionStatus.Committed) throw new InvalidOperationException("Not committed.");
            ct.ThrowIfCancellationRequested();
            string after = Snapshot(doc, view, new HashSet<long>(), ct);
            if (after != expected || !elementIds.SequenceEqual(AllElementIds(doc)))
                throw new InvalidOperationException("Uventet parameter-, geometri-, element- eller viewendring.");
            File.WriteAllText(stem + " etter.tsv", after, new UTF8Encoding(false));
            var final = group.Assimilate();
            log.Add("Group=" + final);
            if (final != TransactionStatus.Committed) throw new InvalidOperationException("Group not committed.");
        }
        finally
        {
            ui.DialogBoxShowing -= handler;
            if (group.GetStatus() == TransactionStatus.Started) log.Add("Rollback=" + group.RollBack());
            File.WriteAllLines(stem + ".txt", log, new UTF8Encoding(false));
        }
        return string.Join(Environment.NewLine, log) + Environment.NewLine + stem;
    }

    private static long[] AllElementIds(Document doc) => new FilteredElementCollector(doc)
        .WherePasses(new LogicalOrFilter(new ElementIsElementTypeFilter(false), new ElementIsElementTypeFilter(true)))
        .ToElementIds().Select(id => id.Value).Order().ToArray();

    private static string Snapshot(Document doc, View view, HashSet<long> additionallyHidden, CancellationToken ct)
    {
        var lines = new List<string> { "ElementId\tUniqueId\tHidden\tState" };
        foreach (Element e in new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_CableTray)
            .WhereElementIsNotElementType().ToElements().OrderBy(e => e.Id.Value))
        {
            ct.ThrowIfCancellationRequested();
            var values = new List<string> { e.GetTypeId().Value.ToString(), e.Pinned.ToString() };
            foreach (Parameter p in e.Parameters.Cast<Parameter>().OrderBy(p => p.Id.Value))
                values.Add(p.Id.Value + "=" + (p.StorageType switch {
                    StorageType.String => p.AsString() ?? "",
                    StorageType.Integer => p.AsInteger().ToString(CultureInfo.InvariantCulture),
                    StorageType.Double => p.AsDouble().ToString("R", CultureInfo.InvariantCulture),
                    StorageType.ElementId => p.AsElementId().Value.ToString(CultureInfo.InvariantCulture),
                    _ => "" }));
            if (e.Location is LocationCurve lc)
                values.Add(lc.Curve.GetEndPoint(0) + "|" + lc.Curve.GetEndPoint(1) + "|" + lc.Curve.Length.ToString("R", CultureInfo.InvariantCulture));
            string state = Convert.ToBase64String(Encoding.UTF8.GetBytes(string.Join("\n", values)));
            lines.Add(e.Id.Value + "\t" + e.UniqueId + "\t" + (e.IsHidden(view) || additionallyHidden.Contains(e.Id.Value)) + "\t" + state);
        }
        return string.Join("\n", lines);
    }

    private sealed class RejectFailures(List<string> log) : IFailuresPreprocessor
    {
        public FailureProcessingResult PreprocessFailures(FailuresAccessor accessor)
        {
            var failures = accessor.GetFailureMessages();
            foreach (var failure in failures) log.Add("REVIT: " + failure.GetDescriptionText());
            return failures.Count == 0 ? FailureProcessingResult.Continue : FailureProcessingResult.ProceedWithRollBack;
        }
    }
}
