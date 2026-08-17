[CmdletBinding()]
param(
    [string]$ReportPath,
    [UInt64]$SeedStart = 1,
    [int]$GateACount = 100,
    [int]$MixedCount = 1000,
    [int]$ColonyCountCampaign = 25,
    [int]$RuntimeSampleRate = 100,
    [ValidateSet("proxy", "native", "both")]
    [string]$MovementValidation = "both",
    [ValidateSet("off", "mixed", "shoreline", "land-details")]
    [string]$Topology = "off",
    [switch]$PreserveFailures,
    [switch]$Overwrite
)

$ErrorActionPreference = "Stop"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$engineRoot = Join-Path $root "engine"
$utility = Join-Path $engineRoot "bin\OpenRA.Utility.exe"
$dotnetRoot = Join-Path $root ".tools\dotnet"

if ([string]::IsNullOrWhiteSpace($ReportPath))
{
    $phase = if ($Topology -eq "land-details") { "phase-6b-clear-land-details" } elseif ($Topology -eq "shoreline") { "phase-5b-shoreline-materialization" } elseif ($Topology -eq "mixed") { "phase-4c-blocking-topology" } else { "phase-4a-native-movement-validation" }
    $ReportPath = Join-Path $root "artifacts\rmg\$phase\campaign\bounded-reference.json"
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
    "--mixed-count", $MixedCount,
    "--colony-count-campaign", $ColonyCountCampaign,
    "--movement-validation", $MovementValidation,
    "--topology", $Topology,
    "--runtime-sample-rate", $RuntimeSampleRate
)
if ($PreserveFailures)
{
    $arguments += "--preserve-failures"
}
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
