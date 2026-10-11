[CmdletBinding()]
param([string]$OutputDirectory='dist/velopack-input')
$ErrorActionPreference='Stop'
dotnet publish src/BoshaVault.Windows -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -p:DebugType=None -p:DebugSymbols=false --output $OutputDirectory
if ($LASTEXITCODE) { throw 'Windows multi-file publish failed.' }
$hostDirectory='dist/velopack-host'
dotnet publish src/BoshaVault.NativeHost -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -p:DebugType=None -p:DebugSymbols=false --output $hostDirectory
if ($LASTEXITCODE) { throw 'Native host multi-file publish failed.' }
Get-ChildItem $hostDirectory -File | ForEach-Object {
    $target=Join-Path $OutputDirectory $_.Name
    if ((Test-Path $target) -and (Get-FileHash $target).Hash -ne (Get-FileHash $_.FullName).Hash) { throw "Conflicting bundled runtime file: $($_.Name)" }
    Copy-Item $_.FullName $target -Force
}
Copy-Item browser-extension "$OutputDirectory/browser-extension" -Recurse -Force
Copy-Item src/BoshaVault.Core/Assets/public_suffix_list.dat "$OutputDirectory/public_suffix_list.dat" -Force
Copy-Item tools/Install-BrowserAutofill.ps1 "$OutputDirectory/Install-BrowserAutofill.ps1" -Force
Copy-Item START-HERE.html "$OutputDirectory/START-HERE.html" -Force
