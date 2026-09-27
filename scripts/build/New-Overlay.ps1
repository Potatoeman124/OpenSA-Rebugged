[CmdletBinding()]
param(
    [string]$Root,
    [string]$BasePath,
    [ValidateSet("1.1")]
    [string]$Version = "1.1"
)

$ErrorActionPreference = "Stop"
if ([string]::IsNullOrWhiteSpace($Root)) { $Root = Join-Path $PSScriptRoot "../.." }
$rootPath = [IO.Path]::GetFullPath($Root)
$baseRoot = $null
if (![string]::IsNullOrWhiteSpace($BasePath))
{ $baseRoot = (Resolve-Path -LiteralPath $BasePath).Path.TrimEnd('\', '/') }
$artifactsRoot = Join-Path $rootPath "artifacts"
$stagePath = Join-Path $artifactsRoot "overlay-$Version/payload"
$packageRoot = $artifactsRoot
$archiveName = "OpenSA-reBugged-$Version-overlay-20230905-x64.zip"
$archivePath = Join-Path $packageRoot $archiveName
$expectedEngine = "386f691c2e1f469596ef6f58e258a10a176bdc3d"
$utf8 = New-Object Text.UTF8Encoding($false)

function Invoke-Native
{
    param([string]$FilePath, [string[]]$ArgumentList)
    & $FilePath @ArgumentList
    if ($LASTEXITCODE -ne 0) { throw "Command failed ($LASTEXITCODE): $FilePath" }
}

function Assert-GeneratedPath
{
    param([string]$Path)
    $absolute = [IO.Path]::GetFullPath($Path)
    $prefix = [IO.Path]::GetFullPath($artifactsRoot).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    if (!$absolute.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) -or
        ($baseRoot -and ($baseRoot.StartsWith($absolute, [StringComparison]::OrdinalIgnoreCase) -or
        $absolute.StartsWith($baseRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase))))
    { throw "Unsafe generated path: $absolute" }
    if ((Test-Path -LiteralPath $absolute) -and
        (Get-Item -LiteralPath $absolute).Attributes.HasFlag([IO.FileAttributes]::ReparsePoint))
    { throw "Generated path must not be a link: $absolute" }
}

# The recorded baseline makes packaging independent of personal installation paths
# and remains usable after a developer has already installed reBugged locally.
$baselinePath = Join-Path $rootPath "packaging/overlay/opensa-20230905-x64.json"
$baseline = Get-Content -LiteralPath $baselinePath -Raw | ConvertFrom-Json
if ($baseline.format -ne 1 -or $baseline.version -ne "20230905" -or
    $baseline.platform -ne "win-x64" -or $baseline.engine -ne $expectedEngine)
{ throw "Unsupported overlay baseline manifest: $baselinePath" }
$baselineHashes = @{}
foreach ($file in $baseline.files.PSObject.Properties)
{
    if (($file.Name -ne "OpenRA.Mods.OpenSA.dll" -and !$file.Name.StartsWith("mods/sa/")) -or
        $file.Name.Contains("..") -or $file.Value -notmatch '^[0-9a-f]{64}$')
    { throw "Invalid file or hash in overlay baseline: $($file.Name)" }
    $baselineHashes[$file.Name] = [string]$file.Value
}
if (!$baselineHashes.ContainsKey("mods/sa/mod.yaml") -or !$baselineHashes.ContainsKey("OpenRA.Mods.OpenSA.dll"))
{ throw "Incomplete overlay baseline manifest." }

# Optional read-only verification of a stock installation; never needed by the task.
if ($baseRoot)
{
    if ([IO.File]::ReadAllText((Join-Path $baseRoot "VERSION")).Trim() -ne $expectedEngine)
    { throw "The overlay requires the stock OpenSA 20230905 engine ($expectedEngine)." }
    $baseYaml = [IO.File]::ReadAllText((Join-Path $baseRoot "mods/sa/mod.yaml"))
    if ($baseYaml -notmatch '(?m)^\tVersion: 20230905\s*$')
    { throw "The overlay must be built against an unmodified OpenSA 20230905 installation." }
    foreach ($name in @("OpenSA.exe", "OpenRA.Utility.exe", "SDL2.dll"))
    {
        $data = [IO.File]::ReadAllBytes((Join-Path $baseRoot $name))
        $peOffset = [BitConverter]::ToInt32($data, 0x3c)
        if ([BitConverter]::ToUInt16($data, $peOffset + 4) -ne 0x8664)
        { throw "The overlay requires the x64 installation: $name is not x64." }
    }
    foreach ($name in @("OpenRA.Game.dll", "OpenRA.Mods.Common.dll", "OpenRA.Mods.Cnc.dll", "OpenRA.Mods.OpenSA.dll", "COPYING"))
    {
        if (!(Test-Path -LiteralPath (Join-Path $baseRoot $name) -PathType Leaf))
        { throw "Incomplete base installation: missing $name" }
    }

    foreach ($path in $baselineHashes.Keys)
    {
        $file = Join-Path $baseRoot $path
        if (!(Test-Path -LiteralPath $file -PathType Leaf) -or
            (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ne $baselineHashes[$path])
        { throw "The installation differs from the recorded stock baseline: $path" }
    }
}

$dotnetExe = Join-Path $rootPath ".tools/dotnet/dotnet.exe"
if (!(Test-Path -LiteralPath $dotnetExe -PathType Leaf))
{ throw "Pinned SDK missing. Run build-pipeline.cmd bootstrap first." }
$policyScript = Join-Path $PSScriptRoot "Check-AssetPolicy.ps1"
Invoke-Native "powershell.exe" @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $policyScript, "-Mode", "Release", "-Root", $rootPath, "-ReleaseVersion", $Version)

$env:DOTNET_ROOT = Join-Path $rootPath ".tools/dotnet"
$env:NUGET_PACKAGES = Join-Path $rootPath ".tools/nuget-packages"
$env:DOTNET_MULTILEVEL_LOOKUP = "0"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
Push-Location $rootPath
try
{
    Invoke-Native $dotnetExe @("build", "OpenRA.Mods.OpenSA/OpenRA.Mods.OpenSA.csproj", "-c", "Release", "--nologo", "-p:TargetPlatform=win-x64")
}
finally { Pop-Location }

Assert-GeneratedPath $stagePath
Assert-GeneratedPath $archivePath
if (Test-Path -LiteralPath $stagePath)
{
    # A previous test tree must never be used as staging, particularly if it contains links.
    if (Get-ChildItem -LiteralPath $stagePath -Recurse -Force | Where-Object { $_.Attributes.HasFlag([IO.FileAttributes]::ReparsePoint) })
    { throw "Refusing to clean staging containing links: $stagePath" }
    Remove-Item -LiteralPath $stagePath -Recurse -Force
}
New-Item -ItemType Directory -Path $stagePath -Force | Out-Null
New-Item -ItemType Directory -Path $packageRoot -Force | Out-Null

$changes = New-Object System.Collections.Generic.List[object]
function Add-DeltaFile
{
    param([string]$RelativePath, [byte[]]$Bytes)
    $sha = [Security.Cryptography.SHA256]::Create()
    try { $newHash = [BitConverter]::ToString($sha.ComputeHash($Bytes)).Replace("-", "").ToLowerInvariant() }
    finally { $sha.Dispose() }
    $oldHash = $baselineHashes[$RelativePath]
    if ($oldHash -eq $newHash) { return }
    $target = Join-Path $stagePath $RelativePath
    New-Item -ItemType Directory -Path (Split-Path $target -Parent) -Force | Out-Null
    [IO.File]::WriteAllBytes($target, $Bytes)
    $changes.Add([ordered]@{ path = $RelativePath; action = $(if ($oldHash) { "replace" } else { "add" }); baseSha256 = $oldHash; sha256 = $newHash; bytes = $Bytes.Length })
}

# Only the mod assembly and changed mod content are shipped. The base launcher,
# engine, common mod, native libraries and .NET runtime are deliberately reused.
Add-DeltaFile "OpenRA.Mods.OpenSA.dll" ([IO.File]::ReadAllBytes((Join-Path $rootPath "engine/bin/OpenRA.Mods.OpenSA.dll")))
$modRoot = Join-Path $rootPath "mods/sa"
foreach ($file in Get-ChildItem -LiteralPath $modRoot -File -Recurse | Sort-Object FullName)
{
    $relative = "mods/sa/" + $file.FullName.Substring($modRoot.Length + 1).Replace('\', '/')
    if ($relative -eq "mods/sa/mod.yaml")
    {
        $yaml = [IO.File]::ReadAllText($file.FullName)
        $pattern = New-Object Text.RegularExpressions.Regex('(?m)^(\s*Version:)\s*.*$')
        $yaml = $pattern.Replace($yaml, ('$1 ' + $Version), 1)
        $yaml = $yaml.Replace('/{DEV_VERSION}: User', "/$Version" + ': User')
        Add-DeltaFile $relative ($utf8.GetBytes($yaml))
    }
    else { Add-DeltaFile $relative ([IO.File]::ReadAllBytes($file.FullName)) }
}

# Retain the audit alongside the already scoped content exception.
foreach ($item in @(
    @("docs/ASSET_PROVENANCE_AUDIT.md", "rebugged-release/provenance/AUDIT.md"),
    @("assets/provenance/2026-09-18-inventory.json", "rebugged-release/provenance/2026-09-18-inventory.json"),
    @("assets/provenance/release-1.1-exception.json", "rebugged-release/provenance/release-1.1-exception.json"),
    @("COPYING", "rebugged-release/COPYING")))
{ Add-DeltaFile $item[1] ([IO.File]::ReadAllBytes((Join-Path $rootPath $item[0]))) }

$sourceCommit = (& git -C $rootPath rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0) { throw "Cannot resolve source revision." }
$readme = @"
OSArB 1.1: Return of the RMG - overlay for OpenSA 20230905 x64

INSTALL
1. Close OpenSA. Back up the entire existing OpenSA installation folder.
2. Copy this ZIP into that folder (the one containing OpenSA.exe) and extract
   it directly there. Allow replacing existing files. Do not extract into a
   new subfolder. Do not delete the existing installation first.
3. Start your usual OpenSA.exe or existing OpenSA shortcut.

This is an update for a WORKING OpenSA 20230905 x64 installation, not a
standalone game. It reuses that installation's engine, launcher, .NET runtime,
native libraries, unchanged content and already installed Swarm Assault assets.
No installer, asset download, import command or administrator script is needed.
Files are added/replaced only; there are no deletion or registry actions.

Your existing OpenRA settings and assets remain in their current location.
reBugged's custom maps/replays use version 1.1. Old 20230905 maps and replays
are retained in their old folders; saved games/replays are not converted.
To undo this update, restore the backed-up installation folder.
The Windows shortcut and installed-program/uninstaller name remain OpenSA.

PAYLOAD AND SOURCE
rebugged-overlay-manifest.json records every payload file and its SHA-256,
plus expected hashes for replaced baseline files. The ZIP contains neither
an executable nor the eight external Game/Start ANI, DDF, MIN and SDF bundles.
Mod source: https://github.com/Potatoeman124/OpenSA-Rebugged/tree/$sourceCommit
Engine source: https://github.com/OpenRA/OpenRA/tree/$expectedEngine
Code license: rebugged-release/COPYING (GPLv3 or later).
Attribution: mods/sa/CREDITS and mods/sa/ASSET_ATTRIBUTIONS.md.
Inherited-content exception: mods/sa/RELEASE_EXCEPTION.md.
Preserved provenance audit: rebugged-release/provenance/.
"@
Add-DeltaFile "REBUGGED-README.txt" ($utf8.GetBytes($readme))
$manifest = [ordered]@{
    format = 1; product = "OpenSA reBugged"; version = $Version
    sourceCommit = $sourceCommit
    baseline = [ordered]@{ product = "OpenSA"; version = "20230905"; platform = "win-x64"; engine = $expectedEngine }
    installation = "Extract at the existing OpenSA.exe directory; replace files; delete nothing."
    files = @($changes.ToArray())
}
[IO.File]::WriteAllText((Join-Path $stagePath "rebugged-overlay-manifest.json"), ($manifest | ConvertTo-Json -Depth 8), $utf8)
Invoke-Native "powershell.exe" @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $policyScript, "-Mode", "Release", "-Root", $rootPath, "-StagePath", $stagePath, "-ReleaseVersion", $Version)

Add-Type -AssemblyName System.IO.Compression.FileSystem
if (Test-Path -LiteralPath $archivePath) { Remove-Item -LiteralPath $archivePath -Force }
[IO.Compression.ZipFile]::CreateFromDirectory($stagePath, $archivePath, [IO.Compression.CompressionLevel]::Optimal, $false)
$hash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText((Join-Path $packageRoot "$archiveName.sha256"), "$hash  $archiveName`n", $utf8)
Write-Host "Overlay ready: $archivePath"
Write-Host "$($changes.Count + 1) files; $((Get-Item -LiteralPath $archivePath).Length) bytes; SHA-256 $hash"
