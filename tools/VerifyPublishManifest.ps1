#Requires -Version 5.1
<#
.SYNOPSIS
  Verifies SignalUI publish output contains GigE / mvIMPACT files (plan: verify-out-manifest).

.PARAMETER OutDir
  Folder produced by: dotnet publish singalUI\singalUI.csproj -c Release -o <OutDir>
#>
param(
    [Parameter(Mandatory = $true)]
    [string] $OutDir
)

$ErrorActionPreference = "Stop"
$OutDir = (Resolve-Path -LiteralPath $OutDir).Path

$errors = @()

$exe = Get-ChildItem -LiteralPath $OutDir -Filter "*.exe" -File -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $exe) { $errors += "No .exe in publish folder." }

$cti = Join-Path $OutDir "mvGenTLProducer.cti"
if (-not (Test-Path -LiteralPath $cti)) { $errors += "Missing mvGenTLProducer.cti (GenTL producer)." }

$managed = Join-Path $OutDir "mv.impact.acquire.dll"
if (-not (Test-Path -LiteralPath $managed)) { $errors += "Missing mv.impact.acquire.dll." }

$mvDir = Join-Path $OutDir "MatrixVision"
if (-not (Test-Path -LiteralPath $mvDir -PathType Container)) {
    $errors += "Missing MatrixVision\ folder with native mv*.dll."
}
else {
    $native = Get-ChildItem -LiteralPath $mvDir -Filter "mv*.dll" -File -Recurse -ErrorAction SilentlyContinue
    if (-not $native -or $native.Count -eq 0) { $errors += "MatrixVision\ contains no mv*.dll." }
}

if ($errors.Count -gt 0) {
    Write-Host "VERIFY FAILED: $OutDir" -ForegroundColor Red
    $errors | ForEach-Object { Write-Host "  - $_" -ForegroundColor Yellow }
    exit 1
}

Write-Host "OK: publish manifest looks complete: $OutDir" -ForegroundColor Green
Write-Host "  exe: $($exe.Name)"
Write-Host "  native mv*.dll count: $((Get-ChildItem -LiteralPath $mvDir -Filter 'mv*.dll' -File -Recurse).Count)"
exit 0
