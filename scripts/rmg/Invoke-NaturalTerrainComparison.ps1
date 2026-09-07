[CmdletBinding()]
param(
    [string]$OutputDirectory,
    [ValidateSet(128, 256)][int]$MapSize = 256,
    [string]$ManifestPath,
    [switch]$DiagnosticsOnly
)

$ErrorActionPreference = "Stop"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
if (!$OutputDirectory) { $OutputDirectory = Join-Path $root ("artifacts\rmg\reassessment-comparison\" + (Get-Date -Format "yyyyMMdd-HHmmss")) }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $OutputDirectory) { throw "Output directory already exists; preserve previous attempts." }
New-Item -ItemType Directory -Path $OutputDirectory | Out-Null
if ($ManifestPath)
{
    $manifest = Get-Content -Raw -LiteralPath $ManifestPath | ConvertFrom-Json
    $seeds = @($manifest.fresh_seeds)
}
else
{
    $rng = [Security.Cryptography.RandomNumberGenerator]::Create()
    try
    {
        $seeds = @(1..2 | ForEach-Object {
            $bytes = New-Object byte[] 8
            $rng.GetBytes($bytes)
            [BitConverter]::ToUInt64($bytes, 0).ToString()
        })
    }
    finally { $rng.Dispose() }
}
$cases = @()
if ($DiagnosticsOnly)
{
    foreach ($seed in @("265249412814339965", "464831658165234256"))
    {
        foreach ($method in @("Fields", "Regions"))
        { $cases += [ordered]@{ seed = $seed; size = 256; method = $method; complexity = "Standard"; diagnostic = $true } }
    }
}
else
{
    foreach ($seed in $seeds)
    {
        foreach ($method in @("Fields", "Regions"))
        {
            foreach ($complexity in @("Low", "Standard", "High"))
            { $cases += [ordered]@{ seed = $seed; size = $MapSize; method = $method; complexity = $complexity; diagnostic = $false } }
        }
    }
}
$sourceHashes = @{}
Get-ChildItem -LiteralPath (Join-Path $root "OpenRA.Mods.OpenSA\Rmg\Reassessment") -Filter "*.cs" | ForEach-Object {
    $sourceHashes[$_.Name] = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
}
[ordered]@{
    experiment = "natural-reassessment-comparison-v1"; status = "PREDECLARED_DESIGN_COMPARISON"
    fresh_seeds = $seeds; seed_source = "cryptographic draws recorded before generation; supplied manifest reused when present"
    created_utc = [DateTime]::UtcNow.ToString("o"); source_sha256 = $sourceHashes
    assembly_sha256 = (Get-FileHash -LiteralPath (Join-Path $root "engine\bin\OpenRA.Mods.OpenSA.dll")).Hash.ToLowerInvariant()
    cases = $cases
} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $OutputDirectory "manifest.json") -Encoding UTF8

$dotnetRoot = Join-Path $root ".tools\dotnet"
$env:DOTNET_ROOT = $dotnetRoot
$env:DOTNET_MULTILEVEL_LOOKUP = "0"
$env:ENGINE_DIR = ".."
$env:MOD_SEARCH_PATHS = "$root\mods,.\mods"
$env:PATH = "$dotnetRoot;$env:PATH"
$utility = Join-Path $root "engine\bin\OpenRA.Utility.exe"
Push-Location (Join-Path $root "engine")
try
{
    & $utility sa --compare-natural-terrain --self-test *> (Join-Path $OutputDirectory "self-tests.log")
    if ($LASTEXITCODE -ne 0) { throw "Comparison self-tests failed; see self-tests.log." }
    foreach ($case in $cases)
    {
        $name = "$($case.size)-$($case.seed)-$($case.method)-$($case.complexity)"
        $caseDirectory = Join-Path $OutputDirectory $name
        $timer = [Diagnostics.Stopwatch]::StartNew()
        & $utility sa --compare-natural-terrain $caseDirectory $case.seed $case.size $case.method $case.complexity *> (Join-Path $OutputDirectory "$name.log")
        $code = $LASTEXITCODE
        [ordered]@{ case = $name; exit_code = $code; process_ms = $timer.Elapsed.TotalMilliseconds } |
            ConvertTo-Json -Compress | Add-Content -LiteralPath (Join-Path $OutputDirectory "attempts.jsonl") -Encoding UTF8
        if ($code -ne 0) { throw "Comparison stopped at $name (exit $code). Retain this attempt and inspect its log." }
        $report = Get-Content -Raw -LiteralPath (Join-Path $caseDirectory "report.json") | ConvertFrom-Json
        Write-Host ("{0}: water {1:N2}%, gravel {2:N2}% land, moss {3:N2}% land, largest dirt square {4}" -f $name,
            $report.metrics.water_percent_map, $report.metrics.gravel_percent_land, $report.metrics.moss_percent_land,
            $report.metrics.largest_all_dirt_square_native)
    }
}
finally { Pop-Location }
Write-Host "Comparison artifacts: $OutputDirectory"
