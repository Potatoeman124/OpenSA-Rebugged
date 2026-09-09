[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][UInt64]$Seed,
    [switch]$Pvp,
    [switch]$Battlefield,
    [ValidateSet("rectangles", "cut-corners", "diamonds")][string]$BlockShape = "cut-corners",
    [ValidateSet("narrow", "standard", "wide")][string]$LaneWidth = "standard",
    [ValidateSet(1, 2, 4)][int]$MirroringAxes = 1,
    [ValidateSet("NORMAL", "DESERT", "SWAMP", "CANDY")][string]$Tileset = "NORMAL",
    [ValidateSet(64, 128, 256, 512)][int]$MapSize = 256,
    [ValidateRange(1, 8)][int]$Players = 4,
    [ValidateSet("small", "medium", "high", "extreme", "ultra")][string]$TerrainComplexity = "medium",
    [ValidateSet("low", "standard", "high", "extreme", "ultra")][string]$WaterAmount = "standard",
    [ValidateSet("low", "standard", "high", "extreme", "ultra")][Alias("SurfaceModifiers")][string]$GravelMossAmount = "standard",
    [ValidateSet("sparse", "standard", "dense", "extreme", "ultra")][string]$NeutralColonyDensity = "standard",
    [bool]$OriginalSurfaceRelations = $true,
    [bool]$PreventColonyOverlapping = $true,
    [ValidateRange(0, 100)][int]$AntsWeight = 100,
    [ValidateRange(0, 100)][int]$BeetlesWeight = 100,
    [ValidateRange(0, 100)][int]$ScorpionsWeight = 100,
    [ValidateRange(0, 100)][int]$SpidersWeight = 100,
    [ValidateRange(0, 100)][int]$WaspsWeight = 100,
    [ValidateRange(0, 100)][int[]]$StartingColonyShares = @(),
    [ValidateSet("closest-to-spawn", "random")][string]$StartingColonyMode = "closest-to-spawn",
    [string]$OutputDirectory,
    [switch]$VerifyRepeatability,
    [switch]$Overwrite
)
$ErrorActionPreference = "Stop"
$StartingColonyMode = $StartingColonyMode.ToLowerInvariant()
$Tileset = $Tileset.ToUpperInvariant()
if ($StartingColonyShares.Count -ne 0 -and $StartingColonyShares.Count -ne $Players) {
    throw "StartingColonyShares must have one value per configured player."
}
if ($MapSize -eq 64 -and $Players -gt 4) {
    throw "64x64 supports 1 through 4 players."
}
if ($Battlefield) {
    if ($Pvp -or $PSBoundParameters.ContainsKey("MirroringAxes")) { throw "Battlefield cannot be combined with Pvp or MirroringAxes." }
    if ($Players -notin @(2, 4, 8)) { throw "Artificial Battlefield supports 2, 4 or 8 players." }
} elseif ($PSBoundParameters.ContainsKey("BlockShape") -or $PSBoundParameters.ContainsKey("LaneWidth")) { throw "BlockShape and LaneWidth require -Battlefield." }
if ($Pvp) {
    $groupSize = if ($MirroringAxes -eq 4) { 8 } else { 2 * $MirroringAxes }
    if ($Players -lt $groupSize -or $Players % $groupSize -ne 0) { throw "This axis choice requires complete groups of $groupSize players." }
} elseif ($PSBoundParameters.ContainsKey('MirroringAxes')) { throw "MirroringAxes requires -Pvp." }
$root = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $root "artifacts\rmg\$(if ($Battlefield) { "battlefield-v18" } elseif ($Pvp) { "regions-v17-pvp" } else { "regions-v16" })\$Seed-$MapSize-$Players-$TerrainComplexity-W$WaterAmount-G$GravelMossAmount-N$NeutralColonyDensity-R$OriginalSurfaceRelations-O$PreventColonyOverlapping-C$AntsWeight-$BeetlesWeight-$ScorpionsWeight-$SpidersWeight-$WaspsWeight-S$($StartingColonyShares -join '-')$(if ($StartingColonyMode -eq 'random') { '-random' })$(if ($Tileset -ne 'NORMAL') { '-' + $Tileset })$(if ($Pvp) { '-axes' + $MirroringAxes })$(if ($Battlefield) { '-' + $BlockShape + '-' + $LaneWidth })"
}
[IO.Directory]::CreateDirectory($OutputDirectory) | Out-Null
$settingsPath = Join-Path $OutputDirectory "settings.json"
if ((Test-Path -LiteralPath $settingsPath) -and !$Overwrite) {
    throw "Output already exists. Choose a new directory or pass -Overwrite."
}
$settings = [ordered]@{
    starting_colony_shares = @(if ($StartingColonyShares.Count -eq 0) { @(0) * $Players } else { $StartingColonyShares })
    schema_version = $(if ($Battlefield) { 12 } elseif ($Pvp) { 11 } else { 10 })
    preset = "balanced"
    seed = $Seed.ToString([Globalization.CultureInfo]::InvariantCulture)
    size = "$MapSize,$MapSize"
    players = $Players
    layout_family = $(if ($Battlefield) { "artificial-battlefield" } elseif ($Pvp) { "natural-landscape-pvp" } else { "natural-landscape" })
    neutral_colony_density = $NeutralColonyDensity
    water_amount = $WaterAmount
    gravel_moss_amount = $GravelMossAmount
    terrain_complexity = $TerrainComplexity
    original_surface_relations = $OriginalSurfaceRelations
    prevent_colony_overlapping = $PreventColonyOverlapping
    neutral_colony_weights = [ordered]@{ ants = $AntsWeight; beetles = $BeetlesWeight; scorpions = $ScorpionsWeight; spiders = $SpidersWeight; wasps = $WaspsWeight }
}
if ($Pvp) { $settings.mirroring_axes = $MirroringAxes }
if ($Battlefield) { $settings.block_shape = $BlockShape; $settings.lane_width = $LaneWidth }
if ($Tileset -ne "NORMAL") { $settings.tileset = $Tileset }
if ($StartingColonyMode -ne "closest-to-spawn") { $settings.starting_colony_mode = $StartingColonyMode }
$settings | ConvertTo-Json | Set-Content -LiteralPath $settingsPath -Encoding utf8
& (Join-Path $PSScriptRoot "Invoke-MapGenerator.ps1") -PlayerSettingsPath $settingsPath `
    -OutputPath (Join-Path $OutputDirectory "map.oramap") -ReportPath (Join-Path $OutputDirectory "report.json") `
    -VerifyRepeatability:$VerifyRepeatability -Overwrite:$Overwrite
