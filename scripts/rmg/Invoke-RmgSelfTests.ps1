[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$engineRoot = Join-Path $root "engine"
$utility = Join-Path $engineRoot "bin\OpenRA.Utility.exe"
$dotnetRoot = Join-Path $root ".tools\dotnet"

if (!(Test-Path -LiteralPath $utility -PathType Leaf))
{
    throw "OpenRA.Utility.exe is missing. Run build-pipeline.cmd validate first."
}

if (!(Test-Path -LiteralPath (Join-Path $dotnetRoot "dotnet.exe") -PathType Leaf))
{
    throw "The pinned local .NET runtime is missing. Run build-pipeline.cmd bootstrap first."
}

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
    & $utility "sa" "--validate-sa-rmg"
    if ($LASTEXITCODE -ne 0)
    {
        throw "Focused RMG self-tests failed with exit code $LASTEXITCODE."
    }
}
finally
{
    Pop-Location
}

Write-Host "Focused V1-V10, inherited-baseline, parameter-matrix, terrain-catalogue, materializer, and native-validator RMG self-tests passed." -ForegroundColor Green
