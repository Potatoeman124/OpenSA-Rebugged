[CmdletBinding()]
param(
    [string]$Root,
    [Parameter(Mandatory = $true)]
    [ValidatePattern("^[0-9A-Za-z._-]+$")]
    [string]$Version,
    [ValidateSet("x64", "x86")]
    [string]$Architecture = "x64"
)

$ErrorActionPreference = "Stop"
if ([string]::IsNullOrWhiteSpace($Root))
{
    $Root = Join-Path $PSScriptRoot "..\.."
}

$rootPath = [IO.Path]::GetFullPath($Root)
$engineRoot = Join-Path $rootPath "engine"
$env:DOTNET_ROOT = Join-Path $rootPath ".tools/dotnet"
$env:NUGET_PACKAGES = Join-Path $rootPath ".tools/nuget-packages"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
$dotnetExe = Join-Path $rootPath ".tools\dotnet\dotnet.exe"
$artifactsRoot = Join-Path $rootPath "artifacts"
$stageRoot = Join-Path $artifactsRoot "staging"
$packageRoot = Join-Path $artifactsRoot "packages"
$stageName = "OpenSA-reBugged-$Version-win-$Architecture"
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
Invoke-Native "powershell.exe" @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $policyScript, "-Mode", "Release", "-Root", $rootPath, "-ReleaseVersion", $Version) $rootPath

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
    "-p:TargetPlatform=win-$Architecture",
    "-p:CopyGenericLauncher=False",
    "-p:CopyCncDll=True",
    "-p:CopyD2kDll=False",
    "-r", "win-$Architecture",
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

$modProject = Join-Path $rootPath "OpenRA.Mods.OpenSA/OpenRA.Mods.OpenSA.csproj"
Invoke-Native $dotnetExe @("publish", $modProject, "-c", "Release", "--nologo", "-r", "win-$Architecture", "-p:TargetPlatform=win-$Architecture", "-p:PublishDir=$stagePath", "--self-contained", "true") $rootPath

# Wrap the existing PNG icon sizes in an ICO container without changing pixels.
$iconPath = Join-Path $stagePath "sa.ico"
$iconSizes = @(16, 24, 32, 48, 256)
$iconData = @($iconSizes | ForEach-Object { ,([IO.File]::ReadAllBytes((Join-Path $rootPath "packaging/artwork/icon_$($_)x$($_).png"))) })
$iconStream = [IO.File]::Create($iconPath)
$iconWriter = New-Object IO.BinaryWriter($iconStream)
try
{
    $iconWriter.Write([uint16]0); $iconWriter.Write([uint16]1); $iconWriter.Write([uint16]$iconSizes.Count)
    $iconOffset = 6 + 16 * $iconSizes.Count
    for ($i = 0; $i -lt $iconSizes.Count; $i++)
    {
        $dimension = $iconSizes[$i] % 256
        $iconWriter.Write([byte]$dimension); $iconWriter.Write([byte]$dimension)
        $iconWriter.Write([byte]0); $iconWriter.Write([byte]0)
        $iconWriter.Write([uint16]1); $iconWriter.Write([uint16]32)
        $iconWriter.Write([uint32]$iconData[$i].Length); $iconWriter.Write([uint32]$iconOffset)
        $iconOffset += $iconData[$i].Length
    }
    foreach ($icon in $iconData) { $iconWriter.Write([byte[]]$icon) }
}
finally { $iconWriter.Dispose(); $iconStream.Dispose() }

$launcherProject = Join-Path $engineRoot "OpenRA.WindowsLauncher\OpenRA.WindowsLauncher.csproj"
$launcherArguments = @(
    "publish", $launcherProject,
    "-c", "Release",
    "--nologo",
    "-r", "win-$Architecture",
    "-p:LauncherName=OpenSA",
    "-p:LauncherIcon=$iconPath",
    "-p:DisplayName=OpenSA reBugged",
    "-p:TargetPlatform=win-$Architecture",
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

Copy-Item -LiteralPath (Join-Path $rootPath "assets/provenance") -Destination (Join-Path $stagePath "provenance") -Recurse
Copy-Item -LiteralPath (Join-Path $rootPath "docs/ASSET_PROVENANCE_AUDIT.md") -Destination (Join-Path $stagePath "provenance/AUDIT.md")

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

Invoke-Native "powershell.exe" @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $policyScript, "-Mode", "Release", "-Root", $rootPath, "-StagePath", $stagePath, "-ReleaseVersion", $Version) $rootPath

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
