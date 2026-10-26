using System.ComponentModel.DataAnnotations;
using CW.Assistant.Extensions.Contracts.Fields;

namespace Rule14Runner;

public class Rule14RunnerArgs
{
    [TextField(Label = "Arbeidsområderot", ToolTip = "Absolutt prosjektrot for logs/reports. Tom verdi bruker runnerens katalog.")]
    [StringLength(1024)]
    public string WorkspaceRoot { get; set; } = "";
}