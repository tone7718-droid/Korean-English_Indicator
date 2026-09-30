#Requires -Version 5.1
[CmdletBinding()]
param([string]$Configuration = "Release")
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Definition
Set-Location $root
Write-Host "== SDK =="
dotnet --version
if ($LASTEXITCODE -ne 0) { throw "dotnet SDK check failed." }
Write-Host "== Restore =="
dotnet restore "HanEngIndicator.sln"
if ($LASTEXITCODE -ne 0) { throw "Restore failed." }
Write-Host "== Build =="
dotnet build "HanEngIndicator.sln" -c $Configuration --no-restore
if ($LASTEXITCODE -ne 0) { throw "Build failed." }
Write-Host "== Test =="
dotnet test "tests/HanEngIndicator.Tests/HanEngIndicator.Tests.csproj" -c $Configuration --no-build
if ($LASTEXITCODE -ne 0) { throw "Tests failed." }
Write-Host "Build and tests complete."
