# Read-only per-profile browser integration report.
# Does not access browser passwords, history, cookies or URLs.
[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
$hostFile=Join-Path $env:LOCALAPPDATA 'BoshaVault\NativeMessaging\com.boshavault.desktop.json'
$approved=@()
if(Test-Path -LiteralPath $hostFile -PathType Leaf){
    try{
        $hostManifest=Get-Content -LiteralPath $hostFile -Raw | ConvertFrom-Json
        if($hostManifest.name -eq 'com.boshavault.desktop'){
            $approved=@($hostManifest.allowed_origins | Where-Object { $_ -match '^chrome-extension://[a-p]{32}/$' } | ForEach-Object {
                $_.Substring('chrome-extension://'.Length,32)
            })
        }
    }catch {Write-Warning 'Local native host manifest is invalid. Use the Lockvyn setup script.'}
}
$rootList=@(
    @{Name='Chrome';Path=(Join-Path $env:LOCALAPPDATA 'Google\Chrome\User Data')}
    @{Name='Edge';Path=(Join-Path $env:LOCALAPPDATA 'Microsoft\Edge\User Data')}
    @{Name='Brave';Path=(Join-Path $env:LOCALAPPDATA 'BraveSoftware\Brave-Browser\User Data')}
    @{Name='Vivaldi';Path=(Join-Path $env:LOCALAPPDATA 'Vivaldi\User Data')}
    @{Name='Chromium';Path=(Join-Path $env:LOCALAPPDATA 'Chromium\User Data')}
    @{Name='Opera';Path=(Join-Path $env:APPDATA 'Opera Software\Opera Stable')}
)
foreach($browser in $rootList){
    if(!(Test-Path -LiteralPath $browser.Path -PathType Container)){continue}
    $profiles=if($browser.Name -eq 'Opera'){@(Get-Item -LiteralPath $browser.Path)}
              else{@(Get-ChildItem -LiteralPath $browser.Path -Directory -ErrorAction SilentlyContinue |
                  Where-Object { $_.Name -eq 'Default' -or $_.Name -match '^Profile [0-9]+$' })}
    foreach($profile in $profiles){
        $found=@()
        $extensionDir=Join-Path $profile.FullName 'Extensions'
        if(Test-Path -LiteralPath $extensionDir -PathType Container){
            $found=@(Get-ChildItem -LiteralPath $extensionDir -Directory -ErrorAction SilentlyContinue |
                Where-Object {$approved -contains $_.Name} | ForEach-Object {$_.Name})
        }
        $preferencesFile=Join-Path $profile.FullName 'Preferences'
        if($approved.Count -gt 0 -and (Test-Path -LiteralPath $preferencesFile -PathType Leaf)){
            try{
                $settings=(Get-Content -LiteralPath $preferencesFile -Raw | ConvertFrom-Json).extensions.settings
                foreach($extensionId in $approved){
                    if($settings.PSObject.Properties.Name -contains $extensionId){$found+= $extensionId}
                }
            }catch{ } # Browsers may lock or change profile JSON while running.
        }
        [pscustomobject]@{
            Browser=$browser.Name
            Profile=$profile.Name
            LockvynExtension=if(@($found | Select-Object -Unique).Count){'Detected'}else{'Not detected'}
            NativeHostApproved=($approved.Count -gt 0)
        }
    }
}
Write-Host 'Report is read-only. Missing profiles need a one-time user-approved extension install.' -ForegroundColor DarkGray
