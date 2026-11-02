<#
.SYNOPSIS
    Collect System.Runtime metrics from CrossGOverlay while Settings is hidden and no game runs.
.NOTES
    Requires dotnet-counters in benchmarks/tools. Never attaches to a game process.
#>
param(
    [ValidateRange(10, 600)][int]$DurationSeconds = 45,
    [string]$OutputPrefix = ''
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$exe = Join-Path $root 'src\CrosshairOverlay\bin\x64\Release\net8.0-windows\win-x64\CrossGOverlay.exe'
$tool = Join-Path $PSScriptRoot 'tools\dotnet-counters.exe'
$dataRoot = Join-Path $PSScriptRoot 'results\app-data'
if (-not (Test-Path -LiteralPath $tool -PathType Leaf)) { throw 'Install dotnet-counters into benchmarks/tools first.' }
if (Get-Process CrossGOverlay -ErrorAction SilentlyContinue) { throw 'Close the existing CrossGOverlay instance first.' }
if (Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.ProcessName -match '^DeltaForce' }) {
    throw 'This script is for app-only diagnostics and will not run while Delta Force is detected.'
}
New-Item -ItemType Directory -Path $dataRoot -Force | Out-Null
$env:CROSSGOVERLAY_BENCHMARK_ROOT = $dataRoot
if (-not $OutputPrefix) {
    $OutputPrefix = Join-Path $PSScriptRoot ('results\managed-gc-' + [DateTimeOffset]::UtcNow.ToString('yyyyMMddTHHmmssZ'))
}

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class BenchmarkWindow {
    [DllImport("user32.dll", SetLastError=true)]
    public static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
}
'@

$logPath = Join-Path $dataRoot "logs\crosshair-$(Get-Date -Format yyyyMMdd).log"
$originalLength = if (Test-Path -LiteralPath $logPath) { (Get-Item -LiteralPath $logPath).Length } else { 0L }
$process = $null
try {
    $process = Start-Process -FilePath $exe -PassThru
    $startupTimer = [Diagnostics.Stopwatch]::StartNew()
    $started = $false
    while ($startupTimer.Elapsed.TotalSeconds -lt 30) {
        if ($process.HasExited) { throw 'Application exited during startup.' }
        if (Test-Path -LiteralPath $logPath) {
            $stream = [IO.File]::Open($logPath, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite)
            try {
                if ($stream.Length -gt $originalLength) {
                    $stream.Seek($originalLength, [IO.SeekOrigin]::Begin) | Out-Null
                    $reader = [IO.StreamReader]::new($stream, [Text.Encoding]::UTF8, $true, 4096, $true)
                    try { $newText = $reader.ReadToEnd() } finally { $reader.Dispose() }
                    if ($newText -match '\[FTL\] CrosshairOverlay\.App:') { throw 'Application startup failed.' }
                    if ($newText -match 'CrosshairOverlay\.App:.*?runtime \+ .*?App\.xaml') {
                        $started = $true
                        break
                    }
                }
            } finally { $stream.Dispose() }
        }
        Start-Sleep -Milliseconds 50
    }
    if (-not $started) { throw 'Startup completion was not logged.' }
    $process.Refresh()
    $settingsHwnd = $process.MainWindowHandle
    if ($settingsHwnd -eq [IntPtr]::Zero) { throw 'Settings HWND was not found.' }
    if (-not [BenchmarkWindow]::PostMessage($settingsHwnd, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero)) {
        throw 'Could not hide Settings.'
    }
    Start-Sleep -Seconds 5
    if ($process.HasExited) { throw 'Application exited before collection.' }
    $duration = ([TimeSpan]::FromSeconds($DurationSeconds)).ToString('dd\:hh\:mm\:ss')
    & $tool collect --process-id $process.Id --refresh-interval 2 --duration $duration --counters System.Runtime --format csv --output $OutputPrefix
    if ($LASTEXITCODE -ne 0) { throw "dotnet-counters exited with code $LASTEXITCODE." }
    Write-Host "[Benchmark] Managed GC metrics saved with prefix $OutputPrefix"

    # Windows exposes per-process graphics memory through Performance Counters. These
    # counters do not inject into the app or any game and are sampled only outside games.
    try {
        $gpuPaths = @((Get-Counter -ListSet 'GPU Process Memory' -ErrorAction Stop).PathsWithInstances |
            Where-Object { $_ -match "pid_$($process.Id)_" -and $_ -match '\\(Dedicated Usage|Shared Usage)$' })
        if ($gpuPaths.Count -gt 0) {
            $gpuRows = [Collections.Generic.List[object]]::new()
            foreach ($snapshot in (Get-Counter -Counter $gpuPaths -SampleInterval 2 -MaxSamples 15 -ErrorAction Stop)) {
                foreach ($sample in $snapshot.CounterSamples) {
                    $gpuRows.Add([pscustomobject]@{
                        TimestampUtc = $snapshot.Timestamp.ToUniversalTime().ToString('o')
                        CounterPath = $sample.Path
                        UsageType = if ($sample.Path -match 'Dedicated Usage$') { 'Dedicated' } else { 'Shared' }
                        Bytes = [math]::Round($sample.CookedValue, 0)
                        Megabytes = [math]::Round($sample.CookedValue / 1MB, 4)
                    })
                }
            }
            $gpuRows | Export-Csv -LiteralPath ($OutputPrefix + '-gpu-memory.csv') -NoTypeInformation -Encoding UTF8
            Write-Host "[Benchmark] GPU process-memory counters saved to $OutputPrefix-gpu-memory.csv"
        } else {
            Write-Host '[Benchmark] No GPU Process Memory instance exposed for this PID.'
        }
    } catch {
        Write-Warning "GPU process-memory counters unavailable: $($_.Exception.Message)"
    }
} finally {
    if ($process) {
        if (-not $process.HasExited) { $process.Kill(); $process.WaitForExit(5000) | Out-Null }
        $process.Dispose()
    }
}
