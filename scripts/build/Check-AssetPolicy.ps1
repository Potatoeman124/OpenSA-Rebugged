[CmdletBinding()]
param(
    [ValidateSet("Inventory", "Release")]
    [string]$Mode = "Inventory",
    [string]$Root,
    [string]$StagePath,
    [string]$ReportPath
)

$ErrorActionPreference = "Stop"
if ([string]::IsNullOrWhiteSpace($Root))
{
    $Root = Join-Path $PSScriptRoot "..\.."
}

$rootPath = [IO.Path]::GetFullPath($Root)
$policyPath = Join-Path $rootPath "assets\provenance-policy.json"
if (!(Test-Path -LiteralPath $policyPath -PathType Leaf))
{
    throw "Asset policy not found: $policyPath"
}

$policy = Get-Content -LiteralPath $policyPath -Raw | ConvertFrom-Json
$repositoryFiles = @(& git -C $rootPath ls-files --cached --others --exclude-standard)
if ($LASTEXITCODE -ne 0)
{
    throw "Unable to enumerate tracked files with git."
}

$findings = New-Object System.Collections.Generic.List[object]
$candidateExtensions = @($policy.defaultDenyExtensions | ForEach-Object { $_.ToLowerInvariant() })
$textExtensions = @(".cmd", ".config", ".cs", ".ftl", ".json", ".md", ".nsi", ".ps1", ".sh", ".yaml", ".yml")

function Find-MatchingRule
{
    param([string]$Path, [object[]]$Rules)
    foreach ($rule in @($Rules))
    {
        if ($null -ne $rule -and $Path -like $rule.glob)
        {
            return $rule
        }
    }

    return $null
}

foreach ($relativePath in $repositoryFiles)
{
    $normalizedPath = $relativePath.Replace([IO.Path]::DirectorySeparatorChar, [char]47)
    $fullPath = Join-Path $rootPath $relativePath
    if (!(Test-Path -LiteralPath $fullPath -PathType Leaf))
    {
        continue
    }

    $extension = [IO.Path]::GetExtension($relativePath).ToLowerInvariant()
    if ($candidateExtensions -contains $extension)
    {
        $approvedRule = Find-MatchingRule -Path $normalizedPath -Rules @($policy.approved)
        if ($null -eq $approvedRule)
        {
            $unresolvedRule = Find-MatchingRule -Path $normalizedPath -Rules @($policy.unresolved)
            if ($null -ne $unresolvedRule)
            {
                $findings.Add([pscustomobject]@{ kind = "unresolved"; path = $normalizedPath; reason = $unresolvedRule.reason })
            }
            else
            {
                $findings.Add([pscustomobject]@{ kind = "unclassified"; path = $normalizedPath; reason = "Content-bearing file has no approved provenance entry." })
            }
        }
    }

    if ($textExtensions -contains $extension)
    {
        $content = [IO.File]::ReadAllText($fullPath)
        foreach ($marker in @($policy.textMarkers))
        {
            if ($content -match $marker.pattern)
            {
                $findings.Add([pscustomobject]@{ kind = "source-marker"; path = $normalizedPath; reason = $marker.reason })
            }
        }

        if ($normalizedPath -eq "mods/sa/mod.yaml" -and $content -match "(?m)^\s*QuickDownload\s*:")
        {
            $findings.Add([pscustomobject]@{ kind = "forbidden-download"; path = $normalizedPath; reason = "Original-game assets must not have a remote quick-download route." })
        }
    }
}

if ($StagePath)
{
    $resolvedStage = [IO.Path]::GetFullPath($StagePath)
    if (!(Test-Path -LiteralPath $resolvedStage -PathType Container))
    {
        throw "Stage path not found: $resolvedStage"
    }

    $forbiddenNames = @($policy.forbiddenPackageFileNames)
    foreach ($file in Get-ChildItem -LiteralPath $resolvedStage -File -Recurse)
    {
        if ($forbiddenNames -contains $file.Name)
        {
            $relativeStagePath = $file.FullName.Substring($resolvedStage.Length).TrimStart([IO.Path]::DirectorySeparatorChar).Replace([IO.Path]::DirectorySeparatorChar, [char]47)
            $findings.Add([pscustomobject]@{ kind = "packaged-original"; path = $relativeStagePath; reason = "Original-game asset file is forbidden in build and release artifacts." })
        }
    }
}

$groupedFindings = @($findings | Group-Object kind | Sort-Object Name)
Write-Host "Asset policy mode: $Mode"
Write-Host "Repository files inspected: $($repositoryFiles.Count)"
if ($StagePath)
{
    Write-Host "Staging directory inspected: $([IO.Path]::GetFullPath($StagePath))"
}

if ($findings.Count -eq 0)
{
    Write-Host "Asset policy check passed." -ForegroundColor Green
}
else
{
    Write-Host "Asset policy findings: $($findings.Count)" -ForegroundColor Yellow
    foreach ($group in $groupedFindings)
    {
        Write-Host ("  {0}: {1}" -f $group.Name, $group.Count)
    }

    $findings | Select-Object -First 25 | ForEach-Object {
        Write-Host ("  [{0}] {1} - {2}" -f $_.kind, $_.path, $_.reason)
    }
    if ($findings.Count -gt 25)
    {
        Write-Host "  ... $($findings.Count - 25) additional findings omitted from console output."
    }
}

if ($ReportPath)
{
    $absoluteReportPath = [IO.Path]::GetFullPath($ReportPath)
    $reportDirectory = Split-Path -Parent $absoluteReportPath
    if (!(Test-Path -LiteralPath $reportDirectory))
    {
        New-Item -ItemType Directory -Path $reportDirectory -Force | Out-Null
    }

    [pscustomobject]@{
        mode = $Mode
        generatedUtc = [DateTime]::UtcNow.ToString("o")
        findingCount = $findings.Count
        findings = @($findings | ForEach-Object { $_ })
    } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $absoluteReportPath -Encoding UTF8
    Write-Host "Full report: $absoluteReportPath"
}

if ($Mode -eq "Release" -and $findings.Count -gt 0)
{
    Write-Host "Release blocked: all asset provenance findings must be resolved." -ForegroundColor Red
    exit 1
}

exit 0
