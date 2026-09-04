[CmdletBinding()]
param(
    [switch]$InstallForPlay,
    [switch]$Overwrite
)

$ErrorActionPreference = "Stop"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$generator = Join-Path $PSScriptRoot "Invoke-PlayerMapGenerator.ps1"
$cases = @(
    [pscustomobject]@{ Seed = [UInt64]8300001; Players = 2; Symmetry = "horizontal"; Layout = "open-fields";      Water = "standard" },
    [pscustomobject]@{ Seed = [UInt64]8300002; Players = 2; Symmetry = "vertical";   Layout = "contested-center"; Water = "high" },
    [pscustomobject]@{ Seed = [UInt64]8300003; Players = 2; Symmetry = "rotational"; Layout = "contested-center"; Water = "low" },
    [pscustomobject]@{ Seed = [UInt64]5722426127237601134; Players = 4; Symmetry = "rotational"; Layout = "contested-center"; Water = "high" },
    [pscustomobject]@{ Seed = [UInt64]8300005; Players = 4; Symmetry = "vertical";   Layout = "contested-center"; Water = "standard" },
    [pscustomobject]@{ Seed = [UInt64]8300006; Players = 4; Symmetry = "rotational"; Layout = "open-fields";      Water = "standard" }
)

$families = @("structured-competitive", "artificial-battlefield")
foreach ($case in $cases)
{
    foreach ($family in $families)
    {
        $arguments = @{
            Seed = $case.Seed
            Preset = "balanced"
            Players = $case.Players
            Symmetry = $case.Symmetry
            LayoutFamily = $family
            Layout = $case.Layout
            NeutralColonyDensity = "standard"
            WaterAmount = $case.Water
            TacticalTerrain = "standard"
            MovementValidation = "both"
            InstallForPlay = $InstallForPlay
            Overwrite = $Overwrite
        }
        & $generator @arguments
        if ($LASTEXITCODE -ne 0)
        {
            exit $LASTEXITCODE
        }
    }
}

$destination = if ($InstallForPlay)
{
    Join-Path $env:APPDATA "OpenRA\maps\sa\{DEV_VERSION}"
}
else
{
    Join-Path $root "artifacts\rmg\phase-8c-structured-competitive\examples"
}
Write-Host "Prepared twelve paired Phase 8C layout-family maps in $destination" -ForegroundColor Green
