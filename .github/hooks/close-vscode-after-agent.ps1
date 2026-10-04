param(
    [switch]$CloseWindows,
    [int]$ParentProcessId = 0
)

if (-not $CloseWindows) {
    $markerPath = Join-Path $env:TEMP "rie-bim-agent-complete.txt"
    if (-not (Test-Path -LiteralPath $markerPath -PathType Leaf)) {
        exit 0
    }

    Remove-Item -LiteralPath $markerPath -Force
    $scriptPath = $PSCommandPath
    $arguments = '-NoProfile -ExecutionPolicy Bypass -File "{0}" -CloseWindows -ParentProcessId {1}' -f $scriptPath, $PID
    Start-Process -FilePath (Join-Path $PSHOME "powershell.exe") `
        -ArgumentList $arguments -WindowStyle Hidden
    exit 0
}

if ($ParentProcessId -gt 0) {
    Wait-Process -Id $ParentProcessId -Timeout 10 -ErrorAction SilentlyContinue
}

$codeProcessIds = @(
    Get-Process -Name Code -ErrorAction SilentlyContinue |
        ForEach-Object { $_.Id }
)
if ($codeProcessIds.Count -eq 0) {
    exit 0
}

$nativeCode = @'
using System.Collections.Generic;
using System.Runtime.InteropServices;

public static class VSCodeWindowCloser
{
    private delegate bool EnumWindowsCallback(System.IntPtr window, System.IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsCallback callback, System.IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(System.IntPtr window, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(System.IntPtr window);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(System.IntPtr window, uint message, System.IntPtr wParam, System.IntPtr lParam);

    public static void Close(int[] processIds)
    {
        var processIdSet = new HashSet<int>(processIds);
        EnumWindows((window, parameter) =>
        {
            uint processId;
            GetWindowThreadProcessId(window, out processId);
            if (IsWindowVisible(window) && processIdSet.Contains((int)processId))
            {
                PostMessage(window, 0x0010, System.IntPtr.Zero, System.IntPtr.Zero);
            }
            return true;
        }, System.IntPtr.Zero);
    }
}
'@

Add-Type -TypeDefinition $nativeCode
[VSCodeWindowCloser]::Close([int[]]$codeProcessIds)