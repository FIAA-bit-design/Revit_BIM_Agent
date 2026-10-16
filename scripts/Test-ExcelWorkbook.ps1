param(
    [Parameter(Mandatory = $true)]
    [string[]]$Paths
)

$ErrorActionPreference = 'Stop'
$resolvedPaths = @($Paths | ForEach-Object {
    (Resolve-Path -LiteralPath $_ -ErrorAction Stop).ProviderPath
})
$excel = $null
try {
    $excel = New-Object -ComObject Excel.Application
    $excel.Visible = $false
    $excel.DisplayAlerts = $true
    foreach ($path in $resolvedPaths) {
        $book = $null
        try {
            # Explicit COM defaults avoid optional-argument binding differences in PowerShell.
            $book = $excel.Workbooks.Open($path, 0, $true, 5, '', '', $true, 2, '', $false, $false, 0, $false, $false, 0)
            Write-Output "OK: normal read-only Excel open, DisplayAlerts=True, CorruptLoad=0: $path"
        }
        finally {
            if ($null -ne $book) {
                try {
                    $book.Close($false)
                }
                finally {
                    [void][System.Runtime.InteropServices.Marshal]::ReleaseComObject($book)
                }
            }
        }
    }
}
finally {
    if ($null -ne $excel) {
        try {
            $excel.Quit()
        }
        finally {
            [void][System.Runtime.InteropServices.Marshal]::ReleaseComObject($excel)
        }
    }
}
