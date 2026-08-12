[CmdletBinding()]
param(
    [string]$OutputDirectory
)

$ErrorActionPreference = "Stop"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$generator = Join-Path $PSScriptRoot "Invoke-MapGenerator.ps1"

if ([string]::IsNullOrWhiteSpace($OutputDirectory))
{
    $OutputDirectory = Join-Path $root "artifacts\rmg\phase-4c-blocking-topology\reference-matrix"
}
elseif (![System.IO.Path]::IsPathRooted($OutputDirectory))
{
    $OutputDirectory = Join-Path $root $OutputDirectory
}

$mapsDirectory = Join-Path $OutputDirectory "maps"
$reportsDirectory = Join-Path $OutputDirectory "reports"
New-Item -ItemType Directory -Force -Path $mapsDirectory, $reportsDirectory | Out-Null

$cases = @(
    [pscustomobject]@{ Id = "2p-open-horizontal"; Seed = [UInt64]45001; Players = 2; Archetype = "open"; Symmetry = "horizontal" },
    [pscustomobject]@{ Id = "2p-open-vertical"; Seed = [UInt64]45002; Players = 2; Archetype = "open"; Symmetry = "vertical" },
    [pscustomobject]@{ Id = "2p-open-rotational"; Seed = [UInt64]45003; Players = 2; Archetype = "open"; Symmetry = "rotational" },
    [pscustomobject]@{ Id = "2p-central-horizontal"; Seed = [UInt64]45004; Players = 2; Archetype = "central-contest"; Symmetry = "horizontal" },
    [pscustomobject]@{ Id = "2p-central-vertical"; Seed = [UInt64]45005; Players = 2; Archetype = "central-contest"; Symmetry = "vertical" },
    [pscustomobject]@{ Id = "2p-central-rotational"; Seed = [UInt64]45006; Players = 2; Archetype = "central-contest"; Symmetry = "rotational" },
    [pscustomobject]@{ Id = "4p-open-horizontal"; Seed = [UInt64]43001; Players = 4; Archetype = "open"; Symmetry = "horizontal" },
    [pscustomobject]@{ Id = "4p-open-vertical"; Seed = [UInt64]43002; Players = 4; Archetype = "open"; Symmetry = "vertical" },
    [pscustomobject]@{ Id = "4p-open-rotational"; Seed = [UInt64]43003; Players = 4; Archetype = "open"; Symmetry = "rotational" },
    [pscustomobject]@{ Id = "4p-central-horizontal"; Seed = [UInt64]44001; Players = 4; Archetype = "central-contest"; Symmetry = "horizontal" },
    [pscustomobject]@{ Id = "4p-central-vertical"; Seed = [UInt64]44002; Players = 4; Archetype = "central-contest"; Symmetry = "vertical" },
    [pscustomobject]@{ Id = "4p-central-rotational"; Seed = [UInt64]44003; Players = 4; Archetype = "central-contest"; Symmetry = "rotational" }
)

$results = @()
$failures = @()
foreach ($matrixCase in $cases)
{
    Write-Host "Validating $($matrixCase.Id)..." -ForegroundColor Cyan
    $primaryMap = Join-Path $mapsDirectory "$($matrixCase.Id).oramap"
    $repeatMap = Join-Path $mapsDirectory "$($matrixCase.Id)-repeat.oramap"
    $primaryReport = Join-Path $reportsDirectory "$($matrixCase.Id).json"
    $repeatReport = Join-Path $reportsDirectory "$($matrixCase.Id)-repeat.json"

    $common = @{
        Seed = $matrixCase.Seed
        Players = $matrixCase.Players
        Archetype = $matrixCase.Archetype
        Symmetry = $matrixCase.Symmetry
        Topology = "mixed"
        MovementValidation = "both"
        Overwrite = $true
    }
    & $generator @common -OutputPath $primaryMap -ReportPath $primaryReport
    & $generator @common -OutputPath $repeatMap -ReportPath $repeatReport

    $primary = Get-Content -LiteralPath $primaryReport -Raw | ConvertFrom-Json
    $repeat = Get-Content -LiteralPath $repeatReport -Raw | ConvertFrom-Json
    $identityFields = @(
        "logical_hash_sha256",
        "actor_hash_sha256",
        "graph_hash_sha256",
        "canonical_map_yaml_bin_sha256",
        "engine_uid_sha1"
    )
    $identityMatches = $true
    foreach ($field in $identityFields)
    {
        if ($primary.$field -ne $repeat.$field)
        {
            $identityMatches = $false
            $failures += "$($matrixCase.Id): repeat identity mismatch in $field."
        }
    }

    $density = [double]$primary.blocking_topology.obstacle_density_percent
    $minimumDensity = if ($matrixCase.Archetype -eq "open") { 10.0 } else { 14.0 }
    $maximumDensity = if ($matrixCase.Archetype -eq "open") { 14.0 } else { 18.0 }
    $expectedChokeSegments = if ($matrixCase.Archetype -eq "central-contest") { 2 } else { 0 }
    $actualChokeSegments = [int]$primary.validation.metrics.chokepoint_segment_count
    $routes = @($primary.movement_validation.native.route_measurements)
    $routeWidthsAccepted = @($routes | Where-Object { !$_.traversable -or !$_.meets_configured_width }).Count -eq 0
    $chokeWidthsExact = @($routes | Where-Object { $_.contains_intentional_choke -and $_.widest_path_native -ne 3 }).Count -eq 0
    $packageAccepted =
        $primary.package_validation.package_reload -eq "passed" -and
        $primary.package_validation.rules_sequences_initialization -eq "passed" -and
        $primary.package_validation.map_yaml_lint -eq "passed"
    $accepted =
        [bool]$primary.validation.accepted -and
        [bool]$primary.movement_validation.native.accepted -and
        $packageAccepted -and
        $identityMatches -and
        $density -ge $minimumDensity -and
        $density -le $maximumDensity -and
        $actualChokeSegments -eq $expectedChokeSegments -and
        $routeWidthsAccepted -and
        $chokeWidthsExact -and
        [int]$primary.movement_validation.native.metrics.terrain_semantic_mismatch_cells -eq 0 -and
        [int]$primary.movement_validation.native.metrics.production_exit_failures -eq 0

    if (!$accepted)
    {
        $failures += "$($matrixCase.Id): one or more topology, movement, package, or density assertions failed."
    }

    $results += [pscustomobject]@{
        id = $matrixCase.Id
        seed = $matrixCase.Seed
        players = $matrixCase.Players
        archetype = $matrixCase.Archetype
        symmetry = $matrixCase.Symmetry
        accepted = $accepted
        obstacle_density_percent = $density
        obstacle_regions = [int]$primary.validation.metrics.obstacle_region_count
        chokepoint_segments = $actualChokeSegments
        minimum_route_width_native = [int]$primary.movement_validation.native.metrics.minimum_usable_route_width_native
        logical_hash_sha256 = $primary.logical_hash_sha256
        canonical_map_yaml_bin_sha256 = $primary.canonical_map_yaml_bin_sha256
        engine_uid_sha1 = $primary.engine_uid_sha1
        repeat_identity_matches = $identityMatches
    }
}

$summary = [ordered]@{
    schema_version = "1.0"
    generator_version = 2
    configuration_id = "normal-water-blocking-v2"
    cases = $results
    total_cases = $results.Count
    accepted_cases = @($results | Where-Object { $_.accepted }).Count
    repeat_generations = $results.Count
    failures = $failures
}
$summaryPath = Join-Path $OutputDirectory "reference-matrix.json"
$summary | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $summaryPath -Encoding UTF8

if ($failures.Count -ne 0)
{
    throw "Blocking-topology reference matrix failed. See $summaryPath"
}

Write-Host "Blocking-topology reference matrix passed: $($results.Count)/$($results.Count) cases and all canonical repeat identities." -ForegroundColor Green
Write-Host "Summary: $summaryPath" -ForegroundColor Green
