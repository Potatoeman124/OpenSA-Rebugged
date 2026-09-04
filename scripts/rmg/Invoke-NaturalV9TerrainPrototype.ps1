[CmdletBinding()]
param(
    [string]$OutputRoot,
    [UInt64[]]$RootSeeds = @(92001, 92002, 92003),
    [ValidateRange(0, 7)]
    [int]$CandidateIndex = 0,
    [ValidateSet("correlated-field-baseline", "correlated-field-with-basin-potential")]
    [string]$Variant = "correlated-field-baseline",
    [bool]$OriginalSurfaceRelations = $true,
    [switch]$GenerateMatrix,
    [switch]$Overwrite
)

$ErrorActionPreference = "Stop"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$engineRoot = Join-Path $root "engine"
$utility = Join-Path $engineRoot "bin\OpenRA.Utility.exe"
$dotnetRoot = Join-Path $root ".tools\dotnet"
if ([string]::IsNullOrWhiteSpace($OutputRoot))
{
    $OutputRoot = Join-Path $root "artifacts\rmg\natural-v9-surface-relations\candidates"
}
elseif (![System.IO.Path]::IsPathRooted($OutputRoot))
{
    $OutputRoot = Join-Path $root $OutputRoot
}

if (!(Test-Path -LiteralPath $utility -PathType Leaf))
{
    throw "OpenRA.Utility.exe is missing. Run build-pipeline.cmd validate first."
}

$env:DOTNET_ROOT = $dotnetRoot
$env:DOTNET_MULTILEVEL_LOOKUP = "0"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
$env:DOTNET_NOLOGO = "1"
$env:ENGINE_DIR = ".."
$env:MOD_SEARCH_PATHS = "$root\mods,.\mods"
$env:PATH = "$dotnetRoot;$env:PATH"

$jobs = @()
if ($GenerateMatrix)
{
    foreach ($variantId in @("correlated-field-baseline", "correlated-field-with-basin-potential"))
    {
        $variantDirectory = if ($variantId -eq "correlated-field-baseline") { "variant-a" } else { "variant-b" }
        foreach ($rootSeed in $RootSeeds)
        {
            foreach ($index in 0..7)
            {
                $jobs += [PSCustomObject]@{
                    variant = $variantId
                    variant_directory = $variantDirectory
                    root_seed = [UInt64]$rootSeed
                    candidate_index = $index
                }
            }
        }
    }
}
else
{
    $jobs += [PSCustomObject]@{
        variant = $Variant
        variant_directory = if ($Variant -eq "correlated-field-baseline") { "variant-a" } else { "variant-b" }
        root_seed = [UInt64]$RootSeeds[0]
        candidate_index = $CandidateIndex
    }
}

$records = @()
$first = $true
Push-Location $engineRoot
try
{
    foreach ($job in $jobs)
    {
        $relationDirectory = if ($OriginalSurfaceRelations) { "original" } else { "unrestricted" }
        $candidateDirectory = Join-Path $OutputRoot "$relationDirectory\$($job.variant_directory)\root-$($job.root_seed)\candidate-$($job.candidate_index.ToString('00'))"
        $arguments = @(
            "sa",
            "--prototype-natural-v9-terrain",
            $candidateDirectory,
            "--root-seed", $job.root_seed.ToString(),
            "--candidate-index", $job.candidate_index.ToString(),
            "--variant", $job.variant,
            "--original-surface-relations", $OriginalSurfaceRelations.ToString().ToLowerInvariant()
        )
        if ($Overwrite)
        {
            $arguments += "--overwrite"
        }

        if ($first)
        {
            $arguments += "--self-test"
            $first = $false
        }

        $total = [System.Diagnostics.Stopwatch]::StartNew()
        $output = @(& $utility $arguments 2>&1)
        $exitCode = $LASTEXITCODE
        $total.Stop()
        $output | ForEach-Object { Write-Host $_ }
        if ($exitCode -ne 0)
        {
            throw "Natural V9 terrain prototype generation failed with exit code $exitCode."
        }

        $generation = [double]::NaN
        $export = [double]::NaN
        foreach ($line in $output)
        {
            if ($line -match '^Generation milliseconds: ([0-9.]+)$')
            {
                $generation = [double]::Parse($Matches[1], [Globalization.CultureInfo]::InvariantCulture)
            }
            elseif ($line -match '^Export milliseconds: ([0-9.]+)$')
            {
                $export = [double]::Parse($Matches[1], [Globalization.CultureInfo]::InvariantCulture)
            }
        }

        $records += [PSCustomObject]@{
            variant = $job.variant
            root_seed = $job.root_seed.ToString()
            candidate_index = $job.candidate_index
            candidate_directory = $candidateDirectory
            generation_ms = $generation
            export_ms = $export
            total_process_ms = $total.Elapsed.TotalMilliseconds
        }
    }
}
finally
{
    Pop-Location
}

if ($GenerateMatrix)
{
    $metricsDirectory = Join-Path (Split-Path -Parent $OutputRoot) "metrics"
    New-Item -ItemType Directory -Force -Path $metricsDirectory | Out-Null
    $performancePath = Join-Path $metricsDirectory "generation-performance.json"
    $payload = [ordered]@{
        schema_version = 2
        prototype_id = "natural-v9-terrain-prototype-step2"
        original_surface_relations = $OriginalSurfaceRelations
        candidate_count = $records.Count
        records = $records
    }
    $payload | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $performancePath -Encoding utf8
    Write-Host "Generation performance: $performancePath" -ForegroundColor Green
}

Write-Host "Generated $($records.Count) terrain-only prototype candidate(s)." -ForegroundColor Green
