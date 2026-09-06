[CmdletBinding()]
param(
    [string]$OutputDirectory
)

$ErrorActionPreference = "Stop"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$generator = Join-Path $PSScriptRoot "Invoke-MapGenerator.ps1"
$fixturePath = Join-Path $root "docs\rmg\regression\v7_v8_preservation_baseline.json"

if ([string]::IsNullOrWhiteSpace($OutputDirectory))
{
    $OutputDirectory = Join-Path $root "artifacts\rmg\v7-v8-preservation"
}
elseif (![System.IO.Path]::IsPathRooted($OutputDirectory))
{
    $OutputDirectory = Join-Path $root $OutputDirectory
}

if (!(Test-Path -LiteralPath $fixturePath -PathType Leaf))
{
    throw "V7/V8 preservation fixture not found: $fixturePath"
}

$fixture = Get-Content -LiteralPath $fixturePath -Raw | ConvertFrom-Json
$powershell = (Get-Process -Id $PID).Path
$settingsDirectory = Join-Path $OutputDirectory "settings"
$summaryPath = Join-Path $OutputDirectory "verification-summary.json"
$identityFields = @(
    "logical_hash_sha256",
    "actor_hash_sha256",
    "graph_hash_sha256",
    "canonical_map_yaml_bin_sha256",
    "engine_uid_sha1"
)
$failures = New-Object System.Collections.Generic.List[string]
$results = New-Object System.Collections.Generic.List[object]
New-Item -ItemType Directory -Force -Path $settingsDirectory | Out-Null

function Invoke-IndependentGeneration
{
    param(
        [Parameter(Mandatory = $true)]
        [object]$Case,
        [Parameter(Mandatory = $true)]
        [string]$RunName,
        [Parameter(Mandatory = $true)]
        [string]$SettingsPath
    )

    $runDirectory = Join-Path $OutputDirectory $RunName
    $mapDirectory = Join-Path $runDirectory "maps"
    $reportDirectory = Join-Path $runDirectory "reports"
    New-Item -ItemType Directory -Force -Path $mapDirectory, $reportDirectory | Out-Null
    $mapPath = Join-Path $mapDirectory "$($Case.id).oramap"
    $reportPath = Join-Path $reportDirectory "$($Case.id).json"
    $arguments = @(
        "-NoProfile",
        "-ExecutionPolicy", "Bypass",
        "-File", $generator,
        "-PlayerSettingsPath", $SettingsPath,
        "-OutputPath", $mapPath,
        "-ReportPath", $reportPath,
        "-MovementValidation", "both",
        "-Overwrite"
    )
    $output = & $powershell $arguments 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0)
    {
        throw "$($Case.id) $RunName generation failed: $output"
    }

    if (!(Test-Path -LiteralPath $reportPath -PathType Leaf))
    {
        throw "$($Case.id) $RunName did not produce a report."
    }

    return Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
}

foreach ($case in @($fixture.cases))
{
    Write-Host "Preserving $($case.id)..." -ForegroundColor Cyan
    $settingsPath = Join-Path $settingsDirectory "$($case.id).json"
    $case.requested_settings | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $settingsPath -Encoding UTF8

    $first = Invoke-IndependentGeneration -Case $case -RunName "run-a" -SettingsPath $settingsPath
    $repeat = Invoke-IndependentGeneration -Case $case -RunName "run-b" -SettingsPath $settingsPath
    $caseFailures = New-Object System.Collections.Generic.List[string]

    foreach ($field in $identityFields)
    {
        if ($first.$field -ne $repeat.$field)
        {
            $caseFailures.Add("independent-process mismatch in $field")
        }

        if ($first.$field -ne $case.identities.$field)
        {
            $caseFailures.Add("fixture mismatch in $field")
        }
    }

    if ([int]$first.generator_version -ne [int]$case.generator_version)
    {
        $caseFailures.Add("generator version expected $($case.generator_version) but found $($first.generator_version)")
    }

    if ($first.configuration_id -ne $case.configuration_id -or
        [int]$first.configuration_version -ne [int]$case.configuration_version)
    {
        $caseFailures.Add("profile identity differs from the fixture")
    }

    if ($first.layout_family -ne $case.layout_family)
    {
        $caseFailures.Add("layout family expected $($case.layout_family) but found $($first.layout_family)")
    }

    $actualNormalized = $first.player_settings.normalized
    $expectedNormalized = $case.normalized_settings.PSObject.Copy()
    # Added by the approved V10 UI; inert on the frozen V7/V8 terrain paths.
    $expectedNormalized | Add-Member -NotePropertyName original_surface_relations -NotePropertyValue $true
    $actualNames = @($actualNormalized.PSObject.Properties.Name | Sort-Object)
    $expectedNames = @($expectedNormalized.PSObject.Properties.Name | Sort-Object)
    $differentValues = @($expectedNames | Where-Object { $actualNormalized.$_ -cne $expectedNormalized.$_ })
    if (($actualNames -join ",") -cne ($expectedNames -join ",") -or $differentValues.Count -gt 0)
    {
        $caseFailures.Add("normalized player settings differ from the fixture")
    }

    if ([int]$first.neutral_colonies -ne [int]$case.neutral_colonies_achieved -or
        [int]$first.neutral_colonies_requested -ne [int]$case.neutral_colonies_requested)
    {
        $caseFailures.Add("neutral-colony adaptation differs from the fixture")
    }

    $accepted = [bool]$first.validation.accepted -and
        [bool]$first.movement_validation.native.accepted -and
        $first.package_validation.package_reload -eq "passed" -and
        $first.package_validation.rules_sequences_initialization -eq "passed" -and
        $first.package_validation.map_yaml_lint -eq "passed"
    if (!$accepted)
    {
        $caseFailures.Add("generation, native movement, or package validation rejected the map")
    }

    foreach ($failure in $caseFailures)
    {
        $failures.Add("$($case.id): $failure")
    }

    $results.Add([ordered]@{
        id = $case.id
        accepted = $accepted -and $caseFailures.Count -eq 0
        generator_version = [int]$first.generator_version
        layout_family = $first.layout_family
        neutral_colonies = [int]$first.neutral_colonies
        identities = [ordered]@{
            logical_hash_sha256 = $first.logical_hash_sha256
            actor_hash_sha256 = $first.actor_hash_sha256
            graph_hash_sha256 = $first.graph_hash_sha256
            canonical_map_yaml_bin_sha256 = $first.canonical_map_yaml_bin_sha256
            engine_uid_sha1 = $first.engine_uid_sha1
        }
        independent_process_identity_matches = $caseFailures.Count -eq 0
    })
}

$naturalSettingsPath = Join-Path $settingsDirectory "natural-landscape-v10.json"
$naturalMapPath = Join-Path $OutputDirectory "natural-landscape-v10.oramap"
$naturalReportPath = Join-Path $OutputDirectory "natural-landscape-v10.report.json"
$fixture.natural_landscape.requested_settings | ConvertTo-Json -Depth 8 |
    Set-Content -LiteralPath $naturalSettingsPath -Encoding UTF8
$naturalArguments = @(
    "-NoProfile",
    "-ExecutionPolicy", "Bypass",
    "-File", $generator,
    "-PlayerSettingsPath", $naturalSettingsPath,
    "-OutputPath", $naturalMapPath,
    "-ReportPath", $naturalReportPath,
    "-MovementValidation", "both",
    "-Overwrite"
)
$previousErrorActionPreference = $ErrorActionPreference
$ErrorActionPreference = "Continue"
$naturalOutput = & $powershell $naturalArguments 2>&1 | Out-String
$naturalExitCode = $LASTEXITCODE
$ErrorActionPreference = $previousErrorActionPreference
# Natural Landscape was subsequently implemented; it must now select V10,
# never fall back to either frozen family. The historical fixture stays untouched.
$naturalIsV10 = $false
if ($naturalExitCode -eq 0 -and (Test-Path -LiteralPath $naturalMapPath) -and (Test-Path -LiteralPath $naturalReportPath))
{
    $naturalReport = Get-Content -LiteralPath $naturalReportPath -Raw | ConvertFrom-Json
    $naturalIsV10 = $naturalReport.generator_version -eq 10 -and
        $naturalReport.layout_family -eq "natural-landscape" -and
        $naturalReport.validation.accepted -eq $true -and
        $naturalReport.movement_validation.native.accepted -eq $true
}
if (!$naturalIsV10)
{
    $failures.Add("Natural Landscape did not generate a validated V10 map independently of V7/V8.")
}

$summary = [ordered]@{
    checkpoint_schema_version = $fixture.checkpoint_schema_version
    fixture = $fixturePath
    independent_process_runs_per_case = 2
    cases = $results
    natural_landscape_is_v10 = $naturalIsV10
    total_cases = $results.Count
    accepted_cases = @($results | Where-Object { $_.accepted }).Count
    failures = $failures
}
$summary | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $summaryPath -Encoding UTF8

if ($failures.Count -ne 0)
{
    throw "V7/V8 preservation regression failed. See $summaryPath"
}

Write-Host "V7/V8 preservation passed: $($results.Count)/$($results.Count) cases matched the fixture across two independent processes." -ForegroundColor Green
Write-Host "Natural Landscape isolation passed: a validated V10 map was produced, not a V7/V8 fallback." -ForegroundColor Green
Write-Host "Summary: $summaryPath" -ForegroundColor Green
