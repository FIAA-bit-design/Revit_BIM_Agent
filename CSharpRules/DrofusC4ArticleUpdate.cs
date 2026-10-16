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
    private static readonly long[] TypeIds = { 14584591, 14584679, 16536696 };
    private static readonly string[] Uids = { "78e7bd1a-3b2a-4798-b431-9ee8b9dff742-00de8b0f",
        "78e7bd1a-3b2a-4798-b431-9ee8b9dff742-00de8b67", "e58d3a79-a3e6-45a4-a1e4-a2f81d3bbf51-00fc5478" };
    private static readonly string[] Names = { "Kabelstige 400 mm x 60 mm C4", "Kabelstige 600 mm x 60 mm C4", "Veggkanal 73 mm x 123 mm C4" };
    private static readonly int[] Before = { 15500, 15499, 15508 };
    private static readonly int[] After = { 16289, 16290, 16291 };
    private static readonly int[] Widths = { 400, 600, 123 };
    private static readonly long[][] Instances = { new long[] { 8934084, 8934147, 8934904, 8934930, 8935225, 8935258 },
        new long[] { 8934763, 8934877, 8973105 }, new long[] { 13226313, 13964615 } };

    public string Execute(UIApplication ui, Document doc, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        const string model = "Autodesk Docs://Fornebubanen/U_F_BAS_FBU_RIE_XXX.rvt";
        if (doc == null || doc.PathName != model || ui.ActiveUIDocument.Document.PathName != model)
            throw new InvalidOperationException("Feil aktiv modell.");
        var all = new FilteredElementCollector(doc).WhereElementIsNotElementType().ToElements();
        var types = Uids.Select(uid => doc.GetElement(uid) ?? throw new InvalidOperationException("Mangler C4-type.")).ToArray();
        for (int i = 0; i < types.Length; i++)
        {
            Element type = types[i];
            var p = type.GetParameters("dRofus_ItemID");
            var users = all.Where(e => e.GetTypeId().Value == TypeIds[i]).ToArray();
            if (type.Id.Value != TypeIds[i] || type.Name != Names[i] || p.Count != 1
                || p[0].StorageType != StorageType.Integer || p[0].IsReadOnly || p[0].AsInteger() != Before[i]
                || !users.Select(e => e.Id.Value).Order().SequenceEqual(Instances[i].Order())
                || users.Any(e => e.Category.Id.Value != (long)BuiltInCategory.OST_CableTray
                    || e.LookupParameter("FOB_Entreprise")?.AsString() != "K5B"
                    || Math.Abs(e.get_Parameter(BuiltInParameter.RBS_CABLETRAY_WIDTH_PARAM).AsDouble() * 304.8 - Widths[i]) > 0.01))
                throw new InvalidOperationException("Endret typegrunnlag: " + TypeIds[i]);
            if (doc.IsWorkshared && WorksharingUtils.GetCheckoutStatus(doc, type.Id) == CheckoutStatus.OwnedByOtherUser)
                throw new InvalidOperationException("C4-type eies av annen bruker.");
        }
        string before = Snapshot(doc, false, ct), expected = Snapshot(doc, true, ct);
        long[] ids = ElementIds(doc);
        string stem = @"D:\Revit\Python\Revit_BIM_Agent\logs\reports\BAS C4 dRofus artikler " + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
        var log = new List<string> { model, "Kun tre C4-typeparametere; ingen occurrence-endring, lagring eller synk." };
        for (int i = 0; i < types.Length; i++) log.Add(TypeIds[i] + " | " + Names[i] + " | " + Before[i] + " -> " + After[i] + " | instanser=" + Instances[i].Length);
        File.WriteAllText(stem + " for.tsv", before, new UTF8Encoding(false));
        using var group = new TransactionGroup(doc, "C4 artikler fra dRofus");
        if (group.Start() != TransactionStatus.Started) throw new InvalidOperationException("Group start failed.");
        EventHandler<DialogBoxShowingEventArgs> handler = (_, args) =>
        {
            if (args is not TaskDialogShowingEventArgs dialog) return;
            string text = new string(dialog.Message.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
            if (text.Contains("tryingtocheckoutalargenumberofelements"))
                log.Add("Checkout accepted=" + dialog.OverrideResult((int)TaskDialogResult.CommandLink1));
            else if (text.Contains("checkout") && text.Contains("workset")) log.Add("Uavklart checkout-dialog: " + dialog.Message);
        };
        ui.DialogBoxShowing += handler;
        try
        {
            using var transaction = new Transaction(doc, "Oppdater tre C4 ItemID");
            if (transaction.Start() != TransactionStatus.Started) throw new InvalidOperationException("Start failed.");
            transaction.SetFailureHandlingOptions(transaction.GetFailureHandlingOptions()
                .SetFailuresPreprocessor(new Reject(log)).SetClearAfterRollback(true));
            for (int i = 0; i < types.Length; i++)
            {
                ct.ThrowIfCancellationRequested();
                if (!types[i].LookupParameter("dRofus_ItemID").Set(After[i])) throw new InvalidOperationException("Set failed.");
            }
            var status = transaction.Commit();
            log.Add("Transaction=" + status);
            if (status != TransactionStatus.Committed) throw new InvalidOperationException("Not committed.");
            ct.ThrowIfCancellationRequested();
            string after = Snapshot(doc, false, ct);
            if (after != expected || !ids.SequenceEqual(ElementIds(doc)))
            {
                File.WriteAllText(stem + " avvik.tsv", after, new UTF8Encoding(false));
                File.WriteAllText(stem + " forventet.tsv", expected, new UTF8Encoding(false));
                throw new InvalidOperationException("Unexpected model change; diagnostic snapshots saved.");
            }
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

    private static string Snapshot(Document doc, bool expected, CancellationToken ct)
    {
        var view = doc.GetElement("5e8f81e4-fcff-4378-8a08-70d893e9ab6c-0110f8e2") as View
            ?? throw new InvalidOperationException("Mangler view.");
        var elements = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_CableTray).ToElements();
        var rows = new List<string> { "ElementId\tUniqueId\tState" };
        foreach (Element e in elements.OrderBy(e => e.Id.Value))
        {
            ct.ThrowIfCancellationRequested();
            var values = new List<string> { e.GetTypeId().Value.ToString(), e.Pinned.ToString(), e.IsHidden(view).ToString() };
            foreach (Parameter p in e.Parameters.Cast<Parameter>().OrderBy(p => p.Id.Value))
            {
                string value = p.StorageType switch {
                    StorageType.String => p.AsString() ?? "",
                    StorageType.Integer => p.AsInteger().ToString(CultureInfo.InvariantCulture),
                    StorageType.Double => p.AsDouble().ToString("R", CultureInfo.InvariantCulture),
                    StorageType.ElementId => p.AsElementId().Value.ToString(CultureInfo.InvariantCulture), _ => "" };
                int index = Array.IndexOf(TypeIds, e.Id.Value);
                if (expected && index >= 0 && p.Id == e.LookupParameter("dRofus_ItemID").Id)
                    value = After[index].ToString(CultureInfo.InvariantCulture);
                values.Add(p.Id.Value + "=" + value);
            }
            if (e.Location is LocationCurve lc)
            {
                foreach (XYZ point in new[] { lc.Curve.GetEndPoint(0), lc.Curve.GetEndPoint(1) })
                    values.Add(string.Join(",", new[] { point.X, point.Y, point.Z }.Select(v => v.ToString("R", CultureInfo.InvariantCulture))));
                values.Add(lc.Curve.Length.ToString("R", CultureInfo.InvariantCulture));
            }
            rows.Add(e.Id.Value + "\t" + e.UniqueId + "\t" + Convert.ToBase64String(Encoding.UTF8.GetBytes(string.Join("\n", values))));
        }
        return string.Join("\n", rows);
    }
    private sealed class Reject(List<string> log) : IFailuresPreprocessor
    {
        public FailureProcessingResult PreprocessFailures(FailuresAccessor accessor)
        {
            var failures = accessor.GetFailureMessages();
            foreach (var failure in failures) log.Add("REVIT: " + failure.GetDescriptionText());
            return failures.Count == 0 ? FailureProcessingResult.Continue : FailureProcessingResult.ProceedWithRollBack;
        }
    }
}
