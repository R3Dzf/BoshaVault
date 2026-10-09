# Run from the unzipped BoshaVault project directory.
# Uses GitHub CLI interactively authorized on YOUR PC. Creates a private repository by default.
[CmdletBinding()]
param(
    [ValidatePattern('^[A-Za-z0-9._-]+$')][string] $RepositoryName = 'BoshaVault',
    [switch] $Public
)
$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot
foreach ($program in @('git','gh')) {
    if (-not (Get-Command $program -ErrorAction SilentlyContinue)) {
        throw "Missing $program. Install Git for Windows and GitHub CLI (winget install Git.Git and winget install GitHub.cli), then run gh auth login."
    }
}
& gh auth status 2>$null | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'GitHub CLI is not logged in. Run gh auth login, then retry.' }
$owner = ((& gh api user --jq '.login') | Out-String).Trim()
if ($LASTEXITCODE -ne 0 -or -not $owner) { throw 'Cannot identify the authenticated GitHub account.' }
$fullName = "$owner/$RepositoryName"
& gh repo view $fullName --json name 1>$null 2>$null
if ($LASTEXITCODE -eq 0) { throw "Repository $fullName already exists. Aborting to prevent overwriting it." }
if (-not (Test-Path -LiteralPath '.git')) {
    & git init -b main
    if ($LASTEXITCODE -ne 0) { throw 'git init failed' }
}
& git add --all
if ($LASTEXITCODE -ne 0) { throw 'git add failed' }
& git diff --cached --check
if ($LASTEXITCODE -ne 0) { throw 'Whitespace/conflict checks failed' }
& git diff --cached --quiet
if ($LASTEXITCODE -ne 0) {
    & git commit -m 'Initial BoshaVault Windows and Android source with GitHub CI'
    if ($LASTEXITCODE -ne 0) { throw 'Git commit failed. Configure git user.name and user.email, then retry.' }
}
$visibility = if ($Public) { '--public' } else { '--private' }
& gh repo create $fullName $visibility --source . --remote origin --push
if ($LASTEXITCODE -ne 0) { throw 'GitHub repository creation or push failed. Inspect the output before retrying.' }
Write-Host "Project published to https://github.com/$fullName" -ForegroundColor Green
Write-Host "Build status and artifacts: https://github.com/$fullName/actions" -ForegroundColor Cyan
Write-Host 'When CI completes, download the Windows and Android artifacts. Read docs/GITHUB-BUILD.md before installing the Android APK.' -ForegroundColor Yellow
