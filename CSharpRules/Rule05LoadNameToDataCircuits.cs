#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.UI;

namespace CW.Assistant.Generated
{
    internal sealed class GeneratedAction
    {
        private const string ScriptVersion = "0.0.1";
        private const string FamilyPrefix = "Spredenett";
        private const string MarkerParameter = "FOB_Merkestreng";
        private const string RoomNumberParameter = "dRofus_RoomfunctionNo";
        private const string RoomNameParameter = "Romnavn";
        private const string LoadNameParameter = "Load Name";
        private const string McParameter = "MC Object Variable 1";
        private const string RequiredMcText = "I fordeling";
        private const string LogPath = @"D:\Revit\Python\Revit_BIM_Agent\logs\history\Rule05_LoadNameToDataCircuits.log";
        private const double SearchRadiusFeet = 50.0 / 304.8;
        private const double PriorityFallbackFeet = 1500.0 / 304.8;
        private const int MaxExecutionSeconds = 120;
        private static readonly string[] Phase1StrongFamilyTokens = { "automatikktavle port", "underfordeling ups", "hovedfordeling ups" };
        private static readonly string[] Phase1PreferredFamilyTokens = { "underfordeling", "automatikktavle", "datarack", "rack", "fordeling" };
        private static readonly string[] Phase1DeprioritizedFamilyTokens = { "brannalarm", "brannmannstabl", "detektor", "sirene", "psu", "sikkerhetsventilasjon", "roykavsug", "røykavsug" };
        private static readonly string[] Phase2ExcludedLoadTokens = { "BYG204-852-ASS-006" };
        private static readonly BuiltInCategory[] ConnectorCategories =
        {
            BuiltInCategory.OST_DataDevices,
            BuiltInCategory.OST_CommunicationDevices,
            BuiltInCategory.OST_ElectricalEquipment,
            BuiltInCategory.OST_FireAlarmDevices
        };

        private sealed class CandidateUpdate
        {
            internal ElectricalSystem Circuit { get; }
            internal string LoadName { get; }
            internal Element? Parent { get; }
            internal Element? Child { get; }
            internal Element? Device { get; }
            internal double DistanceFeet { get; }
            internal string Source { get; }

            internal CandidateUpdate(ElectricalSystem circuit, string loadName, Element? parent, Element? child, Element? device, double distanceFeet, string source)
            {
                Circuit = circuit;
                LoadName = loadName;
                Parent = parent;
                Child = child;
                Device = device;
                DistanceFeet = distanceFeet;
                Source = source;
            }
        }

        public string Execute(UIApplication uiApplication, Document? activeDocument)
        {
            if (activeDocument is null)
            {
                return "FEIL: Ingen aktiv Revit-modell. Regel 5 stoppet før elementlesing.";
            }

            var started = Stopwatch.StartNew();
            var log = new StringBuilder();
            var updates = new Dictionary<long, CandidateUpdate>();
            var lockedParentPhaseCircuits = new HashSet<long>();
            var circuitsByMember = new Dictionary<long, List<ElectricalSystem>>();
            int parentCount = 0;
            int parentNoCircuit = 0;
            int parentNoChild = 0;
            int parentNoMatch = 0;
            int dataDeviceNoCircuit = 0;
            int dataDeviceMissingData = 0;
            int dataDeviceSkipped = 0;
            int alreadyCorrect = 0;
            try
            {
                AppendLine(log, string.Format(CultureInfo.InvariantCulture, "=== Regel 5 v{0} | {1:O} ===", ScriptVersion, DateTime.Now));
                AppendLine(log, "Dokument: " + activeDocument.Title);
                List<Element> modelElectricalEquipment = new FilteredElementCollector(activeDocument)
                    .OfClass(typeof(FamilyInstance))
                    .WhereElementIsNotElementType()
                    .ToElements()
                    .Where(IsElectricalEquipment)
                    .ToList();

                foreach (ElectricalSystem circuit in new FilteredElementCollector(activeDocument).OfClass(typeof(ElectricalSystem)).Cast<ElectricalSystem>())
                {
                    CheckTimeout(started, "kursindeks");
                    foreach (Element member in circuit.Elements)
                    {
                        if (member is null) continue;
                        if (!circuitsByMember.TryGetValue(member.Id.Value, out List<ElectricalSystem>? memberCircuits))
                        {
                            memberCircuits = new List<ElectricalSystem>();
                            circuitsByMember.Add(member.Id.Value, memberCircuits);
                        }
                        memberCircuits.Add(circuit);
                    }
                }

                List<FamilyInstance> parents = new FilteredElementCollector(activeDocument)
                    .OfCategory(BuiltInCategory.OST_DataDevices)
                    .WhereElementIsNotElementType()
                    .OfType<FamilyInstance>()
                    .Where(IsSpredenettParent)
                    .OrderBy(element => element.Id.Value)
                    .ToList();
                parentCount = parents.Count;
                AppendLine(log, "SUMMARY: Spredenett-parents i modell: " + parentCount.ToString(CultureInfo.InvariantCulture));
                if (parents.Count == 0)
                {
                    throw new InvalidOperationException("Fant ingen Spredenett-parent i Data Devices med 'I fordeling' i parameter 'MC Object Variable 1'.");
                }

                foreach (FamilyInstance parent in parents)
                {
                    CheckTimeout(started, "parentfase");
                    if (!circuitsByMember.TryGetValue(parent.Id.Value, out List<ElectricalSystem>? parentCircuits) || parentCircuits.Count == 0)
                    {
                        parentNoCircuit++;
                        AppendLine(log, "SUMMARY: Parent uten kurs: " + parent.Id.Value.ToString(CultureInfo.InvariantCulture));
                        continue;
                    }

                    foreach (ElectricalSystem circuit in parentCircuits)
                    {
                        lockedParentPhaseCircuits.Add(circuit.Id.Value);
                    }

                    List<Element> circuitMembers = parentCircuits
                        .SelectMany(circuit => circuit.Elements.Cast<Element>())
                        .Where(element => element is not null)
                        .GroupBy(element => element.Id.Value)
                        .Select(group => group.First())
                        .ToList();
                    List<Element> children = GetChildCandidates(circuitMembers, parent);
                    string source = "circuit";
                    if (children.Count == 0)
                    {
                        children = GetChildCandidates(modelElectricalEquipment, parent);
                        source = "model";
                    }
                    if (children.Count == 0)
                    {
                        parentNoChild++;
                        AppendLine(log, "SUMMARY: Parent uten child-kandidat: " + parent.Id.Value.ToString(CultureInfo.InvariantCulture));
                        continue;
                    }

                    (Element? child, double distance) = FindNearestChild(parent, children);
                    if (child is null)
                    {
                        parentNoMatch++;
                        continue;
                    }

                    string parentFamily = GetFamilyName(parent);
                    string childFamily = GetFamilyName(child);
                    string marker = GetParameterText(child, MarkerParameter);
                    string room = GetRoomSuffix(parent);
                    if (string.IsNullOrEmpty(room)) room = GetRoomSuffix(child);
                    if (child.Id.Value == parent.Id.Value
                        || childFamily.Length == 0
                        || string.Equals(parentFamily, childFamily, StringComparison.OrdinalIgnoreCase)
                        || marker.Length == 0
                        || room.Length == 0
                        || IsParentLike(childFamily))
                    {
                        parentNoMatch++;
                        AppendLine(log, "SUMMARY: Parent uten gyldig child/romdata: " + parent.Id.Value.ToString(CultureInfo.InvariantCulture));
                        continue;
                    }

                    string loadName = BuildParentLoadName(childFamily, marker, room);
                    foreach (ElectricalSystem circuit in parentCircuits)
                    {
                        CheckTimeout(started, "parentkurs");
                        long circuitId = circuit.Id.Value;
                        if (string.Equals(GetCircuitLoadName(circuit), loadName, StringComparison.Ordinal))
                        {
                            alreadyCorrect++;
                            continue;
                        }

                        if (!updates.TryGetValue(circuitId, out CandidateUpdate? previous) || distance < previous.DistanceFeet)
                        {
                            updates[circuitId] = new CandidateUpdate(circuit, loadName, parent, child, null, distance, source);
                        }
                    }
                }

                List<FamilyInstance> connectorDevices = new List<FamilyInstance>();
                var seenConnectorIds = new HashSet<long>();
                foreach (BuiltInCategory category in ConnectorCategories)
                {
                    foreach (FamilyInstance instance in new FilteredElementCollector(activeDocument)
                        .OfCategory(category)
                        .WhereElementIsNotElementType()
                        .OfType<FamilyInstance>())
                    {
                        if (!IsSpredenettParent(instance) && seenConnectorIds.Add(instance.Id.Value))
                        {
                            connectorDevices.Add(instance);
                        }
                    }
                }

                foreach (FamilyInstance device in connectorDevices)
                {
                    CheckTimeout(started, "devicevalidering");
                    if (!circuitsByMember.TryGetValue(device.Id.Value, out List<ElectricalSystem>? connectedCircuits) || connectedCircuits.Count == 0)
                    {
                        dataDeviceNoCircuit++;
                        continue;
                    }

                    List<ElectricalSystem> dataCircuits = connectedCircuits
                        .GroupBy(circuit => circuit.Id.Value)
                        .Select(group => group.First())
                        .ToList();
                    if (dataCircuits.Count > 1)
                    {
                        List<ElectricalSystem> classifiedDataCircuits = dataCircuits.Where(IsDataCircuit).ToList();
                        if (classifiedDataCircuits.Count == 0)
                        {
                            dataDeviceSkipped++;
                            continue;
                        }
                        dataCircuits = classifiedDataCircuits;
                    }

                    bool isRack = IsElectricalEquipment(device) && GetFamilyName(device).IndexOf("datarack", StringComparison.OrdinalIgnoreCase) >= 0;
                    if (isRack)
                    {
                        string targetPanel = GetParameterText(device, MarkerParameter);
                        dataCircuits = dataCircuits.Where(circuit => IsSupplyCircuit(device, circuit, targetPanel)).ToList();
                        if (dataCircuits.Count == 0)
                        {
                            dataDeviceSkipped++;
                            continue;
                        }
                    }
                    else if (dataCircuits.Count > 1)
                    {
                        dataDeviceSkipped++;
                        continue;
                    }

                    string proposed = BuildDeviceLoadName(device, isRack);
                    if (proposed.Length == 0)
                    {
                        dataDeviceMissingData++;
                        continue;
                    }

                    foreach (ElectricalSystem circuit in dataCircuits)
                    {
                        long circuitId = circuit.Id.Value;
                        if (updates.ContainsKey(circuitId) || lockedParentPhaseCircuits.Contains(circuitId))
                        {
                            dataDeviceSkipped++;
                            continue;
                        }
                        string current = GetCircuitLoadName(circuit);
                        if (Phase2ExcludedLoadTokens.Any(token => current.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0))
                        {
                            dataDeviceSkipped++;
                            continue;
                        }
                        if (string.Equals(current, proposed, StringComparison.Ordinal))
                        {
                            alreadyCorrect++;
                            continue;
                        }
                        updates[circuitId] = new CandidateUpdate(circuit, proposed, null, null, device, 0.0, "data_device_validation");
                    }
                }

                AppendLine(log, "SUMMARY: Parent uten kurs: " + parentNoCircuit.ToString(CultureInfo.InvariantCulture));
                AppendLine(log, "SUMMARY: Parent uten child: " + parentNoChild.ToString(CultureInfo.InvariantCulture));
                AppendLine(log, "SUMMARY: Parent uten match: " + parentNoMatch.ToString(CultureInfo.InvariantCulture));
                AppendLine(log, "SUMMARY: Connector devices uten datakurs: " + dataDeviceNoCircuit.ToString(CultureInfo.InvariantCulture));
                AppendLine(log, "SUMMARY: Connector devices mangler markør/rom/familie: " + dataDeviceMissingData.ToString(CultureInfo.InvariantCulture));
                AppendLine(log, "SUMMARY: Connector devices hoppet over: " + dataDeviceSkipped.ToString(CultureInfo.InvariantCulture));
                AppendLine(log, "SUMMARY: Kurser allerede riktige: " + alreadyCorrect.ToString(CultureInfo.InvariantCulture));

                if (updates.Count > 0)
                {
                    using (var transaction = new Transaction(activeDocument, "Oppdater Load Name for datakurser"))
                    {
                        TransactionStatus startStatus = transaction.Start();
                        if (startStatus != TransactionStatus.Started)
                        {
                            throw new InvalidOperationException("Transaksjonen startet ikke: " + startStatus);
                        }
                        try
                        {
                            foreach (KeyValuePair<long, CandidateUpdate> item in updates.OrderBy(item => item.Key))
                            {
                                CheckTimeout(started, "transaksjon");
                                Parameter? parameter = GetLoadNameParameter(item.Value.Circuit);
                                if (parameter is null || parameter.IsReadOnly || parameter.StorageType != StorageType.String)
                                {
                                    throw new InvalidOperationException("Load Name mangler, er skrivebeskyttet eller er ikke tekst på kurs " + item.Key.ToString(CultureInfo.InvariantCulture));
                                }
                                if (!parameter.Set(item.Value.LoadName))
                                {
                                    throw new InvalidOperationException("Parameter.Set feilet for kurs " + item.Key.ToString(CultureInfo.InvariantCulture));
                                }
                            }
                            TransactionStatus commitStatus = transaction.Commit();
                            if (commitStatus != TransactionStatus.Committed)
                            {
                                throw new InvalidOperationException("Transaksjonen ble ikke committed: " + commitStatus);
                            }
                        }
                        catch
                        {
                            if (transaction.GetStatus() == TransactionStatus.Started) transaction.RollBack();
                            throw;
                        }
                    }
                }

                foreach (KeyValuePair<long, CandidateUpdate> item in updates.OrderBy(item => item.Key))
                {
                    CandidateUpdate update = item.Value;
                    AppendLine(log, string.Format(CultureInfo.InvariantCulture,
                        "OPPDATERT; Kurs ElementId={0}; Load Name={1}; Kilde={2}; Parent={3}; Child={4}; Device={5}",
                        item.Key, update.LoadName, update.Source, update.Parent?.Id.Value.ToString(CultureInfo.InvariantCulture) ?? "", update.Child?.Id.Value.ToString(CultureInfo.InvariantCulture) ?? "", update.Device?.Id.Value.ToString(CultureInfo.InvariantCulture) ?? ""));
                }
                AppendLine(log, "SUMMARY: Resultat: Load Name oppdatert på " + updates.Count.ToString(CultureInfo.InvariantCulture) + " kurs(er).");
                AppendLine(log, "SUMMARY: Varighet sekunder: " + started.Elapsed.TotalSeconds.ToString("F1", CultureInfo.InvariantCulture));
                return SaveLogAndReturn(log, activeDocument, updates.Count, alreadyCorrect, parentCount, dataDeviceNoCircuit, dataDeviceMissingData, dataDeviceSkipped);
            }
            catch (Exception exception)
            {
                AppendLine(log, "FEIL: " + exception);
                string path = SaveLog(log);
                return string.Format(CultureInfo.InvariantCulture,
                    "Regel 5 feilet i '{0}'. Ingen trygg videreføring ble gjort. Feil: {1}. Logg: {2}",
                    activeDocument.Title, exception.Message, path);
            }
        }

        private static List<Element> GetChildCandidates(IEnumerable<Element> elements, Element parent)
        {
            string parentFamily = GetFamilyName(parent);
            return elements.Where(element => element is FamilyInstance
                    && element.Id.Value != parent.Id.Value
                    && IsElectricalEquipment(element)
                    && !string.Equals(GetFamilyName(element), parentFamily, StringComparison.OrdinalIgnoreCase)
                    && !IsParentLike(GetFamilyName(element)))
                .GroupBy(element => element.Id.Value)
                .Select(group => group.First())
                .ToList();
        }

        private static (Element? Element, double Distance) FindNearestChild(Element parent, IList<Element> children)
        {
            (Element? Element, double Distance) nearest = (null, double.PositiveInfinity);
            foreach (Element child in children)
            {
                double? distance = GetDistanceFeet(parent, child);
                if (distance.HasValue && distance.Value < nearest.Distance)
                {
                    nearest = (child, distance.Value);
                }
            }
            if (nearest.Element is null) return (null, SearchRadiusFeet);

            if (IsParentLike(GetFamilyName(parent)) && GetFamilyPriority(GetFamilyName(nearest.Element)) >= 2)
            {
                (Element? Element, double Distance) preferred = (null, double.PositiveInfinity);
                foreach (Element child in children.Where(item => GetFamilyPriority(GetFamilyName(item)) <= 0))
                {
                    double? distance = GetDistanceFeet(parent, child);
                    if (distance.HasValue && distance.Value < preferred.Distance) preferred = (child, distance.Value);
                }
                if (preferred.Element is not null && preferred.Distance <= nearest.Distance + PriorityFallbackFeet)
                {
                    return preferred;
                }
            }
            return nearest;
        }

        private static string BuildParentLoadName(string childFamily, string marker, string room)
        {
            string formattedFamily = childFamily.IndexOf("UPS", StringComparison.Ordinal) >= 0 ? childFamily : LowercaseFirst(childFamily);
            if (childFamily.IndexOf("heis", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "Tilkoblet i " + formattedFamily + " " + marker + " - " + room;
            }
            return "Datakontakt i " + formattedFamily + " " + marker + " - " + room;
        }

        private static string BuildDeviceLoadName(Element device, bool isRack)
        {
            string room = GetRoomSuffix(device);
            if (room.Length == 0) return string.Empty;
            string family = isRack ? "Fibertilkobling datarack" : GetFamilyName(device);
            if (family.StartsWith(FamilyPrefix, StringComparison.OrdinalIgnoreCase)) return "Datakontakt - " + room;
            string marker = GetParameterText(device, MarkerParameter);
            if (family.Length == 0 || marker.Length == 0) return string.Empty;
            string[] parts = family.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 1)
            {
                for (int index = 1; index < parts.Length; index++) parts[index] = parts[index].ToLowerInvariant();
                family = string.Join(" ", parts);
            }
            return family + " " + marker + " - " + room;
        }

        private static bool IsSupplyCircuit(Element device, ElectricalSystem circuit, string targetPanel)
        {
            object? baseEquipment = GetPropertyValue(circuit, "BaseEquipment");
            object? baseId = GetPropertyValue(baseEquipment, "Id");
            object? deviceId = GetPropertyValue(device, "Id");
            object? baseValue = GetPropertyValue(baseId, "Value");
            object? deviceValue = GetPropertyValue(deviceId, "Value");
            if (baseValue is not null && deviceValue is not null)
            {
                return !Equals(baseValue, deviceValue);
            }
            string panel = new[] { "Panel", "Panel Name", "Electrical - Panel", "System Name" }
                .Select(name => GetParameterText(circuit, name))
                .FirstOrDefault(value => value.Length > 0) ?? string.Empty;
            return targetPanel.Length > 0 && panel.Length > 0
                && !string.Equals(panel, targetPanel, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsDataCircuit(ElectricalSystem circuit)
        {
            foreach (string propertyName in new[] { "SystemType", "ElectricalSystemType", "SystemClassification" })
            {
                object? value = GetPropertyValue(circuit, propertyName);
                if (value?.ToString()?.IndexOf("data", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            foreach (string name in new[] { "System Type", "System Classification", "Load Classification", "Type of System" })
            {
                if (GetParameterText(circuit, name).IndexOf("data", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return false;
        }

        private static Parameter? GetLoadNameParameter(ElectricalSystem circuit)
        {
            Parameter? parameter = circuit.get_Parameter(BuiltInParameter.RBS_ELEC_CIRCUIT_NAME);
            return parameter ?? circuit.LookupParameter(LoadNameParameter);
        }

        private static string GetCircuitLoadName(ElectricalSystem circuit)
        {
            Parameter? parameter = GetLoadNameParameter(circuit);
            if (parameter is null || !parameter.HasValue) return string.Empty;
            string? value = parameter.StorageType == StorageType.String ? parameter.AsString() : parameter.AsValueString();
            return (value ?? string.Empty).Trim();
        }

        private static string GetParameterText(Element element, string parameterName)
        {
            Parameter? parameter = element.LookupParameter(parameterName);
            if (parameter is null || !parameter.HasValue) return string.Empty;
            string? value = parameter.StorageType == StorageType.String ? parameter.AsString() : parameter.AsValueString();
            return (value ?? string.Empty).Trim();
        }

        private static string GetRoomSuffix(Element element)
        {
            string number = GetParameterText(element, RoomNumberParameter);
            string name = GetParameterText(element, RoomNameParameter);
            if (number.Length == 0 || name.Length == 0 || number == "--" || name == "--") return string.Empty;
            return number + " " + name;
        }

        private static string GetFamilyName(Element element)
        {
            return element is FamilyInstance instance ? (instance.Symbol?.Family?.Name ?? string.Empty).Trim() : string.Empty;
        }

        private static bool IsSpredenettParent(Element element)
        {
            return element is FamilyInstance
                && IsCategory(element, BuiltInCategory.OST_DataDevices)
                && GetFamilyName(element).StartsWith(FamilyPrefix, StringComparison.OrdinalIgnoreCase)
                && GetParameterText(element, McParameter).IndexOf(RequiredMcText, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsParentLike(string familyName) => familyName.StartsWith(FamilyPrefix, StringComparison.OrdinalIgnoreCase);

        private static int GetFamilyPriority(string familyName)
        {
            string normalized = familyName.ToLowerInvariant();
            if (Phase1StrongFamilyTokens.Any(token => normalized.Contains(token))) return -1;
            if (Phase1PreferredFamilyTokens.Any(token => normalized.Contains(token))) return 0;
            if (Phase1DeprioritizedFamilyTokens.Any(token => normalized.Contains(token))) return 2;
            return 1;
        }

        private static bool IsElectricalEquipment(Element element) => IsCategory(element, BuiltInCategory.OST_ElectricalEquipment);

        private static bool IsCategory(Element element, BuiltInCategory category)
        {
            return element.Category is not null && element.Category.Id.Value == (long)category;
        }

        private static XYZ? GetElementPoint(Element element)
        {
            if (element.Location is LocationPoint point) return point.Point;
            if (element.Location is LocationCurve curve) return curve.Curve.Evaluate(0.5, true);
            BoundingBoxXYZ? box = element.get_BoundingBox(null);
            return box is null ? null : (box.Min + box.Max) * 0.5;
        }

        private static double? GetDistanceFeet(Element first, Element second)
        {
            XYZ? firstPoint = GetElementPoint(first);
            XYZ? secondPoint = GetElementPoint(second);
            if (firstPoint is not null && secondPoint is not null) return firstPoint.DistanceTo(secondPoint);
            BoundingBoxXYZ? firstBox = first.get_BoundingBox(null);
            BoundingBoxXYZ? secondBox = second.get_BoundingBox(null);
            if (firstBox is null || secondBox is null) return null;
            double dx = Math.Max(0.0, Math.Max(firstBox.Min.X - secondBox.Max.X, secondBox.Min.X - firstBox.Max.X));
            double dy = Math.Max(0.0, Math.Max(firstBox.Min.Y - secondBox.Max.Y, secondBox.Min.Y - firstBox.Max.Y));
            double dz = Math.Max(0.0, Math.Max(firstBox.Min.Z - secondBox.Max.Z, secondBox.Min.Z - firstBox.Max.Z));
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        private static string LowercaseFirst(string value)
        {
            if (value.Length == 0) return value;
            return char.ToLowerInvariant(value[0]) + value.Substring(1);
        }

        private static object? GetPropertyValue(object? instance, string propertyName)
        {
            if (instance is null) return null;
            try { return instance.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public)?.GetValue(instance); }
            catch { return null; }
        }

        private static void CheckTimeout(Stopwatch stopwatch, string phase)
        {
            if (stopwatch.Elapsed.TotalSeconds > MaxExecutionSeconds)
            {
                throw new TimeoutException(string.Format(CultureInfo.InvariantCulture,
                    "Regel 5 avbrutt etter {0:F1}s (fase {1}, grense {2}s).", stopwatch.Elapsed.TotalSeconds, phase, MaxExecutionSeconds));
            }
        }

        private static void AppendLine(StringBuilder builder, string line) => builder.AppendLine(line);

        private static string SaveLog(StringBuilder log)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            File.AppendAllText(LogPath, log.ToString() + Environment.NewLine, new UTF8Encoding(false));
            return LogPath;
        }

        private static string SaveLogAndReturn(StringBuilder log, Document document, int updated, int unchanged, int parents, int noCircuit, int missingData, int skipped)
        {
            string path = SaveLog(log);
            return string.Format(CultureInfo.InvariantCulture,
                "Regel 5 v{0} i '{1}': oppdatert {2} kurser; allerede riktig {3}; parents {4}; enheter uten kurs {5}; mangler data {6}; hoppet over {7}. Logg: {8}",
                ScriptVersion, document.Title, updated, unchanged, parents, noCircuit, missingData, skipped, path);
        }
    }
}