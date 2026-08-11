[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [ValidateSet("help", "bootstrap", "build", "validate", "audit-assets", "verify-assets", "portable", "import-assets")]
    [string]$Command = "help",
    [string]$OriginalGamePath,
    [string]$SupportDir,
    [string]$Version = (Get-Date -Format "yyyyMMdd")
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$sdkVersion = "6.0.428"
$sdkUrl = "https://builds.dotnet.microsoft.com/dotnet/Sdk/6.0.428/dotnet-sdk-6.0.428-win-x64.zip"
$sdkSha512 = "c027cb47b264a13e529f8c7f3ba33ac91152b56749c8681fede1d6cd48723ae1e5f04a43bac1302ee81e35a5383f3e169654e5bb7c1d331dc11cce5a95052e32"
$toolsRoot = Join-Path $root ".tools"
$dotnetRoot = Join-Path $toolsRoot "dotnet"
$dotnetExe = Join-Path $dotnetRoot "dotnet.exe"

function Get-ConfigValue
{
    param([string]$Name)
    $match = Select-String -LiteralPath (Join-Path $root "mod.config") -Pattern ("^{0}=""([^""]+)""" -f [regex]::Escape($Name)) | Select-Object -First 1
    if ($null -eq $match)
    {
        throw "Required mod.config value is missing: $Name"
    }

    return $match.Matches[0].Groups[1].Value
}

$engineVersion = Get-ConfigValue "ENGINE_VERSION"
$engineRoot = Join-Path $root "engine"

function Invoke-Native
{
    param(
        [string]$FilePath,
        [string[]]$ArgumentList,
        [string]$WorkingDirectory = $root
    )

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

function Initialize-LocalSdk
{
    if (!(Test-Path -LiteralPath $dotnetExe -PathType Leaf))
    {
        $downloadsDirectory = Join-Path $toolsRoot "downloads"
        $sdkArchive = Join-Path $downloadsDirectory "dotnet-sdk-$sdkVersion-win-x64.zip"
        $sdkStage = Join-Path $toolsRoot "dotnet-stage"
        New-Item -ItemType Directory -Path $downloadsDirectory -Force | Out-Null

        if (!(Test-Path -LiteralPath $sdkArchive -PathType Leaf))
        {
            Write-Host "Downloading pinned .NET SDK $sdkVersion..."
            [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
            $client = New-Object Net.WebClient
            $client.DownloadFile($sdkUrl, $sdkArchive)
        }

        $actualSdkHash = (Get-FileHash -LiteralPath $sdkArchive -Algorithm SHA512).Hash.ToLowerInvariant()
        if ($actualSdkHash -ne $sdkSha512)
        {
            throw "Pinned .NET SDK archive failed SHA-512 verification: $sdkArchive"
        }

        if (Test-Path -LiteralPath $sdkStage)
        {
            Remove-Item -LiteralPath $sdkStage -Recurse -Force
        }

        New-Item -ItemType Directory -Path $sdkStage | Out-Null
        Expand-Archive -LiteralPath $sdkArchive -DestinationPath $sdkStage
        if (!(Test-Path -LiteralPath (Join-Path $sdkStage "dotnet.exe") -PathType Leaf))
        {
            throw "Pinned .NET SDK archive did not contain dotnet.exe."
        }

        if (Test-Path -LiteralPath $dotnetRoot)
        {
            throw "Refusing to overwrite an unexpected local SDK directory: $dotnetRoot"
        }

        Move-Item -LiteralPath $sdkStage -Destination $dotnetRoot
    }

    $env:DOTNET_ROOT = $dotnetRoot
    $env:DOTNET_MULTILEVEL_LOOKUP = "0"
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
    $env:DOTNET_NOLOGO = "1"
    $env:NUGET_PACKAGES = Join-Path $toolsRoot "nuget-packages"
    $env:PATH = "$dotnetRoot;$env:PATH"
    New-Item -ItemType Directory -Path $env:NUGET_PACKAGES -Force | Out-Null

    $actualVersion = & $dotnetExe --version
    if ($LASTEXITCODE -ne 0 -or $actualVersion.Trim() -ne $sdkVersion)
    {
        throw "Expected local .NET SDK $sdkVersion, found '$actualVersion'."
    }

    Write-Host "Pinned .NET SDK ready: $sdkVersion" -ForegroundColor Green
}

function Initialize-Engine
{
    $versionFile = Join-Path $engineRoot "VERSION"
    if (Test-Path -LiteralPath $versionFile -PathType Leaf)
    {
        $currentVersion = (Get-Content -LiteralPath $versionFile -TotalCount 1).Trim()
        if ($currentVersion -eq $engineVersion)
        {
            Write-Host "Pinned OpenRA engine ready: $engineVersion" -ForegroundColor Green
            return
        }

        throw "Engine version mismatch. Expected $engineVersion, found $currentVersion. Move or remove the ignored engine directory, then bootstrap again."
    }

    if (Test-Path -LiteralPath $engineRoot)
    {
        throw "An unversioned engine directory already exists. Move or remove it before bootstrapping: $engineRoot"
    }

    if ($null -eq (Get-Command git.exe -ErrorAction SilentlyContinue))
    {
        throw "Git is required to acquire the pinned OpenRA engine commit."
    }

    $engineStage = Join-Path $toolsRoot "engine-stage-$engineVersion"
    if (Test-Path -LiteralPath $engineStage)
    {
        Remove-Item -LiteralPath $engineStage -Recurse -Force
    }

    New-Item -ItemType Directory -Path $engineStage | Out-Null
    try
    {
        Invoke-Native "git.exe" @("init", $engineStage)
        Invoke-Native "git.exe" @("-C", $engineStage, "remote", "add", "origin", "https://github.com/OpenRA/OpenRA.git")
        Invoke-Native "git.exe" @("-C", $engineStage, "fetch", "--depth", "1", "origin", $engineVersion)
        Invoke-Native "git.exe" @("-C", $engineStage, "checkout", "--detach", "FETCH_HEAD")
        $actualEngineCommit = (& git.exe -C $engineStage rev-parse HEAD).Trim()
        if ($LASTEXITCODE -ne 0 -or $actualEngineCommit -ne $engineVersion)
        {
            throw "Fetched OpenRA engine commit did not match $engineVersion."
        }

        Move-Item -LiteralPath $engineStage -Destination $engineRoot
    }
    catch
    {
        if (Test-Path -LiteralPath $engineStage)
        {
            Remove-Item -LiteralPath $engineStage -Recurse -Force
        }
        throw
    }

    Invoke-Native (Join-Path $engineRoot "make.cmd") @("version", $engineVersion) $engineRoot
    if (!(Test-Path -LiteralPath $versionFile -PathType Leaf) -or (Get-Content -LiteralPath $versionFile -TotalCount 1).Trim() -ne $engineVersion)
    {
        throw "OpenRA engine version initialization failed."
    }

    Write-Host "Pinned OpenRA engine ready: $engineVersion" -ForegroundColor Green
}

function Initialize-Dependencies
{
    Initialize-LocalSdk
    Initialize-Engine
}

function Invoke-Build
{
    Initialize-Dependencies
    Invoke-Native "cmd.exe" @("/d", "/c", "make.cmd", "all") $root

    $requiredOutputs = @(
        (Join-Path $engineRoot "bin\OpenRA.exe"),
        (Join-Path $engineRoot "bin\OpenRA.Game.dll"),
        (Join-Path $engineRoot "bin\OpenRA.Utility.exe"),
        (Join-Path $engineRoot "bin\OpenRA.Mods.OpenSA.dll")
    )
    foreach ($output in $requiredOutputs)
    {
        if (!(Test-Path -LiteralPath $output -PathType Leaf))
        {
            throw "Build completed without required output: $output"
        }
    }

    Write-Host "OpenSA Release-configuration build completed (not a release package)." -ForegroundColor Green
}

function Invoke-PowerShellScript
{
    param([string]$ScriptPath, [string[]]$ArgumentList = @())
    $scriptArguments = @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $ScriptPath) + $ArgumentList
    Invoke-Native "powershell.exe" $scriptArguments $root
}

function Invoke-AssetPolicy
{
    param([ValidateSet("Inventory", "Release")][string]$Mode)
    $reportDirectory = Join-Path $root "artifacts\compliance"
    $reportPath = Join-Path $reportDirectory ("asset-policy-{0}.json" -f $Mode.ToLowerInvariant())
    Invoke-PowerShellScript (Join-Path $root "scripts\build\Check-AssetPolicy.ps1") @("-Mode", $Mode, "-Root", $root, "-ReportPath", $reportPath)
}

function Invoke-Validation
{
    Invoke-Build
    Invoke-Native "cmd.exe" @("/d", "/c", "make.cmd", "check") $root
    Invoke-Native "cmd.exe" @("/d", "/c", "make.cmd", "test") $root
    Invoke-PowerShellScript (Join-Path $root "scripts\build\Test-LuaSyntax.ps1") @("-Root", $root)
    Invoke-AssetPolicy "Inventory"
    Write-Host "Build and runtime-data validation completed." -ForegroundColor Green
}

function Show-Help
{
    Write-Host "OpenSA build pipeline"
    Write-Host ""
    Write-Host "  build-pipeline.cmd bootstrap"
    Write-Host "      Acquire and verify .NET $sdkVersion and OpenRA $engineVersion."
    Write-Host "  build-pipeline.cmd build"
    Write-Host "      Compile the engine and OpenSA in Release configuration."
    Write-Host "  build-pipeline.cmd validate"
    Write-Host "      Build, run static checks, validate MiniYAML/maps, Lua, and asset inventory."
    Write-Host "  build-pipeline.cmd audit-assets"
    Write-Host "      Produce an informational tracked-content provenance inventory."
    Write-Host "  build-pipeline.cmd verify-assets"
    Write-Host "      Enforce the release asset gate. Findings produce a non-zero exit code."
    Write-Host "  build-pipeline.cmd portable [-Version YYYYMMDD]"
    Write-Host "      Validate and stage an asset-free self-contained Windows package."
    Write-Host "  build-pipeline.cmd import-assets -OriginalGamePath C:\Path\To\SwarmAssault"
    Write-Host "      Verify and copy original assets to the user's OpenRA support directory."
}

try
{
    switch ($Command)
    {
        "help" {
            Show-Help
        }
        "bootstrap" {
            Initialize-Dependencies
        }
        "build" {
            Invoke-Build
        }
        "validate" {
            Invoke-Validation
        }
        "audit-assets" {
            Invoke-AssetPolicy "Inventory"
        }
        "verify-assets" {
            Invoke-AssetPolicy "Release"
        }
        "portable" {
            Invoke-Validation
            Invoke-AssetPolicy "Release"
            Invoke-PowerShellScript (Join-Path $root "scripts\build\New-Portable.ps1") @("-Root", $root, "-Version", $Version)
        }
        "import-assets" {
            if ([string]::IsNullOrWhiteSpace($OriginalGamePath))
            {
                throw "import-assets requires -OriginalGamePath pointing to an original installation containing Game and Start directories."
            }

            $importArguments = @("-SourcePath", $OriginalGamePath)
            if (![string]::IsNullOrWhiteSpace($SupportDir))
            {
                $importArguments += @("-DestinationRoot", $SupportDir)
            }
            Invoke-PowerShellScript (Join-Path $root "scripts\build\Import-OriginalAssets.ps1") $importArguments
        }
    }
}
catch
{
    Write-Host $_.Exception.Message -ForegroundColor Red
    exit 1
}
