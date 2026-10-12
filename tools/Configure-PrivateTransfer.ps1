[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$ProgramPath)
$ErrorActionPreference='Stop'
$program=(Resolve-Path -LiteralPath $ProgramPath).Path
if (!(Test-Path -LiteralPath $program -PathType Leaf) -or [IO.Path]::GetFileName($program) -notin 'Lockvyn.exe','BoshaVault.exe') {
    throw 'Choose the installed Lockvyn executable.'
}
# Owner-invoked/UAC-approved only. Never change network profiles, disable the
# firewall, allow Public/Domain traffic or open an unrestricted numeric port.
$parameters=@{
    Name='Lockvyn-Private-Transfer'
    DisplayName='Lockvyn local encrypted transfer (private network only)'
    Description='Only this app, TCP, private networks and local subnet peers. The app still requires its temporary TLS pin and pairing token.'
    Direction='Inbound';Action='Allow';Enabled='True';Profile='Private'
    Protocol='TCP';Program=$program;RemoteAddress='LocalSubnet';EdgeTraversalPolicy='Block'
}
if (Get-NetFirewallRule -Name $parameters.Name -ErrorAction SilentlyContinue) {
    $display=$parameters.DisplayName
    $parameters.Remove('DisplayName')
    Set-NetFirewallRule @parameters -NewDisplayName $display | Out-Null
} else {
    New-NetFirewallRule @parameters | Out-Null
}
