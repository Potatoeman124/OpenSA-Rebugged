[CmdletBinding()]
param(
    [string]$Root,
    [Parameter(Mandatory = $true)]
    [ValidatePattern("^[0-9A-Za-z._-]+$")]
    [string]$Version
)

$ErrorActionPreference = "Stop"
if ([string]::IsNullOrWhiteSpace($Root))
{
    $Root = Join-Path $PSScriptRoot "..\.."
}

$rootPath = [IO.Path]::GetFullPath($Root)
$engineRoot = Join-Path $rootPath "engine"
$dotnetExe = Join-Path $rootPath ".tools\dotnet\dotnet.exe"
$artifactsRoot = Join-Path $rootPath "artifacts"
$stageRoot = Join-Path $artifactsRoot "staging"
$packageRoot = Join-Path $artifactsRoot "packages"
$stageName = "OpenSA-$Version-win-x64"
$stagePath = Join-Path $stageRoot $stageName
$archivePath = Join-Path $packageRoot "$stageName.zip"

if (!(Test-Path -LiteralPath $dotnetExe -PathType Leaf))
{
    throw "Pinned local .NET SDK not found. Run build-pipeline.cmd bootstrap first."
}

function Invoke-Native
{
    param([string]$FilePath, [string[]]$ArgumentList, [string]$WorkingDirectory)
    Push-Location $WorkingDirectory
    try
    {
        & $FilePath @ArgumentList
        $nativeExitCode = $LASTEXITCODE
    }
    finally
    {
        Pop-Location
    }

    if ($nativeExitCode -ne 0)
    {
        throw "Command failed with exit code ${nativeExitCode}: $FilePath $($ArgumentList -join ' ')"
    }
}

function Assert-GeneratedPath
{
    param([string]$Path)
    $absolutePath = [IO.Path]::GetFullPath($Path)
    $absoluteArtifacts = [IO.Path]::GetFullPath($artifactsRoot).TrimEnd([IO.Path]::DirectorySeparatorChar)
    if (!$absolutePath.StartsWith($absoluteArtifacts + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase))
    {
        throw "Refusing to modify a path outside the generated artifacts directory: $absolutePath"
    }
}

$policyScript = Join-Path $rootPath "scripts\build\Check-AssetPolicy.ps1"
Invoke-Native "powershell.exe" @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $policyScript, "-Mode", "Release", "-Root", $rootPath) $rootPath

Assert-GeneratedPath $stagePath
Assert-GeneratedPath $archivePath
if (Test-Path -LiteralPath $stagePath)
{
    Remove-Item -LiteralPath $stagePath -Recurse -Force
}
if (Test-Path -LiteralPath $archivePath)
{
    Remove-Item -LiteralPath $archivePath -Force
}

New-Item -ItemType Directory -Path $stagePath -Force | Out-Null
New-Item -ItemType Directory -Path $packageRoot -Force | Out-Null

$publishArguments = @(
    "publish",
    "-c", "Release",
    "--nologo",
    "-p:TargetPlatform=win-x64",
    "-p:CopyGenericLauncher=False",
    "-p:CopyCncDll=True",
    "-p:CopyD2kDll=False",
    "-r", "win-x64",
    "-p:PublishDir=$stagePath",
    "--self-contained", "true"
)
Invoke-Native $dotnetExe $publishArguments $engineRoot

$engineDataFiles = @("VERSION", "AUTHORS", "COPYING", "IP2LOCATION-LITE-DB1.IPV6.BIN.ZIP", "global mix database.dat")
foreach ($dataFile in $engineDataFiles)
{
    $sourceFile = Join-Path $engineRoot $dataFile
    if (!(Test-Path -LiteralPath $sourceFile -PathType Leaf))
    {
        throw "Required engine package file is missing: $sourceFile"
    }
    Copy-Item -LiteralPath $sourceFile -Destination $stagePath
}

Copy-Item -LiteralPath (Join-Path $engineRoot "glsl") -Destination $stagePath -Recurse
New-Item -ItemType Directory -Path (Join-Path $stagePath "mods") -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $engineRoot "mods\common") -Destination (Join-Path $stagePath "mods") -Recurse
Copy-Item -LiteralPath (Join-Path $rootPath "mods\sa") -Destination (Join-Path $stagePath "mods") -Recurse
Copy-Item -LiteralPath (Join-Path $rootPath "mods\sacontent") -Destination (Join-Path $stagePath "mods") -Recurse

foreach ($modAssembly in Get-ChildItem -LiteralPath (Join-Path $engineRoot "bin") -File | Where-Object { $_.Name -like "OpenRA.Mods.OpenSA.*" })
{
    Copy-Item -LiteralPath $modAssembly.FullName -Destination $stagePath
}

$launcherProject = Join-Path $engineRoot "OpenRA.WindowsLauncher\OpenRA.WindowsLauncher.csproj"
$launcherArguments = @(
    "publish", $launcherProject,
    "-c", "Release",
    "--nologo",
    "-r", "win-x64",
    "-p:LauncherName=OpenSA",
    "-p:TargetPlatform=win-x64",
    "-p:ModID=sa",
    "-p:PublishDir=$stagePath",
    "-p:FaqUrl=https://github.com/OpenRA/OpenRA/wiki/FAQ",
    "-p:InformationalVersion=$Version",
    "--self-contained", "true"
)
Invoke-Native $dotnetExe $launcherArguments $engineRoot

$stagedModYaml = Join-Path $stagePath "mods\sa\mod.yaml"
$modYamlContent = [IO.File]::ReadAllText($stagedModYaml)
$versionPattern = New-Object Text.RegularExpressions.Regex("(?m)^(\s*Version:)\s*.*$")
$modYamlContent = $versionPattern.Replace($modYamlContent, ('$1 ' + $Version), 1)
[IO.File]::WriteAllText($stagedModYaml, $modYamlContent, (New-Object Text.UTF8Encoding($false)))

$stageTools = Join-Path $stagePath "tools"
New-Item -ItemType Directory -Path $stageTools -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $rootPath "scripts\build\Import-OriginalAssets.ps1") -Destination $stageTools
Copy-Item -LiteralPath (Join-Path $rootPath "assets\original-game-manifest.json") -Destination $stageTools

$assetInstructions = @(
    "OpenSA does not include or download original Swarm Assault assets.",
    "",
    "From the source checkout, import a user-owned original installation with:",
    "  build-pipeline.cmd import-assets -OriginalGamePath C:\Path\To\SwarmAssault",
    "",
    "For this portable package, you may instead run:",
    "  powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools\Import-OriginalAssets.ps1 -SourcePath C:\Path\To\SwarmAssault",
    "",
    "Assets are verified and copied to the current user's OpenRA support directory."
)
$assetInstructions | Set-Content -LiteralPath (Join-Path $stagePath "ASSETS.txt") -Encoding UTF8

Invoke-Native "powershell.exe" @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $policyScript, "-Mode", "Release", "-Root", $rootPath, "-StagePath", $stagePath) $rootPath

if (!(Test-Path -LiteralPath (Join-Path $stagePath "OpenSA.exe") -PathType Leaf))
{
    throw "Portable staging did not produce OpenSA.exe."
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($stagePath, $archivePath, [IO.Compression.CompressionLevel]::Optimal, $true)
$archiveHash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
$hashPath = "$archivePath.sha256"
"$archiveHash  $([IO.Path]::GetFileName($archivePath))" | Set-Content -LiteralPath $hashPath -Encoding ASCII

Write-Host "Portable package: $archivePath" -ForegroundColor Green
Write-Host "SHA-256: $archiveHash"
