[CmdletBinding()]
param([Parameter(Mandatory)][string]$Version,[string]$Repository='R3Dzf/BoshaVault')
$ErrorActionPreference='Stop'
if ($Version -notmatch '^\d+\.\d+\.\d+$' -or $Repository -notmatch '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$') { throw 'Invalid version/repository.' }
$json=gh api "repos/$Repository/releases?per_page=100"
if ($LASTEXITCODE) { throw 'Cannot read published releases.' }
$releases=($json -join "`n") | ConvertFrom-Json -NoEnumerate
$target=@($releases | Where-Object tag_name -eq "windows-v$Version")[0]
if (!$target -or $target.draft -or $target.prerelease) { throw 'Target must be an existing stable Windows release.' }
$candidates=@(foreach ($release in $releases) {
    if ($release.draft -or $release.prerelease -or $release.tag_name -notmatch '^windows-v(\d+\.\d+\.\d+)$') { continue }
    $candidate=$Matches[1]
    if ([version]$candidate -lt [version]$Version -and @($release.assets | Where-Object name -eq "Lockvyn-$candidate-full.nupkg").Count) { $release }
})
$previous=@($candidates | Sort-Object { [version]$_.tag_name.Substring(9) } -Descending)
if (!$previous.Count) { throw 'No published baseline is available; refusing a synthetic delta.' }
$base=$previous[0];$baseVersion=$base.tag_name.Substring(9)
$root=Join-Path $env:RUNNER_TEMP ('lockvyn-real-delta-'+[guid]::NewGuid().ToString('N'))
$old=Join-Path $root 'base';$feed=Join-Path $root 'feed';New-Item -ItemType Directory -Force $old,$feed | Out-Null
function Download-Verified($release,$name,$directory) {
    $asset=@($release.assets | Where-Object name -eq $name)
    if ($asset.Count -ne 1 -or $asset[0].digest -notmatch '^sha256:([a-fA-F0-9]{64})$') { throw 'Published asset/digest is missing.' }
    $expected=$Matches[1]
    gh release download $release.tag_name --repo $Repository --pattern $name --dir $directory --clobber
    if ($LASTEXITCODE) { throw 'Published asset download failed.' }
    $path=Join-Path $directory $name
    if ((Get-FileHash $path -Algorithm SHA256).Hash -ne $expected -or (Get-Item $path).Length -ne $asset[0].size) { throw 'Published asset integrity failed.' }
    return $path
}
$basePackage=Download-Verified $base "Lockvyn-$baseVersion-full.nupkg" $old
$targetPackage=Download-Verified $target "Lockvyn-$Version-full.nupkg" $feed
$feedPath=Download-Verified $target 'releases.win.json' $feed
$legacyPath=Download-Verified $target 'RELEASES' $feed
dotnet tool restore
if ($LASTEXITCODE) { throw 'Pinned Velopack tool restore failed.' }
# Exercise the actual packaging script against this mixed Android/Windows
# release list. A published current version MUST be detected and rejected before
# packing; the earlier nested-array bug incorrectly took the first-release path.
$guardInput=Join-Path $root 'guard-input';New-Item -ItemType Directory -Force $guardInput | Out-Null
Set-Content (Join-Path $guardInput 'Lockvyn.exe') 'Unused baseline-discovery guard fixture'
Set-Content (Join-Path $guardInput 'Lockvyn.NativeHost.exe') 'Unused baseline-discovery guard fixture'
$detected=$false
try { ./tools/Package-Windows.ps1 -InputDirectory $guardInput -OutputDirectory $feed -Repository $Repository }
catch { if ($_.Exception.Message -notlike 'Increase Windows Version above the latest published version*') { throw };$detected=$true }
if (!$detected) { throw 'Published-baseline discovery regression: packaging did not refuse the existing version.' }
Write-Host 'PASS: actual packaging discovers the Windows baseline among Android/Windows releases and refuses republishing its version.'
$delta=Join-Path $feed "Lockvyn-$Version-delta.nupkg"
dotnet vpk delta generate --base $basePackage --new $targetPackage --output $delta --mode BestSpeed
if ($LASTEXITCODE -or !(Test-Path $delta) -or (Get-Item $delta).Length -ge (Get-Item $targetPackage).Length) { throw 'Actual delta generation failed or did not reduce download size.' }
$index=Get-Content $feedPath -Raw | ConvertFrom-Json
$entries=[Collections.Generic.List[object]]::new()
foreach ($asset in $index.Assets) { if (!($asset.Type -eq 'Delta' -and $asset.Version -eq $Version)) { $entries.Add($asset) } }
$entries.Add([pscustomobject]@{
    PackageId='Lockvyn';Version=$Version;Type='Delta';FileName=[IO.Path]::GetFileName($delta)
    SHA1=(Get-FileHash $delta -Algorithm SHA1).Hash;SHA256=(Get-FileHash $delta -Algorithm SHA256).Hash;Size=(Get-Item $delta).Length
})
$index.Assets=$entries.ToArray();$index | ConvertTo-Json -Depth 20 | Set-Content $feedPath -Encoding utf8
dotnet tools/Lockvyn.UpdateChecks/bin/Release/net10.0/Lockvyn.UpdateChecks.dll --published-release-delta $feed (Join-Path $root 'acceptance') $basePackage
if ($LASTEXITCODE) { throw 'Actual delta download/apply/file-preservation acceptance failed. Nothing was published.' }
$legacy=@(Get-Content $legacyPath | Where-Object { $_ -and $_ -notmatch ([regex]::Escape("Lockvyn-$Version-delta.nupkg")) })
$legacy+=((Get-FileHash $delta -Algorithm SHA1).Hash.ToLowerInvariant()+' '+[IO.Path]::GetFileName($delta)+' '+(Get-Item $delta).Length)
$legacy | Set-Content $legacyPath -Encoding utf8
# Add the patch first; advertise it only after the real SDK accepted it. Existing
# installer, portable ZIP and full package are never replaced.
gh release upload "windows-v$Version" $delta --repo $Repository --clobber
if ($LASTEXITCODE) { throw 'Delta upload failed.' }
gh release upload "windows-v$Version" $feedPath $legacyPath --repo $Repository --clobber
if ($LASTEXITCODE) { throw 'Delta feed upload failed.' }
./tools/Publish-WindowsChecksums.ps1 -Version $Version -Repository $Repository -OutputDirectory $feed
$afterJson=gh api "repos/$Repository/releases/tags/windows-v$Version"
if ($LASTEXITCODE) { throw 'Final release read failed.' }
$after=($afterJson -join "`n") | ConvertFrom-Json
foreach ($name in @("Lockvyn-$Version-full.nupkg",'Lockvyn-win-Setup.exe','Lockvyn-win-Portable.zip')) {
    $before=@($target.assets | Where-Object name -eq $name)[0];$now=@($after.assets | Where-Object name -eq $name)[0]
    if (!$before -or !$now -or $before.digest -ne $now.digest -or $before.size -ne $now.size) { throw 'A published application asset changed unexpectedly.' }
}
Write-Host "PASS: Verified real $baseVersion -> $Version delta; $((Get-Item $delta).Length) bytes versus $((Get-Item $targetPackage).Length) full bytes. Existing application assets preserved."
