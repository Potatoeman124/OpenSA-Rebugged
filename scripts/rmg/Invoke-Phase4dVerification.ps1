[CmdletBinding()]
param(
    [string]$OutputDirectory,
    [UInt64]$SeedStart = 1,
    [int]$RuntimeSampleRate = 250,
    [switch]$Overwrite
)

$ErrorActionPreference = "Stop"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
if ([string]::IsNullOrWhiteSpace($OutputDirectory))
{
    $OutputDirectory = Join-Path $root "artifacts\rmg\phase-4d-blocking-topology-verification"
}
elseif (![System.IO.Path]::IsPathRooted($OutputDirectory))
{
    $OutputDirectory = Join-Path $root $OutputDirectory
}

$campaignReport = Join-Path $OutputDirectory "campaign\phase-4d-full.json"
$campaignParameters = @{
    ReportPath = $campaignReport
    SeedStart = $SeedStart
    GateACount = 100
    MixedCount = 5000
    ColonyCountCampaign = 25
    RuntimeSampleRate = $RuntimeSampleRate
    MovementValidation = "both"
    Topology = "mixed"
    PreserveFailures = $true
}
if ($Overwrite)
{
    $campaignParameters.Overwrite = $true
}

& (Join-Path $PSScriptRoot "Invoke-MapGeneratorFuzz.ps1") @campaignParameters
& (Join-Path $PSScriptRoot "Invoke-BlockingTopologyReferenceMatrix.ps1") `
    -OutputDirectory (Join-Path $OutputDirectory "independent-process-matrix")
& (Join-Path $PSScriptRoot "Invoke-RmgV1Regression.ps1") `
    -OutputDirectory (Join-Path $OutputDirectory "v1-regression")

$campaign = Get-Content -LiteralPath $campaignReport -Raw | ConvertFrom-Json
$matrixPath = Join-Path $OutputDirectory "independent-process-matrix\reference-matrix.json"
$matrix = Get-Content -LiteralPath $matrixPath -Raw | ConvertFrom-Json
$gateACampaigns = @($campaign.campaigns | Where-Object { $_.campaign -eq "gate-a" })
$boundaryCampaigns = @($campaign.campaigns | Where-Object { $_.campaign -eq "colony-count" })
$mixedCampaign = @($campaign.campaigns | Where-Object { $_.campaign -eq "mixed" }) | Select-Object -First 1
$gateAFailed = @($gateACampaigns | Where-Object {
    [int]$_.initial_consecutive_cases -lt 100 -or
    [int]$_.required_acceptances -lt 100 -or
    [int]$_.accepted -lt [int]$_.required_acceptances -or
    [int]$_.failures -ne 0
}).Count -ne 0
$boundaryFailed = @($boundaryCampaigns | Where-Object {
    [int]$_.cases -lt 25 -or [int]$_.failures -ne 0
}).Count -ne 0
$runtime = $campaign.runtime_package_campaign
if (!$campaign.self_tests_passed -or
    $gateACampaigns.Count -ne 12 -or
    $gateAFailed -or
    $boundaryCampaigns.Count -ne 48 -or
    $boundaryFailed -or
    $null -eq $mixedCampaign -or
    [int]$mixedCampaign.cases -lt 5000 -or
    [int]$mixedCampaign.failures -ne 0 -or
    [int]$campaign.blocking_failure_cases -ne 0 -or
    [int]$campaign.logical_hard_invalid_cases -ne 0 -or
    [int]$campaign.accepted_hard_invalid_cases -ne 0 -or
    [int]$campaign.generation_exception_cases -ne 0 -or
    [int]$campaign.same_seed_hash_mismatches -ne 0 -or
    [int]$runtime.forced_boundary_samples -lt 48 -or
    [int]$runtime.accepted_cases + [int]$runtime.rejected_candidates -ne [int]$runtime.sampled_cases -or
    ($RuntimeSampleRate -eq 1 -and [int]$runtime.accepted_cases -ne [int]$campaign.accepted_cases) -or
    [int]$runtime.package_hash_mismatches -ne 0 -or
    [int]$runtime.yaml_lint_failures -ne 0 -or
    [int]$runtime.topology_result_disagreements -ne 0 -or
    [int]$runtime.overall_result_disagreements -ne 0 -or
    [int]$matrix.accepted_cases -ne [int]$matrix.total_cases)
{
    throw "Phase 4D verification did not satisfy its automated acceptance gate. Inspect $OutputDirectory."
}

Write-Host "Phase 4D automated verification passed: $($campaign.accepted_cases)/$($campaign.total_cases) accepted fuzz cases with $($campaign.expected_generation_rejections) bounded rejections, $($runtime.accepted_cases) accepted and $($runtime.rejected_candidates) rejected among $($runtime.sampled_cases) package/native candidates, and $($matrix.accepted_cases)/$($matrix.total_cases) independent-process cases." -ForegroundColor Green
