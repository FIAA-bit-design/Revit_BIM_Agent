using System.ComponentModel.DataAnnotations;
using CW.Assistant.Extensions.Contracts.Fields;

namespace Rule13Runner;

public class Rule13RunnerArgs
{
    [TextField(Label = "Arbeidsområderot", ToolTip = "Absolutt prosjektrot med config/CablingEquipmentExceptions.json. Tom verdi bruker konfigurasjonen ved runner-DLL-en.")]
    [StringLength(1024)]
    public string WorkspaceRoot { get; set; } = "";
}
