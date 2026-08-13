[CmdletBinding()]
param(
    [string]$OutputDirectory
)

$ErrorActionPreference = "Stop"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$generator = Join-Path $PSScriptRoot "Invoke-MapGenerator.ps1"
if ([string]::IsNullOrWhiteSpace($OutputDirectory))
{
    $OutputDirectory = Join-Path $root "artifacts\rmg\phase-4d-blocking-topology-verification\v1-regression"
}
elseif (![System.IO.Path]::IsPathRooted($OutputDirectory))
{
    $OutputDirectory = Join-Path $root $OutputDirectory
}

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$mapPath = Join-Path $OutputDirectory "seed-424242.oramap"
$reportPath = Join-Path $OutputDirectory "seed-424242.json"
& $generator `
    -OutputPath $mapPath `
    -ReportPath $reportPath `
    -Seed 424242 `
    -Players 2 `
    -NeutralColonies 10 `
    -Symmetry horizontal `
    -Archetype open `
    -Topology off `
    -MovementValidation both `
    -Overwrite

$expected = [ordered]@{
    logical_hash_sha256 = "38b89f45cd71ec5ec6e120130637711b588950000b546edf2724a02b56f85f98"
    actor_hash_sha256 = "34f5b54024235e2cfb01c6f06902deecc341e048e9290687bd97553ca81da28e"
    graph_hash_sha256 = "ca51b4e69af429a24126a590b83cb79601cd20a3bdbe4600b7bf5e8572b150f1"
    canonical_map_yaml_bin_sha256 = "79bc74876276389faa887824e0a4e25cc30cb4feaab0faa8e05cb1e28c746783"
    engine_uid_sha1 = "f5cbb2969205d85772a94ec1eb4498aa9934354d"
}

$actual = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
$mismatches = @()
foreach ($field in $expected.Keys)
{
    if ($actual.$field -ne $expected[$field])
    {
        $mismatches += "$field expected $($expected[$field]) but found $($actual.$field)"
    }
}

if (![bool]$actual.validation.accepted -or ![bool]$actual.movement_validation.native.accepted)
{
    $mismatches += "The regenerated Version 1 map did not pass proxy and native validation."
}

if ($mismatches.Count -ne 0)
{
    throw "Version 1 RMG regression failed: $($mismatches -join '; ')"
}

Write-Host "Version 1 RMG regression passed: all five seed-424242 identities are unchanged." -ForegroundColor Green
Write-Host "Report: $reportPath" -ForegroundColor Green
