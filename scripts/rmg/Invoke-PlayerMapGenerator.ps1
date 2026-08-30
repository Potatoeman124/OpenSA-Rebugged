[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [UInt64]$Seed,
    [ValidateSet("balanced", "open-conflict", "tactical-crossroads")]
    [string]$Preset = "balanced",
    [ValidateSet(2, 4)]
    [int]$Players = 2,
    [ValidateSet("automatic", "horizontal", "vertical", "rotational")]
    [string]$Symmetry = "automatic",
    [ValidateSet("preset", "natural-landscape", "structured-competitive", "artificial-battlefield")]
    [string]$LayoutFamily = "preset",
    [ValidateSet("preset", "open-fields", "contested-center")]
    [string]$Layout = "preset",
    [ValidateSet("preset", "sparse", "standard", "dense")]
    [string]$NeutralColonyDensity = "preset",
    [ValidateSet("preset", "low", "standard", "high")]
    [string]$WaterAmount = "preset",
    [ValidateSet("preset", "low", "standard", "high")]
    [string]$TacticalTerrain = "preset",
    [ValidateSet("proxy", "native", "both")]
    [string]$MovementValidation = "both",
    [string]$OutputPath,
    [string]$ReportPath,
    [switch]$InstallForPlay,
    [switch]$Overwrite
)

$ErrorActionPreference = "Stop"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$variantName = "$Preset-$LayoutFamily-W$WaterAmount-T$TacticalTerrain"
$settingsDirectory = Join-Path $root "artifacts\rmg\phase-8c-structured-competitive\settings"
$settingsPath = Join-Path $settingsDirectory "player-$Seed-$variantName.json"
[System.IO.Directory]::CreateDirectory($settingsDirectory) | Out-Null

$settings = [ordered]@{
    schema_version = 3
    preset = $Preset
    seed = $Seed.ToString([System.Globalization.CultureInfo]::InvariantCulture)
    players = $Players
    symmetry = $Symmetry
    layout = $Layout
    layout_family = $LayoutFamily
    neutral_colony_density = $NeutralColonyDensity
    water_amount = $WaterAmount
    tactical_terrain = $TacticalTerrain
}
$settings | ConvertTo-Json | Set-Content -LiteralPath $settingsPath -Encoding utf8

if ([string]::IsNullOrWhiteSpace($OutputPath))
{
    $OutputPath = if ($InstallForPlay)
    {
        Join-Path $env:APPDATA "OpenRA\maps\sa\{DEV_VERSION}\OpenSA-RMG-$Seed-$variantName.oramap"
    }
    else
    {
        Join-Path $root "artifacts\rmg\phase-8c-structured-competitive\examples\OpenSA-RMG-$Seed-$variantName.oramap"
    }
}
if ([string]::IsNullOrWhiteSpace($ReportPath))
{
    $ReportPath = Join-Path $root "artifacts\rmg\phase-8c-structured-competitive\reports\OpenSA-RMG-$Seed-$variantName.json"
}
$invokeParameters = @{
    PlayerSettingsPath = $settingsPath
    MovementValidation = $MovementValidation
    InstallForPlay = $InstallForPlay
    Overwrite = $Overwrite
}
if (![string]::IsNullOrWhiteSpace($OutputPath))
{
    $invokeParameters.OutputPath = $OutputPath
}
if (![string]::IsNullOrWhiteSpace($ReportPath))
{
    $invokeParameters.ReportPath = $ReportPath
}

& (Join-Path $PSScriptRoot "Invoke-MapGenerator.ps1") @invokeParameters
if ($LASTEXITCODE -ne 0)
{
    exit $LASTEXITCODE
}

Write-Host "Player settings: $settingsPath" -ForegroundColor Green
