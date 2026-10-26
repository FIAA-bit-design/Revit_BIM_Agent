using System;
using System.ComponentModel.DataAnnotations;
using System.Threading;
using Autodesk.Revit.DB;
using CW.Assistant.Extensions.Contracts;
using CW.Assistant.Extensions.Revit;
using CW.Assistant.Generated;
using Result = CW.Assistant.Extensions.Contracts.Result;

namespace Rule13Runner;

public class Rule13RunnerCommand : IRevitExtension<Rule13RunnerArgs>
{
    public IExtensionResult Run(IRevitExtensionContext context, Rule13RunnerArgs args, CancellationToken cancellationToken)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(args);
            Validator.ValidateObject(args, new ValidationContext(args), true);
            Document? document = context.UIApplication.ActiveUIDocument?.Document;
            if (document is null) return Result.Text.Failed("Ingen aktiv Revit-modell; Rule13Runner v0.0.7 ble ikke kjørt.");
            string path = new Rule13CablingEquipmentReport().Execute(document, args.WorkspaceRoot, cancellationToken);
            return Result.Text.Succeeded("Rule13Runner v0.0.7: Ny utstyrsliste for " + Rule13CablingEquipmentReport.ModelCode(document.PathName) + ": " + path + ". Kablet er tomt; tidligere notater er ikke erstattet. JOS gjelder bare BAS; se Omfang og uavklarte treff.");
        }
        catch (Exception exception)
        {
            return Result.Text.Failed("Rule13Runner v0.0.7 feilet: " + exception);
        }
    }
}
