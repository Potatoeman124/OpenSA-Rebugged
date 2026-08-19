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
    [ValidateSet("preset", "open-fields", "contested-center")]
    [string]$Layout = "preset",
    [ValidateSet("preset", "sparse", "standard", "dense")]
    [string]$NeutralColonyDensity = "preset",
    [ValidateSet("proxy", "native", "both")]
    [string]$MovementValidation = "both",
    [string]$OutputPath,
    [string]$ReportPath,
    [switch]$InstallForPlay,
    [switch]$Overwrite
)

$ErrorActionPreference = "Stop"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$settingsDirectory = Join-Path $root "artifacts\rmg\phase-7c-player-settings\settings"
$settingsPath = Join-Path $settingsDirectory "player-$Seed.json"
[System.IO.Directory]::CreateDirectory($settingsDirectory) | Out-Null

$settings = [ordered]@{
    schema_version = 1
    preset = $Preset
    seed = $Seed.ToString([System.Globalization.CultureInfo]::InvariantCulture)
    players = $Players
    symmetry = $Symmetry
    layout = $Layout
    neutral_colony_density = $NeutralColonyDensity
}
$settings | ConvertTo-Json | Set-Content -LiteralPath $settingsPath -Encoding utf8

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
