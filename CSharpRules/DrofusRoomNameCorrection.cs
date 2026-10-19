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

public sealed class DrofusRoomNameCorrection
{
    private const string Model = "Autodesk Docs://Fornebubanen/U_F_BAS_FBU_RIE_XXX.rvt";
    private const string ReportRoot = @"D:\Revit\Python\Revit_BIM_Agent\logs\reports";
    private const long TargetId = 15674023;
    private const string TargetUniqueId = "231f187a-cecb-4eab-b1a8-083ce21ac82d-00ef2aa7";
    private const long SourceId = 15673873;
    private const string SourceUniqueId = "231f187a-cecb-4eab-b1a8-083ce21ac82d-00ef2a11";
    private const string SourceOccurrenceId = "809155";
    private const int RoomId = 564;
    private const string RoomNumber = "M3G2.03";
    private const string RoomName = "Verkstedhall";

    public string Run(UIApplication ui, Document? doc, CancellationToken token, bool apply)
    {
        if (doc is null || doc.PathName != Model || ui.ActiveUIDocument?.Document.PathName != Model)
            throw new InvalidOperationException("Feil aktiv BAS-modell.");
        token.ThrowIfCancellationRequested();

        Element target = doc.GetElement(new ElementId(TargetId))
            ?? throw new InvalidOperationException("Målelementet finnes ikke.");
        Element source = doc.GetElement(new ElementId(SourceId))
            ?? throw new InvalidOperationException("Kildeelementet finnes ikke.");
        if (target.UniqueId != TargetUniqueId || source.UniqueId != SourceUniqueId
            || target.Category?.Id.Value != (long)BuiltInCategory.OST_CableTray
            || source.Category?.Id.Value != (long)BuiltInCategory.OST_CableTray
            || Text(target, "FOB_Entreprise") != "K5B"
            || Text(source, "FOB_Entreprise") != "K5B"
            || Integer(target, "dRofus_RoomID") != RoomId
            || Integer(source, "dRofus_RoomID") != RoomId
            || Text(source, "drofus_occurrence_id") != SourceOccurrenceId
            || Text(target, "dRofus_RoomfunctionNo") != RoomNumber
            || Text(source, "dRofus_RoomfunctionNo") != RoomNumber
            || Text(source, "Romnavn") != RoomName)
            throw new InvalidOperationException("Mål, dokumentert kilde eller RoomID/romdata avviker fra kontrollert grunnlag.");

        string beforeName = Text(target, "Romnavn");
        if (!IsMissing(beforeName) && beforeName != RoomName)
            throw new InvalidOperationException("Målelementet har en annen eksisterende Romnavn-verdi; den bevares.");
        if (apply && beforeName != RoomName
            && (Parameter(target, "Romnavn").IsReadOnly
                || (doc.IsWorkshared && WorksharingUtils.GetCheckoutStatus(doc, target.Id) == CheckoutStatus.OwnedByOtherUser)))
            throw new InvalidOperationException("Målelementets Romnavn er skrivebeskyttet eller eid av en annen bruker.");

        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
        string stem = Path.Combine(ReportRoot, "BAS Romnavn 15674023 " + timestamp);
        var log = new List<string>
        {
            DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture),
            "Modell=" + Model,
            "Mål=15674023; kilde=15673873/809155; dRofus_RoomID=564; bare Romnavn skrives.",
            "Modus=" + (apply ? "utføring" : "forhåndsvisning"),
            "Romnavn før=" + beforeName + "; planlagt=" + (beforeName == RoomName ? "nei, allerede riktig" : "ja")
        };
        if (File.Exists(stem + ".tsv") || File.Exists(stem + ".txt"))
            throw new InvalidOperationException("Kollisjonsfri rapportfil kunne ikke reserveres.");

        if (apply && beforeName != RoomName)
        {
            void Dialog(object? sender, DialogBoxShowingEventArgs args)
            {
                if (args is not TaskDialogShowingEventArgs task) return;
                string message = task.Message ?? "";
                string normalized = new string(message.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
                if (normalized.Contains("tryingtocheckoutalargenumberofelements"))
                    log.Add("WORKSHARING: " + message[..Math.Min(500, message.Length)].Replace('\n', ' ')
                        + "; valgt=" + args.OverrideResult((int)TaskDialogResult.CommandLink1));
                else if (normalized.Contains("checkout") && normalized.Contains("workset"))
                    log.Add("Uavklart checkout-dialog, ikke overstyrt: " + message[..Math.Min(500, message.Length)]);
            }

            using var group = new TransactionGroup(doc, "Fyll Romnavn fra dokumentert dRofus-rute");
            if (group.Start() != TransactionStatus.Started)
                throw new InvalidOperationException("Transaksjonsgruppe kunne ikke startes.");
            ui.DialogBoxShowing += Dialog;
            try
            {
                using var transaction = new Transaction(doc, "Fyll Romnavn på 15674023");
                if (transaction.Start() != TransactionStatus.Started)
                    throw new InvalidOperationException("Transaksjon kunne ikke startes.");
                transaction.SetFailureHandlingOptions(transaction.GetFailureHandlingOptions()
                    .SetForcedModalHandling(true).SetClearAfterRollback(true).SetFailuresPreprocessor(new Reject(log)));
                token.ThrowIfCancellationRequested();
                if (Text(target, "Romnavn") != beforeName
                    || Integer(target, "dRofus_RoomID") != RoomId
                    || Text(source, "Romnavn") != RoomName
                    || Integer(source, "dRofus_RoomID") != RoomId)
                    throw new InvalidOperationException("Mål eller kilde endret seg før skriving.");
                if (!Parameter(target, "Romnavn").Set(RoomName))
                    throw new InvalidOperationException("Romnavn kunne ikke settes.");
                TransactionStatus status = transaction.Commit();
                log.Add("Transaksjon=" + status);
                if (status != TransactionStatus.Committed)
                    throw new InvalidOperationException("Commit ikke bekreftet.");
                if (Text(target, "Romnavn") != RoomName
                    || Integer(target, "dRofus_RoomID") != RoomId
                    || Text(target, "dRofus_RoomfunctionNo") != RoomNumber)
                    throw new InvalidOperationException("Etterkontroll i transaksjonen feilet.");
                TransactionStatus groupStatus = group.Assimilate();
                log.Add("Gruppe=" + groupStatus);
                if (groupStatus != TransactionStatus.Committed)
                    throw new InvalidOperationException("Transaksjonsgruppecommit ikke bekreftet.");
            }
            catch
            {
                if (group.GetStatus() == TransactionStatus.Started)
                {
                    TransactionStatus rollback = group.RollBack();
                    log.Add("Rollback=" + rollback);
                    if (rollback != TransactionStatus.RolledBack)
                        throw new InvalidOperationException("Rollback ikke bekreftet.");
                }
                throw;
            }
            finally
            {
                ui.DialogBoxShowing -= Dialog;
            }
        }

        string afterName = Text(target, "Romnavn");
        if (apply && afterName != RoomName)
            throw new InvalidOperationException("Separat etterkontroll feilet.");
        string statusText = apply && beforeName != RoomName ? "VERIFIED" : beforeName == RoomName ? "ALREADY_CORRECT" : "PREVIEW";
        File.WriteAllLines(stem + ".tsv", new[]
        {
            "Status\tTargetId\tTargetUniqueId\tSourceId\tSourceUniqueId\tOccurrenceId\tRoomID\tRoomNumber\tRomnavnFør\tRomnavnEtter",
            string.Join("\t", statusText, TargetId, TargetUniqueId, SourceId, SourceUniqueId,
                SourceOccurrenceId, RoomId, RoomNumber, Clean(beforeName), Clean(afterName))
        }, new UTF8Encoding(false));
        log.Add("Etterkontroll=Romnavn " + afterName + "; RoomID=" + Integer(target, "dRofus_RoomID")
            + "; Romnummer=" + Text(target, "dRofus_RoomfunctionNo"));
        File.WriteAllLines(stem + ".txt", log, new UTF8Encoding(false));
        return string.Join(Environment.NewLine, log) + Environment.NewLine + "Rapport=" + stem + ".tsv";
    }

    private static bool IsMissing(string value) => string.IsNullOrWhiteSpace(value) || value.Trim() == "--";
    private static string Clean(string value) => value.Replace("\t", " ").Replace("\r", " ").Replace("\n", " ");

    private static Parameter Parameter(Element element, string name)
    {
        var parameters = element.GetParameters(name);
        if (parameters.Count != 1 || parameters[0].StorageType != StorageType.String)
            throw new InvalidOperationException("Ugyldig tekstparameter " + name + " på " + element.Id.Value);
        return parameters[0];
    }

    private static string Text(Element element, string name)
    {
        var parameters = element.GetParameters(name);
        if (parameters.Count != 1)
            throw new InvalidOperationException("Manglende eller duplisert parameter " + name + " på " + element.Id.Value);
        return parameters[0].StorageType switch
        {
            StorageType.String => parameters[0].AsString() ?? "",
            StorageType.Integer => parameters[0].AsInteger().ToString(CultureInfo.InvariantCulture),
            _ => throw new InvalidOperationException("Ugyldig lagringstype " + name + " på " + element.Id.Value)
        };
    }

    private static int Integer(Element element, string name)
    {
        var parameters = element.GetParameters(name);
        if (parameters.Count != 1 || parameters[0].StorageType != StorageType.Integer)
            throw new InvalidOperationException("Ugyldig integerparameter " + name + " på " + element.Id.Value);
        return parameters[0].AsInteger();
    }

    private sealed class Reject : IFailuresPreprocessor
    {
        private readonly List<string> log;
        internal Reject(List<string> log) => this.log = log;
        public FailureProcessingResult PreprocessFailures(FailuresAccessor accessor)
        {
            var messages = accessor.GetFailureMessages();
            foreach (var message in messages)
                log.Add("REVIT: " + message.GetSeverity() + ": " + message.GetDescriptionText());
            return messages.Count == 0 ? FailureProcessingResult.Continue : FailureProcessingResult.ProceedWithRollBack;
        }
    }
}

internal sealed class GeneratedAction
{
    public string Execute(UIApplication uiApplication, Document? activeDocument, CancellationToken cancellationToken) =>
        new DrofusRoomNameCorrection().Run(uiApplication, activeDocument, cancellationToken, apply: false);
}
