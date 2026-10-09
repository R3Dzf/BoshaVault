# Smoke test for the published Native Messaging console executable.
# This synthetic run uses fake metadata only and no live vault.
[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$Exe)
$ErrorActionPreference='Stop'
$processInfo=[Diagnostics.ProcessStartInfo]::new()
$processInfo.FileName=(Resolve-Path -LiteralPath $Exe).Path
$processInfo.Arguments='chrome-extension://aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa/'
$processInfo.UseShellExecute=$false
$processInfo.CreateNoWindow=$true
$processInfo.RedirectStandardInput=$true
$processInfo.RedirectStandardOutput=$true
$processInfo.RedirectStandardError=$true
$process=[Diagnostics.Process]::Start($processInfo)
try {
    $json=[Text.Encoding]::UTF8.GetBytes('{"op":"list","origin":"https://github.com","entryId":""}')
    $size=[BitConverter]::GetBytes([int]$json.Length)
    $process.StandardInput.BaseStream.Write($size,0,4)
    $process.StandardInput.BaseStream.Write($json,0,$json.Length)
    $process.StandardInput.Close()
    $header=New-Object byte[] 4
    function ReadExact([IO.Stream]$stream,[byte[]]$data) {
        $offset=0
        while($offset -lt $data.Length) {
            $count=$stream.Read($data,$offset,$data.Length-$offset)
            if($count -le 0){throw 'Native messaging response truncated.'}
            $offset+=$count
        }
    }
    ReadExact $process.StandardOutput.BaseStream $header
    $length=[BitConverter]::ToInt32($header,0)
    if($length -lt 2 -or $length -gt 65536){throw 'Native host produced an invalid framed response.'}
    $body=New-Object byte[] $length
    ReadExact $process.StandardOutput.BaseStream $body
    $response=[Text.Encoding]::UTF8.GetString($body) | ConvertFrom-Json
    if($response.status -ne 'unavailable'){throw 'Expected locked/unavailable result with no Windows app running.'}
    if(-not $process.WaitForExit(10000)){throw 'Native host did not exit cleanly.'}
    if($process.ExitCode -ne 0){throw 'Native host process exited with an error.'}
    Write-Host 'PASS: Native Messaging host framed request/response roundtrip; no vault available.'
}
finally {
    if(!$process.HasExited){$process.Kill()}
    $process.Dispose()
}
