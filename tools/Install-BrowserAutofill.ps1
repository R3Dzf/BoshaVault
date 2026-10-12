# Register the local Lockvyn Native Messaging host for a user-approved extension.
# Browser=All covers detected Chrome, Edge and Chromium without changing their
# extension settings, profile data, install policies or toolbar preferences.
[CmdletBinding()]
param(
    [ValidateSet('Chrome','Edge','Chromium','All')][string]$Browser = 'All',
    [ValidatePattern('^$|^[a-p]{32}
)
$ErrorActionPreference = 'Stop'
if (-not $RepairOnly -and [string]::IsNullOrWhiteSpace($ExtensionId)) {
    throw 'One-time setup requires an ExtensionId from the browser extension page. Use -RepairOnly on subsequent updates.'
}
# PowerShell 5.1 may evaluate a parameter's default `$PSScriptRoot`
# before the script path is populated; resolve it in the script body.
if ([string]::IsNullOrWhiteSpace($AppFolder)) {
    $executingScript = $PSCommandPath
    if ([string]::IsNullOrWhiteSpace($executingScript)) {
        $executingScript = $MyInvocation.MyCommand.Path
    }
    if ([string]::IsNullOrWhiteSpace($executingScript)) {
        throw 'Cannot locate the installer script. Pass -AppFolder with the extracted Lockvyn Windows directory.'
    }
    $AppFolder = Split-Path -Parent $executingScript
}
if ([string]::IsNullOrWhiteSpace($AppFolder)) {
    throw 'AppFolder cannot be empty. Extract the complete Lockvyn-Windows-x64 ZIP.'
}
$AppFolder = (Resolve-Path -LiteralPath $AppFolder -ErrorAction Stop).ProviderPath
$binary = Join-Path $AppFolder 'Lockvyn.NativeHost.exe'
if (!(Test-Path -LiteralPath $binary -PathType Leaf)) { $binary = Join-Path $AppFolder 'BoshaVault.NativeHost.exe' }
$desktop = Join-Path $AppFolder 'Lockvyn.exe'
if (!(Test-Path -LiteralPath $desktop -PathType Leaf)) { $desktop = Join-Path $AppFolder 'BoshaVault.exe' }
if (!(Test-Path -LiteralPath $binary -PathType Leaf) -or !(Test-Path -LiteralPath $desktop -PathType Leaf)) {
    throw 'Extract the complete Lockvyn-Windows-x64 ZIP first, then run this script from that folder.'
}
if (!(Test-Path -LiteralPath (Join-Path $AppFolder 'browser-extension\manifest.json') -PathType Leaf)) {
    throw 'The browser-extension folder is missing; extract the complete ZIP.'
}
$directory = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'BoshaVault\NativeMessaging'
New-Item -ItemType Directory -Force -Path $directory | Out-Null
$file = Join-Path $directory 'com.boshavault.desktop.json'
$allowed = @()
if (Test-Path -LiteralPath $file) {
    $previous = Get-Content -LiteralPath $file -Raw | ConvertFrom-Json
    if ($previous.name -ne 'com.boshavault.desktop' -or $previous.type -ne 'stdio') {
        throw 'Existing Native Messaging manifest does not belong to Lockvyn.'
    }
    $existing = @($previous.allowed_origins)
    if (@($existing | Where-Object { $_ -notmatch '^chrome-extension://[a-p]{32}/
@{
    name='com.boshavault.desktop'
    description='Lockvyn Windows locally approved password fills'
    path=(Resolve-Path -LiteralPath $binary).Path
    type='stdio'
    allowed_origins=$allowed
} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $file -Encoding UTF8
# PowerShell 5.1 Set-Content -Encoding UTF8 emits a BOM. Chromium's JSON
# manifest parser expects UTF-8; write BOM-free UTF-8 explicitly.
$json = Get-Content -LiteralPath $file -Raw
[IO.File]::WriteAllText($file,$json,(New-Object System.Text.UTF8Encoding($false)))
$browsers = @{
    Chrome = @{ Data = (Join-Path $env:LOCALAPPDATA 'Google\Chrome\User Data'); Registry = 'HKCU:\Software\Google\Chrome\NativeMessagingHosts' }
    Edge = @{ Data = (Join-Path $env:LOCALAPPDATA 'Microsoft\Edge\User Data'); Registry = 'HKCU:\Software\Microsoft\Edge\NativeMessagingHosts' }
    Chromium = @{ Data = (Join-Path $env:LOCALAPPDATA 'Chromium\User Data'); Registry = 'HKCU:\Software\Chromium\NativeMessagingHosts' }
}
$targets = if ($Browser -eq 'All') {
    @($browsers.Keys | Where-Object { Test-Path -LiteralPath $browsers[$_].Data -PathType Container })
} else { @($Browser) }
$registered = @()
foreach ($target in $targets) {
    $key = Join-Path $browsers[$target].Registry 'com.boshavault.desktop'
    if ($RepairOnly) {
        # Never create a new browser registration without prior opt-in.
        if (-not (Test-Path -LiteralPath $key)) { continue }
        $existing = (Get-Item -LiteralPath $key).GetValue('')
        if ($existing -ne $file) { continue }
    }
    New-Item -Path $key -Force | Out-Null
    Set-Item -Path $key -Value $file
    $registered += $target
}
Write-Host "Lockvyn native host: $($registered -join ', ')" -ForegroundColor Green
Write-Host "Approved extension IDs: $($allowed.Count)"
Write-Host 'Existing browser profiles share this registration, but each profile still needs the extension installed once.'
Write-Host 'No browser extensions, browser policy, browser profiles or toolbar preferences were changed.'
Write-Host 'Restart the browser after initial setup; subsequent Lockvyn updates repair the host path automatically.'
)][string]$ExtensionId = '',
    [switch]$RepairOnly,
    [string]$AppFolder
)
$ErrorActionPreference = 'Stop'
# PowerShell 5.1 may evaluate a parameter's default `$PSScriptRoot`
# before the script path is populated; resolve it in the script body.
if ([string]::IsNullOrWhiteSpace($AppFolder)) {
    $executingScript = $PSCommandPath
    if ([string]::IsNullOrWhiteSpace($executingScript)) {
        $executingScript = $MyInvocation.MyCommand.Path
    }
    if ([string]::IsNullOrWhiteSpace($executingScript)) {
        throw 'Cannot locate the installer script. Pass -AppFolder with the extracted Lockvyn Windows directory.'
    }
    $AppFolder = Split-Path -Parent $executingScript
}
if ([string]::IsNullOrWhiteSpace($AppFolder)) {
    throw 'AppFolder cannot be empty. Extract the complete Lockvyn-Windows-x64 ZIP.'
}
$AppFolder = (Resolve-Path -LiteralPath $AppFolder -ErrorAction Stop).ProviderPath
$binary = Join-Path $AppFolder 'Lockvyn.NativeHost.exe'
if (!(Test-Path -LiteralPath $binary -PathType Leaf)) { $binary = Join-Path $AppFolder 'BoshaVault.NativeHost.exe' }
$desktop = Join-Path $AppFolder 'Lockvyn.exe'
if (!(Test-Path -LiteralPath $desktop -PathType Leaf)) { $desktop = Join-Path $AppFolder 'BoshaVault.exe' }
if (!(Test-Path -LiteralPath $binary -PathType Leaf) -or !(Test-Path -LiteralPath $desktop -PathType Leaf)) {
    throw 'Extract the complete Lockvyn-Windows-x64 ZIP first, then run this script from that folder.'
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
    description='Lockvyn Windows locally approved password fills'
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
Write-Host "Registered Lockvyn in $Browser." -ForegroundColor Green
Write-Host "Allowed extension: $ExtensionId"
Write-Host "Keep this extracted app folder at its current path. Restart $Browser now."
Write-Host "No administrator permissions needed; your passwords stay encrypted in your local vault."
 }).Count -gt 0) {
        throw 'Invalid existing approved extension origin. Refusing to broaden access.'
    }
    $allowed = $existing
}
if (-not $RepairOnly) { $allowed += "chrome-extension://$ExtensionId/" }
$allowed = @($allowed | Sort-Object -Unique)
if ($RepairOnly) {
    $registrations = @(
        'HKCU:\Software\Google\Chrome\NativeMessagingHosts\com.boshavault.desktop',
        'HKCU:\Software\Microsoft\Edge\NativeMessagingHosts\com.boshavault.desktop',
        'HKCU:\Software\Chromium\NativeMessagingHosts\com.boshavault.desktop'
    )
    if (@($registrations | Where-Object {
        (Test-Path -LiteralPath $_) -and ((Get-Item -LiteralPath $_).GetValue('') -eq $file)
    }).Count -eq 0) {
        throw 'No previously approved browser registration found; nothing to repair.'
    }
}
if ($allowed.Count -eq 0) { throw 'No approved browser extensions yet. Run one-time setup first.' }
if ($RepairOnly -and -not (Test-Path -LiteralPath $file)) {
    throw 'Cannot repair a browser host that was never explicitly configured.'
}
@{
    name='com.boshavault.desktop'
    description='Lockvyn Windows locally approved password fills'
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
Write-Host "Registered Lockvyn in $Browser." -ForegroundColor Green
Write-Host "Allowed extension: $ExtensionId"
Write-Host "Keep this extracted app folder at its current path. Restart $Browser now."
Write-Host "No administrator permissions needed; your passwords stay encrypted in your local vault."
)][string]$ExtensionId = '',
    [switch]$RepairOnly,
    [string]$AppFolder
)
$ErrorActionPreference = 'Stop'
# PowerShell 5.1 may evaluate a parameter's default `$PSScriptRoot`
# before the script path is populated; resolve it in the script body.
if ([string]::IsNullOrWhiteSpace($AppFolder)) {
    $executingScript = $PSCommandPath
    if ([string]::IsNullOrWhiteSpace($executingScript)) {
        $executingScript = $MyInvocation.MyCommand.Path
    }
    if ([string]::IsNullOrWhiteSpace($executingScript)) {
        throw 'Cannot locate the installer script. Pass -AppFolder with the extracted Lockvyn Windows directory.'
    }
    $AppFolder = Split-Path -Parent $executingScript
}
if ([string]::IsNullOrWhiteSpace($AppFolder)) {
    throw 'AppFolder cannot be empty. Extract the complete Lockvyn-Windows-x64 ZIP.'
}
$AppFolder = (Resolve-Path -LiteralPath $AppFolder -ErrorAction Stop).ProviderPath
$binary = Join-Path $AppFolder 'Lockvyn.NativeHost.exe'
if (!(Test-Path -LiteralPath $binary -PathType Leaf)) { $binary = Join-Path $AppFolder 'BoshaVault.NativeHost.exe' }
$desktop = Join-Path $AppFolder 'Lockvyn.exe'
if (!(Test-Path -LiteralPath $desktop -PathType Leaf)) { $desktop = Join-Path $AppFolder 'BoshaVault.exe' }
if (!(Test-Path -LiteralPath $binary -PathType Leaf) -or !(Test-Path -LiteralPath $desktop -PathType Leaf)) {
    throw 'Extract the complete Lockvyn-Windows-x64 ZIP first, then run this script from that folder.'
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
    description='Lockvyn Windows locally approved password fills'
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
Write-Host "Registered Lockvyn in $Browser." -ForegroundColor Green
Write-Host "Allowed extension: $ExtensionId"
Write-Host "Keep this extracted app folder at its current path. Restart $Browser now."
Write-Host "No administrator permissions needed; your passwords stay encrypted in your local vault."
