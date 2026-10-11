[CmdletBinding()]
param([string]$OutputDirectory='dist/velopack-input')
$ErrorActionPreference='Stop'
dotnet publish src/BoshaVault.Windows -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -p:DebugType=None -p:DebugSymbols=false --output $OutputDirectory
if ($LASTEXITCODE) { throw 'Windows multi-file publish failed.' }
$hostDirectory='dist/velopack-host'
# The console host keeps its own bundled runtime. WPF and console framework
# assemblies are not interchangeable even when their names/versions match.
dotnet publish src/BoshaVault.NativeHost -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false --output $hostDirectory
if ($LASTEXITCODE) { throw 'Native host publish failed.' }
Copy-Item "$hostDirectory/Lockvyn.NativeHost.exe" "$OutputDirectory/Lockvyn.NativeHost.exe" -Force
Copy-Item browser-extension "$OutputDirectory/browser-extension" -Recurse -Force
Copy-Item src/BoshaVault.Core/Assets/public_suffix_list.dat "$OutputDirectory/public_suffix_list.dat" -Force
Copy-Item tools/Install-BrowserAutofill.ps1 "$OutputDirectory/Install-BrowserAutofill.ps1" -Force
Copy-Item START-HERE.html "$OutputDirectory/START-HERE.html" -Force
