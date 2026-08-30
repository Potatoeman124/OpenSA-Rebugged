[CmdletBinding()]
param(
    [string]$OutputDirectory,
    [string]$GeneratedMapDirectory,
    [string]$GeneratedMapPattern = "*.oramap"
)

$ErrorActionPreference = "Stop"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$engineRoot = Join-Path $root "engine"
$utility = Join-Path $engineRoot "bin\OpenRA.Utility.exe"
$dotnetRoot = Join-Path $root ".tools\dotnet"

if ([string]::IsNullOrWhiteSpace($OutputDirectory))
{
    $OutputDirectory = Join-Path $root "artifacts\rmg\phase-7a-battlefield-layout-audit\corpus"
}
elseif (![System.IO.Path]::IsPathRooted($OutputDirectory))
{
    $OutputDirectory = Join-Path $root $OutputDirectory
}

if ([string]::IsNullOrWhiteSpace($GeneratedMapDirectory))
{
    $GeneratedMapDirectory = Join-Path $root "artifacts\rmg\phase-6c-weighted-land-cover\examples"
}
elseif (![System.IO.Path]::IsPathRooted($GeneratedMapDirectory))
{
    $GeneratedMapDirectory = Join-Path $root $GeneratedMapDirectory
}

$generatedMaps = @()
if (Test-Path -LiteralPath $GeneratedMapDirectory -PathType Container)
{
    $generatedMaps = @(Get-ChildItem -LiteralPath $GeneratedMapDirectory -Filter $GeneratedMapPattern -File | Sort-Object Name | ForEach-Object FullName)
}

if (!(Test-Path -LiteralPath $utility -PathType Leaf))
{
    throw "OpenRA.Utility.exe is missing. Run build-pipeline.cmd build first."
}

if (!(Test-Path -LiteralPath (Join-Path $dotnetRoot "dotnet.exe") -PathType Leaf))
{
    throw "The pinned local .NET runtime is missing. Run build-pipeline.cmd bootstrap first."
}

New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$env:DOTNET_ROOT = $dotnetRoot
$env:DOTNET_MULTILEVEL_LOOKUP = "0"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
$env:DOTNET_NOLOGO = "1"
$env:ENGINE_DIR = ".."
$env:MOD_SEARCH_PATHS = "$root\mods,.\mods"
$env:PATH = "$dotnetRoot;$env:PATH"

Push-Location $engineRoot
try
{
    & $utility "sa" "--audit-rmg-battlefield-layouts" $OutputDirectory @generatedMaps
    if ($LASTEXITCODE -ne 0)
    {
        throw "RMG battlefield-layout audit failed with exit code $LASTEXITCODE."
    }
}
finally
{
    Pop-Location
}

Write-Host "RMG battlefield-layout and Water-morphology audit written to $OutputDirectory" -ForegroundColor Green
