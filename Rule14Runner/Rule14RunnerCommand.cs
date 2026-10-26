using System;
using System.ComponentModel.DataAnnotations;
using System.Threading;
using Autodesk.Revit.DB;
using CW.Assistant.Extensions.Contracts;
using CW.Assistant.Extensions.Revit;
using CW.Assistant.Generated;
using Result = CW.Assistant.Extensions.Contracts.Result;

namespace Rule14Runner;

public class Rule14RunnerCommand : IRevitExtension<Rule14RunnerArgs>
{
    public IExtensionResult Run(IRevitExtensionContext context, Rule14RunnerArgs args, CancellationToken cancellationToken)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(args);
            Validator.ValidateObject(args, new ValidationContext(args), true);
            cancellationToken.ThrowIfCancellationRequested();
            Document? document = context.UIApplication.ActiveUIDocument?.Document;
            if (document is null) return Result.Text.Failed("Ingen aktiv Revit-modell; Regel 14 ble ikke kjørt.");

            string result = new Rule14MissingRoomInformationReport().Execute(document, args.WorkspaceRoot, cancellationToken);
            return Result.Text.Succeeded(result);
        }
        catch (OperationCanceledException)
        {
            return Result.Text.Failed("Regel 14 ble avbrutt før rapporten var ferdig.");
        }
        catch (Exception exception)
        {
            return Result.Text.Failed("Regel 14 feilet: " + exception);
        }
    }
}