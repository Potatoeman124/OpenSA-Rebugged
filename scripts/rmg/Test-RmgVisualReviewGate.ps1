[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$OutputRoot
)

$ErrorActionPreference = "Stop"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
if (![System.IO.Path]::IsPathRooted($OutputRoot))
{
    $OutputRoot = Join-Path $root $OutputRoot
}

$OutputRoot = (Resolve-Path -LiteralPath $OutputRoot).Path
$reviewPath = Join-Path $OutputRoot "visual-review.csv"
if (!(Test-Path -LiteralPath $reviewPath -PathType Leaf))
{
    throw "Visual review worksheet is missing: $reviewPath"
}

$rows = @(Import-Csv -LiteralPath $reviewPath)
if ($rows.Count -eq 0)
{
    throw "Visual review worksheet contains no rows: $reviewPath"
}

$axes = @("natural_shapes", "boundary_quality", "surface_relations", "layout_distinctiveness")
$problems = @()
$rejections = @()
foreach ($row in $rows)
{
    $prefix = "Run $($row.run), seed $($row.seed)"
    if ($row.mechanical_status -ne "passed")
    {
        $problems += "$prefix is not a mechanical pass."
        continue
    }

    $status = $row.visual_status.ToUpperInvariant()
    if ($status -notin @("ACCEPT", "REJECT"))
    {
        $problems += "$prefix has visual_status '$($row.visual_status)'; expected ACCEPT or REJECT."
        continue
    }

    $axisValues = @{}
    foreach ($axis in $axes)
    {
        $value = $row.$axis.ToUpperInvariant()
        $axisValues[$axis] = $value
        if ($value -notin @("ACCEPT", "REJECT"))
        {
            $problems += "$prefix has $axis '$($row.$axis)'; expected ACCEPT or REJECT."
        }
    }

    $failedAxes = @($axes | Where-Object { $axisValues[$_] -eq "REJECT" })
    if ($status -eq "ACCEPT" -and $failedAxes.Count -gt 0)
    {
        $problems += "$prefix is marked ACCEPT but rejects axis/axes: $($failedAxes -join ', ')."
    }
    elseif ($status -eq "REJECT" -and $failedAxes.Count -eq 0)
    {
        $problems += "$prefix is marked REJECT but no review axis identifies the defect."
    }

    if ($status -eq "REJECT")
    {
        if ([string]::IsNullOrWhiteSpace($row.visual_notes))
        {
            $problems += "$prefix is rejected without concrete visual_notes."
        }

        $rejections += [PSCustomObject]@{
            run = [int]$row.run
            seed = $row.seed
            rejected_axes = $failedAxes
            notes = $row.visual_notes
        }
    }
}

$gateStatus = if ($problems.Count -gt 0) { "incomplete-or-invalid" } elseif ($rejections.Count -gt 0) { "blocked-by-visual-rejection" } else { "accepted" }
$result = [ordered]@{
    schema_version = 1
    status = $gateStatus
    reviewed_maps = $rows.Count
    accepted_maps = @($rows | Where-Object { $_.visual_status.ToUpperInvariant() -eq "ACCEPT" }).Count
    rejected_maps = $rejections.Count
    problems = $problems
    rejections = $rejections
}
$resultPath = Join-Path $OutputRoot "visual-review-gate.json"
$result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $resultPath -Encoding UTF8

# Keep the saved manifest consistent with the reviewed worksheet; previews remain unchanged.
$manifestPath = Join-Path $OutputRoot "visual-review-manifest.json"
if ($problems.Count -eq 0 -and (Test-Path -LiteralPath $manifestPath))
{
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    foreach ($record in $manifest.records)
    {
        $row = $rows | Where-Object { [int]$_.run -eq [int]$record.run -and $_.seed -eq $record.seed } | Select-Object -First 1
        if ($null -eq $row) { throw "Missing reviewed record for run $($record.run)." }
        $record.visual_status = $row.visual_status
        $record.visual_notes = $row.visual_notes
    }
    $manifest.status = $gateStatus
    $manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $manifestPath -Encoding UTF8
}

if ($problems.Count -gt 0)
{
    $problems | ForEach-Object { Write-Host $_ -ForegroundColor Red }
    Write-Host "Visual gate: INCOMPLETE. Result: $resultPath" -ForegroundColor Red
    exit 2
}

if ($rejections.Count -gt 0)
{
    $rejections | ForEach-Object { Write-Host ("REJECT run {0}, seed {1}: {2}" -f $_.run, $_.seed, $_.notes) -ForegroundColor Yellow }
    Write-Host "Visual gate: BLOCKED by $($rejections.Count) rejected map(s). Result: $resultPath" -ForegroundColor Yellow
    exit 1
}

Write-Host "Visual gate: ACCEPTED for all $($rows.Count) maps. Result: $resultPath" -ForegroundColor Green
