# Install the BoshaVault native-messaging host for an unpacked browser extension.
# Run with the extension ID shown by chrome://extensions or edge://extensions.
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)]
    [ValidateSet('Chrome','Edge')][string]$Browser,
    [Parameter(Mandatory=$true)]
    [ValidatePattern('^[a-p]{32}$')][string]$ExtensionId,
    [string]$AppFolder = $PSScriptRoot
)
$ErrorActionPreference = 'Stop'
$binary = Join-Path $AppFolder 'BoshaVault.NativeHost.exe'
$desktop = Join-Path $AppFolder 'BoshaVault.exe'
if (!(Test-Path -LiteralPath $binary -PathType Leaf) -or !(Test-Path -LiteralPath $desktop -PathType Leaf)) {
    throw 'Extract the complete BoshaVault-Windows-x64 ZIP first, then run this script from that folder.'
}
if (!(Test-Path -LiteralPath (Join-Path $AppFolder 'browser-extension\manifest.json') -PathType Leaf)) {
    throw 'The browser-extension folder is missing; extract the complete ZIP.'
}
$directory = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'BoshaVault\NativeMessaging'
New-Item -ItemType Directory -Force -Path $directory | Out-Null
$file = Join-Path $directory 'com.boshavault.desktop.json'
$origin = "chrome-extension://$ExtensionId/"
$allowed = @()
if (Test-Path -LiteralPath $file) {
    $previous = Get-Content -LiteralPath $file -Raw | ConvertFrom-Json
    if ($previous.name -eq 'com.boshavault.desktop') {
        $allowed = @($previous.allowed_origins | Where-Object { $_ -match '^chrome-extension://[a-p]{32}/$' })
    }
}
$allowed = @($allowed + $origin | Sort-Object -Unique)
@{
    name='com.boshavault.desktop'
    description='BoshaVault Windows locally approved password fills'
    path=(Resolve-Path -LiteralPath $binary).Path
    type='stdio'
    allowed_origins=$allowed
} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $file -Encoding UTF8
# PowerShell 5.1 Set-Content -Encoding UTF8 emits a BOM. Chromium's JSON
# manifest parser expects UTF-8; write BOM-free UTF-8 explicitly.
$json = Get-Content -LiteralPath $file -Raw
[IO.File]::WriteAllText($file,$json,(New-Object System.Text.UTF8Encoding($false)))
$base = if ($Browser -eq 'Chrome') {
    'HKCU:\Software\Google\Chrome\NativeMessagingHosts'
} else {
    'HKCU:\Software\Microsoft\Edge\NativeMessagingHosts'
}
$key = Join-Path $base 'com.boshavault.desktop'
New-Item -Path $key -Force | Out-Null
Set-Item -Path $key -Value $file
Write-Host "Registered BoshaVault in $Browser." -ForegroundColor Green
Write-Host "Allowed extension: $ExtensionId"
Write-Host "Keep this extracted app folder at its current path. Restart $Browser now."
Write-Host "No administrator permissions needed; your passwords stay encrypted in your local vault."
