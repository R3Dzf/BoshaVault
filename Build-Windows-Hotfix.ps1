# Produces a self-contained Windows x64 build from this source. Does not touch vault data.
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
if (Get-Process BoshaVault -ErrorAction SilentlyContinue) {
    throw 'Exit the old BoshaVault from its tray icon first. Keep an encrypted backup before replacing the EXE.'
}
$sdks = @(dotnet --list-sdks 2>$null)
if (-not ($sdks | Where-Object { $_ -match '^10\.' })) {
    throw 'The .NET 10 SDK is required. Install it and rerun this script (dotnet --list-sdks).'
}
Write-Host 'Running core and encryption tests...' -ForegroundColor Cyan
& dotnet run --project src/BoshaVault.Tests -c Release
if ($LASTEXITCODE -ne 0) { throw 'Tests failed. No release was produced.' }
Write-Host 'Testing real local TLS handshake with the Windows certificate API...' -ForegroundColor Cyan
& dotnet run --project src/BoshaVault.Tests -c Release -- --tls-handshake-tests
if ($LASTEXITCODE -ne 0) { throw 'TLS/Schannel handshake failed. Read the error before building.' }
$dist = Join-Path $PSScriptRoot 'dist\windows-pairing-fix'
Write-Host 'Publishing self-contained Windows x64 build...' -ForegroundColor Cyan
& dotnet publish src/BoshaVault.Windows -c Release -r win-x64 --self-contained true '-p:PublishSingleFile=true' '-p:IncludeNativeLibrariesForSelfExtract=true' '-p:DebugType=None' '-p:DebugSymbols=false' -o $dist
if ($LASTEXITCODE -ne 0) { throw 'Windows build failed.' }
$zip = Join-Path $PSScriptRoot 'BoshaVault-Windows-PairingFix.zip'
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $dist '*') -DestinationPath $zip
Write-Host "Built: $zip" -ForegroundColor Green
Write-Host 'Back up your encrypted vault, Exit via tray, replace the OLD program folder files, then reopen BoshaVault.'
Write-Host 'The user vault stays under %LOCALAPPDATA%\BoshaVault. Do not delete it.'
