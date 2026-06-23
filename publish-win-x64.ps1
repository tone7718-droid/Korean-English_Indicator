#Requires -Version 5.1
<#
.SYNOPSIS
    Publish HanEngIndicator as a single self-contained Windows x64 EXE,
    package it as a ZIP, and emit a SHA-256 hash for both.
.DESCRIPTION
    Run from the repository root on Windows with the .NET 8 SDK installed:

        powershell -ExecutionPolicy Bypass -File .\publish-win-x64.ps1

    Output goes to .\artifacts:
        artifacts\publish\HanEngIndicator.exe   (the single EXE)
        artifacts\HanEngIndicator-win-x64.zip    (zipped EXE + README)
        artifacts\SHA256SUMS.txt                 (hashes)
#>
[CmdletBinding()]
param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Definition
Set-Location $root

$artifacts   = Join-Path $root "artifacts"
$publishDir  = Join-Path $artifacts "publish"
$project     = Join-Path $root "src/HanEngIndicator/HanEngIndicator.csproj"

if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
New-Item -ItemType Directory -Path $publishDir -Force | Out-Null

Write-Host "== publish (single-file, self-contained, win-x64) ==" -ForegroundColor Cyan
dotnet publish $project `
    -c $Configuration `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:PublishTrimmed=false `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=embedded `
    -o $publishDir

$exe = Join-Path $publishDir "HanEngIndicator.exe"
if (-not (Test-Path $exe)) { throw "Publish failed: $exe not found." }

# Bundle the EXE + README into a ZIP.
$readme = Join-Path $root "README.md"
$zip = Join-Path $artifacts "HanEngIndicator-win-x64.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }

$staging = Join-Path $artifacts "_zip"
if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
New-Item -ItemType Directory -Path $staging -Force | Out-Null
Copy-Item $exe $staging
if (Test-Path $readme) { Copy-Item $readme $staging }
Compress-Archive -Path (Join-Path $staging "*") -DestinationPath $zip -Force
Remove-Item $staging -Recurse -Force

# SHA-256 for EXE and ZIP.
$sums = Join-Path $artifacts "SHA256SUMS.txt"
$lines = @()
foreach ($f in @($exe, $zip)) {
    $h = (Get-FileHash -Algorithm SHA256 -Path $f).Hash.ToLower()
    $name = Split-Path $f -Leaf
    $lines += "$h  $name"
    Write-Host "SHA256  $name = $h" -ForegroundColor Green
}
$lines | Set-Content -Path $sums -Encoding ascii

Write-Host ""
Write-Host "Done." -ForegroundColor Green
Write-Host "  EXE : $exe"
Write-Host "  ZIP : $zip"
Write-Host "  Hash: $sums"
