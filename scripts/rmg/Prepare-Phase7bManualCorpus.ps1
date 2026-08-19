[CmdletBinding()]
param(
    [switch]$InstallForPlay,
    [switch]$ReplaceInstalledRmgCorpus,
    [switch]$Overwrite
)

$ErrorActionPreference = "Stop"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$generator = Join-Path $PSScriptRoot "Invoke-MapGenerator.ps1"
$cases = @(
    [pscustomobject]@{ Seed = [UInt64]3100000; Players = 2; Colonies = 8;  Symmetry = "horizontal"; Archetype = "open" },
    [pscustomobject]@{ Seed = [UInt64]3100009; Players = 2; Colonies = 10; Symmetry = "vertical";   Archetype = "central-contest" },
    [pscustomobject]@{ Seed = [UInt64]3100016; Players = 2; Colonies = 14; Symmetry = "rotational"; Archetype = "open" },
    [pscustomobject]@{ Seed = [UInt64]3100023; Players = 2; Colonies = 20; Symmetry = "rotational"; Archetype = "central-contest" },
    [pscustomobject]@{ Seed = [UInt64]3100024; Players = 4; Colonies = 12; Symmetry = "horizontal"; Archetype = "open" },
    [pscustomobject]@{ Seed = [UInt64]3100025; Players = 4; Colonies = 12; Symmetry = "horizontal"; Archetype = "central-contest" },
    [pscustomobject]@{ Seed = [UInt64]3100026; Players = 4; Colonies = 12; Symmetry = "vertical";   Archetype = "open" },
    [pscustomobject]@{ Seed = [UInt64]3100047; Players = 4; Colonies = 24; Symmetry = "rotational"; Archetype = "central-contest" }
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
    if (!$mapDirectory.StartsWith($mapRootPrefix, [System.StringComparison]::OrdinalIgnoreCase) -or (Split-Path -Leaf $mapDirectory) -ne "{DEV_VERSION}")
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
        Players = $case.Players
        NeutralColonies = $case.Colonies
        Symmetry = $case.Symmetry
        Archetype = $case.Archetype
        Topology = "battlefield-layout"
        MovementValidation = "both"
        InstallForPlay = $InstallForPlay
        Overwrite = $Overwrite
    }
    & $generator @arguments
}

$destination = if ($InstallForPlay) { Join-Path $env:APPDATA "OpenRA\maps\sa\{DEV_VERSION}" } else { Join-Path $root "artifacts\rmg\phase-7b-battlefield-layout\examples" }
Write-Host "Prepared eight Phase 7B comparison maps in $destination" -ForegroundColor Green
