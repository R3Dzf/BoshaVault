[CmdletBinding()]
param([Parameter(Mandatory)][string]$Version, [string]$Repository='R3Dzf/BoshaVault', [string]$OutputDirectory='dist/velopack')
$ErrorActionPreference='Stop'
if ($Version -notmatch '^\d+\.\d+\.\d+$' -or $Repository -notmatch '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$') { throw 'Invalid release version or repository.' }
$tag="windows-v$Version"
$releaseJson=gh api "repos/$Repository/releases/tags/$tag"
if ($LASTEXITCODE) { throw 'Cannot read uploaded release assets.' }
$release=($releaseJson -join "`n") | ConvertFrom-Json
if ($release.tag_name -ne $tag -or $release.prerelease) { throw 'Unexpected release tag or prerelease.' }
$assets=@($release.assets | Where-Object name -ne 'SHA256SUMS.txt' | Sort-Object name)
foreach ($required in @('Lockvyn-win-Setup.exe','Lockvyn-win-Portable.zip',"Lockvyn-$Version-full.nupkg",'releases.win.json')) {
    if ($required -notin $assets.name) { throw "Missing published asset: $required" }
}
New-Item -ItemType Directory -Force $OutputDirectory | Out-Null
$lines=@()
foreach ($asset in $assets) {
    if ($asset.name -notmatch '^(Lockvyn-\d+\.\d+\.\d+-(full|delta)\.nupkg|Lockvyn-win-(Setup\.exe|Portable\.zip)|releases\.win\.json|RELEASES)$') { throw 'Unexpected release asset filename.' }
    if ($asset.digest -notmatch '^sha256:([a-fA-F0-9]{64})$') { throw "Missing GitHub digest for $($asset.name)." }
    $expected=$Matches[1].ToLowerInvariant()
    $file=Join-Path $OutputDirectory $asset.name
    $matchesLocal=(Test-Path $file) -and (Get-FileHash $file -Algorithm SHA256).Hash.ToLowerInvariant() -eq $expected
    if (!$matchesLocal) {
        gh release download $tag --repo $Repository --pattern $asset.name --dir $OutputDirectory --clobber
        if ($LASTEXITCODE) { throw "Release asset download failed: $($asset.name)" }
    }
    $actual=(Get-FileHash $file -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -ne $expected -or (Get-Item $file).Length -ne $asset.size) { throw "Published asset integrity failed: $($asset.name)" }
    $lines+="$actual  $($asset.name)"
}
$feed=Get-Content (Join-Path $OutputDirectory 'releases.win.json') -Raw | ConvertFrom-Json
$full=@($feed.Assets | Where-Object { $_.PackageId -eq 'Lockvyn' -and $_.Version -eq $Version -and $_.Type -eq 'Full' })
if ($full.Count -ne 1 -or $full[0].FileName -ne "Lockvyn-$Version-full.nupkg") { throw 'Published feed does not identify the target full package.' }
$package=Join-Path $OutputDirectory $full[0].FileName
if ((Get-FileHash $package -Algorithm SHA256).Hash -ne $full[0].SHA256 -or (Get-Item $package).Length -ne $full[0].Size) { throw 'Published feed checksum or size differs from its package.' }
$sums=Join-Path $OutputDirectory 'SHA256SUMS.txt'
$lines | Set-Content $sums -Encoding utf8
gh release upload $tag $sums --repo $Repository --clobber
if ($LASTEXITCODE) { throw 'Release checksum upload failed.' }
Write-Host "PASS: SHA256SUMS covers all $($assets.Count) actual release assets; package and public feed agree."
