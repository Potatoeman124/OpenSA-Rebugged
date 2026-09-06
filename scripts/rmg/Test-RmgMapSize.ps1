[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$OutputRoot,
    [ValidateSet(128, 256)]
    [int]$MapSize = 256,
    [string]$BaselineRoot
)

$ErrorActionPreference = "Stop"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
if (![IO.Path]::IsPathRooted($OutputRoot)) { $OutputRoot = Join-Path $root $OutputRoot }
if ($BaselineRoot -and ![IO.Path]::IsPathRooted($BaselineRoot)) { $BaselineRoot = Join-Path $root $BaselineRoot }
Add-Type -AssemblyName System.IO.Compression.FileSystem
$directories = @(Get-ChildItem -LiteralPath (Join-Path $OutputRoot "runs") -Directory)
if ($directories.Count -eq 0) { throw "No map runs found." }
$records = foreach ($directory in $directories)
{
    $report = Get-Content -LiteralPath (Join-Path $directory.FullName "report.json") -Raw | ConvertFrom-Json
    $settings = Get-Content -LiteralPath (Join-Path $directory.FullName "player-settings.json") -Raw | ConvertFrom-Json
    $archive = [IO.Compression.ZipFile]::OpenRead((Join-Path $directory.FullName "map.oramap"))
    try
    {
        $reader = [IO.StreamReader]::new($archive.GetEntry("map.yaml").Open())
        try { $yaml = $reader.ReadToEnd() } finally { $reader.Dispose() }
    }
    finally { $archive.Dispose() }
    $stored = $MapSize + 4
    if ($yaml -notmatch "(?m)^MapSize: $stored,$stored\r?$" -or
        $yaml -notmatch "(?m)^Bounds: 2,2,$MapSize,$MapSize\r?$")
    {
        throw "Run $($directory.Name) has unexpected stored geometry or playable bounds."
    }
    if ($report.players -ne $settings.players -or
        $report.player_settings.normalized.size -ne "$MapSize,$MapSize" -or
        $report.validation.accepted -ne $true -or
        $report.movement_validation.native.accepted -ne $true)
    {
        throw "Run $($directory.Name) did not prove the requested settings and both validation gates."
    }
    if ($MapSize -eq 256 -and $report.configuration_id -ne "normal-natural-landscape-v10-256")
    {
        throw "Large map used the wrong profile."
    }
    $baselineMatched = $null
    if ($BaselineRoot)
    {
        $baselineDirectory = @(Get-ChildItem -LiteralPath (Join-Path $BaselineRoot "runs") -Directory |
            Where-Object { $_.Name -match ("-" + [regex]::Escape([string]$report.seed) + "$") })
        if ($baselineDirectory.Count -ne 1) { throw "Expected one baseline for seed $($report.seed)." }
        $baseline = Get-Content -LiteralPath (Join-Path $baselineDirectory[0].FullName "report.json") -Raw | ConvertFrom-Json
        foreach ($hash in @("logical_hash_sha256", "actor_hash_sha256", "graph_hash_sha256",
            "canonical_map_yaml_bin_sha256", "engine_uid_sha1"))
        {
            if ($report.$hash -ne $baseline.$hash) { throw "Seed $($report.seed) changed $hash." }
        }
        $baselineMatched = $true
    }
    [PSCustomObject]@{
        seed = [string]$report.seed
        playable_size = $MapSize
        stored_size = $stored
        players = $report.players
        colonies = $report.neutral_colonies
        colonies_requested = $report.neutral_colonies_requested
        engine_ms = $report.performance.total_ms
        baseline_hashes_matched = $baselineMatched
    }
}
$result = [ordered]@{ status = "passed"; maps = $records.Count; records = @($records) }
$result | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $OutputRoot "size-validation.json") -Encoding UTF8
Write-Host "Map-size validation passed for $($records.Count) map(s)." -ForegroundColor Green
