#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.UI;

namespace CW.Assistant.Generated;

public sealed class SpaceCoverageAnalyzer
{
    private const string BasPath = "Autodesk Docs://Fornebubanen/U_F_BAS_FBU_RIE_XXX.rvt";
    private const string ViewUid = "5e8f81e4-fcff-4378-8a08-70d893e9ab6c-0110f8e2";
    private static readonly double[] Fractions = { 0, .10, .20, .30, .40, .50, .60, .70, .80, .90, 1 };
    private static readonly double[] Heights = { .05, .25, .50, .75, .95 };

    public string Analyze(UIApplication ui, Document? document, string scopePath, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (document is null || document.PathName != BasPath || ui.ActiveUIDocument?.Document.PathName != BasPath
            || document.ActiveView.UniqueId != ViewUid)
            throw new InvalidOperationException("STOPP: feil aktiv BAS-modell eller view.");
        Document doc = document;
        string[] input = File.ReadAllLines(scopePath, Encoding.UTF8);
        if (input.Length != 54 || input[0] != "ModelPath\tViewUniqueId\tElementId\tUniqueId\tRepresentativeUniqueId")
            throw new InvalidDataException("Forventet eksport av de 53 uavklarte segmentene.");
        string[][] scope = input.Skip(1).Select(line => line.Split('\t')).ToArray();
        if (scope.Any(row => row.Length != 5 || row[0] != BasPath || row[1] != ViewUid)
            || scope.Select(row => row[3]).Distinct(StringComparer.Ordinal).Count() != 53
            || scope.Select(row => row[4]).Distinct(StringComparer.Ordinal).Count() != 50)
            throw new InvalidDataException("Ugyldig eller tvetydig analyseomfang.");
        Space[] spaces = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_MEPSpaces)
            .WhereElementIsNotElementType().OfType<Space>().Where(space => space.Area > 0 && space.Volume > 0).ToArray();
        var boxes = spaces.ToDictionary(space => space.Id.Value, space => space.get_BoundingBox(null));
        var pins = spaces.ToDictionary(space => space.Id.Value, space => space.Pinned);
        var candidates = new List<string[]> { new[] {
            "ElementId", "UniqueId", "RepresentativeUniqueId", "Merkestreng", "SpaceId", "SpaceUniqueId",
            "SpaceNummer", "SpaceNavn", "SpaceFase", "ViewFase", "SammeFase", "Pinned",
            "DirekteTreffAv11", "ProjiserteTreffAv11", "UtvidelseOppMm", "UtvidelseNedMm",
            "SpaceBunnKoteMm", "SpaceToppKoteMm", "ForingsveiBunnKoteMm", "ForingsveiToppKoteMm",
            "UpperLevel", "UpperOffset", "LowerOffset", "Vurdering" } };
        var summary = new List<string[]> { new[] {
            "ElementId", "UniqueId", "RepresentativeUniqueId", "Merkestreng", "DirekteSpaceAntall",
            "ProjiserteSpaceAntall", "HeleStrekkKandidater", "Vurdering", "dRofus_RoomID", "dRofus_RoomfunctionNo" } };
        ElementId? viewPhase = doc.ActiveView.get_Parameter(BuiltInParameter.VIEW_PHASE)?.AsElementId();
        foreach (string[] row in scope)
        {
            token.ThrowIfCancellationRequested();
            Element element = doc.GetElement(row[3]) ?? throw new InvalidOperationException("Mangler element " + row[2]);
            if (element.Id.Value.ToString(CultureInfo.InvariantCulture) != row[2]
                || element.Category?.Id.Value != (long)BuiltInCategory.OST_CableTray
                || Text(element, "FOB_Entreprise") != "K5B")
                throw new InvalidOperationException("Elementomfang er endret: " + row[2]);
            Curve curve = (element.Location as LocationCurve)?.Curve ?? throw new InvalidOperationException("Mangler kurve.");
            XYZ[] points = Fractions.Select(t => curve.Evaluate(t, true)).ToArray();
            ElementOnPhaseStatus phaseStatus = viewPhase is null ? ElementOnPhaseStatus.None : element.GetPhaseStatus(viewPhase);
            bool phaseActive = phaseStatus == ElementOnPhaseStatus.New || phaseStatus == ElementOnPhaseStatus.Existing;
            Parameter heightParameter = element.get_Parameter(BuiltInParameter.RBS_CABLETRAY_HEIGHT_PARAM)
                ?? throw new InvalidOperationException("Mangler kabelbrohøyde.");
            double halfHeight = heightParameter.AsDouble() / 2;
            double bottom = points.Min(point => point.Z) - halfHeight, top = points.Max(point => point.Z) + halfHeight;
            int directSpaces = 0, projectedSpaces = 0, wholeCandidates = 0;
            foreach (Space space in spaces)
            {
                token.ThrowIfCancellationRequested();
                BoundingBoxXYZ? box = boxes[space.Id.Value];
                if (box is null) continue;
                XYZ min = box.Transform.OfPoint(box.Min), max = box.Transform.OfPoint(box.Max);
                // Projection is a diagnostic probe, not proof that the whole tray fits.
                int direct = 0, projected = 0;
                foreach (XYZ point in points)
                {
                    if (point.X < min.X || point.X > max.X || point.Y < min.Y || point.Y > max.Y) continue;
                    if (space.IsPointInSpace(point)) direct++;
                    if (Heights.Any(t => space.IsPointInSpace(new XYZ(point.X, point.Y, min.Z + (max.Z - min.Z) * t))))
                        projected++;
                }
                if (direct == 0 && projected == 0) continue;
                if (direct > 0) directSpaces++;
                if (projected > 0) projectedSpaces++;
                ElementId spacePhase = space.get_Parameter(BuiltInParameter.ROOM_PHASE_ID)?.AsElementId()
                    ?? throw new InvalidOperationException("Space mangler fase: " + space.Id.Value);
                bool samePhase = viewPhase is not null && spacePhase == viewPhase && phaseActive;
                if (projected == points.Length && samePhase) wholeCandidates++;
                string status = !samePhase ? "Annen fase; ikke automatisk kandidat"
                    : direct == points.Length ? "Senterlinjeprøver allerede innenfor Space"
                    : projected == points.Length ? "Mulig høydeavvik; krev volum- og overlappskontroll før justering"
                    : "Bare delvis dekning; sidegrense/kryssing må avklares";
                status += "; elementfasestatus=" + phaseStatus;
                candidates.Add(new[] {
                    row[2], row[3], row[4], Text(element, "FOB_Merkestreng"), space.Id.Value.ToString(), space.UniqueId,
                    space.Number, space.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? space.Name,
                    doc.GetElement(spacePhase)?.Name ?? "", viewPhase is null ? "" : doc.GetElement(viewPhase)?.Name ?? "",
                    samePhase.ToString(), space.Pinned.ToString(), direct.ToString(), projected.ToString(),
                    Mm(Math.Max(0, top - max.Z)), Mm(Math.Max(0, min.Z - bottom)), Mm(min.Z), Mm(max.Z), Mm(bottom), Mm(top),
                    Display(space, BuiltInParameter.ROOM_UPPER_LEVEL), Display(space, BuiltInParameter.ROOM_UPPER_OFFSET),
                    Display(space, BuiltInParameter.ROOM_LOWER_OFFSET), status });
            }
            string assessment = wholeCandidates == 1 ? "Én kandidat dekker alle XY-prøver i viewfasen; se kandidater"
                : wholeCandidates > 1 ? "Tvetydig: flere heldekkende kandidater i viewfasen"
                : projectedSpaces > 0 ? "Delvis dekning eller annen fase; ingen entydig høyderetting"
                : "Ingen Space ved XY-projeksjonsprøver; undersøk sidegrenser/lenker";
            summary.Add(new[] { row[2], row[3], row[4], Text(element, "FOB_Merkestreng"), directSpaces.ToString(),
                projectedSpaces.ToString(), wholeCandidates.ToString(), assessment + "; elementfasestatus=" + phaseStatus,
                Value(element, "dRofus_RoomID"), Value(element, "dRofus_RoomfunctionNo") });
        }
        if (spaces.Any(space => space.Pinned != pins[space.Id.Value])) throw new InvalidOperationException("Pin-status endret under lesende analyse.");
        string stem = Path.Combine(@"D:\Revit\Python\Revit_BIM_Agent\logs\reports",
            "BAS Space dekning analyse " + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture));
        Write(stem + " elementer.tsv", summary);
        Write(stem + " kandidater.tsv", candidates);
        string report = "Kun lesende analyse | " + DateTimeOffset.Now.ToString("O") + Environment.NewLine
            + "BAS: 50 uavklarte føringsveier / 53 segmenter. Space i vertsmodell med positivt areal/volum=" + spaces.Length + Environment.NewLine
            + "11 senterlinjeprøver per segment og 5 vertikale projeksjonshøyder per Space. Fase oppgis. Ikke full volumkontroll; ingen direkte dRofus-kontroll."
            + Environment.NewLine + "Høydeforslag bruker Space-boundingbox og kabelbroens nominelle halvhøyde; er kun diagnostiske, ikke sikre skriveverdier."
            + Environment.NewLine + "Ingen transaksjoner, avpinning, endringer, lagring eller synkronisering. Pin-status bevart."
            + Environment.NewLine + stem;
        File.WriteAllText(stem + ".txt", report, new UTF8Encoding(false));
        return report;
    }

    private static string Mm(double value) => (value * 304.8).ToString("F1", CultureInfo.InvariantCulture);
    private static string Text(Element element, string name)
    {
        var parameters = element.GetParameters(name);
        if (parameters.Count != 1 || parameters[0].StorageType != StorageType.String)
            throw new InvalidOperationException("Ugyldig tekstparameter " + name + ": " + element.Id.Value);
        return parameters[0].AsString() ?? "";
    }
    private static string Value(Element element, string name)
    {
        var parameters = element.GetParameters(name);
        if (parameters.Count != 1) throw new InvalidOperationException("Ugyldig parameter " + name);
        Parameter parameter = parameters[0];
        return !parameter.HasValue ? "" : parameter.StorageType == StorageType.Integer
            ? parameter.AsInteger().ToString(CultureInfo.InvariantCulture) : Text(element, name);
    }
    private static string Display(Element element, BuiltInParameter name)
    {
        Parameter? parameter = element.get_Parameter(name);
        return parameter is null ? "[parameter mangler]" : parameter.AsValueString() ?? parameter.AsString() ?? "";
    }
    private static void Write(string path, List<string[]> rows)
    {
        if (rows.Any(row => row.Any(value => value.IndexOfAny(new[] { '\t', '\r', '\n' }) >= 0)))
            throw new InvalidDataException("Kontrolltegn i TSV-eksport.");
        if (rows.Any(row => row.Length != rows[0].Length))
            throw new InvalidDataException("Radbredde samsvarer ikke med overskriftene.");
        File.WriteAllLines(path, rows.Select(row => string.Join("\t", row)), new UTF8Encoding(false));
    }
}
