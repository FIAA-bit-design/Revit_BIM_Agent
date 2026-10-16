# Rule12Runner

Standalone Revit 2026 view utility. It is not part of the standard BIM rule sequence.

For the configured 3D view, the runner groups visible `OST_CableTray` instances by exact, trimmed `FOB_Merkestreng`. It keeps the longest curve in each group (lowest ElementId breaks length ties) and hides the remaining instances in that view only. Elements with blank or `--` marks are left visible. No model elements are deleted and no parameters or geometry are changed.

The runner is restricted to `U_F_BAS_FBU_RIE_XXX.rvt`. `PreviewOnly` reports the planned representatives and hidden ElementIds without modifying the view.

Build with `dotnet build Rule12Runner.csproj --configuration Debug` from this directory.