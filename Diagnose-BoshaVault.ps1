# Read-only diagnostics. Run while Windows shows an active, unexpired transfer QR.
# No pairing token, certificate pin or vault contents are captured.
$ErrorActionPreference = 'Continue'
Write-Host 'BoshaVault pairing diagnostic (read only)' -ForegroundColor Cyan
$bv = Get-Process BoshaVault -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $bv) {
    Write-Host 'BoshaVault.exe is not running. Launch it first.' -ForegroundColor Yellow
    return
}
Write-Host "BoshaVault PID: $($bv.Id)"
Write-Host "Executable: $($bv.Path)"
Write-Host 'Listening TCP ports:' -ForegroundColor Cyan
$listeners = @(Get-NetTCPConnection -State Listen -OwningProcess $bv.Id -ErrorAction SilentlyContinue | Where-Object { $_.LocalAddress -ne '::' })
if (-not $listeners) {
    Write-Host 'No listening port. Unlock the vault, open Devices & backup and Start a private transfer.' -ForegroundColor Yellow
} else {
    $listeners | Select-Object LocalAddress, LocalPort, State, OwningProcess | Format-Table -AutoSize
    Write-Host 'The script does not connect to the listener, so it cannot consume a pairing attempt.'
}
Write-Host 'Up private IPv4 interfaces:' -ForegroundColor Cyan
Get-NetIPAddress -AddressFamily IPv4 -ErrorAction SilentlyContinue |
    Where-Object { $_.IPAddress -match '^(10\.|192\.168\.|172\.(1[6-9]|2[0-9]|3[01])\.)' -and $_.AddressState -eq 'Preferred' } |
    Select-Object InterfaceAlias, IPAddress, PrefixLength | Format-Table -AutoSize
Write-Host 'Active firewall profiles:' -ForegroundColor Cyan
Get-NetFirewallProfile -ErrorAction SilentlyContinue | Select-Object Name, Enabled, DefaultInboundAction | Format-Table -AutoSize
Write-Host 'Firewall app rules for the running executable:' -ForegroundColor Cyan
$exe = $bv.Path
try {
    Get-NetFirewallApplicationFilter -ErrorAction Stop |
        Where-Object { $_.Program -ieq $exe } |
        ForEach-Object { $_ | Get-NetFirewallRule } |
        Select-Object DisplayName, Enabled, Direction, Action, Profile | Format-Table -AutoSize
} catch { Write-Host 'Could not inspect app-specific firewall rules.' }
$log = Join-Path $env:LOCALAPPDATA 'BoshaVault\transfer.log'
Write-Host 'Most recent transfer stages (no secret pairing codes):' -ForegroundColor Cyan
if (Test-Path $log) { Get-Content $log -Tail 25 } else { Write-Host 'No transfer.log yet; it is created by the new Windows build.' }
Write-Host 'Done. Do not share a screenshot showing the QR or a BV1 pairing code.' -ForegroundColor Green
