[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][UInt64]$Seed,
    [switch]$Pvp,
    [switch]$Battlefield,
    [switch]$Crossroads,
    [switch]$Ring,
    [switch]$DividedLands,
    [switch]$Strongholds,
    [bool]$GenerateCastles = $true,
    [bool]$RespectStartingSafeArea = $true,
    [bool]$OwnStartingStronghold = $false,
    [ValidateSet("none", "one", "two")][string]$LandCrossings = "one",
    [ValidateSet("narrow", "standard", "wide")][string]$CrossingWidth = "standard",
    [ValidateSet("round", "octagonal", "square")][string]$RingShape = "round",
    [ValidateSet("narrow", "standard", "wide")][string]$RingWidth = "standard",
    [ValidateSet("none", "standard", "many")][string]$SideConnections = "standard",
    [ValidateSet("rectangles", "cut-corners", "diamonds")][string]$BlockShape = "cut-corners",
    [ValidateSet("narrow", "standard", "wide")][Alias("ApproachWidth")][string]$LaneWidth = "standard",
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
$SideConnections = $SideConnections.ToLowerInvariant()
$LaneWidth = $LaneWidth.ToLowerInvariant()
if ($StartingColonyShares.Count -ne 0 -and $StartingColonyShares.Count -ne $Players) {
    throw "StartingColonyShares must have one value per configured player."
}
if ($MapSize -eq 64 -and $Players -gt 4) {
    throw "64x64 supports 1 through 4 players."
}
if (@($Pvp, $Battlefield, $Crossroads, $Ring, $DividedLands, $Strongholds).Where({ $_ }).Count -gt 1) { throw "Choose only one layout family." }
if (($Crossroads -or $Ring -or $DividedLands) -and $PSBoundParameters.ContainsKey("BlockShape")) { throw "BlockShape applies only to Battlefield." }
if (!$Crossroads -and $PSBoundParameters.ContainsKey("SideConnections")) { throw "SideConnections requires -Crossroads." }
if (!$Ring -and ($PSBoundParameters.ContainsKey("RingShape") -or $PSBoundParameters.ContainsKey("RingWidth"))) { throw "RingShape and RingWidth require -Ring." }
if (!$DividedLands -and ($PSBoundParameters.ContainsKey("LandCrossings") -or $PSBoundParameters.ContainsKey("CrossingWidth"))) { throw "LandCrossings and CrossingWidth require -DividedLands." }
if ($DividedLands -and $PSBoundParameters.ContainsKey("LaneWidth")) { throw "Use CrossingWidth for -DividedLands." }
if ($Ring -and $PSBoundParameters.ContainsKey("LaneWidth")) { throw "Use RingWidth for -Ring." }
if ($Battlefield -or $Crossroads -or $Ring -or $DividedLands) {
    if ($Pvp -or $PSBoundParameters.ContainsKey("MirroringAxes")) { throw "Planned layouts cannot be combined with Pvp or MirroringAxes." }
    if ($Players -notin @(2, 4, 8)) { throw "Planned layouts support 2, 4 or 8 players." }
} elseif ($PSBoundParameters.ContainsKey("BlockShape") -or $PSBoundParameters.ContainsKey("LaneWidth")) { throw "BlockShape requires -Battlefield; LaneWidth requires -Battlefield or -Crossroads." }
if ($Pvp) {
    $groupSize = if ($MirroringAxes -eq 4) { 8 } else { 2 * $MirroringAxes }
    if ($Players -lt $groupSize -or $Players % $groupSize -ne 0) { throw "This axis choice requires complete groups of $groupSize players." }
} elseif ($PSBoundParameters.ContainsKey('MirroringAxes')) { throw "MirroringAxes requires -Pvp." }
if (!$Strongholds -and $PSBoundParameters.ContainsKey("GenerateCastles")) { throw "GenerateCastles requires -Strongholds." }
if (!$Strongholds -and $PSBoundParameters.ContainsKey("OwnStartingStronghold")) { throw "OwnStartingStronghold requires -Strongholds." }
$root = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $root "artifacts\rmg\$(if ($Strongholds) { "strongholds-v22" } elseif ($DividedLands) { "divided-lands-v21" } elseif ($Ring) { "ring-v20" } elseif ($Crossroads) { "crossroads-v19" } elseif ($Battlefield) { "battlefield-v18" } elseif ($Pvp) { "regions-v17-pvp" } else { "regions-v16" })\$Seed-$MapSize-$Players-$TerrainComplexity-W$WaterAmount-G$GravelMossAmount-N$NeutralColonyDensity$(if ($Strongholds) { "-Castles$GenerateCastles" })-R$OriginalSurfaceRelations-O$PreventColonyOverlapping$(if (!$RespectStartingSafeArea) { "-NoSafeArea" })$(if ($OwnStartingStronghold) { "-OwnStronghold" })-C$AntsWeight-$BeetlesWeight-$ScorpionsWeight-$SpidersWeight-$WaspsWeight-S$($StartingColonyShares -join '-')$(if ($StartingColonyMode -eq 'random') { '-random' })$(if ($Tileset -ne 'NORMAL') { '-' + $Tileset })$(if ($Pvp) { '-axes' + $MirroringAxes })$(if ($Battlefield) { '-' + $BlockShape + '-' + $LaneWidth })$(if ($DividedLands) { '-' + $LandCrossings + '-' + $CrossingWidth })$(if ($Ring) { '-' + $RingShape + '-' + $RingWidth })$(if ($Crossroads) { '-' + $LaneWidth + '-' + $SideConnections })"
}
[IO.Directory]::CreateDirectory($OutputDirectory) | Out-Null
$settingsPath = Join-Path $OutputDirectory "settings.json"
if ((Test-Path -LiteralPath $settingsPath) -and !$Overwrite) {
    throw "Output already exists. Choose a new directory or pass -Overwrite."
}
$settings = [ordered]@{
    starting_colony_shares = @(if ($StartingColonyShares.Count -eq 0) { @(0) * $Players } else { $StartingColonyShares })
    schema_version = $(if ($Strongholds) { 16 } elseif ($DividedLands) { 15 } elseif ($Ring) { 14 } elseif ($Crossroads) { 13 } elseif ($Battlefield) { 12 } elseif ($Pvp) { 11 } else { 10 })
    preset = "balanced"
    seed = $Seed.ToString([Globalization.CultureInfo]::InvariantCulture)
    size = "$MapSize,$MapSize"
    players = $Players
    layout_family = $(if ($Strongholds) { "strongholds" } elseif ($DividedLands) { "divided-lands" } elseif ($Ring) { "ring" } elseif ($Crossroads) { "crossroads" } elseif ($Battlefield) { "artificial-battlefield" } elseif ($Pvp) { "natural-landscape-pvp" } else { "natural-landscape" })
    neutral_colony_density = $NeutralColonyDensity
    water_amount = $WaterAmount
    gravel_moss_amount = $GravelMossAmount
    terrain_complexity = $TerrainComplexity
    original_surface_relations = $OriginalSurfaceRelations
    prevent_colony_overlapping = $PreventColonyOverlapping
    neutral_colony_weights = [ordered]@{ ants = $AntsWeight; beetles = $BeetlesWeight; scorpions = $ScorpionsWeight; spiders = $SpidersWeight; wasps = $WaspsWeight }
}
if (!$RespectStartingSafeArea -or $OwnStartingStronghold) { $settings.schema_version = 17 }
if (!$RespectStartingSafeArea) { $settings.respect_starting_safe_area = $false }
if ($OwnStartingStronghold) { $settings.own_starting_stronghold = $true }
if ($Strongholds) { $settings.generate_castles = $GenerateCastles }
if ($DividedLands) { $settings.land_crossings = $LandCrossings.ToLowerInvariant(); $settings.crossing_width = $CrossingWidth.ToLowerInvariant() }
if ($Ring) { $settings.ring_shape = $RingShape.ToLowerInvariant(); $settings.ring_width = $RingWidth.ToLowerInvariant() }
if ($Pvp) { $settings.mirroring_axes = $MirroringAxes }
if ($Battlefield) { $settings.block_shape = $BlockShape; $settings.lane_width = $LaneWidth }
if ($Crossroads) { $settings.approach_width = $LaneWidth; $settings.side_connections = $SideConnections }
if ($Tileset -ne "NORMAL") { $settings.tileset = $Tileset }
if ($StartingColonyMode -ne "closest-to-spawn") { $settings.starting_colony_mode = $StartingColonyMode }
$settings | ConvertTo-Json | Set-Content -LiteralPath $settingsPath -Encoding utf8
& (Join-Path $PSScriptRoot "Invoke-MapGenerator.ps1") -PlayerSettingsPath $settingsPath `
    -OutputPath (Join-Path $OutputDirectory "map.oramap") -ReportPath (Join-Path $OutputDirectory "report.json") `
    -VerifyRepeatability:$VerifyRepeatability -Overwrite:$Overwrite
