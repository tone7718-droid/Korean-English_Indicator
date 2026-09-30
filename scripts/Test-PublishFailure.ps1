#Requires -Version 5.1
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $root 'dist/HanEngIndicator.exe'
$before = (Get-FileHash $exe -Algorithm SHA256).Hash
function global:dotnet { $global:LASTEXITCODE = 7 }
try {
    $failed = $false
    try { & (Join-Path $root 'publish-win-x64.ps1') }
    catch { $failed = $true }
    if (-not $failed) { throw 'Publish incorrectly succeeded after dotnet failed.' }
    if ((Get-FileHash $exe -Algorithm SHA256).Hash -ne $before) {
        throw 'Failed publish changed the existing executable.'
    }
    $failed = $false
    try { & (Join-Path $root 'build.ps1') }
    catch { $failed = $true }
    if (-not $failed) { throw 'Build incorrectly succeeded after dotnet failed.' }
}
finally { Remove-Item Function:\dotnet }
Write-Host 'Failure handling preserved the previous release.'
