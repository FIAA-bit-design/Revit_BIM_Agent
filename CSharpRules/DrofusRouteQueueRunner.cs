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

public sealed class DrofusRouteQueueRunner
{
    private const string Root = @"D:\Revit\Python\Revit_BIM_Agent";
    private static string QueuePath => Path.Combine(Root, @"config\DrofusRouteQueue.json");
    private static string RegistryPath => Path.Combine(Root, @"config\DrofusConfirmedRoutes.tsv");
    private static string Reports => Path.Combine(Root, @"logs\reports");

    public string Execute(UIApplication ui, Document doc, string action, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (doc.PathName != DrofusRouteQueueStore.Model || ui.ActiveUIDocument?.Document.PathName != doc.PathName
            || doc.ActiveView.UniqueId != DrofusRouteQueueStore.ViewUid)
            throw new InvalidOperationException("Aktiv BAS-modell og KS Kablebro dRofus ma bekreftes.");
        if (action is not ("RegisterSelection" or "PreviewBatch" or "ApplyBatch"))
            throw new ArgumentException("Velg RegisterSelection, PreviewBatch eller ApplyBatch.");
        using var queueLock = new FileStream(QueuePath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var queue = DrofusRouteQueueStore.Load(QueuePath);
        var log = new List<string> { DateTimeOffset.Now.ToString("O"), action,
            "Kun viewskjuling ved ApplyBatch. Ingen parameterendring, lagring, synk eller dRofus-sletting." };
        string report = Path.Combine(Reports, "BAS strekk ko " + DateTime.Now.ToString("yyyyMMdd_HHmmss_fffffff"));
        try
        {
            var registry = ReadRegistry();
            if (action == "RegisterSelection")
            {
                Register(ui, doc, queue, registry, token);
                DrofusRouteQueueStore.Save(QueuePath, queue, Reports);
                log.Add("Markering lagret; modellen er ikke endret.");
            }
            else
            {
                foreach (var route in queue.Routes.Where(r => r.Status != "Applied"))
                {
                    token.ThrowIfCancellationRequested();
                    string? issue = Check(doc, route, registry);
                    if (issue != null)
                    {
                        route.Status = "Blocked";
                        route.Detail = issue;
                        log.Add(route.Route + ": blokkert: " + issue);
                        continue;
                    }
                    if (action == "PreviewBatch")
                    {
                        route.Status = "Pending";
                        route.Detail = "Kontrollert; venter pa samlet utforing.";
                    }
                    else
                    {
                        Apply(ui, doc, route, token, log);
                        // Save durable result before promoting links; a retry repairs an interrupted promotion.
                        route.Status = "Applied";
                        route.Detail = "Viewskjuling committed og sammenlignet; parameterberikelse ikke utfort.";
                        DrofusRouteQueueStore.Save(QueuePath, queue, Reports);
                    }
                }
                if (action == "ApplyBatch")
                {
                    foreach (var route in queue.Routes.Where(r => r.Status == "Applied"
                        && r.Members.Any(m => !registry.Any(row => row[5] == m.UniqueId))))
                    {
                        token.ThrowIfCancellationRequested();
                        string? issue = Check(doc, route, registry);
                        if (issue != null)
                        {
                            route.Status = "Blocked";
                            route.Detail = "Viewskjuling var committed, men registerpromotering er blokkert: " + issue;
                            log.Add(route.Route + ": " + route.Detail);
                            DrofusRouteQueueStore.Save(QueuePath, queue, Reports);
                            continue;
                        }
                        Promote(route, registry);
                    }
                }
                DrofusRouteQueueStore.Save(QueuePath, queue, Reports);
            }
            DrofusRouteQueueReport.Write(queue, QueuePath, report + ".xlsx");
            log.Add("Ko: " + queue.Routes.Count + " grupper; Pending=" + queue.Routes.Count(r => r.Status == "Pending")
                + "; Applied=" + queue.Routes.Count(r => r.Status == "Applied") + "; Blocked=" + queue.Routes.Count(r => r.Status == "Blocked"));
            log.Add("Excel (tidsstemplet, avledet rapport): " + report + ".xlsx");
            log.Add("Excel-normalapning er ikke testet inne i Revit-rutinen; valider rapporten med Test-ExcelWorkbook.ps1.");
            return "Rule12Runner: " + string.Join(Environment.NewLine, log);
        }
        catch (Exception exception)
        {
            log.Add("FEIL: " + exception);
            throw;
        }
        finally
        {
            File.WriteAllLines(report + ".txt", log, new UTF8Encoding(false));
        }
    }

    private static string[][] ReadRegistry()
    {
        string[] lines = File.ReadAllLines(RegistryPath);
        if (lines.Length == 0 || lines[0] != DrofusRouteQueueStore.RegistryHeader)
            throw new InvalidDataException("Feil header i bekreftet strekkregister.");
        var rows = lines.Skip(1).Select(l => l.Split('\t')).ToArray();
        if (rows.Any(r => r.Length != 9 || r[0] != DrofusRouteQueueStore.Model || r[1] != DrofusRouteQueueStore.ViewUid)
            || rows.Select(r => r[5]).Distinct().Count() != rows.Length)
            throw new InvalidDataException("Ugyldig/overlappende bekreftet strekkregister.");
        return rows;
    }

    private static void Register(UIApplication ui, Document doc, DrofusRouteQueue queue, string[][] registry, CancellationToken token)
    {
        var ids = ui.ActiveUIDocument.Selection.GetElementIds();
        if (ids.Count < 2) throw new InvalidOperationException("Marker minst to segmenter pa ett strekk.");
        var used = registry.Select(r => r[5]).Concat(queue.Routes.SelectMany(r => r.Members).Select(m => m.UniqueId)).ToHashSet();
        var visible = Visible(doc);
        var elements = ids.Select(id => doc.GetElement(id) ?? throw new InvalidOperationException("Mangler valgt element.")).ToArray();
        var members = new List<DrofusQueuedMember>();
        foreach (Element e in elements)
        {
            token.ThrowIfCancellationRequested();
            string? issue = Scope(doc, e);
            if (issue != null || !visible.Contains(e.Id.Value) || used.Contains(e.UniqueId))
                throw new InvalidOperationException(e.Id.Value + ": " + (issue ?? "Ikke synlig eller allerede registrert; avklar overlapp."));
            members.Add(ReadMember(e));
        }
        if (elements.Select(e => e.GetTypeId().Value).Distinct().Count() != 1
            || members.Any(m => Math.Abs(m.WidthMm - members[0].WidthMm) > 0.01 || Math.Abs(m.HeightMm - members[0].HeightMm) > 0.01))
            throw new InvalidOperationException("Ulik type, C4/utforelse eller dimensjon; marker samme strekk eller avklar.");
        var choice = DrofusRouteQueueStore.Choose(members);
        queue.Routes.Add(new DrofusQueuedRoute {
            Route = "BAS-" + choice.Member.ElementId, RepresentativeUniqueId = choice.Member.UniqueId,
            SelectionReason = choice.Reason, RegisteredAt = DateTime.Now, Members = members,
            Detail = "Venter pa utforing. Merke/sekvens/rominfo er kun snapshot." });
    }

    private static string? Check(Document doc, DrofusQueuedRoute route, string[][] registry)
    {
        var visible = Visible(doc);
        foreach (var member in route.Members)
        {
            Element? e = doc.GetElement(member.UniqueId);
            if (e == null || e.Id.Value != member.ElementId) return route.Route + ": element/UniqueId mangler.";
            string? scope = Scope(doc, e);
            if (scope != null) return scope;
            if (State(e) != member.State) return member.ElementId + ": modelldata endret siden registrering; registrer pa nytt etter avklaring.";
            bool kept = member.UniqueId == route.RepresentativeUniqueId;
            if (kept && !visible.Contains(e.Id.Value)) return "Representanten er ikke synlig.";
            if (!kept && !e.IsHidden(doc.ActiveView) && (!visible.Contains(e.Id.Value) || !e.CanBeHidden(doc.ActiveView)))
                return member.ElementId + ": kan ikke skjules i view.";
            if (route.Status == "Applied" && !kept && !e.IsHidden(doc.ActiveView)) return "Tidligere utfort skjuling er endret.";
            var existing = registry.FirstOrDefault(r => r[5] == member.UniqueId);
            if (existing != null && (existing[3] != route.RepresentativeUniqueId || existing[2] != route.Route
                || existing[4] != member.ElementId.ToString(CultureInfo.InvariantCulture)
                || existing[6] != member.OccurrenceId.ToString(CultureInfo.InvariantCulture)))
                return "Medlemskap kolliderer med bekreftet register.";
        }
        return null;
    }

    private static void Apply(UIApplication ui, Document doc, DrofusQueuedRoute route, CancellationToken token, List<string> log)
    {
        View view = doc.ActiveView;
        if (doc.IsWorkshared && WorksharingUtils.GetCheckoutStatus(doc, view.Id) == CheckoutStatus.OwnedByOtherUser)
            throw new InvalidOperationException("View eies av annen bruker.");
        var hide = route.Members.Where(m => m.UniqueId != route.RepresentativeUniqueId)
            .Select(m => doc.GetElement(m.UniqueId)).Where(e => !e.IsHidden(view)).Select(e => e.Id).ToList();
        var all = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_CableTray).WhereElementIsNotElementType().ToElements();
        var before = all.ToDictionary(e => e.UniqueId, State);
        var beforeHidden = all.ToDictionary(e => e.UniqueId, e => e.IsHidden(view));
        var hiddenIds = hide.Select(id => id.Value).ToHashSet();
        if (hide.Count == 0) { log.Add(route.Route + ": allerede skjult; modellkontroll bestatt."); return; }
        using var group = new TransactionGroup(doc, "Samlet dRofus-view: " + route.Route);
        if (group.Start() != TransactionStatus.Started) throw new InvalidOperationException("Gruppe kunne ikke startes.");
        EventHandler<DialogBoxShowingEventArgs> dialog = (_, args) =>
        {
            if (args is not TaskDialogShowingEventArgs task) return;
            string normal = new string(task.Message.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
            if (normal.Contains("tryingtocheckoutalargenumberofelements"))
                log.Add("Checkout: " + task.Message.Substring(0, Math.Min(500, task.Message.Length))
                    + " | accepted=" + task.OverrideResult((int)TaskDialogResult.CommandLink1));
            else if (normal.Contains("checkout") && normal.Contains("workset")) log.Add("Uavklart checkout: " + task.Message);
        };
        ui.DialogBoxShowing += dialog;
        try
        {
            using (var transaction = new Transaction(doc, "Skjul registrerte ekstrasegmenter"))
            {
                if (transaction.Start() != TransactionStatus.Started) throw new InvalidOperationException("Start feilet.");
                transaction.SetFailureHandlingOptions(transaction.GetFailureHandlingOptions()
                    .SetClearAfterRollback(true).SetFailuresPreprocessor(new Reject(log)));
                token.ThrowIfCancellationRequested();
                view.HideElements(hide);
                var status = transaction.Commit();
                log.Add(route.Route + ": Transaction=" + status);
                if (status != TransactionStatus.Committed) throw new InvalidOperationException("Commit feilet.");
            }
            token.ThrowIfCancellationRequested();
            var after = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_CableTray).WhereElementIsNotElementType().ToElements();
            if (after.Count != before.Count || after.Any(e => !before.TryGetValue(e.UniqueId, out string? state)
                || state != State(e) || e.IsHidden(view) != (beforeHidden[e.UniqueId] || hiddenIds.Contains(e.Id.Value))))
                throw new InvalidOperationException("Uventet modellendring; tilbakeforer gruppen.");
            var result = group.Assimilate();
            log.Add(route.Route + ": Group=" + result);
            if (result != TransactionStatus.Committed) throw new InvalidOperationException("Gruppecommit feilet.");
        }
        finally
        {
            ui.DialogBoxShowing -= dialog;
            if (group.GetStatus() == TransactionStatus.Started)
            {
                var result = group.RollBack();
                log.Add(route.Route + ": Rollback=" + result);
                if (result != TransactionStatus.RolledBack) throw new InvalidOperationException("Rollback feilet.");
            }
        }
    }

    private static void Promote(DrofusQueuedRoute route, string[][] existing)
    {
        var rows = existing.Select(r => string.Join("\t", r)).ToList();
        foreach (var m in route.Members.Where(m => !existing.Any(r => r[5] == m.UniqueId)))
            rows.Add(string.Join("\t", DrofusRouteQueueStore.Model, DrofusRouteQueueStore.ViewUid, route.Route,
                route.RepresentativeUniqueId, m.ElementId, m.UniqueId, m.OccurrenceId, F(m.WidthMm), F(m.HeightMm)));
        // Re-read for each group so earlier promotions in this batch cannot be overwritten.
        var current = File.ReadAllLines(RegistryPath).Skip(1).ToHashSet();
        current.UnionWith(rows);
        DrofusRouteQueueStore.SaveText(RegistryPath, DrofusRouteQueueStore.RegistryHeader + "\n"
            + string.Join("\n", current.OrderBy(r => r, StringComparer.Ordinal)) + "\n", Reports);
    }

    private static string? Scope(Document doc, Element e)
    {
        if (e.Category?.Id.Value != (long)BuiltInCategory.OST_CableTray || e is ElementType || e.Location is not LocationCurve)
            return "Bare rette kabelbrosegmenter med kurve stottes.";
        if (Text(e, "FOB_Entreprise") != "K5B") return "Elementet er utenfor K5B.";
        Element type = doc.GetElement(e.GetTypeId());
        if ((type.Name + " " + type.get_Parameter(BuiltInParameter.SYMBOL_FAMILY_NAME_PARAM)?.AsString())
            .IndexOf("Strømskinne", StringComparison.OrdinalIgnoreCase) >= 0) return "Stromskinne er utenfor omfang.";
        return null;
    }
    private static HashSet<long> Visible(Document doc) => new FilteredElementCollector(doc, doc.ActiveView.Id)
        .OfCategory(BuiltInCategory.OST_CableTray).WhereElementIsNotElementType().ToElementIds().Select(id => id.Value).ToHashSet();
    private static string Text(Element e, string name)
    {
        var p = e.GetParameters(name);
        if (p.Count != 1 || p[0].StorageType != StorageType.String) throw new InvalidOperationException("Ugyldig " + name);
        return p[0].AsString() ?? "";
    }
    private static DrofusQueuedMember ReadMember(Element e) => new() {
        ElementId = e.Id.Value, UniqueId = e.UniqueId, OccurrenceId = e.LookupParameter("drofus_occurrence_id").AsInteger(),
        TypeName = e.Document.GetElement(e.GetTypeId()).Name, WidthMm = e.get_Parameter(BuiltInParameter.RBS_CABLETRAY_WIDTH_PARAM).AsDouble() * 304.8,
        HeightMm = e.get_Parameter(BuiltInParameter.RBS_CABLETRAY_HEIGHT_PARAM).AsDouble() * 304.8,
        LengthFeet = ((LocationCurve)e.Location).Curve.Length, Mark = Text(e, "FOB_Merkestreng"),
        Sequence = Text(e, "FOB_Sekvensnummer"), RoomNumber = Text(e, "dRofus_RoomfunctionNo"), State = State(e) };
    private static string F(double value) => value.ToString("R", CultureInfo.InvariantCulture);
    private static string State(Element e)
    {
        var values = new List<string> { e.Id.Value.ToString(), e.UniqueId, e.GetTypeId().Value.ToString(), e.Pinned.ToString() };
        foreach (Element subject in new[] { e, e.Document.GetElement(e.GetTypeId()) })
            foreach (Parameter p in subject.Parameters.Cast<Parameter>().OrderBy(p => p.Id.Value))
                values.Add(subject.Id.Value + "|" + p.Id.Value + "=" + (p.StorageType switch {
                    StorageType.String => p.AsString() ?? "", StorageType.Integer => p.AsInteger().ToString(CultureInfo.InvariantCulture),
                    StorageType.Double => F(p.AsDouble()), StorageType.ElementId => p.AsElementId().Value.ToString(), _ => "" }));
        if (e.Location is LocationCurve lc)
        {
            foreach (XYZ p in lc.Curve.Tessellate()) values.Add(string.Join(",", F(p.X), F(p.Y), F(p.Z)));
            values.Add(F(lc.Curve.Length));
        }
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(string.Join("\n", values)));
    }
    private sealed class Reject(List<string> log) : IFailuresPreprocessor
    {
        public FailureProcessingResult PreprocessFailures(FailuresAccessor accessor)
        {
            var messages = accessor.GetFailureMessages();
            foreach (var m in messages) log.Add("REVIT: " + m.GetDescriptionText());
            return messages.Count == 0 ? FailureProcessingResult.Continue : FailureProcessingResult.ProceedWithRollBack;
        }
    }
}
