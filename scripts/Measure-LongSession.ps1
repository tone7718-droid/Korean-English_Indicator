#Requires -Version 5.1
[CmdletBinding()]
param(
    [ValidateRange(1, 1440)][int]$DurationMinutes = 75,
    [ValidateRange(1, 300)][int]$IntervalSeconds = 30,
    [string]$OutputPath = "HanEngIndicator-long-session.csv"
)
$ErrorActionPreference = "Stop"
# Run alongside the real app and chart program. No screenshots or typed text.
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class HanEngResourceProbe {
    [DllImport("user32.dll")] public static extern uint GetGuiResources(IntPtr process, uint flag);
}
'@
$process = @(Get-Process -Name HanEngIndicator -ErrorAction Stop)
if ($process.Count -ne 1) { throw "Start exactly one HanEngIndicator instance first." }
$indicatorPid = $process[0].Id
$stopAt = (Get-Date).AddMinutes($DurationMinutes)
$first = $true
while ((Get-Date) -le $stopAt) {
    $p = Get-Process -Id $indicatorPid -ErrorAction Stop
    $row = [pscustomobject]@{
        Time = (Get-Date).ToString("o")
        PID = $indicatorPid
        CPUSeconds = $p.TotalProcessorTime.TotalSeconds
        PrivateMB = [Math]::Round($p.PrivateMemorySize64 / 1MB, 2)
        WorkingMB = [Math]::Round($p.WorkingSet64 / 1MB, 2)
        Handles = $p.HandleCount
        Threads = $p.Threads.Count
        GDI = [HanEngResourceProbe]::GetGuiResources($p.Handle, 0)
        USER = [HanEngResourceProbe]::GetGuiResources($p.Handle, 1)
    }
    $row | Export-Csv -Path $OutputPath -NoTypeInformation -Encoding UTF8 -Append:(-not $first)
    $first = $false
    Start-Sleep -Seconds $IntervalSeconds
}
Write-Host "Measurements saved: $OutputPath. Compare beginning/end and annotate any observed lag."
