[CmdletBinding()]
param(
    [ValidateRange(1, 100)]
    [int]$Count = 15,
    [UInt64[]]$Seeds,
    [switch]$StratifyFamilies,
    [string]$OutputRoot,
    [ValidateRange(1, 8)]
    [int]$Players = 4,
    [ValidateSet(128, 256)]
    [int]$MapSize = 128,
    [switch]$AlternatePlayers,
    [ValidateSet("balanced", "open-conflict", "tactical-crossroads")]
    [string]$Preset = "balanced",
    [ValidateSet("open-fields", "contested-center")]
    [string]$BattlefieldPlan = "contested-center",
    [ValidateSet("natural-landscape", "structured-competitive", "artificial-battlefield")]
    [string]$LayoutFamily = "natural-landscape",
    [ValidateSet("sparse", "standard", "dense")]
    [string]$NeutralColonyDensity = "standard",
    [ValidateSet("low", "standard", "high")]
    [string]$WaterAmount = "standard",
    [ValidateSet("low", "standard", "high")]
    [string]$TacticalTerrain = "standard",
    [bool]$OriginalSurfaceRelations = $true,
    [ValidateRange(2, 8)]
    [int]$Columns = 5,
    [switch]$Overwrite
)

$ErrorActionPreference = "Stop"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$generator = Join-Path $PSScriptRoot "Invoke-MapGenerator.ps1"
$debugPreviewExporter = Join-Path $PSScriptRoot "Export-RmgDebugPreview.ps1"
if ([string]::IsNullOrWhiteSpace($OutputRoot))
{
    $timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
    $OutputRoot = Join-Path $root "artifacts\rmg\visual-review\$timestamp"
}
elseif (![System.IO.Path]::IsPathRooted($OutputRoot))
{
    $OutputRoot = Join-Path $root $OutputRoot
}

if ((Test-Path -LiteralPath $OutputRoot) -and !$Overwrite)
{
    throw "Visual-review output already exists: $OutputRoot. Pass -Overwrite or choose another path."
}

if (!(Test-Path -LiteralPath $generator -PathType Leaf))
{
    throw "Map generator wrapper is missing: $generator"
}

if (!(Test-Path -LiteralPath $debugPreviewExporter -PathType Leaf))
{
    throw "Debug preview exporter is missing: $debugPreviewExporter"
}

function New-RandomDisplaySeed
{
    param([Security.Cryptography.RandomNumberGenerator]$Generator)

    [UInt64]$exclusiveUpperBound = 1000000000000000000
    [UInt64]$rejectionLimit = [UInt64]::MaxValue - ([UInt64]::MaxValue % $exclusiveUpperBound)
    $bytes = New-Object byte[] 8
    do
    {
        $Generator.GetBytes($bytes)
        [UInt64]$value = [BitConverter]::ToUInt64($bytes, 0)
    }
    while ($value -ge $rejectionLimit)

    return [UInt64]($value % $exclusiveUpperBound)
}

function Get-NaturalFamily
{
    param([UInt64]$Seed)
    # Mirror the generator's fixed seed-to-family hash, without floating-point UInt64 rounding.
    Add-Type -AssemblyName System.Numerics
    $mask = [System.Numerics.BigInteger]::Parse("18446744073709551615")
    $z = ([System.Numerics.BigInteger]$Seed + [System.Numerics.BigInteger]::Parse("5570761768844610639") +
        [System.Numerics.BigInteger]::Parse("11400714819323198485")) -band $mask
    $z = (($z -bxor ($z -shr 30)) * [System.Numerics.BigInteger]::Parse("13787848793156543929")) -band $mask
    $z = (($z -bxor ($z -shr 27)) * [System.Numerics.BigInteger]::Parse("10723151780598845931")) -band $mask
    $z = $z -bxor ($z -shr 31)
    return @("lake-district", "coastal-shelf", "river-valley", "inland-sea", "wetlands")[[int]($z % 5)]
}

function Export-MapPreview
{
    param(
        [Parameter(Mandatory = $true)]
        [string]$MapPath,
        [Parameter(Mandatory = $true)]
        [string]$PreviewPath
    )

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($MapPath)
    try
    {
        $entry = $archive.Entries | Where-Object { $_.FullName -ieq "map.png" } | Select-Object -First 1
        if ($null -eq $entry)
        {
            throw "Generated map package does not contain map.png: $MapPath"
        }

        [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $PreviewPath, $true)
    }
    finally
    {
        $archive.Dispose()
    }
}

function New-ContactSheet
{
    param(
        [Parameter(Mandatory = $true)]
        [object[]]$Records,
        [Parameter(Mandatory = $true)]
        [string]$ImageProperty,
        [Parameter(Mandatory = $true)]
        [string]$OutputPath,
        [Parameter(Mandatory = $true)]
        [string]$Title,
        [Parameter(Mandatory = $true)]
        [int]$ColumnCount
    )

    Add-Type -AssemblyName System.Drawing
    $tileWidth = 300
    $tileHeight = 360
    $headerHeight = 54
    $rows = [int][Math]::Ceiling($Records.Count / [double]$ColumnCount)
    $bitmap = [Drawing.Bitmap]::new($tileWidth * $ColumnCount, $headerHeight + $tileHeight * $rows)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    $titleFont = [Drawing.Font]::new("Segoe UI Semibold", 16)
    $labelFont = [Drawing.Font]::new("Consolas", 9)
    $smallFont = [Drawing.Font]::new("Segoe UI", 8)
    $titleBrush = [Drawing.SolidBrush]::new([Drawing.Color]::White)
    $labelBrush = [Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(232, 232, 232))
    $mutedBrush = [Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(170, 170, 170))
    $borderPen = [Drawing.Pen]::new([Drawing.Color]::FromArgb(72, 72, 72), 1)
    try
    {
        $graphics.Clear([Drawing.Color]::FromArgb(18, 18, 18))
        $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
        $graphics.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::Half
        $graphics.DrawString($Title, $titleFont, $titleBrush, 14, 14)
        foreach ($record in $Records)
        {
            $index = [int]$record.run - 1
            $column = $index % $ColumnCount
            $row = [int][Math]::Floor($index / [double]$ColumnCount)
            $originX = $column * $tileWidth
            $originY = $headerHeight + $row * $tileHeight
            $imagePath = [string]$record.$ImageProperty
            $image = [Drawing.Image]::FromFile($imagePath)
            try
            {
                $availableWidth = 272
                $availableHeight = 272
                $scale = [Math]::Min($availableWidth / [double]$image.Width, $availableHeight / [double]$image.Height)
                $drawWidth = [int][Math]::Round($image.Width * $scale)
                $drawHeight = [int][Math]::Round($image.Height * $scale)
                $drawX = $originX + [int](($tileWidth - $drawWidth) / 2)
                $drawY = $originY + 8 + [int](($availableHeight - $drawHeight) / 2)
                $graphics.DrawImage($image, $drawX, $drawY, $drawWidth, $drawHeight)
                $graphics.DrawRectangle($borderPen, $drawX, $drawY, $drawWidth, $drawHeight)
            }
            finally
            {
                $image.Dispose()
            }

            $textY = $originY + 288
            $graphics.DrawString(("{0:D2}  seed {1}" -f [int]$record.run, $record.seed), $labelFont, $labelBrush, $originX + 10, $textY)
            $textY += 20
            $graphics.DrawString(("Water {0:N1}% / interior {1:N1}% / bodies {2}" -f
                [double]$record.water_percent, [double]$record.interior_water_percent, [int]$record.water_bodies),
                $smallFont, $mutedBrush, $originX + 10, $textY)
            $textY += 18
            $graphics.DrawString(("Colonies {0}/{1} / mechanical PASS / review: worksheet" -f
                [int]$record.colonies, [int]$record.colonies_requested),
                $smallFont, $mutedBrush, $originX + 10, $textY)
        }

        $bitmap.Save($OutputPath, [Drawing.Imaging.ImageFormat]::Png)
    }
    finally
    {
        $graphics.Dispose()
        $bitmap.Dispose()
        $titleFont.Dispose()
        $labelFont.Dispose()
        $smallFont.Dispose()
        $titleBrush.Dispose()
        $labelBrush.Dispose()
        $mutedBrush.Dispose()
        $borderPen.Dispose()
    }
}

New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null
$runRoot = Join-Path $OutputRoot "runs"
New-Item -ItemType Directory -Force -Path $runRoot | Out-Null

if ($StratifyFamilies -and (($Count % 5) -ne 0 -or $Seeds.Count -gt 0 -or $LayoutFamily -ne "natural-landscape"))
{
    throw "-StratifyFamilies requires Natural Landscape, no explicit seeds, and a Count divisible by five."
}
$familyCounts = @{}
$selectedSeeds = @()
if ($null -ne $Seeds -and $Seeds.Count -gt 0)
{
    $selectedSeeds = @($Seeds)
}
else
{
    $random = [Security.Cryptography.RandomNumberGenerator]::Create()
    try
    {
        $seen = [Collections.Generic.HashSet[UInt64]]::new()
        while ($selectedSeeds.Count -lt $Count)
        {
            $candidate = New-RandomDisplaySeed -Generator $random
            $family = Get-NaturalFamily -Seed $candidate
            if ($StratifyFamilies -and [int]$familyCounts[$family] -ge ($Count / 5))
            {
                continue
            }
            if ($seen.Add($candidate))
            {
                $selectedSeeds += $candidate
                $familyCounts[$family] = [int]$familyCounts[$family] + 1
            }
        }
    }
    finally
    {
        $random.Dispose()
    }
}

$records = @()
for ($index = 0; $index -lt $selectedSeeds.Count; $index++)
{
    [UInt64]$seed = $selectedSeeds[$index]
    $run = $index + 1
    $directory = Join-Path $runRoot ("{0:D2}-{1}" -f $run, $seed)
    New-Item -ItemType Directory -Force -Path $directory | Out-Null
    $settingsPath = Join-Path $directory "player-settings.json"
    $mapPath = Join-Path $directory "map.oramap"
    $reportPath = Join-Path $directory "report.json"
    $logPath = Join-Path $directory "generation.log"
    $previewPath = Join-Path $directory "map-preview.png"
    $debugPreviewPath = Join-Path $directory "debug-preview.png"

    $settings = [ordered]@{
        schema_version = $(if ($MapSize -eq 256) { 4 } else { 3 })
        preset = $Preset
        seed = $seed.ToString([Globalization.CultureInfo]::InvariantCulture)
        players = $(if ($AlternatePlayers) { if ($index % 2 -eq 0) { 4 } else { 2 } } else { $Players })
        symmetry = "automatic"
        layout = $BattlefieldPlan
        layout_family = $LayoutFamily
        neutral_colony_density = $NeutralColonyDensity
        water_amount = $WaterAmount
        tactical_terrain = $TacticalTerrain
    }
    if ($LayoutFamily -eq "natural-landscape")
    {
        $settings.original_surface_relations = $OriginalSurfaceRelations
    }

    if ($MapSize -eq 256) { $settings.size = "256,256" }
    $settings | ConvertTo-Json | Set-Content -LiteralPath $settingsPath -Encoding UTF8
    $stopwatch = [Diagnostics.Stopwatch]::StartNew()
    $previousErrorActionPreference = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try
    {
        $output = @(& powershell -NoProfile -ExecutionPolicy Bypass -File $generator `
            -PlayerSettingsPath $settingsPath -OutputPath $mapPath -ReportPath $reportPath `
            -MovementValidation both -Overwrite 2>&1)
        $exitCode = $LASTEXITCODE
    }
    finally
    {
        $ErrorActionPreference = $previousErrorActionPreference
    }
    $stopwatch.Stop()
    $output | Set-Content -LiteralPath $logPath -Encoding UTF8
    if ($exitCode -ne 0)
    {
        $failure = [ordered]@{
            schema_version = 1
            status = "mechanical-failure"
            failed_run = $run
            failed_seed = $seed.ToString([Globalization.CultureInfo]::InvariantCulture)
            exit_code = $exitCode
            log = $logPath
            completed_runs = $records
        }
        $failure | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $OutputRoot "failure.json") -Encoding UTF8
        Get-Content -LiteralPath $logPath -Tail 80 | ForEach-Object { Write-Host $_ }
        throw "RMG visual-review generation failed at run $run, seed $seed. See $logPath"
    }

    Export-MapPreview -MapPath $mapPath -PreviewPath $previewPath
    & powershell -NoProfile -ExecutionPolicy Bypass -File $debugPreviewExporter `
        -ReportPath $reportPath -OutputPath $debugPreviewPath -CellSize 6 *> (Join-Path $directory "debug-preview.log")
    if ($LASTEXITCODE -ne 0)
    {
        throw "Debug preview export failed at run $run, seed $seed."
    }

    $report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
    if ($LayoutFamily -eq "natural-landscape" -and $OriginalSurfaceRelations)
    {
        $metrics = $report.validation.metrics
        if ($metrics.natural_surface_authority_enforced -ne 1 -or
            $metrics.placement_changed_native_surface_cells -ne 0 -or
            $metrics.placement_changed_terrain_templates -ne 0 -or
            $metrics.placement_changed_water_cells -ne 0 -or
            $report.movement_validation.native.metrics.original_surface_dirt_placement_enforced -ne $true -or
            $report.movement_validation.native.metrics.non_dirt_start_coverage_cells -ne 0 -or
            $report.movement_validation.native.metrics.non_dirt_neutral_colony_coverage_cells -ne 0)
        {
            throw "Surface authority was not proven at run $run, seed $seed."
        }
    }
    if ($LayoutFamily -eq "natural-landscape" -and
        $report.natural_landscape.variant -notlike ("v10-" + (Get-NaturalFamily -Seed $seed) + "*"))
    {
        throw "Generation changed the seed's landscape family: $seed"
    }
    $record = [PSCustomObject][ordered]@{
        run = $run
        seed = $seed.ToString([Globalization.CultureInfo]::InvariantCulture)
        mechanical_status = "passed"
        map_size = $MapSize
        players = $settings.players
        morphology = $report.natural_landscape.variant
        boundary_risk = $report.validation.metrics.natural_visual_risk
        longest_water_run = $report.validation.metrics.natural_water_longest_run_native
        projection_changed_percent = $report.validation.metrics.natural_projection_changed_percent
        generation_ms = [Math]::Round($stopwatch.Elapsed.TotalMilliseconds, 1)
        engine_generation_ms = $report.performance.total_ms
        water_percent = [Math]::Round([double]$report.blocking_topology.obstacle_density_percent, 3)
        interior_water_percent = [Math]::Round([double]$report.blocking_topology.interior_density_percent, 3)
        water_bodies = [int]$report.blocking_topology.water_body_count
        largest_body_share_percent = [Math]::Round([double]$report.blocking_topology.largest_body_share_percent, 3)
        colonies = [int]$report.neutral_colonies
        colonies_requested = [int]$report.neutral_colonies_requested
        map = $mapPath
        report = $reportPath
        log = $logPath
        map_preview = $previewPath
        debug_preview = $debugPreviewPath
        visual_status = "UNREVIEWED"
        visual_notes = ""
    }
    $records += $record
    Write-Host ("PASS {0:D2}/{1} seed={2}; preview={3}" -f $run, $selectedSeeds.Count, $seed, $previewPath) -ForegroundColor Green
}

$contactSheetPath = Join-Path $OutputRoot "map-preview-contact-sheet.png"
$debugContactSheetPath = Join-Path $OutputRoot "debug-preview-contact-sheet.png"
New-ContactSheet -Records $records -ImageProperty "map_preview" -OutputPath $contactSheetPath `
    -Title "OpenSA RMG visual review - actual map previews" -ColumnCount $Columns
New-ContactSheet -Records $records -ImageProperty "debug_preview" -OutputPath $debugContactSheetPath `
    -Title "OpenSA RMG visual review - topology diagnostics" -ColumnCount $Columns

$manifest = [ordered]@{
    schema_version = 1
    status = "awaiting-visual-review"
    family_stratified = [bool]$StratifyFamilies
    policy = "Mechanical success is not visual acceptance. Every successful generation must be inspected and assigned ACCEPT or REJECT before a user review corpus is prepared."
    generated_at = (Get-Date).ToString("o", [Globalization.CultureInfo]::InvariantCulture)
    configuration = [ordered]@{
        preset = $Preset
        players = $Players
        alternate_players = [bool]$AlternatePlayers
        map_size = $MapSize
        battlefield_plan = $BattlefieldPlan
        layout_family = $LayoutFamily
        neutral_colony_density = $NeutralColonyDensity
        water_amount = $WaterAmount
        tactical_terrain = $TacticalTerrain
        original_surface_relations = $OriginalSurfaceRelations
    }
    contact_sheet = $contactSheetPath
    debug_contact_sheet = $debugContactSheetPath
    records = $records
}
$manifestPath = Join-Path $OutputRoot "visual-review-manifest.json"
$manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $manifestPath -Encoding UTF8

$reviewRows = $records | Select-Object run, seed, mechanical_status, visual_status, `
    @{ Name = "natural_shapes"; Expression = { "UNREVIEWED" } }, `
    @{ Name = "boundary_quality"; Expression = { "UNREVIEWED" } }, `
    @{ Name = "surface_relations"; Expression = { "UNREVIEWED" } }, `
    @{ Name = "layout_distinctiveness"; Expression = { "UNREVIEWED" } }, visual_notes
$reviewCsvPath = Join-Path $OutputRoot "visual-review.csv"
$reviewRows | Export-Csv -LiteralPath $reviewCsvPath -NoTypeInformation -Encoding UTF8

$guide = @"
# RMG visual review gate

Mechanical generation completed for $($records.Count) map(s). This corpus is **not review-ready** until every row in ``visual-review.csv`` is inspected.

Inspect ``map-preview-contact-sheet.png`` first, then use the individual map and debug previews when a candidate needs closer inspection.

For every successful map, classify:

- ``natural_shapes``: organic macro geography rather than rectangles, corridors, or obvious construction;
- ``boundary_quality``: no dominant staircase, long orthogonal run, square basin, or block-grid signature;
- ``surface_relations``: transitions look geologically composed rather than thin mandatory halos;
- ``layout_distinctiveness``: materially different composition from the other seeds;
- ``visual_status``: ``ACCEPT`` only if every axis is acceptable, otherwise ``REJECT`` with a concrete note.

A mechanical PASS never overrides a visual REJECT. Any rejected candidate must feed back into implementation before a user-facing review corpus is prepared.
"@
$guidePath = Join-Path $OutputRoot "REVIEW-GATE.md"
$guide | Set-Content -LiteralPath $guidePath -Encoding UTF8

Write-Host "Visual review manifest: $manifestPath" -ForegroundColor Cyan
Write-Host "Actual-preview contact sheet: $contactSheetPath" -ForegroundColor Cyan
Write-Host "Diagnostic contact sheet: $debugContactSheetPath" -ForegroundColor Cyan
Write-Host "Review worksheet: $reviewCsvPath" -ForegroundColor Yellow
Write-Host "Status: awaiting explicit visual review; this corpus is not ready for user delivery." -ForegroundColor Yellow
