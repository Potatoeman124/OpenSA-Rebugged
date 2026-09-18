[CmdletBinding()]
param([string]$Root)
if (!$Root) { $Root = Join-Path $PSScriptRoot "..\.." }
$ErrorActionPreference = "Stop"
$Root = [IO.Path]::GetFullPath($Root)
$check = Join-Path $Root "scripts\build\Check-AssetPolicy.ps1"
$fixture = Join-Path $Root ("artifacts\asset-policy-tests\" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path (Join-Path $fixture "assets") -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $fixture "stage") -Force | Out-Null
& git -C $fixture init --quiet
if ($LASTEXITCODE -ne 0) { throw "Fixture git init failed." }
[IO.File]::WriteAllBytes((Join-Path $fixture "sample.wav"), [byte[]]@(1, 2, 3))
Set-Content -LiteralPath (Join-Path $fixture "evidence.md") -Value "Fixture evidence, not an actual asset approval."
$approval = [ordered]@{
    glob = "sample.wav"; author = "Test author"; source = "https://example.invalid/test"
    license = "Test-only license"; evidence = "evidence.md"
    sha256 = (Get-FileHash -LiteralPath (Join-Path $fixture "sample.wav") -Algorithm SHA256).Hash
}
$policy = [ordered]@{
    defaultDenyExtensions = @(".wav"); approved = @($approval); unresolved = @()
    textMarkers = @(@{ pattern = "Original" + "Marker"; reason = "Fixture marker" })
    forbiddenPackageFileNames = @("Game.ANI")
}
function Check-Case
{
    param([string]$Name, [int]$ExpectedExit, [string]$Kind, [string]$Mode = "Release", [switch]$Stage)
    $policy | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $fixture "assets\provenance-policy.json") -Encoding UTF8
    $report = Join-Path $fixture "$Name.report.json"
    $argsList = @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $check, "-Root", $fixture, "-Mode", $Mode, "-ReportPath", $report)
    if ($Stage) { $argsList += @("-StagePath", (Join-Path $fixture "stage")) }
    & powershell.exe @argsList *> (Join-Path $fixture "$Name.log")
    if ($LASTEXITCODE -ne $ExpectedExit) { throw "$Name returned $LASTEXITCODE; expected $ExpectedExit." }
    $result = Get-Content -LiteralPath $report -Raw | ConvertFrom-Json
    if ($Kind -and @($result.findings | Where-Object kind -eq $Kind).Count -eq 0) { throw "$Name did not report $Kind." }
    if (!$Kind -and $result.findingCount -ne 0) { throw "$Name unexpectedly reported findings." }
    # Reports and logs are test outputs, not future input assets/markers.
    Set-Content -LiteralPath (Join-Path $fixture ".gitignore") -Value "*.report.json`n*.log`nstage/"
    Write-Host "PASS $Name"
}
# Ignore fixture policy text: marker patterns must not match themselves.
$policy.textMarkers[0].pattern = "Original" + "[ ]Marker"
Check-Case "approved" 0 ""
$approval.sha256 = "0" * 64
Check-Case "changed-bytes" 1 "invalid-approval"
$approval.sha256 = (Get-FileHash -LiteralPath (Join-Path $fixture "sample.wav") -Algorithm SHA256).Hash
$approval.evidence = "missing.md"
Check-Case "missing-evidence" 1 "invalid-approval"
$approval.evidence = "evidence.md"
$approval.license = ""
Check-Case "missing-license" 1 "invalid-approval"
$approval.license = "Test-only license"
$approval.glob = "*.wav"
Check-Case "wildcard" 1 "invalid-approval"
$approval.glob = "sample.wav"
$policy.approved = @()
Check-Case "unapproved" 1 "unclassified"
Check-Case "inventory-stays-informational" 0 "unclassified" "Inventory"
$policy.approved = @($approval)
Set-Content -LiteralPath (Join-Path $fixture "marker.md") -Value ("Original" + " Marker")
Check-Case "source-marker" 1 "source-marker"
Set-Content -LiteralPath (Join-Path $fixture "marker.md") -Value "No flagged content."
[IO.File]::WriteAllBytes((Join-Path $fixture "stage\Game.ANI"), [byte[]]@(1))
Check-Case "forbidden-package" 1 "packaged-original" -Stage
Write-Host "All 9 asset-policy checks passed. Evidence: $fixture"
