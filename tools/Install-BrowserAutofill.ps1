# Local user-approved Lockvyn Native Messaging setup for Chromium browsers.
# -Browser All only registers the bridge for detected Chrome, Edge, Chromium.
# It never installs extensions, edits browser profiles or creates force-install policies.
[CmdletBinding()]
param(
    [ValidateSet('Chrome','Edge','Chromium','All')][string]$Browser = 'All',
    [ValidatePattern('^(?:[a-p]{32})?$')][string]$ExtensionId = '',
    [switch]$RepairOnly,
    [string]$AppFolder
)
$ErrorActionPreference = 'Stop'
if (-not $RepairOnly -and [string]::IsNullOrWhiteSpace($ExtensionId)) {
    throw 'Enter a 32-letter browser extension ID for first-time setup; -RepairOnly is for existing approvals.'
}
if ([string]::IsNullOrWhiteSpace($AppFolder)) {
    $scriptPath = $PSCommandPath
    if ([string]::IsNullOrWhiteSpace($scriptPath)) { $scriptPath = $MyInvocation.MyCommand.Path }
    if ([string]::IsNullOrWhiteSpace($scriptPath)) { throw 'Cannot locate app; pass -AppFolder.' }
    $AppFolder = Split-Path -Parent $scriptPath
}
$AppFolder = (Resolve-Path -LiteralPath $AppFolder -ErrorAction Stop).ProviderPath
$binary = Join-Path $AppFolder 'Lockvyn.NativeHost.exe'
if (!(Test-Path -LiteralPath $binary -PathType Leaf)) {
    $binary = Join-Path $AppFolder 'BoshaVault.NativeHost.exe'
}
$desktop = Join-Path $AppFolder 'Lockvyn.exe'
if (!(Test-Path -LiteralPath $desktop -PathType Leaf)) {
    $desktop = Join-Path $AppFolder 'BoshaVault.exe'
}
if (!(Test-Path -LiteralPath $binary -PathType Leaf) -or
    !(Test-Path -LiteralPath $desktop -PathType Leaf)) {
    throw 'Install or extract the complete Lockvyn Windows package before browser setup.'
}
if (!(Test-Path -LiteralPath (Join-Path $AppFolder 'browser-extension\manifest.json') -PathType Leaf)) {
    throw 'Missing browser-extension folder. Install the complete Windows package.'
}
$directory = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'BoshaVault\NativeMessaging'
$file = Join-Path $directory 'com.boshavault.desktop.json'
$browsers = @{
    Chrome = @{
        Data = (Join-Path $env:LOCALAPPDATA 'Google\Chrome\User Data')
        Registry = 'HKCU:\Software\Google\Chrome\NativeMessagingHosts'
    }
    Edge = @{
        Data = (Join-Path $env:LOCALAPPDATA 'Microsoft\Edge\User Data')
        Registry = 'HKCU:\Software\Microsoft\Edge\NativeMessagingHosts'
    }
    Chromium = @{
        Data = (Join-Path $env:LOCALAPPDATA 'Chromium\User Data')
        Registry = 'HKCU:\Software\Chromium\NativeMessagingHosts'
    }
}
$approved = @()
if (Test-Path -LiteralPath $file -PathType Leaf) {
    $previous = Get-Content -LiteralPath $file -Raw | ConvertFrom-Json
    if ($previous.name -ne 'com.boshavault.desktop' -or $previous.type -ne 'stdio') {
        throw 'The existing host manifest is not a valid Lockvyn registration.'
    }
    foreach ($origin in @($previous.allowed_origins)) {
        if (-not ($origin -is [string]) -or $origin -notmatch '^chrome-extension://[a-p]{32}/$') {
            throw 'The existing host manifest contains an invalid extension origin.'
        }
        $approved += $origin
    }
}
if ($RepairOnly) {
    $existingRegistration = $false
    foreach ($candidate in $browsers.Keys) {
        $key = Join-Path $browsers[$candidate].Registry 'com.boshavault.desktop'
        if (Test-Path -LiteralPath $key) {
            $value = (Get-Item -LiteralPath $key).GetValue('')
            if ($value -eq $file) { $existingRegistration = $true }
        }
    }
    if (-not $existingRegistration) {
        throw 'No previous user-approved Lockvyn browser host registration exists.'
    }
} else {
    $approved += "chrome-extension://$ExtensionId/"
}
$approved = @($approved | Sort-Object -Unique)
if ($approved.Count -eq 0) { throw 'No previously approved browser extension IDs were found.' }
$targets = if ($Browser -eq 'All') {
    @($browsers.Keys | Where-Object {
        Test-Path -LiteralPath $browsers[$_].Data -PathType Container
    })
} else { @($Browser) }
$manifest = @{
    name = 'com.boshavault.desktop'
    description = 'Lockvyn Windows locally approved password fills'
    path = (Resolve-Path -LiteralPath $binary).ProviderPath
    type = 'stdio'
    allowed_origins = $approved
} | ConvertTo-Json -Depth 5
New-Item -ItemType Directory -Force -Path $directory | Out-Null
[IO.File]::WriteAllText($file,$manifest,(New-Object Text.UTF8Encoding($false)))
$registered = @()
foreach ($browserName in $targets) {
    $key = Join-Path $browsers[$browserName].Registry 'com.boshavault.desktop'
    if ($RepairOnly) {
        if (!(Test-Path -LiteralPath $key)) { continue }
        if ((Get-Item -LiteralPath $key).GetValue('') -ne $file) { continue }
    }
    New-Item -Path $key -Force | Out-Null
    Set-Item -Path $key -Value $file
    $registered += $browserName
}
Write-Host "Lockvyn host: $($registered -join ', ')" -ForegroundColor Green
Write-Host "Approved extension IDs: $($approved.Count)"
Write-Host 'Each browser profile still requires user-approved extension installation once.'
Write-Host 'Future Lockvyn updates repair the approved local host path automatically.'
Write-Host 'Browser profiles, extension install policies and toolbar configuration were not modified.'
