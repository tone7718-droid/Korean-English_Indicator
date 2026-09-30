#Requires -Version 5.1
[CmdletBinding()]
param([string]$Configuration = "Release")
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Definition
Set-Location $root
$project = Join-Path $root "src/HanEngIndicator/HanEngIndicator.csproj"
$artifacts = Join-Path $root "artifacts"
$staging = Join-Path $artifacts ([Guid]::NewGuid().ToString("N"))
$singleDir = Join-Path $staging "single"
$folderDir = Join-Path $staging "folder"
$packageDir = Join-Path $staging "packages"
$dist = Join-Path $root "dist"
$backup = Join-Path $root ("dist.previous-" + [Guid]::NewGuid().ToString("N"))
$extras = @("README.md", "Install-AutoStartAdmin.cmd", "Uninstall-AutoStartAdmin.cmd", "scripts/Measure-LongSession.ps1")
foreach ($f in $extras) {
    if (-not (Test-Path (Join-Path $root $f))) { throw "Missing release file: $f" }
}
try {
    foreach ($d in @($singleDir, $folderDir, $packageDir)) {
        New-Item -ItemType Directory -Path $d -Force | Out-Null
    }
    dotnet publish $project -c $Configuration -r win-x64 --self-contained true `
        -p:PublishSingleFile=true -p:PublishTrimmed=false `
        -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
        -p:DebugType=embedded -o $singleDir
    if ($LASTEXITCODE -ne 0) { throw "Single-file publish failed ($LASTEXITCODE)." }
    dotnet publish $project -c $Configuration -r win-x64 --self-contained true `
        -p:PublishSingleFile=false -p:IncludeNativeLibrariesForSelfExtract=false `
        -p:EnableCompressionInSingleFile=false -p:DebugType=none -o $folderDir
    if ($LASTEXITCODE -ne 0) { throw "Folder publish failed ($LASTEXITCODE)." }
    foreach ($d in @($singleDir, $folderDir)) {
        if (-not (Test-Path (Join-Path $d "HanEngIndicator.exe"))) { throw "Executable missing in $d" }
        foreach ($f in $extras) {
            $destination = Join-Path $d $f
            New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
            Copy-Item (Join-Path $root $f) $destination -Force
        }
    }
    Copy-Item (Join-Path $singleDir "HanEngIndicator.exe") $packageDir
    Compress-Archive -Path (Join-Path $singleDir "*") -DestinationPath (Join-Path $packageDir "HanEngIndicator-win-x64.zip")
    Compress-Archive -Path (Join-Path $folderDir "*") -DestinationPath (Join-Path $packageDir "HanEngIndicator-win-x64-folder.zip")
    $names = @("HanEngIndicator.exe", "HanEngIndicator-win-x64.zip", "HanEngIndicator-win-x64-folder.zip")
    $lines = foreach ($n in $names) {
        $hash = (Get-FileHash -Algorithm SHA256 -Path (Join-Path $packageDir $n)).Hash.ToLower()
        "$hash  $n"
    }
    $lines | Set-Content -Path (Join-Path $packageDir "SHA256SUMS.txt") -Encoding ascii
    # Preserve the last working release until both builds and packages succeed.
    if (Test-Path $dist) { Move-Item $dist $backup }
    try { Move-Item $packageDir $dist }
    catch {
        if (Test-Path $backup) { Move-Item $backup $dist }
        throw
    }
    if (Test-Path $backup) { Remove-Item $backup -Recurse -Force }
    Write-Host "Publish complete: $dist"
}
finally {
    if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
}
