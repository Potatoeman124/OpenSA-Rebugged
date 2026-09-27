[CmdletBinding()]
param(
    [string]$Root,
    [string]$Version = "1.1",
    [ValidateSet("x64", "x86")][string]$Architecture = "x64",
    [string]$OutputDirectory
)
$ErrorActionPreference = "Stop"
if (!$Root) { $Root = Join-Path $PSScriptRoot "../.." }
$Root = [IO.Path]::GetFullPath($Root)
if (!$OutputDirectory) { $OutputDirectory = Join-Path $Root "artifacts/1_1_Release" }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$stage = Join-Path $Root "artifacts/staging/OpenSA-reBugged-$Version-win-$Architecture"
$compiler = Join-Path $Root ".tools/nsis-3.11/makensis.exe"
if (!(Test-Path -LiteralPath $compiler)) { throw "Install the checksum-verified NSIS 3.11 ZIP in .tools first. See docs/BUILDING.md." }
if (!(Test-Path -LiteralPath (Join-Path $stage "OpenSA.exe"))) { throw "Build the matching portable stage first." }
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $Root "scripts/build/Check-AssetPolicy.ps1") -Mode Release -Root $Root -ReleaseVersion $Version -StagePath $stage
if ($LASTEXITCODE -ne 0) { throw "Staged package failed the release gate." }
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$installer = Join-Path $OutputDirectory "OpenSA-reBugged-$Version-$Architecture.exe"
$compilerArgs = @(
    "/V2", "/DSRCDIR=$stage", "/DTAG=$Version", "/DMOD_ID=sa",
    "/DPACKAGING_WINDOWS_INSTALL_DIR_NAME=OpenSA-reBugged-$Version-$Architecture",
    "/DPACKAGING_WINDOWS_LAUNCHER_NAME=OpenSA", "/DPACKAGING_DISPLAY_NAME=OpenSA reBugged $Version ($Architecture)",
    "/DPACKAGING_WEBSITE_URL=https://github.com/Potatoeman124/OpenSA-Rebugged",
    "/DPACKAGING_AUTHORS=OpenSA and OpenSA reBugged contributors",
    "/DPACKAGING_WINDOWS_REGISTRY_KEY=OpenSA-reBugged-$Version-$Architecture",
    "/DPACKAGING_WINDOWS_LICENSE_FILE=$Root/COPYING", "/DOUTFILE=$installer"
)
if ($Architecture -eq "x86") { $compilerArgs += "/DUSE_PROGRAMFILES32=true" }
$compilerArgs += (Join-Path $Root "packaging/windows/buildpackage.nsi")
& $compiler @compilerArgs
if ($LASTEXITCODE -ne 0) { throw "NSIS build failed." }
$archive = Join-Path $Root "artifacts/packages/OpenSA-reBugged-$Version-win-$Architecture.zip"
Copy-Item -LiteralPath $archive -Destination (Join-Path $OutputDirectory "OpenSA-reBugged-$Version-$Architecture-winportable.zip") -Force
Write-Host "Installer: $installer"
