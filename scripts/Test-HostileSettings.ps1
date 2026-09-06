[CmdletBinding()]
param([switch]$Render)

$ErrorActionPreference = "Stop"
$taskRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$taskOutput = Join-Path $taskRoot "artifacts\hostiles"
New-Item -ItemType Directory -Path $taskOutput -Force | Out-Null
$env:DOTNET_ROOT = Join-Path $taskRoot ".tools\dotnet"
$env:DOTNET_MULTILEVEL_LOOKUP = "0"
$env:ENGINE_DIR = Join-Path $taskRoot "engine"
$env:MOD_SEARCH_PATHS = "$taskRoot\mods,$taskRoot\engine\mods"
$taskDotnet = Join-Path $env:DOTNET_ROOT "dotnet.exe"
$taskUtility = Join-Path $taskRoot "engine\bin\OpenRA.Utility.exe"

Push-Location $taskRoot
try {
    & $taskDotnet build OpenSA.sln -c Release --no-restore --nologo 2>&1 | Tee-Object -FilePath (Join-Path $taskOutput "build.log")
    if ($LASTEXITCODE -ne 0) { throw "Hostile settings build failed." }
    & $taskUtility sa --validate-sa-hostiles 2>&1 | Tee-Object -FilePath (Join-Path $taskOutput "self-tests.log")
    if ($LASTEXITCODE -ne 0) { throw "Hostile settings self-tests failed." }
    & $taskUtility sa --validate-sa-presets $taskOutput 2>&1 | Tee-Object -FilePath (Join-Path $taskOutput "presets.log")
    if ($LASTEXITCODE -ne 0) { throw "Game option preset tests failed." }
    & $taskUtility sa --check-yaml 2>&1 | Tee-Object -FilePath (Join-Path $taskOutput "yaml.log")
    if ($LASTEXITCODE -ne 0) { throw "Mod YAML validation failed." }
    if ($Render) {
        & $taskUtility sa --render-sa-hostiles (Join-Path $taskOutput "ui") 2>&1 | Tee-Object -FilePath (Join-Path $taskOutput "runtime-ui.log")
        if ($LASTEXITCODE -ne 0) { throw "Renderer or live-world hostile tests failed." }
        & $taskUtility sa --render-sa-hostiles (Join-Path $taskOutput "ui-small") --small 2>&1 | Tee-Object -FilePath (Join-Path $taskOutput "runtime-ui-small.log")
        if ($LASTEXITCODE -ne 0) { throw "Small-window renderer or live-world tests failed." }
    }
}
finally {
    Pop-Location
}
Write-Host "Hostile settings checks passed. Evidence: $taskOutput"
