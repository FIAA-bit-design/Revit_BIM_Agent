namespace Rule12Runner;

public class Rule12RunnerArgs
{
    [TextField(Label = "Handling", ToolTip = "RegisterSelection: registrer markering. PreviewBatch/ApplyBatch: kontroller/utfør køen. FillFittingRoomText: fyll manglende rominfo på K5B-bend/kryss/T-stykker.")]
    [Required]
    [RegularExpression("^(RegisterSelection|PreviewBatch|ApplyBatch|LegacyPreview|FillFittingRoomText)$")]
    public string Action { get; set; } = "RegisterSelection";

    [TextField(Label = "3D-view", ToolTip = "KS Kablebro dRofus: ett synlig segment per dokumentert strekk.")]
    [Required(ErrorMessage = "Viewnavn er påkrevd.")]
    [StringLength(100, MinimumLength = 1, ErrorMessage = "Viewnavnet må være mellom 1 og 100 tegn.")]
    public string ViewName { get; set; } = "KS Kablebro dRofus";

    [BooleanField(Label = "Bare forhåndsvisning")]
    public bool PreviewOnly { get; set; } = true;

    [TextField(Label = "Mål-UniqueId-er", ToolTip = "Bare FillFittingRoomText: valgfri liste med UniqueId-er skilt med komma, semikolon eller linjeskift. Tom liste behandler alle støttede fittings.")]
    public string TargetUniqueIds { get; set; } = "";
}