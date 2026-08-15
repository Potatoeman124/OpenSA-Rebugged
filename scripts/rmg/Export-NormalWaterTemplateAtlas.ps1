[CmdletBinding()]
param(
	[string]$OutputDirectory
)

$ErrorActionPreference = "Stop"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$engineRoot = Join-Path $root "engine"
$utility = Join-Path $engineRoot "bin\OpenRA.Utility.exe"
$dotnetRoot = Join-Path $root ".tools\dotnet"

if ([string]::IsNullOrWhiteSpace($OutputDirectory))
{
	$OutputDirectory = Join-Path $root "artifacts\rmg\phase-5a-terrain-audit\visual-atlas"
}
elseif (![System.IO.Path]::IsPathRooted($OutputDirectory))
{
	$OutputDirectory = Join-Path $root $OutputDirectory
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
	& $utility "sa" "--export-rmg-normal-water-atlas" $OutputDirectory
	if ($LASTEXITCODE -ne 0)
	{
		throw "NORMAL Water atlas export failed with exit code $LASTEXITCODE."
	}
}
finally
{
	Pop-Location
}

Write-Host "Local-only Phase 5A atlas written to $OutputDirectory" -ForegroundColor Green
