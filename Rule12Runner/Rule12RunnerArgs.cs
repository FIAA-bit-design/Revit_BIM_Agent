namespace Rule12Runner;

public class Rule12RunnerArgs
{
    [TextField(Label = "Handling", ToolTip = "RegisterSelection: legg til markering uten modellendring. PreviewBatch: kontroller køen. ApplyBatch: utfør registrert kø.")]
    [Required]
    [RegularExpression("^(RegisterSelection|PreviewBatch|ApplyBatch|LegacyPreview)$")]
    public string Action { get; set; } = "RegisterSelection";

    [TextField(Label = "3D-view", ToolTip = "KS Kablebro dRofus: ett synlig segment per dokumentert strekk.")]
    [Required(ErrorMessage = "Viewnavn er påkrevd.")]
    [StringLength(100, MinimumLength = 1, ErrorMessage = "Viewnavnet må være mellom 1 og 100 tegn.")]
    public string ViewName { get; set; } = "KS Kablebro dRofus";

    [BooleanField(Label = "Bare forhåndsvisning")]
    public bool PreviewOnly { get; set; } = true;
}