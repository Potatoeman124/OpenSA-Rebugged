[CmdletBinding()]
param(
    [string]$ReportPath,
    [UInt64]$SeedStart = 1,
    [int]$GateACount = 100,
    [int]$MixedCount = 1000,
    [switch]$Overwrite
)

$ErrorActionPreference = "Stop"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$engineRoot = Join-Path $root "engine"
$utility = Join-Path $engineRoot "bin\OpenRA.Utility.exe"
$dotnetRoot = Join-Path $root ".tools\dotnet"

if ([string]::IsNullOrWhiteSpace($ReportPath))
{
    $ReportPath = Join-Path $root "artifacts\rmg\phase-3-generator-core\fuzz\phase-3-fuzz.json"
}
elseif (![System.IO.Path]::IsPathRooted($ReportPath))
{
    $ReportPath = Join-Path $root $ReportPath
}

if (!(Test-Path -LiteralPath $utility -PathType Leaf))
{
    throw "OpenRA.Utility.exe is missing. Run build-pipeline.cmd validate first."
}

$arguments = @(
    "sa",
    "--fuzz-sa-map-generator",
    $ReportPath,
    "--seed-start", $SeedStart,
    "--gate-a-count", $GateACount,
    "--mixed-count", $MixedCount
)
if ($Overwrite)
{
    $arguments += "--overwrite"
}

$env:DOTNET_ROOT = $dotnetRoot
$env:DOTNET_MULTILEVEL_LOOKUP = "0"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
$env:DOTNET_NOLOGO = "1"
$env:ENGINE_DIR = ".."
$env:MOD_SEARCH_PATHS = "$root\mods,.\mods"
$env:PATH = "$dotnetRoot;$env:PATH"

Push-Location $engineRoot
try
{
    & $utility $arguments
    if ($LASTEXITCODE -ne 0)
    {
        throw "RMG fuzz validation failed with exit code $LASTEXITCODE. See $ReportPath."
    }
}
finally
{
    Pop-Location
}

Write-Host "RMG fuzz validation passed. Report: $ReportPath" -ForegroundColor Green
