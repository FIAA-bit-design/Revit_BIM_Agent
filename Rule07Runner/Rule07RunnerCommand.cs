//-----------------------------------------------------------------------------
// Rule07RunnerCommand.cs
//
// This file contains the main implementation of the Rule07Runner,
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

namespace Rule07Runner;

/// <summary>
/// Main command class for the Rule07Runner.
/// This class contains the core logic of the extension that executes within Revit.
/// </summary>
/// <remarks>
/// The class implements IRevitExtension with Rule07RunnerArgs as the input type,
/// which means it will receive input parameters as defined in Rule07RunnerArgs.
/// </remarks>
public class Rule07RunnerCommand : IRevitExtension<Rule07RunnerArgs>
{
    /// <summary>
    /// The main entry point for the Revit extension.
    /// This method is called by the Assistant platform when the extension is executed.
    /// </summary>
    /// <param name="context">Provides access to the Revit application and document</param>
    /// <param name="args">Input parameters as configured by the user</param>
    /// <param name="cancellationToken">Token for handling cancellation requests</param>
    /// <returns>An IExtensionResult indicating success or failure with a message</returns>
    public IExtensionResult Run(IRevitExtensionContext context, Rule07RunnerArgs args, CancellationToken cancellationToken)
    {
        // Step 1: Access the active Revit document
        // This provides access to the open Revit model
        var document = context.UIApplication.ActiveUIDocument?.Document;
        
        // Always check for null to handle cases where no document is open
        if (document is null)
            return Result.Text.Failed("Revit has no active model open; Rule 7 was not run.");

        if (!string.Equals(document.Title, "U_F_BAS_FBU_RIE_XXX", StringComparison.Ordinal)
            || !string.Equals(document.PathName, "Autodesk Docs://Fornebubanen/U_F_BAS_FBU_RIE_XXX.rvt", StringComparison.Ordinal))
            return Result.Text.Failed("Active model does not match the verified BAS model; Rule 7 was not run.");

        if (cancellationToken.IsCancellationRequested)
            return Result.Text.Failed("Rule 7 was cancelled before execution.");

        try
        {
            var rule = new CW.Assistant.Generated.GeneratedAction();
            string result = rule.Execute(context.UIApplication, document);
            return result.StartsWith("Regel 7 v0.0.15:", StringComparison.Ordinal)
                ? Result.Text.Succeeded(result)
                : Result.Text.Failed(result);
        }
        catch (Exception exception)
        {
            return Result.Text.Failed("Regel 7-runner feilet: " + exception);
        }

#if false

        // Step 2: Get selected elements from the active Revit view
        // This retrieves the elements that the user has selected in Revit
        var selectedObjects = context.UIApplication.ActiveUIDocument!.Selection.GetElementIds();
        
        // Step 3: Create and start a transaction
        // Transactions are required for any modifications to the Revit model
        // The transaction name appears in Revit's Undo/Redo menu
        using var transaction = new Transaction(document, "Rule07Runner");
        transaction.Start();

        try
        {
            // Step 4: Modify the model (example)
            // Loop through each selected element and perform operations
            foreach (var elementId in selectedObjects)
            {
                if (cancellationToken.IsCancellationRequested)
                    break; // Exit if cancellation is requested
                    
                var element = document.GetElement(elementId);
                
                // This is where you would implement your specific element modification logic
                // For example:
                // - Modify parameters
                // - Change geometry
                // - Create elements
                // - etc.
            }
            
            if (cancellationToken.IsCancellationRequested)
            {
                transaction.RollBack(); // Rollback if cancellation was requested
                return Result.Text.Failed("Operation cancelled by user");
            }

            // Step 5: Commit the transaction to save changes to the model 
            transaction.Commit();
            
            // Step 6: Create a message with results to display to the user
            var message = $"Operation completed successfully. Input text was: {args.TextInput}";
            
            // Step 7: Return a success result with the message
            return Result.Text.Succeeded(message);
        }
        catch (Exception ex)
        {
            // Handle exceptions by rolling back the transaction and returning failure
            if (transaction.HasStarted() && !transaction.HasEnded())
                transaction.RollBack();
                
            return Result.Text.Failed($"Error: {ex.Message}");
        }
#endif
    }
}