[CmdletBinding()]
param(
    [switch]$InstallForPlay,
    [switch]$ReplaceInstalledRmgCorpus,
    [switch]$Overwrite
)

$ErrorActionPreference = "Stop"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$generator = Join-Path $PSScriptRoot "Invoke-PlayerMapGenerator.ps1"
$cases = @(
    [pscustomobject]@{ Seed = [UInt64]8301001; Preset = "balanced";            Players = 2; Symmetry = "rotational"; Layout = "preset";      Density = "standard"; Water = "low";      Tactical = "standard" },
    [pscustomobject]@{ Seed = [UInt64]8301001; Preset = "balanced";            Players = 2; Symmetry = "rotational"; Layout = "preset";      Density = "standard"; Water = "standard"; Tactical = "standard" },
    [pscustomobject]@{ Seed = [UInt64]8301001; Preset = "balanced";            Players = 2; Symmetry = "rotational"; Layout = "preset";      Density = "standard"; Water = "high";     Tactical = "standard" },
    [pscustomobject]@{ Seed = [UInt64]8301002; Preset = "balanced";            Players = 4; Symmetry = "horizontal"; Layout = "open-fields"; Density = "standard"; Water = "standard"; Tactical = "low" },
    [pscustomobject]@{ Seed = [UInt64]8301002; Preset = "balanced";            Players = 4; Symmetry = "horizontal"; Layout = "open-fields"; Density = "standard"; Water = "standard"; Tactical = "standard" },
    [pscustomobject]@{ Seed = [UInt64]8301002; Preset = "balanced";            Players = 4; Symmetry = "horizontal"; Layout = "open-fields"; Density = "standard"; Water = "standard"; Tactical = "high" },
    [pscustomobject]@{ Seed = [UInt64]8200002; Preset = "balanced";            Players = 2; Symmetry = "vertical";   Layout = "preset";      Density = "standard"; Water = "low";      Tactical = "standard" },
    [pscustomobject]@{ Seed = [UInt64]9200041; Preset = "tactical-crossroads"; Players = 4; Symmetry = "horizontal"; Layout = "open-fields"; Density = "standard";   Water = "standard"; Tactical = "high" },
    [pscustomobject]@{ Seed = [UInt64]8301003; Preset = "open-conflict";       Players = 2; Symmetry = "rotational";   Layout = "preset";      Density = "preset";   Water = "high";     Tactical = "high" },
    [pscustomobject]@{ Seed = [UInt64]5722426127237601134;  Preset = "balanced"; Players = 4; Symmetry = "rotational"; Layout = "contested-center"; Density = "dense"; Water = "high"; Tactical = "high" },
    [pscustomobject]@{ Seed = [UInt64]16542743543062672902; Preset = "balanced"; Players = 4; Symmetry = "vertical";   Layout = "contested-center"; Density = "dense"; Water = "high"; Tactical = "high" }
)
if ($ReplaceInstalledRmgCorpus -and !$InstallForPlay)
{
    throw "-ReplaceInstalledRmgCorpus requires -InstallForPlay."
}

if ($ReplaceInstalledRmgCorpus)
{
    $mapRoot = [System.IO.Path]::GetFullPath((Join-Path $env:APPDATA "OpenRA\maps\sa"))
    $mapDirectory = [System.IO.Path]::GetFullPath((Join-Path $env:APPDATA "OpenRA\maps\sa\{DEV_VERSION}"))
    $mapRootPrefix = $mapRoot.TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
    if (!$mapDirectory.StartsWith($mapRootPrefix, [System.StringComparison]::OrdinalIgnoreCase) -or
        (Split-Path -Leaf $mapDirectory) -ne "{DEV_VERSION}")
    {
        throw "Refusing to replace generated maps outside the expected development map directory."
    }

    if (Test-Path -LiteralPath $mapDirectory -PathType Container)
    {
        foreach ($map in Get-ChildItem -LiteralPath $mapDirectory -Filter "OpenSA-RMG-*.oramap" -File)
        {
            Remove-Item -LiteralPath $map.FullName -Force
        }
    }
}

foreach ($case in $cases)
{
    $arguments = @{
        Seed = $case.Seed
        Preset = $case.Preset
        Players = $case.Players
        Symmetry = $case.Symmetry
        Layout = $case.Layout
        NeutralColonyDensity = $case.Density
        WaterAmount = $case.Water
        TacticalTerrain = $case.Tactical
        MovementValidation = "both"
        InstallForPlay = $InstallForPlay
        Overwrite = $Overwrite
    }
    & $generator @arguments
}

$destination = if ($InstallForPlay) { Join-Path $env:APPDATA "OpenRA\maps\sa\{DEV_VERSION}" } else { Join-Path $root "artifacts\rmg\phase-8a-parameterized-battlefield\examples" }
Write-Host "Prepared eleven Phase 8A parameterized-battlefield maps in $destination" -ForegroundColor Green
