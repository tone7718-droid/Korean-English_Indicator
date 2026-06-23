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

Write-Host "== dotnet --version ==" -ForegroundColor Cyan
dotnet --version

Write-Host "== restore ==" -ForegroundColor Cyan
dotnet restore "HanEngIndicator.sln"

Write-Host "== build ($Configuration) ==" -ForegroundColor Cyan
dotnet build "HanEngIndicator.sln" -c $Configuration --no-restore

Write-Host "== test ==" -ForegroundColor Cyan
dotnet test "tests/HanEngIndicator.Tests/HanEngIndicator.Tests.csproj" -c $Configuration

Write-Host "Build + tests complete." -ForegroundColor Green
