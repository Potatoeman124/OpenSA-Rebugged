[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [Parameter(Mandatory = $true)]
    [string]$SourcePath,
    [string]$DestinationRoot = (Join-Path ([Environment]::GetFolderPath("ApplicationData")) "OpenRA\Content\sa"),
    [string]$ManifestPath
)

$ErrorActionPreference = "Stop"
if ([string]::IsNullOrWhiteSpace($ManifestPath))
{
    $adjacentManifest = Join-Path $PSScriptRoot "original-game-manifest.json"
    if (Test-Path -LiteralPath $adjacentManifest -PathType Leaf)
    {
        $ManifestPath = $adjacentManifest
    }
    else
    {
        $ManifestPath = Join-Path $PSScriptRoot "..\..\assets\original-game-manifest.json"
    }
}

$sourceRoot = [IO.Path]::GetFullPath($SourcePath)
$destinationRootPath = [IO.Path]::GetFullPath($DestinationRoot)
$manifestFullPath = [IO.Path]::GetFullPath($ManifestPath)

if (!(Test-Path -LiteralPath $sourceRoot -PathType Container))
{
    throw "Original-game directory not found: $sourceRoot"
}
if (!(Test-Path -LiteralPath $manifestFullPath -PathType Leaf))
{
    throw "Original-game manifest not found: $manifestFullPath"
}

$manifest = Get-Content -LiteralPath $manifestFullPath -Raw | ConvertFrom-Json
$validatedFiles = New-Object System.Collections.Generic.List[object]
foreach ($entry in @($manifest.files))
{
    $sourceRelative = $entry.source.Replace("/", [IO.Path]::DirectorySeparatorChar)
    $sourceFile = Join-Path $sourceRoot $sourceRelative
    if (!(Test-Path -LiteralPath $sourceFile -PathType Leaf))
    {
        throw "Required original-game file is missing: $sourceFile"
    }

    $fileInfo = Get-Item -LiteralPath $sourceFile
    if ($fileInfo.Length -ne [long]$entry.size)
    {
        throw "Unexpected size for $sourceFile. Expected $($entry.size), found $($fileInfo.Length)."
    }

    $actualHash = (Get-FileHash -LiteralPath $sourceFile -Algorithm SHA256).Hash
    if ($actualHash -ne $entry.sha256)
    {
        throw "Unexpected SHA-256 for $sourceFile. This importer currently supports only the known original Windows release."
    }

    $destinationRelative = $entry.destination.Replace("/", [IO.Path]::DirectorySeparatorChar)
    $validatedFiles.Add([pscustomobject]@{
        source = $sourceFile
        destination = (Join-Path $destinationRootPath $destinationRelative)
        sha256 = $actualHash
    })
}

Write-Host "Validated $($validatedFiles.Count) original-game files." -ForegroundColor Green
foreach ($file in $validatedFiles)
{
    $destinationDirectory = Split-Path -Parent $file.destination
    if ($PSCmdlet.ShouldProcess($file.destination, "Copy verified original-game asset"))
    {
        if (!(Test-Path -LiteralPath $destinationDirectory))
        {
            New-Item -ItemType Directory -Path $destinationDirectory -Force | Out-Null
        }

        Copy-Item -LiteralPath $file.source -Destination $file.destination -Force
        $copiedHash = (Get-FileHash -LiteralPath $file.destination -Algorithm SHA256).Hash
        if ($copiedHash -ne $file.sha256)
        {
            throw "Post-copy verification failed for $($file.destination)."
        }
    }
}

Write-Host "Original-game assets are available at: $destinationRootPath" -ForegroundColor Green
Write-Host "No original-game asset was written to the repository or build artifacts."
