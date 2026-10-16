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
    private static readonly long[] Ids = { 12351764, 12351767, 12351769, 12351771, 12351773 };
    private static readonly string[] Suffixes = { "00bb31fa", "00bb3300", "00bb338a", "00bb3441", "00bb3514" };
    private const string Model = "Autodesk Docs://Fornebubanen/U_F_BAS_FBU_RIE_XXX.rvt";
    private const string ViewUid = "5e8f81e4-fcff-4378-8a08-70d893e9ab6c-0110f8e2";

    public string Execute(UIApplication ui, Document doc, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (doc == null || doc.PathName != Model || ui.ActiveUIDocument.Document.PathName != Model
            || doc.ActiveView.UniqueId != ViewUid
            || !ui.ActiveUIDocument.Selection.GetElementIds().Select(id => id.Value).Order().SequenceEqual(Ids.Order()))
            throw new InvalidOperationException("Endret modell, view eller bekreftet utvalg.");
        View view = doc.ActiveView;
        for (int i = 0; i < Ids.Length; i++)
        {
            Element e = doc.GetElement("5f883497-5a39-4b7c-b120-6b279b0b6c8e-" + Suffixes[i]);
            if (e == null || e.Id.Value != Ids[i] || e.GetTypeId().Value != 3770819
                || e.Category.Id.Value != (long)BuiltInCategory.OST_CableTray
                || e.LookupParameter("FOB_Entreprise")?.AsString() != "K5B"
                || e.LookupParameter("FOB_Leveransepakke")?.AsString() != "LPE371"
                || e.LookupParameter("drofus_occurrence_id")?.AsInteger() != 808278 + i
                || !string.IsNullOrEmpty(e.LookupParameter("FOB_Merkestreng")?.AsString())
                || e.IsHidden(view) || !e.CanBeHidden(view))
                throw new InvalidOperationException("Endret elementgrunnlag: " + Ids[i]);
        }
        if (doc.IsWorkshared && WorksharingUtils.GetCheckoutStatus(doc, view.Id) == CheckoutStatus.OwnedByOtherUser)
            throw new InvalidOperationException("View eies av en annen bruker.");
        var hide = Ids.Skip(1).ToHashSet();
        string before = Snapshot(doc, view, new HashSet<long>(), ct);
        string expected = Snapshot(doc, view, hide, ct);
        long[] allIds = ElementIds(doc);
        string stem = @"D:\Revit\Python\Revit_BIM_Agent\logs\reports\BAS bekreftet 300mm strekk view "
            + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
        var log = new List<string> { Model, "View=" + view.UniqueId,
            "Brukerbekreftet representant=12351764 | occurrence=808278",
            "Skjul=" + string.Join(",", Ids.Skip(1)), "Ingen parameterendring. 600 mm-strekk urort." };
        File.WriteAllText(stem + " for.tsv", before, new UTF8Encoding(false));
        using var group = new TransactionGroup(doc, "En representant for bekreftet 300 mm-strekk");
        if (group.Start() != TransactionStatus.Started) throw new InvalidOperationException("Group start failed.");
        EventHandler<DialogBoxShowingEventArgs> handler = (_, args) =>
        {
            if (args is not TaskDialogShowingEventArgs dialog) return;
            string text = new string(dialog.Message.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
            if (text.Contains("tryingtocheckoutalargenumberofelements"))
                log.Add("Checkout accepted=" + dialog.OverrideResult((int)TaskDialogResult.CommandLink1));
            else if (text.Contains("checkout") && text.Contains("workset"))
                log.Add("Uavklart checkout-dialog: " + dialog.Message);
        };
        ui.DialogBoxShowing += handler;
        try
        {
            using var transaction = new Transaction(doc, "Skjul fire bekreftede 300 mm-segmenter");
            if (transaction.Start() != TransactionStatus.Started) throw new InvalidOperationException("Transaction start failed.");
            transaction.SetFailureHandlingOptions(transaction.GetFailureHandlingOptions()
                .SetFailuresPreprocessor(new RejectFailures(log)).SetClearAfterRollback(true));
            ct.ThrowIfCancellationRequested();
            view.HideElements(Ids.Skip(1).Select(id => new ElementId(id)).ToList());
            var status = transaction.Commit();
            log.Add("Transaction=" + status);
            if (status != TransactionStatus.Committed) throw new InvalidOperationException("Not committed.");
            ct.ThrowIfCancellationRequested();
            string after = Snapshot(doc, view, new HashSet<long>(), ct);
            if (after != expected || !allIds.SequenceEqual(ElementIds(doc)))
                throw new InvalidOperationException("Unexpected model change.");
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

    private static long[] ElementIds(Document doc) => new FilteredElementCollector(doc)
        .WherePasses(new LogicalOrFilter(new ElementIsElementTypeFilter(false), new ElementIsElementTypeFilter(true)))
        .ToElementIds().Select(id => id.Value).Order().ToArray();

    private static string Snapshot(Document doc, View view, HashSet<long> hide, CancellationToken ct)
    {
        var rows = new List<string> { "ElementId\tUniqueId\tHidden\tState" };
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
                foreach (XYZ p in new[] { lc.Curve.GetEndPoint(0), lc.Curve.GetEndPoint(1) })
                    values.Add(string.Join(",", new[] { p.X, p.Y, p.Z }.Select(v => v.ToString("R", CultureInfo.InvariantCulture))));
            rows.Add(e.Id.Value + "\t" + e.UniqueId + "\t" + (e.IsHidden(view) || hide.Contains(e.Id.Value))
                + "\t" + Convert.ToBase64String(Encoding.UTF8.GetBytes(string.Join("\n", values))));
        }
        return string.Join("\n", rows);
    }

    private sealed class RejectFailures(List<string> log) : IFailuresPreprocessor
    {
        public FailureProcessingResult PreprocessFailures(FailuresAccessor accessor)
        {
            var messages = accessor.GetFailureMessages();
            foreach (var message in messages) log.Add("REVIT: " + message.GetDescriptionText());
            return messages.Count == 0 ? FailureProcessingResult.Continue : FailureProcessingResult.ProceedWithRollBack;
        }
    }
}
