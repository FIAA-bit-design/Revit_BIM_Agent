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
    private const string Root = @"D:\Revit\Python\Revit_BIM_Agent";
    public string Execute(UIApplication ui, Document doc, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (doc == null || doc.PathName != Model || ui.ActiveUIDocument.Document.PathName != Model
            || doc.ActiveView.UniqueId != ViewUid) throw new InvalidOperationException("Feil modell/view.");
        var selected = ui.ActiveUIDocument.Selection.GetElementIds().Select(doc.GetElement).OrderBy(e => e.Id.Value).ToArray();
        if (!selected.Select(e => e.UniqueId).SequenceEqual(new[] {
            "94efd9e4-d480-4349-832d-c2ef7cc26dde-00ab4c67",
            "94efd9e4-d480-4349-832d-c2ef7cc26dde-00ab4cda",
            "94efd9e4-d480-4349-832d-c2ef7cc26dde-00ab4d50" }))
            throw new InvalidOperationException("Utvalget er ikke den forhandskontrollerte gruppen.");
        if (selected.Length < 2) throw new InvalidOperationException("Minst to bekreftede segmenter kreves.");
        string[] registry = File.ReadAllLines(Path.Combine(Root, @"config\DrofusConfirmedRoutes.tsv"));
        var registered = registry.Skip(1).Select(line => line.Split('\t')[5]).ToHashSet();
        var visible = new FilteredElementCollector(doc, doc.ActiveView.Id).OfCategory(BuiltInCategory.OST_CableTray)
            .WhereElementIsNotElementType().ToElementIds().Select(id => id.Value).ToHashSet();
        foreach (Element e in selected)
        {
            ct.ThrowIfCancellationRequested();
            string name = doc.GetElement(e.GetTypeId()).Name;
            if (e.Category.Id.Value != (long)BuiltInCategory.OST_CableTray
                || e.LookupParameter("FOB_Entreprise")?.AsString() != "K5B"
                || name.IndexOf("Strømskinne", StringComparison.OrdinalIgnoreCase) >= 0
                || registered.Contains(e.UniqueId) || !visible.Contains(e.Id.Value)
                || !e.CanBeHidden(doc.ActiveView) || e.Location is not LocationCurve)
                throw new InvalidOperationException("Ugyldig/allerede registrert segment: " + e.Id.Value);
        }
        if (selected.Select(e => e.GetTypeId().Value).Distinct().Count() != 1
            || selected.Select(e => e.get_Parameter(BuiltInParameter.RBS_CABLETRAY_WIDTH_PARAM).AsDouble()).Distinct().Count() != 1
            || selected.Select(e => e.get_Parameter(BuiltInParameter.RBS_CABLETRAY_HEIGHT_PARAM).AsDouble()).Distinct().Count() != 1)
            throw new InvalidOperationException("Ulik type/utførelse/dimensjon må avklares.");
        var candidates = selected.Where(e => e.LookupParameter("drofus_occurrence_id").AsInteger() > 0
            && Valid(e.LookupParameter("FOB_Merkestreng").AsString())
            && Valid(e.LookupParameter("FOB_Sekvensnummer").AsString())).ToArray();
        Element kept = candidates.Length == 1 ? candidates[0] : selected
            .OrderByDescending(e => ((LocationCurve)e.Location).Curve.Length).ThenBy(e => e.Id.Value).First();
        var hidden = selected.Where(e => e.Id != kept.Id).Select(e => e.Id.Value).ToHashSet();
        if (doc.IsWorkshared && WorksharingUtils.GetCheckoutStatus(doc, doc.ActiveView.Id) == CheckoutStatus.OwnedByOtherUser)
            throw new InvalidOperationException("View eies av annen bruker.");
        string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
        string stem = Path.Combine(Root, @"logs\reports\BAS markert strekk " + stamp);
        string route = "BAS-" + kept.Id.Value;
        string[] membership = selected.Select(e => string.Join("\t", new[] { Model, ViewUid, route, kept.UniqueId,
            e.Id.Value.ToString(), e.UniqueId, e.LookupParameter("drofus_occurrence_id").AsInteger().ToString(),
            F(e.get_Parameter(BuiltInParameter.RBS_CABLETRAY_WIDTH_PARAM).AsDouble() * 304.8),
            F(e.get_Parameter(BuiltInParameter.RBS_CABLETRAY_HEIGHT_PARAM).AsDouble() * 304.8) })).ToArray();
        File.WriteAllLines(stem + " koblinger.tsv", new[] { registry[0] }.Concat(membership), new UTF8Encoding(false));
        string before = Snapshot(doc, new HashSet<long>(), ct), expected = Snapshot(doc, hidden, ct);
        File.WriteAllText(stem + " for.tsv", before, new UTF8Encoding(false));
        var log = new List<string> { Model, "Behold=" + kept.Id.Value + " | " + kept.UniqueId,
            "Valg=" + (candidates.Length == 1 ? "entydig synkronisert representant" : "lengste; laveste ID ved lik lengde"),
            "Skjul=" + string.Join(",", hidden), "Registeret oppdateres etter separat etterkontroll; koblingsforslag er bevart." };
        using var group = new TransactionGroup(doc, "Brukerbekreftet kabelbrostrekk");
        if (group.Start() != TransactionStatus.Started) throw new InvalidOperationException("Group start failed.");
        EventHandler<DialogBoxShowingEventArgs> handler = (_, args) =>
        {
            if (args is not TaskDialogShowingEventArgs dialog) return;
            string text = new string(dialog.Message.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
            if (text.Contains("tryingtocheckoutalargenumberofelements"))
                log.Add("Checkout accepted=" + dialog.OverrideResult((int)TaskDialogResult.CommandLink1));
            else if (text.Contains("checkout") && text.Contains("workset")) log.Add("Uavklart dialog: " + dialog.Message);
        };
        ui.DialogBoxShowing += handler;
        try
        {
            using var transaction = new Transaction(doc, "Skjul ekstrasegmenter i dRofus-view");
            if (transaction.Start() != TransactionStatus.Started) throw new InvalidOperationException("Start failed.");
            transaction.SetFailureHandlingOptions(transaction.GetFailureHandlingOptions()
                .SetFailuresPreprocessor(new Reject(log)).SetClearAfterRollback(true));
            ct.ThrowIfCancellationRequested();
            doc.ActiveView.HideElements(hidden.Select(id => new ElementId(id)).ToList());
            var status = transaction.Commit();
            log.Add("Transaction=" + status);
            if (status != TransactionStatus.Committed) throw new InvalidOperationException("Commit failed.");
            string after = Snapshot(doc, new HashSet<long>(), ct);
            if (after != expected) throw new InvalidOperationException("Unexpected model change.");
            File.WriteAllText(stem + " etter.tsv", after, new UTF8Encoding(false));
            var final = group.Assimilate();
            log.Add("Group=" + final);
            if (final != TransactionStatus.Committed) throw new InvalidOperationException("Group failed.");
        }
        finally
        {
            ui.DialogBoxShowing -= handler;
            if (group.GetStatus() == TransactionStatus.Started) log.Add("Rollback=" + group.RollBack());
            File.WriteAllLines(stem + ".txt", log, new UTF8Encoding(false));
        }
        return string.Join(Environment.NewLine, log) + Environment.NewLine + stem;
    }
    private static bool Valid(string value) => !string.IsNullOrWhiteSpace(value) && value != "--"
        && !value.StartsWith("Error:", StringComparison.OrdinalIgnoreCase);
    private static string F(double value) => value.ToString("R", CultureInfo.InvariantCulture);
    private static string Snapshot(Document doc, HashSet<long> hide, CancellationToken ct)
    {
        var rows = new List<string> { "ElementId\tUniqueId\tHidden\tState" };
        foreach (Element e in new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_CableTray)
            .WhereElementIsNotElementType().ToElements().OrderBy(e => e.Id.Value))
        {
            ct.ThrowIfCancellationRequested();
            var state = new List<string> { e.GetTypeId().Value.ToString(), e.Pinned.ToString() };
            foreach (Parameter p in e.Parameters.Cast<Parameter>().OrderBy(p => p.Id.Value))
                state.Add(p.Id.Value + "=" + (p.StorageType switch { StorageType.String => p.AsString() ?? "",
                    StorageType.Integer => p.AsInteger().ToString(CultureInfo.InvariantCulture), StorageType.Double => F(p.AsDouble()),
                    StorageType.ElementId => p.AsElementId().Value.ToString(CultureInfo.InvariantCulture), _ => "" }));
            if (e.Location is LocationCurve lc)
            {
                foreach (XYZ point in new[] { lc.Curve.GetEndPoint(0), lc.Curve.GetEndPoint(1) })
                    state.Add(string.Join(",", new[] { point.X, point.Y, point.Z }.Select(F)));
                state.Add(F(lc.Curve.Length));
            }
            rows.Add(e.Id.Value + "\t" + e.UniqueId + "\t" + (e.IsHidden(doc.ActiveView) || hide.Contains(e.Id.Value))
                + "\t" + Convert.ToBase64String(Encoding.UTF8.GetBytes(string.Join("\n", state))));
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
