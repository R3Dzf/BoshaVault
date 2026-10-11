[CmdletBinding()]
param([string]$InputDirectory='dist/velopack-input')
$ErrorActionPreference='Stop'
if ($env:GITHUB_ACTIONS -ne 'true') { throw 'Installed update acceptance requires an isolated GitHub Windows runner.' }
$testRoot=Join-Path $env:RUNNER_TEMP 'lockvyn-update-acceptance'
$testInput=Join-Path $testRoot 'input'; $feed=Join-Path $testRoot 'feed'; $packages=Join-Path $testRoot 'downloads'
New-Item -ItemType Directory -Force $testInput,$feed,$packages | Out-Null
Copy-Item "$InputDirectory/*" $testInput -Recurse -Force
$marker=Join-Path $testInput 'synthetic-update-marker.bin'
$bytes=New-Object byte[] (1024*1024); [Security.Cryptography.RandomNumberGenerator]::Fill($bytes); [IO.File]::WriteAllBytes($marker,$bytes)
dotnet vpk pack --packId Lockvyn --packVersion 9.0.0 --packDir $testInput --mainExe Lockvyn.exe --packTitle Lockvyn --icon src/BoshaVault.Windows/Assets/lockvyn.ico --runtime win-x64 --channel win --outputDir $feed
if ($LASTEXITCODE) { throw 'Acceptance baseline packaging failed.' }
$installer=(Get-ChildItem "$feed/*Setup.exe" | Select-Object -First 1).FullName
$baselineInstaller=Join-Path $testRoot 'baseline-setup.exe'; Copy-Item $installer $baselineInstaller
$bytes[500000]=$bytes[500000] -bxor 1; [IO.File]::WriteAllBytes($marker,$bytes)
dotnet vpk pack --packId Lockvyn --packVersion 9.0.1 --packDir $testInput --mainExe Lockvyn.exe --packTitle Lockvyn --icon src/BoshaVault.Windows/Assets/lockvyn.ico --runtime win-x64 --channel win --outputDir $feed
if ($LASTEXITCODE) { throw 'Acceptance target packaging failed.' }
$delta=Get-ChildItem "$feed/*9.0.1*-delta.nupkg"; $full=Get-ChildItem "$feed/*9.0.1*-full.nupkg"
if ($delta.Count -ne 1 -or $full.Count -ne 1 -or $delta.Length -ge $full.Length) { throw 'Expected a smaller delta package.' }
Write-Host "Delta fixture: $($delta.Length) bytes; full release: $($full.Length) bytes"
dotnet restore tools/Lockvyn.UpdateChecks --locked-mode
if ($LASTEXITCODE) { throw 'Update-check dependency restore failed.' }
dotnet build tools/Lockvyn.UpdateChecks -c Release --no-restore
if ($LASTEXITCODE) { throw 'Update-check build failed.' }
$check=(Resolve-Path 'tools/Lockvyn.UpdateChecks/bin/Release/net10.0/Lockvyn.UpdateChecks.dll').Path
dotnet $check $feed $packages
if ($LASTEXITCODE) { throw 'Delta, fallback, integrity, cancellation or version checks failed.' }
# The runner owns this disposable profile. A pre-existing vault/install is never replaced.
$installRoot=Join-Path $env:LOCALAPPDATA 'Lockvyn'
$dataRoot=Join-Path $env:LOCALAPPDATA 'BoshaVault'
$vault=Join-Path $dataRoot 'vault.boshavault'
if ((Test-Path $installRoot) -or (Test-Path $vault)) { throw 'Acceptance runner already contains an installation or vault.' }
New-Item -ItemType Directory -Force $dataRoot | Out-Null
dotnet $check --create-vault $vault
if ($LASTEXITCODE) { throw 'Synthetic vault creation failed.' }
$before=(Get-FileHash $vault -Algorithm SHA256).Hash
$setup=Start-Process -FilePath $baselineInstaller -ArgumentList '--silent' -PassThru
if (!$setup.WaitForExit(90000) -or $setup.ExitCode -ne 0) { throw 'Silent baseline installation failed.' }
if (!(Test-Path "$installRoot/Update.exe") -or !(Test-Path "$installRoot/current/Lockvyn.exe")) { throw 'Installation layout missing.' }
Get-Process Lockvyn -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq "$installRoot\current\Lockvyn.exe" } | Stop-Process -Force
dotnet $check $feed $installRoot --apply-installed
if ($LASTEXITCODE) { throw 'Installed delta update scheduling failed.' }
$deadline=(Get-Date).AddSeconds(60)
do {
    Start-Sleep -Milliseconds 250
    $version=if (Test-Path "$installRoot/current/sq.version") { ([xml](Get-Content "$installRoot/current/sq.version" -Raw)).package.metadata.version } else { '' }
} until ($version -eq '9.0.1' -or (Get-Date) -gt $deadline)
if ($version -ne '9.0.1') { throw 'Installed version did not advance.' }
if ((Get-FileHash "$installRoot/current/synthetic-update-marker.bin").Hash -ne (Get-FileHash $marker).Hash) { throw 'Installed target content mismatch.' }
if ((Get-FileHash $vault -Algorithm SHA256).Hash -ne $before) { throw 'Installer/updater changed encrypted vault bytes.' }
dotnet $check --verify-vault $vault
if ($LASTEXITCODE) { throw 'Updated installation cannot retain the encrypted vault.' }
$manifest=Join-Path $dataRoot 'NativeMessaging/com.boshavault.desktop.json'
if (Test-Path $manifest) {
    $saved=Get-Content $manifest -Raw | ConvertFrom-Json
    if ($saved.path -ne "$installRoot\current\Lockvyn.NativeHost.exe" -or @($saved.allowed_origins).Count -ne 2) { throw 'Native messaging integration did not survive installation/update.' }
}
Write-Host 'PASS: Real silent install and delta apply preserve encrypted vault and approved browser origins.'
Write-Host 'RESULT: Installed Windows update acceptance passed.'
