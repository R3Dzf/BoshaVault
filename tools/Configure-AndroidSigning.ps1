# Configure stable Android signing for BoshaVault
# Run on a trusted Windows machine, NOT on a shared or malware-infected PC.
# This script never uploads the signing file to the repository.
[CmdletBinding()]
param(
    [string]$KeystorePath = (Join-Path $env:USERPROFILE 'BoshaVault-Signing\BoshaVault-release.p12'),
    [string]$Alias = 'boshavault-release',
    [switch]$UseExisting
)
$ErrorActionPreference = 'Stop'
$repo = 'R3Dzf/BoshaVault'
if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
    throw 'GitHub CLI (gh) is required. Install GitHub CLI and run gh auth login.'
}
if (-not (Get-Command keytool -ErrorAction SilentlyContinue)) {
    throw 'Java JDK keytool is required. Install a current supported JDK first.'
}
& gh auth status | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Sign in using gh auth login.' }
& gh repo view $repo --json name --jq .name | Out-Null
if ($LASTEXITCODE -ne 0) { throw "Cannot access $repo." }
if (-not $KeystorePath.ToLowerInvariant().EndsWith('.p12')) {
    throw 'Use a PKCS#12 file ending in .p12.'
}
if (Test-Path -LiteralPath $KeystorePath) {
    if (-not $UseExisting) {
        throw "The signing file already exists at $KeystorePath. Never overwrite it. Rerun with -UseExisting to reuse it."
    }
} else {
    if ($UseExisting) { throw "Existing signing file not found: $KeystorePath" }
    New-Item -ItemType Directory -Path (Split-Path -Parent $KeystorePath) -Force | Out-Null
    Write-Host "Creating long-term signing certificate: $KeystorePath"
    Write-Host 'Choose a strong password at the keytool prompts; store it offline.'
    & keytool -genkeypair -alias $Alias -keyalg RSA -keysize 4096 -validity 10000 -storetype PKCS12 -keystore $KeystorePath -dname 'CN=BoshaVault Android Release, O=BoshaVault, C=EG'
    if ($LASTEXITCODE -ne 0) { throw 'keytool failed to create signing key.' }
}
# Restrict the local file permissions to the Windows account.
try {
    $acl = Get-Acl -LiteralPath $KeystorePath
    $acl.SetAccessRuleProtection($true, $false)
    $who = [Security.Principal.WindowsIdentity]::GetCurrent().Name
    $rule = New-Object Security.AccessControl.FileSystemAccessRule($who, 'FullControl', 'Allow')
    $acl.AddAccessRule($rule)
    Set-Acl -LiteralPath $KeystorePath -AclObject $acl
} catch {
    Write-Warning 'Could not narrow NTFS file permissions. Protect this key manually.'
}
# Use gh stdin (not command-line arguments) so secrets are not visible in process lists.
function Set-RepoSecret([string]$Name,[string]$Value) {
    $ghPath = (Get-Command gh).Source
    $start = New-Object Diagnostics.ProcessStartInfo
    $start.FileName = $ghPath
    $start.Arguments = "secret set $Name --repo $repo"
    $start.UseShellExecute = $false
    $start.RedirectStandardInput = $true
    $start.RedirectStandardError = $true
    $start.RedirectStandardOutput = $true
    $process = [Diagnostics.Process]::Start($start)
    try {
        $process.StandardInput.Write($Value)
        $process.StandardInput.Close()
        $process.WaitForExit()
        if ($process.ExitCode -ne 0) {
            throw ("Failed to configure a GitHub secret: " + $Name)
        }
    } finally {
        $process.Dispose()
    }
}
$secure = Read-Host 'Enter the signing keystore password again (never share it in chat)' -AsSecureString
$ptr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
try {
    $password = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($ptr)
    try {
        $b64 = [Convert]::ToBase64String([IO.File]::ReadAllBytes($KeystorePath))
        Set-RepoSecret 'BOSHAVAULT_ANDROID_KEYSTORE_BASE64' $b64
        Set-RepoSecret 'BOSHAVAULT_ANDROID_KEYSTORE_PASSWORD' $password
        Set-RepoSecret 'BOSHAVAULT_ANDROID_KEY_ALIAS' $Alias
    } finally {
        $password = $null
        $b64 = $null
    }
} finally {
    [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($ptr)
    $secure.Dispose()
}
Write-Host 'Configured 3 Actions signing secrets (values were not printed).'
Write-Host 'Back up the .p12 and password OFFLINE. If lost, future APK updates cannot use this identity.'
Write-Host 'Existing installations signed by a DIFFERENT key cannot be updated in place.'
Write-Host "To build: gh workflow run build.yml -R $repo --ref main -f signed_release=true"
