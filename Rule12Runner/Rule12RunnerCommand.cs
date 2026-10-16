namespace Rule12Runner;

public class Rule12RunnerCommand : IRevitExtension<Rule12RunnerArgs>
{
    public IExtensionResult Run(IRevitExtensionContext context, Rule12RunnerArgs args, CancellationToken cancellationToken)
    {
        Document? document = context.UIApplication.ActiveUIDocument?.Document;
        if (document is null) return Result.Text.Failed("Ingen aktiv Revit-modell; Rule12Runner ble ikke kjørt.");

        bool isBasModel = string.Equals(document.Title, "U_F_BAS_FBU_RIE_XXX", StringComparison.Ordinal)
            && string.Equals(document.PathName, "Autodesk Docs://Fornebubanen/U_F_BAS_FBU_RIE_XXX.rvt", StringComparison.Ordinal);
        if (!isBasModel) return Result.Text.Failed("Aktiv modell er ikke BAS; Rule12Runner ble ikke kjørt.");
        if (cancellationToken.IsCancellationRequested) return Result.Text.Failed("Rule12Runner ble avbrutt før kjøring.");

        try
        {
            string result;
            if (args.ViewName != "KS Kablebro dRofus") return Result.Text.Failed("Bare KS Kablebro dRofus støttes.");
            if (args.Action == "LegacyPreview")
                result = new CW.Assistant.Generated.Rule12HideDuplicateCableTrays()
                    .Execute(context.UIApplication, document, args.ViewName, true, cancellationToken);
            else
            {
                if (args.Action == "ApplyBatch" && args.PreviewOnly)
                    return Result.Text.Failed("Sett PreviewOnly=false uttrykkelig for samlet utføring.");
                result = new CW.Assistant.Generated.DrofusRouteQueueRunner()
                    .Execute(context.UIApplication, document, args.Action, cancellationToken);
            }
            return result.StartsWith("Rule12Runner:", StringComparison.Ordinal)
                ? Result.Text.Succeeded(result)
                : Result.Text.Failed(result);
        }
        catch (Exception exception)
        {
            return Result.Text.Failed("Rule12Runner feilet: " + exception);
        }
    }
}