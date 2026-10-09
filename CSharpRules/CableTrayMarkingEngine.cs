#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;

namespace CW.Assistant.Generated
{
    internal static class CableTrayMarkingEngine
    {
        private const string SequenceName = "FOB_Sekvensnummer";
        private const string MarkName = "FOB_Merkestreng";
        private const string AreaName = "FOB_Omraade";
        private const string StatusName = "FOB_Status";
        private const string LockedStatus = "S5";
        private const string RegistryPath = @"D:\Revit\Python\Merkestreng kabelbroer\Merkestreng kabelbroer - brukte merkestrenger.csv";
        private const double MaximumProximityMm = 250.0;
        private const int SequenceDigits = 4;

        private static readonly BuiltInCategory[] TargetCategories =
        {
            BuiltInCategory.OST_CableTray,
            BuiltInCategory.OST_CableTrayFitting
        };

        private static readonly HashSet<PartType> AreaConnectorPartTypes = new HashSet<PartType>
        {
            PartType.Elbow, PartType.Tee, PartType.Cross, PartType.Union,
            PartType.ChannelCableTrayElbow, PartType.ChannelCableTrayVerticalElbow,
            PartType.ChannelCableTrayTee, PartType.ChannelCableTrayCross, PartType.ChannelCableTrayUnion,
            PartType.LadderCableTrayElbow, PartType.LadderCableTrayVerticalElbow,
            PartType.LadderCableTrayTee, PartType.LadderCableTrayCross, PartType.LadderCableTrayUnion
        };

        internal sealed class PlannedWrite
        {
            internal Element Owner { get; }
            internal Parameter Parameter { get; }
            internal string Value { get; }
            internal string Reason { get; }

            internal PlannedWrite(Element owner, Parameter parameter, string value, string reason)
            {
                Owner = owner;
                Parameter = parameter;
                Value = value;
                Reason = reason;
            }
        }

        internal sealed class Plan
        {
            internal bool Triggered { get; }
            internal IReadOnlyList<PlannedWrite> Writes { get; }
            internal IReadOnlyList<string> Log { get; }
            internal IReadOnlyList<string[]> RegistryRows { get; }
            internal int GroupCount { get; }
            internal int ElementCount { get; }

            internal Plan(bool triggered, List<PlannedWrite> writes, List<string> log, List<string[]> registryRows, int groupCount, int elementCount)
            {
                Triggered = triggered;
                Writes = writes;
                Log = log;
                RegistryRows = registryRows;
                GroupCount = groupCount;
                ElementCount = elementCount;
            }
        }

        private sealed class ElementData
        {
            internal Element Element { get; }
            internal long Id => Element.Id.Value;
            internal XYZ[] ConnectorOrigins { get; }

            internal ElementData(Element element, XYZ[] connectorOrigins)
            {
                Element = element;
                ConnectorOrigins = connectorOrigins;
            }
        }

        internal static Plan CreatePlan(Document document)
        {
            var log = new List<string>();
            var writes = new List<PlannedWrite>();
            var registryRows = new List<string[]>();
            List<Element> collected = CollectTargets(document);
            List<Element> elements = collected.Where(element => !IsExcluded(document, element)).ToList();

            List<Element> missingCableTrayData = elements
                .Where(element => IsMissing(GetText(GetInstanceParameter(element, SequenceName)))
                    || IsMissing(GetText(GetInstanceParameter(element, MarkName)))
                    || IsMissing(GetText(GetInstanceParameter(element, AreaName))))
                .ToList();
            if (missingCableTrayData.Count == 0)
            {
                return new Plan(false, writes, log, registryRows, 0, elements.Count);
            }

            log.Add(string.Format(CultureInfo.InvariantCulture,
                "KABELBROFORLØPER: funnet {0} kabelbro-/fittingelementer med manglende sekvens, merkestreng eller område; planlegger alle redigerbare grupper.",
                missingCableTrayData.Count));
            log.Add("Omfang: OST_CableTray og OST_CableTrayFitting i aktiv modell; skriptet har ikke FOB_Entreprise-filter.");

            var duplicateReportIds = collected
                .Select(element => new
                {
                    ReportId = GetText(GetInstanceParameter(element, "PGF_RIE_ElementId")),
                    ElementId = element.Id.Value
                })
                .Where(item => !string.IsNullOrWhiteSpace(item.ReportId))
                .GroupBy(item => item.ReportId.Trim(), StringComparer.Ordinal)
                .Where(group => group.Count() > 1)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .ToList();
            if (duplicateReportIds.Count > 0)
            {
                log.Add("BLOKKERING: dupliserte PGF_RIE_ElementId-verdier; ingen kabelbroverdier planlegges.");
                foreach (var duplicate in duplicateReportIds)
                {
                    log.Add("DUPLIKAT PGF_RIE_ElementId '" + duplicate.Key + "': ElementId "
                        + string.Join(",", duplicate.Select(item => item.ElementId).OrderBy(id => id)));
                }
                return new Plan(true, writes, log, registryRows, 0, elements.Count);
            }

            List<ElementData> data = elements.Select(element => new ElementData(element, GetConnectorOrigins(element))).ToList();
            Dictionary<long, Element> elementsById = data.ToDictionary(item => item.Id, item => item.Element);
            Dictionary<long, HashSet<long>> connectorAdjacency = BuildAdjacency(data, includeProximity: false);
            Dictionary<long, HashSet<long>> adjacency = CloneAdjacency(connectorAdjacency);
            int proximityLinks = AddProximityLinks(data, adjacency);
            List<List<long>> groups = FindGroups(adjacency);
            List<List<long>> connectorGroups = FindGroups(connectorAdjacency);
            Dictionary<long, ElementData> dataById = data.ToDictionary(item => item.Id);
            groups = SplitStableGroups(groups, connectorGroups, elementsById, dataById);
            int standaloneTrays = AddStandaloneTrays(groups, elements);

            Dictionary<string, string> registryStatus = LoadRegistryStatuses(log);
            var lockedMarks = new HashSet<string>(registryStatus
                .Where(pair => string.Equals(pair.Value, LockedStatus, StringComparison.OrdinalIgnoreCase))
                .Select(pair => pair.Key), StringComparer.OrdinalIgnoreCase);
            foreach (Element element in collected)
            {
                string mark = GetText(GetInstanceParameter(element, MarkName));
                if (string.IsNullOrWhiteSpace(mark)) continue;
                string key = NormalizeMark(mark);
                string status = GetText(GetInstanceParameter(element, StatusName));
                if (!registryStatus.TryGetValue(key, out string? previousStatus))
                {
                    registryRows.Add(new[] { mark, status, element.Id.Value.ToString(CultureInfo.InvariantCulture), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) });
                    registryStatus[key] = status;
                }
                else if (string.Equals(status, LockedStatus, StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(previousStatus, LockedStatus, StringComparison.OrdinalIgnoreCase))
                {
                    registryRows.Add(new[] { mark, status, element.Id.Value.ToString(CultureInfo.InvariantCulture), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) });
                    registryStatus[key] = status;
                }
                if (string.Equals(status, LockedStatus, StringComparison.OrdinalIgnoreCase)) lockedMarks.Add(key);
            }

            Dictionary<int, int?> sequenceByGroup = ReadGroupSequences(groups, elementsById, lockedMarks, log);
            var sequenceOwners = new Dictionary<int, List<int>>();
            foreach (KeyValuePair<int, int?> pair in sequenceByGroup)
            {
                if (!pair.Value.HasValue) continue;
                if (!sequenceOwners.TryGetValue(pair.Value.Value, out List<int>? ownerGroups))
                {
                    ownerGroups = new List<int>();
                    sequenceOwners.Add(pair.Value.Value, ownerGroups);
                }
                ownerGroups.Add(pair.Key);
            }

            foreach (List<int> duplicateGroups in sequenceOwners.Values.Where(groupIds => groupIds.Count > 1))
            {
                foreach (int groupIndex in duplicateGroups.Skip(1)) sequenceByGroup[groupIndex] = null;
                log.Add("Eksisterende sekvensnummer forekommer på flere grupper; senere grupper nummereres på nytt.");
            }

            var markOwners = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
            for (int groupIndex = 0; groupIndex < groups.Count; groupIndex++)
            {
                foreach (long id in groups[groupIndex])
                {
                    string mark = GetText(GetInstanceParameter(elementsById[id], MarkName));
                    if (string.IsNullOrWhiteSpace(mark)) continue;
                    string key = NormalizeMark(mark);
                    if (!markOwners.TryGetValue(key, out List<int>? owners))
                    {
                        owners = new List<int>();
                        markOwners.Add(key, owners);
                    }
                    if (!owners.Contains(groupIndex)) owners.Add(groupIndex);
                }
            }
            foreach (List<int> duplicateGroups in markOwners.Values.Where(groupIds => groupIds.Count > 1))
            {
                foreach (int groupIndex in duplicateGroups.Skip(1)) sequenceByGroup[groupIndex] = null;
                log.Add("Eksisterende full FOB_Merkestreng forekommer på flere grupper; senere grupper nummereres på nytt.");
            }

            FillSequenceGaps(groups, sequenceByGroup, log);
            var assignments = AssignSequences(groups, sequenceByGroup, elementsById, lockedMarks, log);
            if (assignments.Count != groups.Count)
            {
                log.Add("BLOKKERING: sekvensplanen dekker ikke alle grupper.");
                return new Plan(true, writes, log, registryRows, groups.Count, elements.Count);
            }

            Dictionary<long, string> areaByElement = ResolveAreas(document, elementsById, log);
            var scheduled = new HashSet<string>(StringComparer.Ordinal);
            foreach (PlannedWrite write in PlanValues(document, groups, elementsById, assignments, areaByElement, scheduled, log))
            {
                writes.Add(write);
            }

            log.Add(string.Format(CultureInfo.InvariantCulture,
                "CableTray-markering: grupper {0}; elementer {1}; nærhetslenker {2}; enkeltstående kabelbroer {3}; planlagte parameterendringer {4}.",
                groups.Count, elements.Count, proximityLinks, standaloneTrays, writes.Count));
            return new Plan(true, writes, log, registryRows, groups.Count, elements.Count);
        }

        internal static void PersistRegistryRows(IEnumerable<string[]> rows)
        {
            List<string[]> newRows = rows.ToList();
            if (newRows.Count == 0) return;
            bool exists = File.Exists(RegistryPath);
            using var writer = new StreamWriter(RegistryPath, append: exists, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            if (!exists) writer.WriteLine("Merkestreng;FOB_Status;ElementId;Registrert");
            foreach (string[] row in newRows) writer.WriteLine(string.Join(";", row));
        }

        private static List<Element> CollectTargets(Document document)
        {
            var elements = new List<Element>();
            foreach (BuiltInCategory category in TargetCategories)
            {
                elements.AddRange(new FilteredElementCollector(document)
                    .OfCategory(category)
                    .WhereElementIsNotElementType()
                    .ToElements());
            }
            return elements.OrderBy(element => element.Id.Value).ToList();
        }

        private static bool IsExcluded(Document document, Element element)
        {
            if (IsStrømskinne(document, element) || HasTypeFunctionCode(document, element, "STS")) return true;
            return string.Equals(GetText(GetInstanceParameter(element, StatusName)), LockedStatus, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsStrømskinne(Document document, Element element)
        {
            string instanceName = element.Name ?? string.Empty;
            if (instanceName.StartsWith("Stromskinner", StringComparison.OrdinalIgnoreCase)
                || instanceName.StartsWith("Strømskinner", StringComparison.OrdinalIgnoreCase)) return true;
            Element? type = document.GetElement(element.GetTypeId());
            string typeName = type?.Name ?? string.Empty;
            return typeName.StartsWith("Stromskinner", StringComparison.OrdinalIgnoreCase)
                || typeName.StartsWith("Strømskinner", StringComparison.OrdinalIgnoreCase);
        }

        private static bool HasTypeFunctionCode(Document document, Element element, string expected)
        {
            Parameter? parameter = GetTypeParameter(element, "FOB_Funksjonskode");
            return string.Equals(GetText(parameter)?.Trim(), expected, StringComparison.OrdinalIgnoreCase);
        }

        private static Dictionary<long, HashSet<long>> BuildAdjacency(IEnumerable<ElementData> data, bool includeProximity)
        {
            var adjacency = data.ToDictionary(item => item.Id, _ => new HashSet<long>());
            foreach (ElementData item in data)
            {
                foreach (Connector connector in GetConnectors(item.Element))
                {
                    ConnectorSet? references = null;
                    try { references = connector.AllRefs; }
                    catch { }
                    if (references is null) continue;
                    foreach (Connector reference in references)
                    {
                        Element? owner = reference.Owner;
                        if (owner is null || owner.Id.Value == item.Id || !adjacency.ContainsKey(owner.Id.Value)) continue;
                        adjacency[item.Id].Add(owner.Id.Value);
                        adjacency[owner.Id.Value].Add(item.Id);
                    }
                }
            }
            return adjacency;
        }

        private static Dictionary<long, HashSet<long>> CloneAdjacency(Dictionary<long, HashSet<long>> source)
        {
            return source.ToDictionary(pair => pair.Key, pair => new HashSet<long>(pair.Value));
        }

        private static int AddProximityLinks(List<ElementData> data, Dictionary<long, HashSet<long>> adjacency)
        {
            double cellSize = MaximumProximityMm / 304.8;
            double maxDistanceSquared = cellSize * cellSize;
            var cells = new Dictionary<(int X, int Y, int Z), List<(long Id, XYZ Point)>>();
            foreach (ElementData item in data)
            {
                foreach (XYZ point in item.ConnectorOrigins)
                {
                    var key = ((int)Math.Floor(point.X / cellSize), (int)Math.Floor(point.Y / cellSize), (int)Math.Floor(point.Z / cellSize));
                    if (!cells.TryGetValue(key, out List<(long Id, XYZ Point)>? points))
                    {
                        points = new List<(long Id, XYZ Point)>();
                        cells.Add(key, points);
                    }
                    points.Add((item.Id, point));
                }
            }

            int added = 0;
            foreach (KeyValuePair<(int X, int Y, int Z), List<(long Id, XYZ Point)>> cell in cells)
            {
                for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                for (int dz = -1; dz <= 1; dz++)
                {
                    var neighborKey = (cell.Key.X + dx, cell.Key.Y + dy, cell.Key.Z + dz);
                    if (!cells.TryGetValue(neighborKey, out List<(long Id, XYZ Point)>? others)) continue;
                    foreach ((long firstId, XYZ firstPoint) in cell.Value)
                    foreach ((long secondId, XYZ secondPoint) in others)
                    {
                        if (firstId >= secondId || adjacency[firstId].Contains(secondId)) continue;
                        if (firstPoint.DistanceTo(secondPoint) * firstPoint.DistanceTo(secondPoint) > maxDistanceSquared) continue;
                        adjacency[firstId].Add(secondId);
                        adjacency[secondId].Add(firstId);
                        added++;
                    }
                }
            }
            return added;
        }

        private static List<List<long>> FindGroups(Dictionary<long, HashSet<long>> adjacency)
        {
            var visited = new HashSet<long>();
            var groups = new List<List<long>>();
            foreach (long start in adjacency.Keys.OrderBy(id => id))
            {
                if (!visited.Add(start)) continue;
                var pending = new Stack<long>();
                pending.Push(start);
                var group = new List<long>();
                while (pending.Count > 0)
                {
                    long current = pending.Pop();
                    group.Add(current);
                    foreach (long neighbor in adjacency[current].OrderByDescending(id => id))
                    {
                        if (visited.Add(neighbor)) pending.Push(neighbor);
                    }
                }
                groups.Add(group.OrderBy(id => id).ToList());
            }
            return groups;
        }

        private static List<List<long>> SplitStableGroups(List<List<long>> groups, List<List<long>> connectorGroups, Dictionary<long, Element> elements, Dictionary<long, ElementData> data)
        {
            var connectorGroupById = connectorGroups.SelectMany((group, index) => group.Select(id => (id, index))).ToDictionary(item => item.id, item => item.index);
            var result = new List<List<long>>();
            foreach (List<long> group in groups)
            {
                var partitions = group.GroupBy(id => connectorGroupById[id]).Select(part => part.ToList()).ToList();
                if (partitions.Count <= 1)
                {
                    result.Add(group);
                    continue;
                }
                var marks = partitions.Select(part => part.Select(id => GetText(GetInstanceParameter(elements[id], MarkName)))
                    .Where(value => !string.IsNullOrWhiteSpace(value)).Select(NormalizeMark).Distinct(StringComparer.OrdinalIgnoreCase).ToHashSet(StringComparer.OrdinalIgnoreCase)).ToList();
                var sequences = partitions.Select(part => part.Select(id => ParseSequence(GetText(GetInstanceParameter(elements[id], SequenceName))))
                    .Where(value => value.HasValue).Select(value => value!.Value).Distinct().ToHashSet()).ToList();
                bool stableSplit = marks.Count(set => set.Count > 0) > 1 && marks.SelectMany(set => set).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1
                    && sequences.Count(set => set.Count > 0) > 1 && sequences.SelectMany(set => set).Distinct().Count() > 1;
                bool hiddenDuplicate = false;
                var connectorGroupsByMark = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
                for (int index = 0; index < marks.Count; index++)
                {
                    foreach (string mark in marks[index])
                    {
                        if (!connectorGroupsByMark.TryGetValue(mark, out List<int>? indexes))
                        {
                            indexes = new List<int>();
                            connectorGroupsByMark.Add(mark, indexes);
                        }
                        indexes.Add(index);
                    }
                }
                foreach (List<int> duplicateGroups in connectorGroupsByMark.Values.Where(indexes => indexes.Count > 1))
                {
                    for (int first = 0; first < duplicateGroups.Count && !hiddenDuplicate; first++)
                    for (int second = first + 1; second < duplicateGroups.Count && !hiddenDuplicate; second++)
                    {
                        if (!ConnectorGroupsHaveProximity(partitions[duplicateGroups[first]], partitions[duplicateGroups[second]], data))
                        {
                            hiddenDuplicate = true;
                        }
                    }
                }
                if (stableSplit || hiddenDuplicate) result.AddRange(partitions);
                else result.Add(group);
            }
            return result;
        }

        private static bool ConnectorGroupsHaveProximity(List<long> firstGroup, List<long> secondGroup, Dictionary<long, ElementData> data)
        {
            double maximumDistance = MaximumProximityMm / 304.8;
            foreach (long firstId in firstGroup)
            {
                if (!data.TryGetValue(firstId, out ElementData? first)) continue;
                foreach (long secondId in secondGroup)
                {
                    if (!data.TryGetValue(secondId, out ElementData? second)) continue;
                    foreach (XYZ firstPoint in first.ConnectorOrigins)
                    foreach (XYZ secondPoint in second.ConnectorOrigins)
                    {
                        if (firstPoint.DistanceTo(secondPoint) <= maximumDistance) return true;
                    }
                }
            }
            return false;
        }

        private static int AddStandaloneTrays(List<List<long>> groups, List<Element> allElements)
        {
            var included = groups.SelectMany(group => group).ToHashSet();
            int added = 0;
            foreach (Element tray in allElements.Where(element => IsCategory(element, BuiltInCategory.OST_CableTray)))
            {
                if (included.Add(tray.Id.Value))
                {
                    groups.Add(new List<long> { tray.Id.Value });
                    added++;
                }
            }
            return added;
        }

        private static Dictionary<int, int?> ReadGroupSequences(List<List<long>> groups, Dictionary<long, Element> elements, HashSet<string> lockedMarks, List<string> log)
        {
            var sequences = new Dictionary<int, int?>();
            var markOwners = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
            for (int index = 0; index < groups.Count; index++)
            {
                var existing = groups[index].Select(id => ParseSequence(GetText(GetInstanceParameter(elements[id], SequenceName))))
                    .Where(value => value.HasValue).Select(value => value!.Value).Distinct().ToList();
                sequences[index] = existing.Count == 1 ? existing[0] : null;
                if (existing.Count > 1) log.Add("Gruppe " + (index + 1).ToString(CultureInfo.InvariantCulture) + " har motstridende sekvensverdier og må nummereres på nytt.");
                foreach (long id in groups[index])
                {
                    string mark = GetText(GetInstanceParameter(elements[id], MarkName));
                    if (string.IsNullOrWhiteSpace(mark)) continue;
                    string key = NormalizeMark(mark);
                    if (lockedMarks.Contains(key)) sequences[index] = null;
                    if (!markOwners.TryGetValue(key, out List<int>? owners)) markOwners[key] = owners = new List<int>();
                    if (!owners.Contains(index)) owners.Add(index);
                }
            }
            foreach (List<int> owners in markOwners.Values.Where(values => values.Count > 1))
            {
                foreach (int duplicateGroup in owners.Skip(1)) sequences[duplicateGroup] = null;
                log.Add("Duplisert eksisterende merkestreng på flere grupper; senere gruppe får ny sekvens.");
            }
            return sequences;
        }

        private static void FillSequenceGaps(List<List<long>> groups, Dictionary<int, int?> sequences, List<string> log)
        {
            var kept = sequences.Where(pair => pair.Value.HasValue).Select(pair => (Index: pair.Key, Value: pair.Value!.Value)).ToList();
            if (kept.Count == 0) return;
            int minimum = kept.Min(pair => pair.Value);
            int maximum = kept.Max(pair => pair.Value);
            var used = kept.Select(pair => pair.Value).ToHashSet();
            int gaps = Enumerable.Range(minimum, maximum - minimum + 1).Count(number => !used.Contains(number));
            foreach (var donor in kept.OrderByDescending(pair => pair.Value).Take(gaps)) sequences[donor.Index] = null;
            if (gaps > 0) log.Add("Sekvensgap lukkes ved å nummerere " + gaps.ToString(CultureInfo.InvariantCulture) + " grupper på nytt.");
        }

        private static Dictionary<int, int> AssignSequences(List<List<long>> groups, Dictionary<int, int?> sequences, Dictionary<long, Element> elements, HashSet<string> lockedMarks, List<string> log)
        {
            var assignments = new Dictionary<int, int>();
            var used = sequences.Where(pair => pair.Value.HasValue).Select(pair => pair.Value!.Value).ToHashSet();
            var blockedMarks = new HashSet<string>(lockedMarks, StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<int, int?> pair in sequences.Where(pair => pair.Value.HasValue)) assignments[pair.Key] = pair.Value!.Value;
            foreach (int groupIndex in assignments.Keys)
            {
                foreach (long id in groups[groupIndex])
                {
                    string mark = GetText(GetInstanceParameter(elements[id], MarkName));
                    if (!string.IsNullOrWhiteSpace(mark)) blockedMarks.Add(NormalizeMark(mark));
                }
            }

            int next = FindNextFree(used, 1);
            for (int index = 0; index < groups.Count; index++)
            {
                if (assignments.ContainsKey(index)) continue;
                int candidate = FindNextFree(used, Math.Min(next, used.Count == 0 ? 1 : used.Max() + 1));
                Element representative = elements[groups[index][0]];
                (string Area, string System, string Function) values = GetMarkParts(representative);
                if (values.Area.Length > 0 && values.System.Length > 0 && values.Function.Length > 0)
                {
                    while (blockedMarks.Contains(NormalizeMark(BuildMark(values, candidate)))) candidate = FindNextFree(used, candidate + 1);
                    blockedMarks.Add(NormalizeMark(BuildMark(values, candidate)));
                }
                assignments[index] = candidate;
                used.Add(candidate);
                next = candidate + 1;
            }
            log.Add("Gruppeplanen tildelte sekvensnummer for " + assignments.Count.ToString(CultureInfo.InvariantCulture) + " grupper.");
            return assignments;
        }

        private static int FindNextFree(HashSet<int> used, int start)
        {
            int candidate = Math.Max(1, start);
            while (used.Contains(candidate)) candidate++;
            return candidate;
        }

        private static Dictionary<long, string> ResolveAreas(Document document, Dictionary<long, Element> elements, List<string> log)
        {
            var areas = new Dictionary<long, string>();
            IList<Element> spaces = new FilteredElementCollector(document)
                .OfCategory(BuiltInCategory.OST_MEPSpaces)
                .WhereElementIsNotElementType()
                .ToElements();
            foreach (KeyValuePair<long, Element> pair in elements)
            {
                string existing = GetText(GetInstanceParameter(pair.Value, AreaName));
                if (IsValid(existing))
                {
                    areas[pair.Key] = existing;
                    continue;
                }
                string? resolved = IsConnectorAreaFitting(pair.Value)
                    ? ResolveAreaFromConnectedTrays(pair.Value, log)
                    : ResolveAreaFromSpace(pair.Value, spaces);
                if (IsValid(resolved)) areas[pair.Key] = resolved!;
            }
            return areas;
        }

        private static string? ResolveAreaFromConnectedTrays(Element fitting, List<string> log)
        {
            var values = GetConnectedStraightTrayValues(fitting, AreaName, out string reason);
            var valid = values.Where(pair => IsValid(pair.Value)).Select(pair => pair.Value).Distinct(StringComparer.Ordinal).ToList();
            if (valid.Count == 1) return valid[0];
            if (valid.Count > 1) log.Add("UAVKLART ElementId " + fitting.Id.Value + ": tilkoblede rettstrekk har motstridende FOB_Omraade.");
            else if (!string.IsNullOrEmpty(reason)) log.Add("UAVKLART ElementId " + fitting.Id.Value + ": " + reason);
            return null;
        }

        private static string? ResolveAreaFromSpace(Element element, IList<Element> spaces)
        {
            XYZ? point = GetPlacementPoint(element);
            if (point is null) return null;
            foreach (Element space in spaces)
            {
                try
                {
                    if (space is not Autodesk.Revit.DB.Mechanical.Space mechanicalSpace || !mechanicalSpace.IsPointInSpace(point)) continue;
                    foreach (string name in new[] { "FOB_Område", AreaName })
                    {
                        string value = GetText(GetInstanceParameter(space, name));
                        if (IsValid(value)) return value;
                    }
                }
                catch { }
            }
            return null;
        }

        private static IEnumerable<PlannedWrite> PlanValues(Document document, List<List<long>> groups, Dictionary<long, Element> elements, Dictionary<int, int> assignments, Dictionary<long, string> areas, HashSet<string> scheduled, List<string> log)
        {
            var planned = new List<PlannedWrite>();
            foreach (KeyValuePair<int, int> group in assignments.OrderBy(pair => pair.Key))
            {
                foreach (long id in groups[group.Key])
                {
                    Element element = elements[id];
                    string sequence = group.Value.ToString("D" + SequenceDigits.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
                    Parameter? sequenceParameter = GetInstanceParameter(element, SequenceName);
                    AddWrite(element, sequenceParameter, sequence, "sekvens fra connectorgruppe", planned, scheduled, log);
                    if (areas.TryGetValue(id, out string? area))
                    {
                        AddWrite(element, GetInstanceParameter(element, AreaName), area, "område fra Space eller tilkoblet rett kabelbro", planned, scheduled, log);
                    }
                    (string Area, string System, string Function) parts = GetMarkParts(element, areas);
                    if (parts.Area.Length == 0 || parts.System.Length == 0 || parts.Function.Length == 0)
                    {
                        log.Add("UAVKLART ElementId " + id.ToString(CultureInfo.InvariantCulture) + ": FOB_Merkestreng mangler område, type-system eller type-funksjonskode.");
                        continue;
                    }
                    string mark = BuildMark(parts, group.Value);
                    Parameter? markParameter = GetInstanceParameter(element, MarkName) ?? GetTypeParameter(element, MarkName);
                    AddWrite(element, markParameter, mark, "FOB_Omraade-FOB_System-FOB_Funksjonskode-sekvens", planned, scheduled, log);
                }
            }
            return planned;
        }

        private static void AddWrite(Element owner, Parameter? parameter, string value, string reason, List<PlannedWrite> writes, HashSet<string> scheduled, List<string> log)
        {
            if (parameter is null)
            {
                log.Add("UAVKLART ElementId " + owner.Id.Value.ToString(CultureInfo.InvariantCulture) + ", parameter: målparameter mangler.");
                return;
            }
            if (parameter.StorageType != StorageType.String && parameter.Definition.Name != SequenceName)
            {
                log.Add("UAVKLART ElementId " + owner.Id.Value.ToString(CultureInfo.InvariantCulture) + ", " + parameter.Definition.Name + ": forventet tekstparameter.");
                return;
            }
            if (parameter.IsReadOnly)
            {
                log.Add("UAVKLART ElementId " + owner.Id.Value.ToString(CultureInfo.InvariantCulture) + ", " + parameter.Definition.Name + ": parameteren er skrivebeskyttet.");
                return;
            }
            string oldValue = GetText(parameter);
            if (string.Equals(oldValue, value, StringComparison.Ordinal)) return;
            string key = owner.Id.Value.ToString(CultureInfo.InvariantCulture) + "|" + parameter.Definition.Name;
            if (scheduled.Add(key)) writes.Add(new PlannedWrite(owner, parameter, value, reason));
        }

        private static (string Area, string System, string Function) GetMarkParts(Element element, Dictionary<long, string>? plannedAreas = null)
        {
            string area = plannedAreas is not null && plannedAreas.TryGetValue(element.Id.Value, out string? plannedArea)
                ? plannedArea
                : GetText(GetInstanceParameter(element, AreaName));
            string system = GetText(GetTypeParameter(element, "FOB_System"));
            string function = GetText(GetTypeParameter(element, "FOB_Funksjonskode"));
            bool cableTray = IsCategory(element, BuiltInCategory.OST_CableTray)
                || IsCategory(element, BuiltInCategory.OST_CableTrayFitting);
            if (cableTray && !IsValid(system)) system = "460";
            if (cableTray && !IsValid(function)) function = "KAF";
            return (IsValid(area) ? area : string.Empty, IsValid(system) ? system : string.Empty, IsValid(function) ? function : string.Empty);
        }

        private static string BuildMark((string Area, string System, string Function) parts, int sequence)
        {
            return parts.Area + "-" + parts.System + "-" + parts.Function + "-" + sequence.ToString("D" + SequenceDigits.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        }

        private static Dictionary<string, string> LoadRegistryStatuses(List<string> log)
        {
            var statuses = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!File.Exists(RegistryPath)) return statuses;
            try
            {
                foreach (string line in File.ReadLines(RegistryPath).Skip(1))
                {
                    string[] parts = line.Split(';');
                    if (parts.Length >= 2 && !string.IsNullOrWhiteSpace(parts[0]))
                    {
                        statuses[NormalizeMark(parts[0])] = parts[1].Trim().ToUpperInvariant();
                    }
                }
            }
            catch (Exception exception)
            {
                log.Add("UAVKLART: merkestrengregisteret kunne ikke leses: " + exception.Message);
            }
            return statuses;
        }

        private static Dictionary<long, string> GetConnectedStraightTrayValues(Element fitting, string parameterName, out string reason)
        {
            reason = string.Empty;
            var values = new Dictionary<long, string>();
            foreach (Connector connector in GetConnectors(fitting))
            {
                if (connector.ConnectorType != ConnectorType.End) continue;
                foreach (Connector reference in connector.AllRefs)
                {
                    Element source = reference.Owner;
                    if (source.Id.Value == fitting.Id.Value || reference.ConnectorType != ConnectorType.End || !IsCategory(source, BuiltInCategory.OST_CableTray)) continue;
                    if (!connector.IsConnectedTo(reference) || !reference.IsConnectedTo(connector)) continue;
                    if (source.Location is not LocationCurve location || location.Curve is not Line) continue;
                    values[source.Id.Value] = GetText(GetInstanceParameter(source, parameterName));
                }
            }
            return values;
        }

        private static bool IsConnectorAreaFitting(Element element)
        {
            if (!IsCategory(element, BuiltInCategory.OST_CableTrayFitting) || element is not FamilyInstance instance) return false;
            Parameter? part = instance.Symbol.Family.get_Parameter(BuiltInParameter.FAMILY_CONTENT_PART_TYPE);
            return part is not null && part.StorageType == StorageType.Integer && Enum.IsDefined(typeof(PartType), part.AsInteger())
                && AreaConnectorPartTypes.Contains((PartType)part.AsInteger());
        }

        private static List<Connector> GetConnectors(Element element)
        {
            ConnectorSet? set = null;
            if (element is MEPCurve curve) set = curve.ConnectorManager?.Connectors;
            else if (element is FamilyInstance instance) set = instance.MEPModel?.ConnectorManager?.Connectors;
            return set?.Cast<Connector>().ToList() ?? new List<Connector>();
        }

        private static XYZ[] GetConnectorOrigins(Element element)
        {
            return GetConnectors(element).Select(connector => connector.Origin).ToArray();
        }

        private static XYZ? GetPlacementPoint(Element element)
        {
            if (element.Location is LocationPoint point) return point.Point;
            if (element.Location is LocationCurve curve && curve.Curve is not null) return curve.Curve.Evaluate(0.5, true);
            BoundingBoxXYZ? box = element.get_BoundingBox(null);
            return box is null ? null : (box.Min + box.Max) * 0.5;
        }

        private static Parameter? GetInstanceParameter(Element element, string name) => element.LookupParameter(name);

        private static Parameter? GetTypeParameter(Element element, string name)
        {
            ElementId typeId = element.GetTypeId();
            Element? type = typeId.Value < 0 ? null : element.Document.GetElement(typeId);
            return type?.LookupParameter(name);
        }

        private static string GetText(Parameter? parameter)
        {
            if (parameter is null || !parameter.HasValue) return string.Empty;
            if (parameter.StorageType == StorageType.String) return parameter.AsString()?.Trim() ?? string.Empty;
            return parameter.AsValueString()?.Trim() ?? string.Empty;
        }

        private static int? ParseSequence(string value)
        {
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number) && number > 0 ? number : null;
        }

        private static bool IsMissing(string value) => string.IsNullOrWhiteSpace(value) || string.Equals(value.Trim(), "--", StringComparison.Ordinal);
        private static bool IsValid(string? value) => !string.IsNullOrWhiteSpace(value) && !string.Equals(value.Trim(), "--", StringComparison.Ordinal);
        private static string NormalizeMark(string value) => value.Trim().ToUpperInvariant();
        private static bool IsCategory(Element element, BuiltInCategory category) => element.Category?.Id.Value == (long)category;
    }
}