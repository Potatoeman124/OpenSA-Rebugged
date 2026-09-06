[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$BaselineRoot,
    [Parameter(Mandatory = $true)]
    [string]$OutputRoot,
    [switch]$VerifyRepeatability
)

$ErrorActionPreference = "Stop"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
if (![IO.Path]::IsPathRooted($BaselineRoot)) { $BaselineRoot = Join-Path $root $BaselineRoot }
if (![IO.Path]::IsPathRooted($OutputRoot)) { $OutputRoot = Join-Path $root $OutputRoot }
if (Test-Path -LiteralPath $OutputRoot) { throw "Choose a new output directory; existing evidence is not overwritten." }
$directories = @(Get-ChildItem -LiteralPath (Join-Path $BaselineRoot "runs") -Directory | Sort-Object Name)
if ($directories.Count -eq 0) { throw "No baseline runs found." }
New-Item -ItemType Directory -Path $OutputRoot | Out-Null
Add-Type -AssemblyName System.IO.Compression.FileSystem

function Get-PreviewHash([string]$MapPath)
{
    $archive = [IO.Compression.ZipFile]::OpenRead($MapPath)
    $sha = [Security.Cryptography.SHA256]::Create()
    try
    {
        $entry = $archive.GetEntry("map.png")
        if ($null -eq $entry) { throw "Missing packaged preview: $MapPath" }
        $stream = $entry.Open()
        try { return [BitConverter]::ToString($sha.ComputeHash($stream)) }
        finally { $stream.Dispose() }
    }
    finally { $sha.Dispose(); $archive.Dispose() }
}

$records = @()
foreach ($directory in $directories)
{
    $destination = Join-Path (Join-Path $OutputRoot "runs") $directory.Name
    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    $settingsPath = Join-Path $destination "player-settings.json"
    Copy-Item -LiteralPath (Join-Path $directory.FullName "player-settings.json") -Destination $settingsPath
    $baseline = Get-Content -LiteralPath (Join-Path $directory.FullName "report.json") -Raw | ConvertFrom-Json
    $reportPath = Join-Path $destination "report.json"
    $mapPath = Join-Path $destination "map.oramap"
    $logPath = Join-Path $destination "generation.log"
    $auditArguments = @()
    if ($VerifyRepeatability) { $auditArguments += "-VerifyRepeatability" }
    try
    {
        & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot "Invoke-MapGenerator.ps1") `
            -PlayerSettingsPath $settingsPath -OutputPath $mapPath -ReportPath $reportPath `
            -MovementValidation both @auditArguments *> $logPath
        if ($LASTEXITCODE -ne 0) { throw "Generation failed; see $logPath" }
        $result = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
        if ($result.validation.accepted -ne $true -or $result.movement_validation.native.accepted -ne $true)
        { throw "Logical/native validation did not pass." }
        foreach ($hash in @("logical_hash_sha256", "actor_hash_sha256", "graph_hash_sha256",
            "canonical_map_yaml_bin_sha256", "engine_uid_sha1"))
        {
            if (!$baseline.$hash -or $baseline.$hash -ne $result.$hash) { throw "Seed $($baseline.seed) changed $hash." }
        }
        if ((Get-PreviewHash (Join-Path $directory.FullName "map.oramap")) -ne (Get-PreviewHash $mapPath))
        { throw "Packaged preview changed." }
        if ($result.performance.repeatability_checked -ne [bool]$VerifyRepeatability)
        { throw "The requested audit mode was not used." }
        $records += [pscustomobject]@{
            seed = [string]$baseline.seed
            map_size = $result.player_settings.normalized.size
            all_five_hashes_matched = $true
            packaged_preview_matched = $true
            baseline_total_ms = $baseline.performance.total_ms
            current_total_ms = $result.performance.total_ms
            current_logical_ms = $result.performance.logical_generation_ms
            current_audit_repeat_ms = $result.performance.repeatability_ms
            repeatability_checked = $result.performance.repeatability_checked
            speedup = [Math]::Round($baseline.performance.total_ms / $result.performance.total_ms, 3)
        }
        Write-Host ("PASS seed={0} total={1:N2}s speedup={2}x" -f $baseline.seed, ($result.performance.total_ms / 1000), $records[-1].speedup)
    }
    catch
    {
        [ordered]@{ status = "failed"; seed = [string]$baseline.seed; error = $_.Exception.Message; completed = $records } |
            ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $OutputRoot "performance-comparison.json") -Encoding UTF8
        throw
    }
}
[ordered]@{
    status = "passed"
    baseline = $BaselineRoot
    audit_mode = [bool]$VerifyRepeatability
    timing_note = "Pipeline timings, not process startup; audit repeat cost is reported separately. Run without competing tests."
    maps = $records.Count
    records = $records
} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $OutputRoot "performance-comparison.json") -Encoding UTF8
