#Requires -Version 5.1
<#
.SYNOPSIS
    Restore, build and test HanEngIndicator.
.DESCRIPTION
    Run from the repository root on Windows (or Linux) with the .NET 8 SDK
    installed:

        powershell -ExecutionPolicy Bypass -File .\build.ps1

    This does NOT produce the single-file EXE; use publish-win-x64.ps1 for that.
#>
[CmdletBinding()]
param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Definition
Set-Location $root

Write-Host "== dotnet --version
if ($LASTEXITCODE -ne 0) { throw "dotnet SDK check failed." } ==" -ForegroundColor Cyan
dotnet --version
if ($LASTEXITCODE -ne 0) { throw "dotnet SDK check failed." }

Write-Host "== restore ==" -ForegroundColor Cyan
dotnet restore "HanEngIndicator.sln"
if ($LASTEXITCODE -ne 0) { throw "Restore failed." }

Write-Host "== build ($Configuration) ==" -ForegroundColor Cyan
dotnet build "HanEngIndicator.sln" -c $Configuration --no-restore
if ($LASTEXITCODE -ne 0) { throw "Build failed." }

Write-Host "== test ==" -ForegroundColor Cyan
dotnet test "tests/HanEngIndicator.Tests/HanEngIndicator.Tests.csproj" -c $Configuration --no-build
if ($LASTEXITCODE -ne 0) { throw "Tests failed." }

Write-Host "Build + tests complete." -ForegroundColor Green

