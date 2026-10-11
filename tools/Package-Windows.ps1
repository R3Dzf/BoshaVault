[CmdletBinding()]
param([string]$InputDirectory='dist/velopack-input', [string]$OutputDirectory='dist/velopack', [string]$Repository='R3Dzf/BoshaVault', [switch]$SkipPrevious)
$ErrorActionPreference='Stop'
if ($Repository -notmatch '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$') { throw 'Invalid GitHub repository.' }
$version=([xml](Get-Content src/BoshaVault.Windows/BoshaVault.Windows.csproj -Raw)).Project.PropertyGroup.Version
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Windows release version must use three numeric components.' }
New-Item -ItemType Directory -Force $OutputDirectory | Out-Null
if (!(Test-Path "$InputDirectory/Lockvyn.exe") -or !(Test-Path "$InputDirectory/Lockvyn.NativeHost.exe")) { throw 'Windows package input is incomplete.' }
dotnet tool restore
if ($LASTEXITCODE) { throw 'Velopack CLI restore failed.' }
if (!$SkipPrevious) {
    $headers=@{Accept='application/vnd.github+json'; 'User-Agent'='Lockvyn-Packaging'}
    if ($env:VPK_TOKEN) { $headers.Authorization="Bearer $env:VPK_TOKEN" }
    # Fail on network/auth errors; only a genuinely absent previous Velopack release
    # is a first release. Never silently discard the delta baseline after a failure.
    $releases=@(Invoke-RestMethod -Uri "https://api.github.com/repos/$Repository/releases?per_page=100" -Headers $headers)
    $previous=@($releases | Where-Object { !$_.draft -and !$_.prerelease -and @($_.assets | Where-Object name -eq 'releases.win.json').Count -gt 0 })
    if ($previous.Count) {
        dotnet vpk download github --repoUrl "https://github.com/$Repository" --channel win --outputDir $OutputDirectory --timeout 5
        if ($LASTEXITCODE) { throw 'Previous Windows release download failed.' }
        $feed=Get-Content "$OutputDirectory/releases.win.json" -Raw | ConvertFrom-Json
        $base=@($feed.Assets | Where-Object { $_.Type -eq 'Full' -and $_.PackageId -eq 'Lockvyn' } | Sort-Object { [version]$_.Version } -Descending)[0]
        if (!$base -or [version]$base.Version -ge [version]$version) { throw 'Increase Windows Version above the latest published version before releasing.' }
        if (!(Test-Path (Join-Path $OutputDirectory $base.FileName))) { throw 'Downloaded base package is missing.' }
    } else { Write-Host 'First Velopack release: no published Windows baseline exists.' }
}
dotnet vpk pack --packId Lockvyn --packVersion $version --packDir $InputDirectory --mainExe Lockvyn.exe --packTitle Lockvyn --packAuthors 'Ahmed Bosha' --icon src/BoshaVault.Windows/Assets/lockvyn.ico --runtime win-x64 --channel win --outputDir $OutputDirectory
if ($LASTEXITCODE) { throw 'Windows package creation failed.' }
$feed=Get-Content "$OutputDirectory/releases.win.json" -Raw | ConvertFrom-Json
$target=@($feed.Assets | Where-Object { $_.PackageId -eq 'Lockvyn' -and $_.Version -eq $version -and $_.Type -eq 'Full' })
if ($target.Count -ne 1 -or !(Get-ChildItem "$OutputDirectory/*Setup.exe")) { throw 'Windows installer or full release is missing.' }
Get-ChildItem $OutputDirectory -File | Where-Object { $_.Extension -in '.exe','.zip','.nupkg','.json' -and $_.Name -ne 'assets.win.json' } | Sort-Object Name | ForEach-Object {
    $hash=(Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $($_.Name)"
} | Set-Content "$OutputDirectory/SHA256SUMS.txt" -Encoding utf8
