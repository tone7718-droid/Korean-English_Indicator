#Requires -Version 5.1
<#
.SYNOPSIS
    Publish HanEngIndicator for Windows x64 in BOTH forms, package them as ZIPs,
    copy the standalone EXE, and emit SHA-256 hashes.
.DESCRIPTION
    Run from the repository root on Windows with the .NET 8 SDK installed:

        powershell -ExecutionPolicy Bypass -File .\publish-win-x64.ps1

    Produces (under .\dist):
        HanEngIndicator.exe                    single self-contained EXE
        HanEngIndicator-win-x64.zip            single EXE + README + autostart scripts
        HanEngIndicator-win-x64-folder.zip     folder build (no self-extract; AV-friendly)
                                               + README + autostart scripts
        SHA256SUMS.txt                         hashes for all three artifacts

    The folder build does not self-extract at runtime, which some antivirus
    products (e.g. AhnLab V3) are less likely to flag than the single-file EXE.
#>
[CmdletBinding()]
param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$root       = Split-Path -Parent $MyInvocation.MyCommand.Definition
Set-Location $root

$project    = Join-Path $root "src/HanEngIndicator/HanEngIndicator.csproj"
$artifacts  = Join-Path $root "artifacts"
$singleDir  = Join-Path $artifacts "publish"
$folderDir  = Join-Path $artifacts "folder"
$dist       = Join-Path $root "dist"

$extras = @(
    (Join-Path $root "README.md"),
    (Join-Path $root "Install-AutoStartAdmin.cmd"),
    (Join-Path $root "Uninstall-AutoStartAdmin.cmd")
)

# These are required parts of the release; fail fast if any is missing rather
# than silently shipping an incomplete package.
foreach ($f in $extras) {
    if (-not (Test-Path $f)) { throw "Required release file is missing: $f" }
}

foreach ($d in @($singleDir, $folderDir, $dist)) {
    if (Test-Path $d) { Remove-Item $d -Recurse -Force }
    New-Item -ItemType Directory -Path $d -Force | Out-Null
}

Write-Host "== publish: single-file self-contained EXE ==" -ForegroundColor Cyan
dotnet publish $project -c $Configuration -r win-x64 --self-contained true `
    -p:PublishSingleFile=true `
    -p:PublishTrimmed=false `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=embedded `
    -o $singleDir

Write-Host "== publish: folder build (no self-extract) ==" -ForegroundColor Cyan
dotnet publish $project -c $Configuration -r win-x64 --self-contained true `
    -p:PublishSingleFile=false `
    -p:IncludeNativeLibrariesForSelfExtract=false `
    -p:EnableCompressionInSingleFile=false `
    -p:DebugType=none `
    -o $folderDir

$exe = Join-Path $singleDir "HanEngIndicator.exe"
if (-not (Test-Path $exe)) { throw "Publish failed: $exe not found." }

# Copy the shared extras into the folder build.
foreach ($f in $extras) { if (Test-Path $f) { Copy-Item $f $folderDir -Force } }

# --- dist artifacts ---
Copy-Item $exe $dist -Force

# Single-file ZIP = EXE + extras.
$singleStage = Join-Path $artifacts "_single"
New-Item -ItemType Directory -Path $singleStage -Force | Out-Null
Copy-Item $exe $singleStage -Force
foreach ($f in $extras) { if (Test-Path $f) { Copy-Item $f $singleStage -Force } }
Compress-Archive -Path (Join-Path $singleStage "*") `
    -DestinationPath (Join-Path $dist "HanEngIndicator-win-x64.zip") -Force
Remove-Item $singleStage -Recurse -Force

# Folder ZIP = whole folder build (extras already inside).
Compress-Archive -Path (Join-Path $folderDir "*") `
    -DestinationPath (Join-Path $dist "HanEngIndicator-win-x64-folder.zip") -Force

# --- SHA-256 ---
$sums = Join-Path $dist "SHA256SUMS.txt"
$names = @("HanEngIndicator.exe", "HanEngIndicator-win-x64.zip", "HanEngIndicator-win-x64-folder.zip")
$lines = foreach ($n in $names) {
    $h = (Get-FileHash -Algorithm SHA256 -Path (Join-Path $dist $n)).Hash.ToLower()
    Write-Host "SHA256  $n = $h" -ForegroundColor Green
    "$h  $n"
}
$lines | Set-Content -Path $sums -Encoding ascii

Write-Host ""
Write-Host "Done. Artifacts in $dist" -ForegroundColor Green
