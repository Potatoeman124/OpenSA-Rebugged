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
    [pscustomobject]@{ Seed = [UInt64]3100009; Preset = "balanced";             Players = 2; Symmetry = "automatic";  Layout = "preset";      Density = "preset" },
    [pscustomobject]@{ Seed = [UInt64]3100000; Preset = "open-conflict";        Players = 2; Symmetry = "horizontal"; Layout = "preset";      Density = "preset" },
    [pscustomobject]@{ Seed = [UInt64]3100023; Preset = "tactical-crossroads";  Players = 2; Symmetry = "rotational"; Layout = "preset";      Density = "preset" },
    [pscustomobject]@{ Seed = [UInt64]3100025; Preset = "balanced";             Players = 4; Symmetry = "horizontal"; Layout = "preset";      Density = "preset" },
    [pscustomobject]@{ Seed = [UInt64]3100026; Preset = "open-conflict";        Players = 4; Symmetry = "vertical";   Layout = "preset";      Density = "preset" },
    [pscustomobject]@{ Seed = [UInt64]3100047; Preset = "tactical-crossroads";  Players = 4; Symmetry = "rotational"; Layout = "preset";      Density = "preset" },
    [pscustomobject]@{ Seed = [UInt64]3100016; Preset = "tactical-crossroads";  Players = 2; Symmetry = "rotational"; Layout = "open-fields"; Density = "preset" },
    [pscustomobject]@{ Seed = [UInt64]3100024; Preset = "balanced";             Players = 4; Symmetry = "horizontal"; Layout = "open-fields"; Density = "sparse" }
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
        MovementValidation = "both"
        InstallForPlay = $InstallForPlay
        Overwrite = $Overwrite
    }
    & $generator @arguments
}

$destination = if ($InstallForPlay) { Join-Path $env:APPDATA "OpenRA\maps\sa\{DEV_VERSION}" } else { Join-Path $root "artifacts\rmg\phase-7c-player-settings\examples" }
Write-Host "Prepared eight Phase 7C player-settings maps in $destination" -ForegroundColor Green
