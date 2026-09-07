[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][UInt64]$Seed,
    [ValidateSet(128, 256)][int]$MapSize = 256,
    [ValidateSet(2, 4)][int]$Players = 4,
    [ValidateSet("small", "medium", "high", "extreme", "ultra")][string]$TerrainComplexity = "medium",
    [ValidateSet("low", "standard", "high", "extreme", "ultra")][string]$WaterAmount = "standard",
    [ValidateSet("low", "standard", "high", "extreme", "ultra")][Alias("SurfaceModifiers")][string]$GravelMossAmount = "standard",
    [ValidateSet("sparse", "standard", "dense", "extreme", "ultra")][string]$NeutralColonyDensity = "standard",
    [bool]$OriginalSurfaceRelations = $true,
    [bool]$PreventColonyOverlapping = $true,
    [string]$OutputDirectory,
    [switch]$VerifyRepeatability,
    [switch]$Overwrite
)
$ErrorActionPreference = "Stop"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $root "artifacts\rmg\regions-v14\$Seed-$MapSize-$Players-$TerrainComplexity-W$WaterAmount-G$GravelMossAmount-N$NeutralColonyDensity-R$OriginalSurfaceRelations-O$PreventColonyOverlapping"
}
[IO.Directory]::CreateDirectory($OutputDirectory) | Out-Null
$settingsPath = Join-Path $OutputDirectory "settings.json"
if ((Test-Path -LiteralPath $settingsPath) -and !$Overwrite) {
    throw "Output already exists. Choose a new directory or pass -Overwrite."
}
[ordered]@{
    schema_version = 8
    preset = "balanced"
    seed = $Seed.ToString([Globalization.CultureInfo]::InvariantCulture)
    size = "$MapSize,$MapSize"
    players = $Players
    layout_family = "natural-landscape"
    neutral_colony_density = $NeutralColonyDensity
    water_amount = $WaterAmount
    gravel_moss_amount = $GravelMossAmount
    terrain_complexity = $TerrainComplexity
    original_surface_relations = $OriginalSurfaceRelations
    prevent_colony_overlapping = $PreventColonyOverlapping
} | ConvertTo-Json | Set-Content -LiteralPath $settingsPath -Encoding utf8
& (Join-Path $PSScriptRoot "Invoke-MapGenerator.ps1") -PlayerSettingsPath $settingsPath `
    -OutputPath (Join-Path $OutputDirectory "map.oramap") -ReportPath (Join-Path $OutputDirectory "report.json") `
    -VerifyRepeatability:$VerifyRepeatability -Overwrite:$Overwrite
