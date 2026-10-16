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

namespace CW.Assistant.Generated;

public sealed class Rule12HideDuplicateCableTrays
{
    private const string BasTitle = "U_F_BAS_FBU_RIE_XXX";
    private const string BasPath = "Autodesk Docs://Fornebubanen/U_F_BAS_FBU_RIE_XXX.rvt";
    private const string TargetViewName = "KS Kablebro dRofus";
    private const string MarkParameterName = "FOB_Merkestreng";
    private const string MissingMark = "--";
    private const double MillimetersPerFoot = 304.8;
    private const string ReportDirectory = @"D:\Revit\Python\Revit_BIM_Agent\logs\reports";
    private const string HistoryPath = @"D:\Revit\Python\Revit_BIM_Agent\logs\history\Rule12_CableTrayMarkView.log";

    private sealed class MarkedTray
    {
        internal Element Element { get; }
        internal string Mark { get; }
        internal double Length { get; }

        internal MarkedTray(Element element, string mark, double length)
        {
            Element = element;
            Mark = mark;
            Length = length;
        }
    }

    private sealed class DuplicateGroup
    {
        internal string Mark { get; }
        internal MarkedTray Kept { get; }
        internal List<MarkedTray> Hidden { get; }

        internal DuplicateGroup(string mark, MarkedTray kept, List<MarkedTray> hidden)
        {
            Mark = mark;
            Kept = kept;
            Hidden = hidden;
        }
    }

    public string Execute(UIApplication uiApplication, Document? document, string viewName, bool previewOnly, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Document? activeUiDocument = uiApplication.ActiveUIDocument?.Document;
        if (document is null || activeUiDocument is null
            || !string.Equals(document.Title, BasTitle, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(document.PathName, BasPath, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(activeUiDocument.PathName, document.PathName, StringComparison.OrdinalIgnoreCase))
        {
            return "STOPP: aktiv BAS-modell kunne ikke bekreftes.";
        }

        if (!string.Equals(viewName?.Trim(), TargetViewName, StringComparison.Ordinal))
        {
            return "STOPP: Rule12Runner er avgrenset til viewet '" + TargetViewName + "'.";
        }
        View3D[] views = new FilteredElementCollector(document)
            .OfClass(typeof(View3D))
            .ToElements()
            .OfType<View3D>()
            .Where(view => !view.IsTemplate && string.Equals(view.Name, viewName.Trim(), StringComparison.Ordinal))
            .ToArray();
        if (views.Length != 1) return "STOPP: forventet ett 3D-view med navn '" + viewName + "', fant " + views.Length.ToString(CultureInfo.InvariantCulture) + ".";

        View3D view3D = views[0];
        List<Element> visibleTrays = new FilteredElementCollector(document, view3D.Id)
            .OfCategory(BuiltInCategory.OST_CableTray)
            .WhereElementIsNotElementType()
            .ToElements()
            .ToList();

        var markedTrays = new List<MarkedTray>();
        int placeholderCount = 0;
        int invalidParameterCount = 0;
        int invalidCurveCount = 0;
        foreach (Element element in visibleTrays)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IList<Parameter> parameters = element.GetParameters(MarkParameterName);
            if (parameters.Count != 1 || parameters[0].StorageType != StorageType.String)
            {
                invalidParameterCount++;
                continue;
            }

            string mark = (parameters[0].AsString() ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(mark) || string.Equals(mark, MissingMark, StringComparison.Ordinal))
            {
                placeholderCount++;
                continue;
            }

            if (element.Location is not LocationCurve locationCurve || locationCurve.Curve is null || locationCurve.Curve.Length <= 0.0)
            {
                invalidCurveCount++;
                continue;
            }

            double length = locationCurve.Curve.Length;
            markedTrays.Add(new MarkedTray(element, mark, length));
        }

        if (invalidParameterCount > 0)
        {
            return "STOPP: " + invalidParameterCount.ToString(CultureInfo.InvariantCulture)
                + " kabelbroer har manglende, duplisert eller feiltypet FOB_Merkestreng; ingen elementer ble skjult.";
        }
        if (invalidCurveCount > 0)
        {
            return "STOPP: " + invalidCurveCount.ToString(CultureInfo.InvariantCulture)
                + " kabelbroer mangler gyldig kurvelengde; ingen elementer ble skjult.";
        }

        List<DuplicateGroup> duplicateGroups = markedTrays
            .GroupBy(tray => tray.Mark, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group =>
            {
                List<MarkedTray> ordered = group
                    .OrderByDescending(tray => tray.Length)
                    .ThenBy(tray => tray.Element.Id.Value)
                    .ToList();
                return new DuplicateGroup(group.Key, ordered[0], ordered.Skip(1).ToList());
            })
            .ToList();
        List<ElementId> idsToHide = duplicateGroups.SelectMany(group => group.Hidden).Select(tray => tray.Element.Id).ToList();
        var log = new List<string>
        {
            "Rule12Runner | " + DateTime.Now.ToString("O", CultureInfo.InvariantCulture),
            "Modell=" + document.Title + " | " + document.PathName,
            "View=" + view3D.Name + " | ViewId=" + view3D.Id.Value.ToString(CultureInfo.InvariantCulture),
            "Kategori=OST_CableTray | parameter=" + MarkParameterName + " | blank/---verdier beholdes synlige.",
            "Representant per eksakt merkestreng: lengste kurvesegment; laveste ElementId ved lik lengde.",
            "Synlige kabelbroer=" + visibleTrays.Count.ToString(CultureInfo.InvariantCulture)
                + "; gyldige merkestrenger=" + markedTrays.Count.ToString(CultureInfo.InvariantCulture)
                + "; unike merkestrenger=" + markedTrays.Select(tray => tray.Mark).Distinct(StringComparer.Ordinal).Count().ToString(CultureInfo.InvariantCulture)
                + "; duplikatgrupper=" + duplicateGroups.Count.ToString(CultureInfo.InvariantCulture)
                + "; planlagt skjult=" + idsToHide.Count.ToString(CultureInfo.InvariantCulture)
                + "; blank/--=" + placeholderCount.ToString(CultureInfo.InvariantCulture)
                + "; ugyldig parameter=" + invalidParameterCount.ToString(CultureInfo.InvariantCulture)
                + "; ugyldig kurve=" + invalidCurveCount.ToString(CultureInfo.InvariantCulture),
            "Modus=" + (previewOnly ? "forhåndsvisning, ingen endring" : "skriver visningsskjul")
        };

        foreach (DuplicateGroup group in duplicateGroups)
        {
            log.Add(string.Format(
                CultureInfo.InvariantCulture,
                "MERKE '{0}' behold ElementId {1} ({2:F0} mm); skjul {3}.",
                EscapeLog(group.Mark),
                group.Kept.Element.Id.Value,
                group.Kept.Length * MillimetersPerFoot,
                string.Join(",", group.Hidden.Select(tray => tray.Element.Id.Value.ToString(CultureInfo.InvariantCulture)))));
        }

        if (previewOnly || idsToHide.Count == 0) return SaveAndReturn(log, "Rule12Runner: " + idsToHide.Count.ToString(CultureInfo.InvariantCulture) + " duplikater planlagt skjult.");
        if (idsToHide.Any(id => document.GetElement(id) is not Element element || !element.CanBeHidden(view3D)))
        {
            return SaveAndReturn(log, "STOPP: minst ett duplikatelement kan ikke skjules i viewet; ingen endring utført.");
        }

        using var transactionGroup = new TransactionGroup(document, "Vis én kabelbro per unik merkestreng");
        TransactionStatus groupStart = transactionGroup.Start();
        if (groupStart != TransactionStatus.Started) return SaveAndReturn(log, "STOPP: TransactionGroup startet ikke (" + groupStart + "); viewet er uendret.");

        try
        {
            using (var transaction = new Transaction(document, "Skjul dupliserte kabelbroer i " + view3D.Name))
            {
                TransactionStatus start = transaction.Start();
                if (start != TransactionStatus.Started)
                {
                    transactionGroup.RollBack();
                    return SaveAndReturn(log, "STOPP: transaksjonen startet ikke (" + start + "); viewet er uendret.");
                }

                view3D.HideElements(idsToHide);
                document.Regenerate();
                TransactionStatus commit = transaction.Commit();
                if (commit != TransactionStatus.Committed) throw new InvalidOperationException("Transaksjonen ble ikke committed: " + commit);
            }

            List<Element> after = new FilteredElementCollector(document, view3D.Id)
                .OfCategory(BuiltInCategory.OST_CableTray)
                .WhereElementIsNotElementType()
                .ToElements()
                .ToList();
            int remainingDuplicates = after
                .Select(element => element.GetParameters(MarkParameterName))
                .Where(parameters => parameters.Count == 1 && parameters[0].StorageType == StorageType.String)
                .Select(parameters => (parameters[0].AsString() ?? string.Empty).Trim())
                .Where(mark => !string.IsNullOrWhiteSpace(mark) && !string.Equals(mark, MissingMark, StringComparison.Ordinal))
                .GroupBy(mark => mark, StringComparer.Ordinal)
                .Count(group => group.Count() > 1);
            if (remainingDuplicates > 0)
            {
                TransactionStatus rollback = transactionGroup.RollBack();
                log.Add("ETTERKONTROLL: " + remainingDuplicates.ToString(CultureInfo.InvariantCulture) + " duplikatgrupper gjenstår; TransactionGroup=" + rollback + ".");
                string rollbackResult = rollback == TransactionStatus.RolledBack ? "endringen ble tilbakeført" : "tilbakeføring er ikke bekreftet";
                return SaveAndReturn(log, "Etterkontroll feilet; " + rollbackResult + " (" + rollback + ").");
            }

            TransactionStatus assimilate = transactionGroup.Assimilate();
            if (assimilate != TransactionStatus.Committed)
            {
                return SaveAndReturn(log, "Etterkontroll bestod, men TransactionGroup-status er " + assimilate + ".");
            }

            log.Add("ETTERKONTROLL: synlig kabelbroer=" + after.Count.ToString(CultureInfo.InvariantCulture)
                + "; gjenstående duplikatgrupper=" + remainingDuplicates.ToString(CultureInfo.InvariantCulture)
                + "; TransactionGroup=Committed.");
            return SaveAndReturn(log, "Rule12Runner: skjulte og verifiserte " + idsToHide.Count.ToString(CultureInfo.InvariantCulture)
                + " duplikater i viewet '" + view3D.Name + "'; ingen modellelementer slettet.");
        }
        catch (Exception exception)
        {
            if (transactionGroup.GetStatus() == TransactionStatus.Started) transactionGroup.RollBack();
            log.Add("FEIL=" + exception);
            return SaveAndReturn(log, "Rule12Runner feilet; transaksjonsgruppe tilbakeført der mulig: " + exception.Message);
        }
    }

    private static string EscapeLog(string value) => value.Replace("\r", " ").Replace("\n", " ");

    private static string SaveAndReturn(List<string> log, string result)
    {
        string reportResult;
        try
        {
            Directory.CreateDirectory(ReportDirectory);
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
            string path = Path.Combine(ReportDirectory, "Rule12 kabelbro view " + timestamp + ".txt");
            int suffix = 1;
            while (File.Exists(path)) path = Path.Combine(ReportDirectory, "Rule12 kabelbro view " + timestamp + "_" + (suffix++).ToString(CultureInfo.InvariantCulture) + ".txt");
            File.WriteAllLines(path, log, new UTF8Encoding(false));
            reportResult = "Rapport=" + path;
        }
        catch (Exception exception)
        {
            reportResult = "Rapportfeil=" + exception.Message;
        }

        string historyResult;
        try
        {
            File.AppendAllText(HistoryPath, string.Join(Environment.NewLine, log) + Environment.NewLine, new UTF8Encoding(false));
            historyResult = "Historikk=" + HistoryPath;
        }
        catch (Exception exception)
        {
            historyResult = "Historikkfeil=" + exception.Message;
        }

        return result + " " + reportResult + " " + historyResult;
    }
}