#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Events;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;

namespace CW.Assistant.Generated;

internal sealed class GeneratedAction
{
    private const string Model = "Autodesk Docs://Fornebubanen/U_F_BAS_FBU_RIE_XXX.rvt";
    private const string SpaceUid = "63bacd43-227d-42fc-add5-614967affd10-00ec2c5b";
    private const double VolumeTolerance = 0.000001;
    private static readonly string[] TrayUids = {
        "43994072-83b2-4b9c-82ab-d4f3df0fefa7-00b49986",
        "a602c242-146b-47cf-93d1-b8052ca793fb-00bb22c8",
        "a602c242-146b-47cf-93d1-b8052ca793fb-00bb2494",
        "a602c242-146b-47cf-93d1-b8052ca793fb-00bb2706",
        "a602c242-146b-47cf-93d1-b8052ca793fb-00bb2799",
        "ed230345-c369-4edd-aefb-f3b39f158476-00c22757",
        "7e574faa-444b-4333-aa67-bf75b31f52e6-00d8d651",
        "7e574faa-444b-4333-aa67-bf75b31f52e6-00d8d735",
        "78c9b82b-62d2-4e8f-a385-7137eec5e4ef-00d8d91a",
        "78c9b82b-62d2-4e8f-a385-7137eec5e4ef-00d8d91b",
        "78c9b82b-62d2-4e8f-a385-7137eec5e4ef-00d8da5a",
        "78c9b82b-62d2-4e8f-a385-7137eec5e4ef-00d8da93",
        "d9abc015-e2ad-413e-90cc-4e5ba178f306-00da2147",
        "d9abc015-e2ad-413e-90cc-4e5ba178f306-00da2202"
    };

    public string Execute(UIApplication ui, Document? document, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (document is null || document.PathName != Model || ui.ActiveUIDocument?.Document.PathName != Model
            || document.ActiveView.UniqueId != "5e8f81e4-fcff-4378-8a08-70d893e9ab6c-0110f8e2")
            throw new InvalidOperationException("STOPP: feil aktiv BAS-modell eller view.");
        Document doc = document;
        var log = new List<string> { DateTimeOffset.Now.ToString("O"), Model,
            "Utvidet fullmakt: nødvendige Space-høyder for 30 kandidater, bare bekreftet rom/etasje.",
            "Denne batchen: L4S.28, faktiske geometrihøyder, 14 kandidatsegmenter. Ingen lagring/synk." };
        string report = Path.Combine(@"D:\Revit\Python\Revit_BIM_Agent\logs\reports",
            "BAS L4S28 justeringsforsok " + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + ".txt");
        try
        {
            Run(ui, doc, token, log);
        }
        catch (Exception exception)
        {
            log.Add("FEIL / IKKE GODKJENT: " + exception.GetType().FullName + ": " + exception.Message);
            throw;
        }
        finally
        {
            try { File.WriteAllLines(report, log, new UTF8Encoding(false)); }
            catch (IOException exception)
            {
                throw new IOException("Rapportskriving feilet. Modellresultat: " + string.Join(" | ", log), exception);
            }
        }
        return string.Join(Environment.NewLine, log) + Environment.NewLine + report;
    }

    private static void Run(UIApplication ui, Document doc, CancellationToken token, List<string> log)
    {
        Space space = doc.GetElement(SpaceUid) as Space ?? throw new InvalidOperationException("Mangler L4S.28.");
        if (space.Id.Value != 15480316 || space.Number != "L4S.28")
            throw new InvalidOperationException("Space-identitet endret.");
        if (doc.IsWorkshared && WorksharingUtils.GetCheckoutStatus(doc, space.Id) == CheckoutStatus.OwnedByOtherUser)
            throw new InvalidOperationException("Space eies av annen bruker.");
        ElementId phase = space.get_Parameter(BuiltInParameter.ROOM_PHASE_ID).AsElementId();
        if (doc.ActiveView.get_Parameter(BuiltInParameter.VIEW_PHASE).AsElementId() != phase)
            throw new InvalidOperationException("Feil Space-fase.");
        Parameter lower = space.get_Parameter(BuiltInParameter.ROOM_LOWER_OFFSET);
        if (lower.IsReadOnly) throw new InvalidOperationException("Nedre offset er skrivebeskyttet.");
        bool pin = space.Pinned;
        double oldOffset = lower.AsDouble();
        var trays = TrayUids.Select(uid => doc.GetElement(uid)
            ?? throw new InvalidOperationException("Mangler godkjent segment: " + uid)).ToArray();
        foreach (Element tray in trays)
        {
            ElementOnPhaseStatus status = tray.GetPhaseStatus(phase);
            if (tray.Category?.Id.Value != (long)BuiltInCategory.OST_CableTray
                || tray.LookupParameter("FOB_Entreprise")?.AsString() != "K5B"
                || (status != ElementOnPhaseStatus.New && status != ElementOnPhaseStatus.Existing))
                throw new InvalidOperationException("Segmentets omfang/fase endret: " + tray.Id.Value);
        }
        var traySolids = trays.ToDictionary(tray => tray.Id.Value, tray => Solids(tray.get_Geometry(
            new Options { DetailLevel = ViewDetailLevel.Fine })).ToArray());
        if (traySolids.Values.Any(solids => solids.Length == 0))
            throw new InvalidOperationException("Kan ikke kontrollere et segment uten solidgeometri.");
        var trayState = trays.ToDictionary(tray => tray.Id.Value, Snapshot);
        Solid before = SpaceSolid(doc, space);
        double bottom = Bounds(before).Min.Z;
        double trayBottom = traySolids.Values.SelectMany(solids => solids).Min(solid => Bounds(solid).Min.Z);
        double required = Math.Max(0, bottom - trayBottom + 10 / 304.8);
        double extension = required;
        log.Add("Space=" + space.Id.Value + "; pinned=" + pin + "; gammel nedre offset mm=" + F(oldOffset * 304.8));
        log.Add("Planlagt utvidelse ned mm=" + F(extension * 304.8) + "; inkludert 10 mm klaring.");
        log.Add("Behov for alle segmenters faktiske geometri mm=" + F(required * 304.8)
            + "; nødvendig utvidelse uten tidligere 500 mm-grense.");
        if (extension <= 0)
            throw new InvalidOperationException("Ingen nødvendig utvidelse.");
        var beforeOutside = traySolids.ToDictionary(pair => pair.Key, pair => Outside(pair.Value, before));
        var expandedBounds = Bounds(before);
        expandedBounds.Min = expandedBounds.Min - new XYZ(0, 0, extension);
        Space[] neighbors = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_MEPSpaces)
            .WhereElementIsNotElementType().OfType<Space>()
            .Where(other => other.Id != space.Id && other.Area > 0 && other.Volume > 0
                && other.get_Parameter(BuiltInParameter.ROOM_PHASE_ID).AsElementId() == phase
                && Intersects(expandedBounds, other.get_BoundingBox(null))).ToArray();
        var neighborsBefore = neighbors.ToDictionary(other => other.Id.Value, other => SpaceSolid(doc, other));
        var overlapBefore = neighborsBefore.ToDictionary(pair => pair.Key, pair => Intersection(before, pair.Value));
        var neighborGeometry = neighborsBefore.ToDictionary(pair => pair.Key, pair => Fingerprint(pair.Value));
        var neighborPins = neighbors.ToDictionary(other => other.Id.Value, other => other.Pinned);
        string previousLog = File.ReadAllLines(
            @"D:\Revit\Python\Revit_BIM_Agent\logs\reports\BAS L4S28 justeringsforsok 20261016_100752_889.txt")
            .Single(line => line.StartsWith("Modifiserte elementer=", StringComparison.Ordinal));
        long[] dependentIds = previousLog.Split(';')[0].Substring("Modifiserte elementer=".Length)
            .Split(',').Select(long.Parse).Where(id => id != space.Id.Value).ToArray();
        var dependentParameters = dependentIds.ToDictionary(id => id, id =>
            Parameters(doc.GetElement(new ElementId(id)) ?? throw new InvalidOperationException("Mangler følgeelement: " + id)));
        var dependentPositions = dependentIds.ToDictionary(id => id, id => Position(doc.GetElement(new ElementId(id))));
        var dependentGeometry = dependentIds.ToDictionary(id => id, id =>
        {
            Element element = doc.GetElement(new ElementId(id));
            return element is FamilyInstance ? GeometrySignature(element) : "";
        });
        var modified = new HashSet<long>();
        var addedDeleted = new HashSet<long>();
        void Changed(object? sender, DocumentChangedEventArgs args)
        {
            if (args.GetDocument().PathName != Model) return;
            modified.UnionWith(args.GetModifiedElementIds().Select(id => id.Value));
            addedDeleted.UnionWith(args.GetAddedElementIds().Concat(args.GetDeletedElementIds()).Select(id => id.Value));
        }
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
        using var group = new TransactionGroup(doc, "BAS L4S.28 nødvendig høydejustering");
        if (group.Start() != TransactionStatus.Started) throw new InvalidOperationException("Kunne ikke starte gruppe.");
        ui.Application.DocumentChanged += Changed;
        ui.DialogBoxShowing += Dialog;
        try
        {
            using (var transaction = new Transaction(doc, "L4S.28 nedre offset for faktisk kabelbrogeometri"))
            {
                if (transaction.Start() != TransactionStatus.Started) throw new InvalidOperationException("Kunne ikke starte transaksjon.");
                transaction.SetFailureHandlingOptions(transaction.GetFailureHandlingOptions()
                    .SetFailuresPreprocessor(new RejectFailures(log)).SetClearAfterRollback(true)
                    .SetForcedModalHandling(true));
                if (pin) space.Pinned = false;
                if (!lower.Set(oldOffset - extension)) throw new InvalidOperationException("Offset ble ikke satt.");
                space.Pinned = pin;
                token.ThrowIfCancellationRequested();
                TransactionStatus committed = transaction.Commit();
                log.Add("Hovedtransaksjon=" + committed);
                if (committed != TransactionStatus.Committed) throw new InvalidOperationException("Ikke bekreftet commit.");
            }
            token.ThrowIfCancellationRequested();
            Solid after = SpaceSolid(doc, space);
            bool safe = space.Pinned == pin && Math.Abs(lower.AsDouble() - (oldOffset - extension)) < 1e-8;
            safe &= addedDeleted.Count == 0;
            log.Add("Modifiserte elementer=" + string.Join(",", modified) + "; nye/slettede=" + string.Join(",", addedDeleted));
            foreach (long id in modified.Where(id => id != space.Id.Value))
            {
                if (!dependentParameters.TryGetValue(id, out var oldParameters))
                    throw new InvalidOperationException("Ukjent følgeoppdatering: " + id);
                Element element = doc.GetElement(new ElementId(id)) ?? throw new InvalidOperationException("Følgeelement slettet.");
                var current = Parameters(element);
                if (current.Count != oldParameters.Count) throw new InvalidOperationException("Parameterstruktur endret: " + id);
                foreach (var item in oldParameters)
                {
                    if (!current.TryGetValue(item.Key, out var value) || value.Value != item.Value.Value)
                    {
                        log.Add("Følgeparameter endret: " + id + " " + item.Key + ": " + item.Value.Value + " -> "
                            + (value.Value ?? "[mangler]") + "; readonly=" + item.Value.ReadOnly);
                        if (!item.Value.ReadOnly || !current.ContainsKey(item.Key) || !value.ReadOnly)
                            throw new InvalidOperationException("Redigerbar følgeparameter endret: " + id + " " + item.Key);
                    }
                }
                if (Position(element) != dependentPositions[id]
                    || (element is FamilyInstance && GeometrySignature(element) != dependentGeometry[id]))
                    throw new InvalidOperationException("Følgeelementets plassering/geometri endret: " + id);
            }
            log.Add("Følgeoppdateringer kontrollert: ingen redigerbare parameter-, plasserings- eller fysiske geometriendringer.");
            double lost = Difference(before, after);
            safe &= lost <= VolumeTolerance;
            log.Add("Tapt eksisterende Space-volum ft3=" + F(lost));
            using (Solid added = BooleanOperationsUtils.ExecuteBooleanOperation(after, before, BooleanOperationsType.Difference))
            using (Solid allowed = LowerSlab(Bounds(before), extension))
            {
                double outsideSlab = Difference(added, allowed);
                safe &= outsideSlab <= VolumeTolerance;
                log.Add("Tilleggsvolum utenfor godkjent nedre sone ft3=" + F(outsideSlab));
            }
            int improved = 0;
            foreach (Element tray in trays)
            {
                double outside = Outside(traySolids[tray.Id.Value], after);
                bool covered = outside <= VolumeTolerance;
                if (covered && beforeOutside[tray.Id.Value] > VolumeTolerance) improved++;
                safe &= outside <= beforeOutside[tray.Id.Value] + VolumeTolerance && Snapshot(tray) == trayState[tray.Id.Value];
                log.Add("Segment=" + tray.Id.Value + "; utenfor før ft3=" + F(beforeOutside[tray.Id.Value])
                    + "; utenfor etter ft3=" + F(outside) + "; full soliddekning=" + covered);
            }
            foreach (Space other in neighbors)
            {
                token.ThrowIfCancellationRequested();
                Solid otherAfter = SpaceSolid(doc, other);
                double overlap = Intersection(after, otherAfter);
                double increase = overlap - overlapBefore[other.Id.Value];
                bool unchanged = Fingerprint(otherAfter) == neighborGeometry[other.Id.Value]
                    && other.Pinned == neighborPins[other.Id.Value];
                safe &= increase <= VolumeTolerance && unchanged;
                if (increase > VolumeTolerance || !unchanged)
                    log.Add("BLOKKERING: Space=" + other.Id.Value + " " + other.Number
                        + "; økt overlapp ft3=" + F(increase) + "; nabo uendret=" + unchanged);
            }
            safe &= improved > 0;
            log.Add("Ny full soliddekning=" + improved + "/14; nabospaces kontrollert=" + neighbors.Length + "; sikkerhetskrav=" + safe);
            if (safe)
            {
                TransactionStatus status = group.Assimilate();
                log.Add("RESULTAT gruppe=" + status);
                if (status != TransactionStatus.Committed) throw new InvalidOperationException("Gruppe ikke bekreftet committed.");
            }
            else
            {
                TransactionStatus status = group.RollBack();
                log.Add("RESULTAT: TILBAKEFØRT, sikkerhetskrav ikke oppfylt; gruppe=" + status);
                if (status != TransactionStatus.RolledBack) throw new InvalidOperationException("Rollback ikke bekreftet.");
                if (space.Pinned != pin || Math.Abs(lower.AsDouble() - oldOffset) > 1e-8)
                    throw new InvalidOperationException("Pin/offset ikke gjenopprettet etter rollback.");
                log.Add("Pin og nedre offset kontrollert gjenopprettet.");
            }
        }
        finally
        {
            ui.DialogBoxShowing -= Dialog;
            ui.Application.DocumentChanged -= Changed;
            if (group.GetStatus() == TransactionStatus.Started)
            {
                TransactionStatus status = group.RollBack();
                log.Add("Avbrutt forsøk; gruppe rollback=" + status);
                if (status != TransactionStatus.RolledBack) throw new InvalidOperationException("Rollback ved avbrudd feilet.");
            }
        }
    }

    private sealed class RejectFailures : IFailuresPreprocessor
    {
        private readonly List<string> log;
        internal RejectFailures(List<string> log) { this.log = log; }
        public FailureProcessingResult PreprocessFailures(FailuresAccessor accessor)
        {
            var failures = accessor.GetFailureMessages();
            foreach (var failure in failures) log.Add("REVIT FAILURE: " + failure.GetSeverity() + ": " + failure.GetDescriptionText());
            return failures.Count == 0 ? FailureProcessingResult.Continue : FailureProcessingResult.ProceedWithRollBack;
        }
    }

    private static Solid SpaceSolid(Document doc, Space space)
    {
        using var calculator = new SpatialElementGeometryCalculator(doc);
        using var result = calculator.CalculateSpatialElementGeometry(space);
        return SolidUtils.Clone(result.GetGeometry());
    }
    private static IEnumerable<Solid> Solids(GeometryElement geometry)
    {
        foreach (GeometryObject item in geometry)
        {
            if (item is Solid solid && solid.Volume > 1e-9) yield return SolidUtils.Clone(solid);
            else if (item is GeometryInstance instance)
                foreach (Solid child in Solids(instance.GetInstanceGeometry())) yield return child;
        }
    }
    private static BoundingBoxXYZ Bounds(Solid solid)
    {
        using var box = solid.GetBoundingBox();
        XYZ[] corners = (from x in new[] { box.Min.X, box.Max.X }
                         from y in new[] { box.Min.Y, box.Max.Y }
                         from z in new[] { box.Min.Z, box.Max.Z }
                         select box.Transform.OfPoint(new XYZ(x, y, z))).ToArray();
        return new BoundingBoxXYZ { Min = new XYZ(corners.Min(p => p.X), corners.Min(p => p.Y), corners.Min(p => p.Z)),
            Max = new XYZ(corners.Max(p => p.X), corners.Max(p => p.Y), corners.Max(p => p.Z)) };
    }
    private static bool Intersects(BoundingBoxXYZ a, BoundingBoxXYZ? b) => b is not null
        && a.Min.X <= b.Max.X && a.Max.X >= b.Min.X && a.Min.Y <= b.Max.Y && a.Max.Y >= b.Min.Y
        && a.Min.Z <= b.Max.Z && a.Max.Z >= b.Min.Z;
    private static double Difference(Solid a, Solid b)
    {
        using Solid result = BooleanOperationsUtils.ExecuteBooleanOperation(a, b, BooleanOperationsType.Difference);
        return result.Volume;
    }
    private static double Intersection(Solid a, Solid b)
    {
        using Solid result = BooleanOperationsUtils.ExecuteBooleanOperation(a, b, BooleanOperationsType.Intersect);
        return result.Volume;
    }
    private static double Outside(Solid[] solids, Solid space) => solids.Sum(solid => Difference(solid, space));
    private static string Fingerprint(Solid solid)
    {
        return F(solid.Volume) + "|" + F(solid.SurfaceArea) + "|" + string.Join(";", solid.Faces.Cast<Face>()
            .SelectMany(face =>
            {
                Mesh mesh = face.Triangulate();
                return Enumerable.Range(0, mesh.NumTriangles).Select(index =>
                {
                    MeshTriangle triangle = mesh.get_Triangle(index);
                    return string.Join("/", Enumerable.Range(0, 3).Select(vertex => Point(triangle.get_Vertex(vertex)))
                        .OrderBy(value => value, StringComparer.Ordinal));
                });
            }).OrderBy(value => value, StringComparer.Ordinal));
    }
    private static string GeometrySignature(Element element) => string.Join("|", Solids(element.get_Geometry(
        new Options { DetailLevel = ViewDetailLevel.Fine })).Select(Fingerprint).OrderBy(value => value, StringComparer.Ordinal));
    private static string Point(XYZ point) => F(point.X) + "," + F(point.Y) + "," + F(point.Z);
    private static string Position(Element element)
    {
        string location = element.Location is LocationPoint point ? Point(point.Point) + "|" + F(point.Rotation)
            : element.Location is LocationCurve curve ? Point(curve.Curve.GetEndPoint(0)) + "|"
                + Point(curve.Curve.GetEndPoint(1)) + "|" + F(curve.Curve.Length) : "";
        if (element is IndependentTag tag) location += "|" + Point(tag.TagHeadPosition);
        return element.UniqueId + "|" + element.GetTypeId().Value + "|" + element.Pinned + "|" + location;
    }
    private static Dictionary<string, (string Value, bool ReadOnly)> Parameters(Element element)
    {
        var values = new Dictionary<string, (string, bool)>(StringComparer.Ordinal);
        foreach (Parameter parameter in element.Parameters)
        {
            string value = !parameter.HasValue ? "[tom]" : parameter.StorageType == StorageType.String ? parameter.AsString() ?? ""
                : parameter.StorageType == StorageType.Integer ? parameter.AsInteger().ToString(CultureInfo.InvariantCulture)
                : parameter.StorageType == StorageType.Double ? F(parameter.AsDouble())
                : parameter.StorageType == StorageType.ElementId ? parameter.AsElementId().Value.ToString(CultureInfo.InvariantCulture) : "";
            values.Add(parameter.Id.Value.ToString(CultureInfo.InvariantCulture), (value, parameter.IsReadOnly));
        }
        return values;
    }
    private static Solid LowerSlab(BoundingBoxXYZ box, double extension)
    {
        double z = box.Min.Z - extension - 1e-7;
        XYZ[] points = { new XYZ(box.Min.X, box.Min.Y, z), new XYZ(box.Max.X, box.Min.Y, z),
            new XYZ(box.Max.X, box.Max.Y, z), new XYZ(box.Min.X, box.Max.Y, z) };
        using var loop = new CurveLoop();
        for (int i = 0; i < points.Length; i++) loop.Append(Line.CreateBound(points[i], points[(i + 1) % points.Length]));
        return GeometryCreationUtilities.CreateExtrusionGeometry(new[] { loop }, XYZ.BasisZ, extension + 2e-7);
    }
    private static string Snapshot(Element tray)
    {
        Curve curve = ((LocationCurve)tray.Location).Curve;
        return tray.UniqueId + "|" + tray.GetTypeId().Value + "|" + tray.Pinned + "|"
            + string.Join("|", new[] { "FOB_Entreprise", "FOB_Leveransepakke", "FOB_Merkestreng",
                "FOB_Omraade", "FOB_Sekvensnummer", "drofus_occurrence_id" }.Select(name => tray.LookupParameter(name)?.AsValueString()
                    ?? tray.LookupParameter(name)?.AsString() ?? "[mangler]"))
            + "|" + curve.GetEndPoint(0) + "|" + curve.GetEndPoint(1) + "|" + F(curve.Length);
    }
    private static string F(double value) => value.ToString("G17", CultureInfo.InvariantCulture);
}
