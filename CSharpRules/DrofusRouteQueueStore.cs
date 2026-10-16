#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace CW.Assistant.Generated;

public sealed class DrofusQueuedMember
{
    public long ElementId { get; set; }
    public string UniqueId { get; set; } = "";
    public int OccurrenceId { get; set; }
    public string TypeName { get; set; } = "";
    public double WidthMm { get; set; }
    public double HeightMm { get; set; }
    public double LengthFeet { get; set; }
    public string Mark { get; set; } = "";
    public string Sequence { get; set; } = "";
    public string RoomNumber { get; set; } = "";
    public string State { get; set; } = "";
}

public sealed class DrofusQueuedRoute
{
    public string Route { get; set; } = "";
    public string RepresentativeUniqueId { get; set; } = "";
    public string SelectionReason { get; set; } = "";
    public string Status { get; set; } = "Pending";
    public string Detail { get; set; } = "";
    public DateTime RegisteredAt { get; set; }
    public List<DrofusQueuedMember> Members { get; set; } = new();
}

public sealed class DrofusRouteQueue
{
    public int Schema { get; set; } = 1;
    public string ModelPath { get; set; } = DrofusRouteQueueStore.Model;
    public string ViewUniqueId { get; set; } = DrofusRouteQueueStore.ViewUid;
    public List<DrofusQueuedRoute> Routes { get; set; } = new();
}

public static class DrofusRouteQueueStore
{
    public const string Model = "Autodesk Docs://Fornebubanen/U_F_BAS_FBU_RIE_XXX.rvt";
    public const string ViewUid = "5e8f81e4-fcff-4378-8a08-70d893e9ab6c-0110f8e2";
    public const string RegistryHeader = "ModelPath\tViewUniqueId\tRoute\tRepresentativeUniqueId\tElementId\tUniqueId\tExpectedOccurrenceId\tWidthMm\tHeightMm";

    public static DrofusRouteQueue Load(string path)
    {
        var queue = File.Exists(path)
            ? JsonSerializer.Deserialize<DrofusRouteQueue>(File.ReadAllText(path))
                ?? throw new InvalidDataException("Tomt koregister.")
            : new DrofusRouteQueue();
        Validate(queue);
        return queue;
    }

    public static void Validate(DrofusRouteQueue queue)
    {
        if (queue.Schema != 1 || queue.ModelPath != Model || queue.ViewUniqueId != ViewUid || queue.Routes is null)
            throw new InvalidDataException("Feil skjemaversjon/modell/view i koregister.");
        var uids = new HashSet<string>(StringComparer.Ordinal);
        var ids = new HashSet<long>();
        var routes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var route in queue.Routes)
        {
            if (route is null || string.IsNullOrWhiteSpace(route.Route) || !routes.Add(route.Route)
                || route.Status is not ("Pending" or "Applied" or "Blocked")
                || route.Members is null || route.Members.Count < 2
                || route.Members.Count(m => m.UniqueId == route.RepresentativeUniqueId) != 1)
                throw new InvalidDataException("Ugyldig gruppe/representant/status.");
            foreach (var member in route.Members)
                if (string.IsNullOrWhiteSpace(member.UniqueId) || !uids.Add(member.UniqueId)
                    || member.ElementId <= 0 || !ids.Add(member.ElementId) || member.OccurrenceId < 0
                    || string.IsNullOrEmpty(member.State) || !double.IsFinite(member.WidthMm) || member.WidthMm <= 0
                    || !double.IsFinite(member.HeightMm) || member.HeightMm <= 0
                    || !double.IsFinite(member.LengthFeet) || member.LengthFeet <= 0)
                    throw new InvalidDataException("Ugyldig eller overlappende segment.");
        }
    }

    public static (DrofusQueuedMember Member, string Reason) Choose(IReadOnlyList<DrofusQueuedMember> members)
    {
        if (members.Count < 2) throw new InvalidDataException("Marker minst to kabelbrosegmenter.");
        var candidates = members.Where(m => m.OccurrenceId > 0 && Valid(m.Mark)
            && Valid(m.Sequence) && m.Sequence.All(c => c >= '0' && c <= '9') && m.Sequence.Any(c => c != '0')).ToArray();
        return candidates.Length == 1
            ? (candidates[0], "Entydig synkronisert representant")
            : (members.OrderByDescending(m => m.LengthFeet).ThenBy(m => m.ElementId).First(),
                "Lengste segment; laveste ElementId ved lik lengde");
    }

    public static void Save(string path, DrofusRouteQueue queue, string reportDirectory)
    {
        Validate(queue);
        SaveText(path, JsonSerializer.Serialize(queue, new JsonSerializerOptions { WriteIndented = true }), reportDirectory);
    }

    public static void SaveText(string path, string content, string reportDirectory)
    {
        Directory.CreateDirectory(reportDirectory);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, content, new UTF8Encoding(false));
            if (File.Exists(path))
            {
                string backup = Path.Combine(reportDirectory, Path.GetFileNameWithoutExtension(path)
                    + " before " + DateTime.Now.ToString("yyyyMMdd_HHmmss_fffffff") + Path.GetExtension(path));
                File.Replace(temporary, path, backup);
            }
            else File.Move(temporary, path);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public static bool Valid(string value) => !string.IsNullOrWhiteSpace(value) && value != "--"
        && !value.StartsWith("Error:", StringComparison.OrdinalIgnoreCase);
}
