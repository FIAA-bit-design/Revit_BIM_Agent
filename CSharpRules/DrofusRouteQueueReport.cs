#nullable enable
using System.Globalization;
using System.Collections.Generic;
using System.Linq;

namespace CW.Assistant.Generated;

public static class DrofusRouteQueueReport
{
    private static string F(double value) => value.ToString("R", CultureInfo.InvariantCulture);
    public static void Write(DrofusRouteQueue queue, string queuePath, string path)
    {
        DrofusRouteQueueStore.Validate(queue);
        var rows = queue.Routes.SelectMany(r => r.Members.Select(m => new[] {
            r.Route, r.Status, r.Detail, r.RegisteredAt.ToString("O"), r.RepresentativeUniqueId,
            r.Members.Single(x => x.UniqueId == r.RepresentativeUniqueId).ElementId.ToString(),
            m.ElementId.ToString(), m.UniqueId, m.UniqueId == r.RepresentativeUniqueId ? "Representant" : "Segment",
            m.TypeName, F(m.WidthMm), F(m.HeightMm), m.OccurrenceId.ToString(), m.Mark, m.Sequence, m.RoomNumber,
            r.SelectionReason, queue.ModelPath, queue.ViewUniqueId })).ToList();
        ExcelListReportWriter.Write(path, new[] {
            new ExcelListReportWriter.WorksheetData("Les meg", new[] { "Tema", "Beskrivelse" }, new List<string[]> {
                new[] { "Autoritet", queuePath + " er medlemsko. Excel er tidsstemplet rapport, ikke redigeringsgrunnlag." },
                new[] { "Pending", "Registrering leser kun markeringen. Ingen viewendring for ApplyBatch." },
                new[] { "Applied", "Viewskjuling er utfort; dette betyr ikke at merke/sekvens/rominfo er komplette." },
                new[] { "Snapshot", "Merke og romnummer er fra registrering, ikke live-verdier eller direkte dRofus-kontroll." },
                new[] { "Notater", "Denne filen overskrives ikke. Bevar manuelle notater i den aktuelle rapportfilen." } }),
            new ExcelListReportWriter.WorksheetData("Segmenter", new[] { "Strekk", "Status", "Detalj", "Registrert",
                "RepresentativeUniqueId", "RepresentantId", "ElementId", "UniqueId", "Rolle", "Typenavn", "Bredde mm",
                "Hoyde mm", "OccurrenceId", "Merkestreng ved registrering", "Sekvens ved registrering",
                "Romnummer ved registrering", "Valggrunnlag", "Modellsti", "ViewUniqueId" }, rows) });
    }
}
