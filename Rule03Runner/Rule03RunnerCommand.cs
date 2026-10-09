//-----------------------------------------------------------------------------
// Rule03RunnerCommand.cs
//
// This file contains the main implementation of the Rule03Runner,
// which executes within the Autodesk Revit environment to automate tasks.
// 
// The command class implements the IRevitExtension interface, which defines
// the contract for all Revit extensions in the Assistant platform.
//
// DEVELOPER GUIDE:
// 1. Implement your core extension logic in the Run method
// 2. Always use transactions for any model modifications
// 3. Check for null or invalid inputs before operations
// 4. Return appropriate success/failure results with informative messages
//-----------------------------------------------------------------------------

namespace Rule03Runner;

/// <summary>
/// Main command class for the Rule03Runner.
/// This class contains the core logic of the extension that executes within Revit.
/// </summary>
/// <remarks>
/// The class implements IRevitExtension with Rule03RunnerArgs as the input type,
/// which means it will receive input parameters as defined in Rule03RunnerArgs.
/// </remarks>
public class Rule03RunnerCommand : IRevitExtension<Rule03RunnerArgs>
{
    /// <summary>
    /// The main entry point for the Revit extension.
    /// This method is called by the Assistant platform when the extension is executed.
    /// </summary>
    /// <param name="context">Provides access to the Revit application and document</param>
    /// <param name="args">Input parameters as configured by the user</param>
    /// <param name="cancellationToken">Token for handling cancellation requests</param>
    /// <returns>An IExtensionResult indicating success or failure with a message</returns>
    public IExtensionResult Run(IRevitExtensionContext context, Rule03RunnerArgs args, CancellationToken cancellationToken)
    {
        var document = context.UIApplication.ActiveUIDocument?.Document;
            if (document is null)
                return Result.Text.Failed("Revit has no active model open; Rule 3 was not run.");
            if (!string.Equals(document.Title, "U_F_BAS_FBU_RIE_XXX", StringComparison.Ordinal)
                || !string.Equals(document.PathName, "Autodesk Docs://Fornebubanen/U_F_BAS_FBU_RIE_XXX.rvt", StringComparison.Ordinal))
                return Result.Text.Failed("Active model does not match the verified BAS model; Rule 3 was not run.");
            if (cancellationToken.IsCancellationRequested)
                return Result.Text.Failed("Rule 3 was cancelled before execution.");

            try
            {
                var rule = new CW.Assistant.Generated.GeneratedAction();
                string result = rule.Execute(context.UIApplication, document);
                return result.StartsWith("Regel 3 v0.0.11:", StringComparison.Ordinal)
                    ? Result.Text.Succeeded(result)
                    : Result.Text.Failed(result);
            }
            catch (Exception exception)
            {
                return Result.Text.Failed("Regel 3-runner feilet: " + exception);
            }
    }
}