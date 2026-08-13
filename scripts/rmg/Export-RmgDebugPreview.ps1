[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ReportPath,
    [string]$OutputPath,
    [ValidateRange(4, 16)]
    [int]$CellSize = 8
)

$ErrorActionPreference = "Stop"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
if (![System.IO.Path]::IsPathRooted($ReportPath))
{
    $ReportPath = Join-Path $root $ReportPath
}

$ReportPath = (Resolve-Path -LiteralPath $ReportPath).Path
if ([string]::IsNullOrWhiteSpace($OutputPath))
{
    $OutputPath = [System.IO.Path]::ChangeExtension($ReportPath, ".png")
}
elseif (![System.IO.Path]::IsPathRooted($OutputPath))
{
    $OutputPath = Join-Path $root $OutputPath
}

$report = Get-Content -LiteralPath $ReportPath -Raw | ConvertFrom-Json
$layers = $report.blocking_topology.debug_layers
if ($null -eq $layers -or @($layers.topology).Count -eq 0)
{
    throw "The report does not contain blocking_topology.debug_layers."
}

$topology = @($layers.topology)
$routes = @($layers.routes)
$clearances = @($layers.clearances)
$chokesRepairs = @($layers.chokes_repairs)
$height = $topology.Count
$width = $topology[0].Length
if ($height -eq 0 -or $width -eq 0 -or $routes.Count -ne $height -or $clearances.Count -ne $height -or $chokesRepairs.Count -ne $height)
{
    throw "Debug layers have inconsistent dimensions."
}

Add-Type -AssemblyName System.Drawing
$margin = 24
$legendWidth = 330
$headerHeight = 72
$imageWidth = 2 * $margin + $width * $CellSize + $legendWidth
$imageHeight = [Math]::Max(2 * $margin + $headerHeight + $height * $CellSize, 660)
$bitmap = [System.Drawing.Bitmap]::new($imageWidth, $imageHeight)
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::None
$graphics.Clear([System.Drawing.Color]::FromArgb(246, 246, 242))

$font = [System.Drawing.Font]::new("Segoe UI", 10)
$smallFont = [System.Drawing.Font]::new("Segoe UI", 8)
$titleFont = [System.Drawing.Font]::new("Segoe UI Semibold", 14)
$textBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(35, 35, 35))
$openBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(231, 224, 188))
$blockedBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(42, 91, 127))
$routeBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(145, 64, 145, 82))
$junctionBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(185, 230, 156, 50))
$chokeBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(230, 205, 45, 139))
$startPen = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(210, 45, 45), 1)
$colonyPen = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(105, 45, 155), 1)
$bothPen = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(20, 20, 20), 1)
$repairPen = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(235, 35, 35), 2)
$borderPen = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(55, 55, 55), 1)

try
{
    $title = "OpenSA RMG topology preview - seed $($report.seed)"
    $subtitle = "$($report.players)P / $($report.neutral_colonies) colonies / $($report.symmetry) / $($report.archetype) / $($report.topology_preset)"
    $graphics.DrawString($title, $titleFont, $textBrush, $margin, $margin)
    $graphics.DrawString($subtitle, $font, $textBrush, $margin, $margin + 30)

    $originX = $margin
    $originY = $margin + $headerHeight
    for ($y = 0; $y -lt $height; $y++)
    {
        for ($x = 0; $x -lt $width; $x++)
        {
            $rectangle = [System.Drawing.Rectangle]::new($originX + $x * $CellSize, $originY + $y * $CellSize, $CellSize, $CellSize)
            $graphics.FillRectangle($(if ($topology[$y][$x] -eq '#') { $blockedBrush } else { $openBrush }), $rectangle)
            if ($routes[$y][$x] -eq 'R')
            {
                $graphics.FillRectangle($routeBrush, $rectangle)
            }
            elseif ($routes[$y][$x] -eq 'J')
            {
                $graphics.FillRectangle($junctionBrush, $rectangle)
            }

            if ($chokesRepairs[$y][$x] -eq 'K')
            {
                $graphics.FillRectangle($chokeBrush, $rectangle)
            }
            elseif ($chokesRepairs[$y][$x] -eq 'X')
            {
                $graphics.DrawLine($repairPen, $rectangle.Left, $rectangle.Top, $rectangle.Right, $rectangle.Bottom)
                $graphics.DrawLine($repairPen, $rectangle.Right, $rectangle.Top, $rectangle.Left, $rectangle.Bottom)
            }

            $clearance = $clearances[$y][$x]
            if ($clearance -eq 'S')
            {
                $graphics.DrawRectangle($startPen, $rectangle)
            }
            elseif ($clearance -eq 'C')
            {
                $graphics.DrawRectangle($colonyPen, $rectangle)
            }
            elseif ($clearance -eq 'B')
            {
                $graphics.DrawRectangle($bothPen, $rectangle)
            }
        }
    }

    $graphics.DrawRectangle($borderPen, $originX, $originY, $width * $CellSize, $height * $CellSize)
    $legendX = $originX + $width * $CellSize + 28
    $legendY = $originY
    $graphics.DrawString("Semantic layers", $titleFont, $textBrush, $legendX, $legendY)
    $legend = @(
        [pscustomobject]@{ Label = "OPEN terrain"; Brush = $openBrush; Pen = $null },
        [pscustomobject]@{ Label = "BLOCKED Water"; Brush = $blockedBrush; Pen = $null },
        [pscustomobject]@{ Label = "Reserved route"; Brush = $routeBrush; Pen = $null },
        [pscustomobject]@{ Label = "Strategic junction"; Brush = $junctionBrush; Pen = $null },
        [pscustomobject]@{ Label = "Choke aperture"; Brush = $chokeBrush; Pen = $null },
        [pscustomobject]@{ Label = "Start clearance"; Brush = $null; Pen = $startPen },
        [pscustomobject]@{ Label = "Colony clearance"; Brush = $null; Pen = $colonyPen },
        [pscustomobject]@{ Label = "Repair cell"; Brush = $null; Pen = $repairPen }
    )

    $legendY += 42
    foreach ($entry in $legend)
    {
        $box = [System.Drawing.Rectangle]::new($legendX, $legendY, 18, 18)
        if ($null -ne $entry.Brush)
        {
            $graphics.FillRectangle($entry.Brush, $box)
        }
        else
        {
            $graphics.DrawRectangle($entry.Pen, $box)
        }

        $graphics.DrawString($entry.Label, $font, $textBrush, $legendX + 28, $legendY - 1)
        $legendY += 28
    }

    $legendY += 10
    $graphics.DrawString("Validation metrics", $titleFont, $textBrush, $legendX, $legendY)
    $legendY += 38
    $metrics = @(
        "Density: $([Math]::Round([double]$report.blocking_topology.obstacle_density_percent, 3))%",
        "Regions: $($report.validation.metrics.obstacle_region_count)",
        "Choke segments: $($report.validation.metrics.chokepoint_segment_count)",
        "Retries: $($report.validation.metrics.retry_count)",
        "Repairs: $($report.validation.metrics.repair_count)",
        "Native min route: $($report.movement_validation.native.metrics.minimum_usable_route_width_native)",
        "Native accepted: $($report.movement_validation.native.accepted)"
    )
    foreach ($metric in $metrics)
    {
        $graphics.DrawString($metric, $smallFont, $textBrush, $legendX, $legendY)
        $legendY += 22
    }

    $outputDirectory = Split-Path -Parent $OutputPath
    if (![string]::IsNullOrWhiteSpace($outputDirectory))
    {
        New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
    }

    $bitmap.Save($OutputPath, [System.Drawing.Imaging.ImageFormat]::Png)
}
finally
{
    $graphics.Dispose()
    $bitmap.Dispose()
    $font.Dispose()
    $smallFont.Dispose()
    $titleFont.Dispose()
    $textBrush.Dispose()
    $openBrush.Dispose()
    $blockedBrush.Dispose()
    $routeBrush.Dispose()
    $junctionBrush.Dispose()
    $chokeBrush.Dispose()
    $startPen.Dispose()
    $colonyPen.Dispose()
    $bothPen.Dispose()
    $repairPen.Dispose()
    $borderPen.Dispose()
}

Write-Host "RMG debug preview: $OutputPath" -ForegroundColor Green
