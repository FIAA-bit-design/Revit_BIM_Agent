namespace Rule12Runner;

public class Rule12RunnerArgs
{
    [TextField(Label = "3D-view", ToolTip = "Viewet der skal vise én kabelbro per unik FOB_Merkestreng.")]
    [Required(ErrorMessage = "Viewnavn er påkrevd.")]
    [StringLength(100, MinimumLength = 1, ErrorMessage = "Viewnavnet må være mellom 1 og 100 tegn.")]
    public string ViewName { get; set; } = "KS Kablebro dRofus";

    [BooleanField(Label = "Bare forhåndsvisning")]
    public bool PreviewOnly { get; set; }
}