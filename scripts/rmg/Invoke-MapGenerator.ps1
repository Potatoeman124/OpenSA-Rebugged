[CmdletBinding()]
param(
    [string]$OutputPath,
    [UInt64]$Seed = 1,
    [ValidateSet(2, 4)]
    [int]$Players = 2,
    [ValidateSet("horizontal", "vertical", "rotational")]
    [string]$Symmetry = "horizontal",
    [ValidateSet("open", "central-contest")]
    [string]$Archetype = "open",
    [ValidateSet("off", "mixed")]
    [string]$Topology = "off",
    [ValidateSet("proxy", "native", "both")]
    [string]$MovementValidation = "both",
    [int]$NeutralColonies = 0,
    [string]$ReportPath,
    [switch]$InstallForPlay,
    [switch]$Overwrite
)

$ErrorActionPreference = "Stop"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$engineRoot = Join-Path $root "engine"
$utility = Join-Path $engineRoot "bin\OpenRA.Utility.exe"
$dotnetRoot = Join-Path $root ".tools\dotnet"

if (!(Test-Path -LiteralPath $utility -PathType Leaf))
{
    throw "OpenRA.Utility.exe is missing. Run build-pipeline.cmd validate first."
}

if (!(Test-Path -LiteralPath (Join-Path $dotnetRoot "dotnet.exe") -PathType Leaf))
{
    throw "The pinned local .NET runtime is missing. Run build-pipeline.cmd bootstrap first."
}

if ($NeutralColonies -eq 0)
{
    $NeutralColonies = if ($Players -eq 2) { 10 } else { 16 }
}

if ([string]::IsNullOrWhiteSpace($OutputPath))
{
    if ($InstallForPlay)
    {
        $mapDirectory = Join-Path $env:APPDATA "OpenRA\maps\sa\{DEV_VERSION}"
        $OutputPath = Join-Path $mapDirectory "OpenSA-RMG-$Seed.oramap"
    }
    else
    {
        $phase = if ($Topology -eq "mixed") { "phase-4c-blocking-topology" } else { "phase-4a-native-movement-validation" }
        $OutputPath = Join-Path $root "artifacts\rmg\$phase\examples\manual-$Seed.oramap"
    }
}
elseif (![System.IO.Path]::IsPathRooted($OutputPath))
{
    $OutputPath = Join-Path $root $OutputPath
}

if ([string]::IsNullOrWhiteSpace($ReportPath))
{
    $phase = if ($Topology -eq "mixed") { "phase-4c-blocking-topology" } else { "phase-4a-native-movement-validation" }
    $ReportPath = Join-Path $root "artifacts\rmg\$phase\reports\manual-$Seed.json"
}
elseif (![System.IO.Path]::IsPathRooted($ReportPath))
{
    $ReportPath = Join-Path $root $ReportPath
}

$arguments = @(
    "sa",
    "--generate-sa-map",
    $OutputPath,
    "--report", $ReportPath,
    "--seed", $Seed,
    "--size", "128,128",
    "--players", $Players,
    "--tileset", "NORMAL",
    "--symmetry", $Symmetry,
    "--archetype", $Archetype,
    "--topology", $Topology,
    "--neutral-colonies", $NeutralColonies,
    "--movement-validation", $MovementValidation,
    "--generator-version", $(if ($Topology -eq "mixed") { "2" } else { "1" })
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
        throw "RMG generation failed with exit code $LASTEXITCODE."
    }
}
finally
{
    Pop-Location
}

Write-Host "Generated map: $OutputPath" -ForegroundColor Green
Write-Host "Validation report: $ReportPath" -ForegroundColor Green
if ($InstallForPlay)
{
    Write-Host "The map is installed in the development user-map folder. Start OpenSA with F5 or Ctrl+F5 and select it from the skirmish map list." -ForegroundColor Cyan
}
