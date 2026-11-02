<#
.SYNOPSIS
    Measure the Release x64 process without attaching a debugger.
.EXAMPLE
    .\benchmarks\Measure-CrossGOverlay.ps1 -Scenario Startup -Runs 10
.EXAMPLE
    .\benchmarks\Measure-CrossGOverlay.ps1 -Scenario IdleHidden -Runs 3 -DurationSeconds 600
.EXAMPLE
    .\benchmarks\Measure-CrossGOverlay.ps1 -Scenario VisibleIdle -Runs 3 -DurationSeconds 60
.EXAMPLE
    .\benchmarks\Measure-CrossGOverlay.ps1 -Scenario Observe -ProcessId 1234 -Name SettingsScroll -Runs 3 -DurationSeconds 180
#>
param(
    [ValidateSet('Startup', 'IdleHidden', 'VisibleIdle', 'Observe')]
    [string]$Scenario = 'IdleHidden',
    [ValidateRange(1, 1000)]
    [int]$Runs = 3,
    [ValidateRange(1, 86400)]
    [int]$DurationSeconds = 60,
    [ValidateRange(100, 60000)]
    [int]$SampleIntervalMs = 1000,
    [int]$ProcessId = 0,
    [string]$Name = '',
    [string]$ExePath = '',
    [string]$OutputDirectory = ''
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
if (-not $ExePath) {
    $ExePath = Join-Path $repoRoot 'src\CrosshairOverlay\bin\x64\Release\net8.0-windows\win-x64\CrossGOverlay.exe'
}
if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path $PSScriptRoot 'results'
}
$ExePath = [IO.Path]::GetFullPath($ExePath)
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
if (-not (Test-Path -LiteralPath $ExePath -PathType Leaf)) { throw "Release x64 executable missing: $ExePath" }
if ($Scenario -ne 'Observe' -and
    @(Get-Process -ErrorAction SilentlyContinue |
        Where-Object { $_.ProcessName -match '^DeltaForce' }).Count -gt 0) {
    throw 'App-only startup/idle benchmarks require Delta Force to be closed.'
}
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$benchmarkRoot = Join-Path $OutputDirectory 'app-data'
if ($Scenario -ne 'Observe') {
    New-Item -ItemType Directory -Path $benchmarkRoot -Force | Out-Null
    # PowerShell runs in its own process; only benchmark child processes inherit this value.
    $env:CROSSGOVERLAY_BENCHMARK_ROOT = $benchmarkRoot
}

if (-not ('CrossGOverlayBenchmark.Native' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
namespace CrossGOverlayBenchmark {
    public static class Native {
        [DllImport("user32.dll", SetLastError=true)]
        public static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")]
        public static extern uint GetGuiResources(IntPtr process, uint flags);
    }
}
'@
}

function Get-LiveProcess([int]$id) {
    try { return [Diagnostics.Process]::GetProcessById($id) }
    catch [ArgumentException] { return $null }
}

function Wait-FirstWindow([Diagnostics.Process]$process, [int]$timeoutMs = 20000) {
    $timer = [Diagnostics.Stopwatch]::StartNew()
    while ($timer.ElapsedMilliseconds -lt $timeoutMs) {
        if ($process.HasExited) { break }
        $process.Refresh()
        if ($process.MainWindowHandle -ne [IntPtr]::Zero) {
            return [pscustomobject]@{ ElapsedMs = [math]::Round($timer.Elapsed.TotalMilliseconds, 2); Handle = $process.MainWindowHandle }
        }
        Start-Sleep -Milliseconds 20
    }
    throw "First HWND not found within $timeoutMs ms (PID $($process.Id))."
}

function Wait-FirstFrameLog([Diagnostics.Process]$process, [string]$logPath, [long]$originalLength,
    [int]$timeoutMs = 30000) {
    $timer = [Diagnostics.Stopwatch]::StartNew()
    $pattern = 'CrosshairOverlay\.Views\.SettingsWindow:.*?(\d+)\s+ms'
    while ($timer.ElapsedMilliseconds -lt $timeoutMs) {
        if ($process.HasExited) { break }
        if (Test-Path -LiteralPath $logPath -PathType Leaf) {
            $stream = [IO.File]::Open($logPath, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite)
            try {
                if ($stream.Length -gt $originalLength) {
                    $stream.Seek($originalLength, [IO.SeekOrigin]::Begin) | Out-Null
                    $reader = [IO.StreamReader]::new($stream, [Text.Encoding]::UTF8, $true, 4096, $true)
                    try { $newText = $reader.ReadToEnd() } finally { $reader.Dispose() }
                    $match = [regex]::Match($newText, $pattern)
                    if ($match.Success) { return [int]$match.Groups[1].Value }
                }
            } finally { $stream.Dispose() }
        }
        Start-Sleep -Milliseconds 25
    }
    throw "Settings first-frame log not found within $timeoutMs ms (PID $($process.Id))."
}

function Wait-StartupCompleteLog([Diagnostics.Process]$process, [string]$logPath, [long]$originalLength,
    [int]$timeoutMs = 30000) {
    $timer = [Diagnostics.Stopwatch]::StartNew()
    while ($timer.ElapsedMilliseconds -lt $timeoutMs) {
        if ($process.HasExited) { break }
        if (Test-Path -LiteralPath $logPath -PathType Leaf) {
            $stream = [IO.File]::Open($logPath, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite)
            try {
                if ($stream.Length -gt $originalLength) {
                    $stream.Seek($originalLength, [IO.SeekOrigin]::Begin) | Out-Null
                    $reader = [IO.StreamReader]::new($stream, [Text.Encoding]::UTF8, $true, 4096, $true)
                    try { $newText = $reader.ReadToEnd() } finally { $reader.Dispose() }
                    if ($newText -match '\[FTL\] CrosshairOverlay\.App:') {
                        throw "Application startup failed (PID $($process.Id)); see $logPath"
                    }
                    if ($newText -match 'CrosshairOverlay\.App:.*?runtime \+ .*?App\.xaml') { return }
                }
            } finally { $stream.Dispose() }
        }
        Start-Sleep -Milliseconds 25
    }
    throw "Application startup completion not found within $timeoutMs ms (PID $($process.Id))."
}

function Read-NewLog([string]$logPath, [long]$originalLength) {
    if (-not (Test-Path -LiteralPath $logPath)) { return '' }
    $stream = [IO.File]::Open($logPath, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite)
    try {
        if ($stream.Length -le $originalLength) { return '' }
        $stream.Seek($originalLength, [IO.SeekOrigin]::Begin) | Out-Null
        $reader = [IO.StreamReader]::new($stream, [Text.Encoding]::UTF8, $true, 4096, $true)
        try { return $reader.ReadToEnd() } finally { $reader.Dispose() }
    } finally { $stream.Dispose() }
}

function Read-Timeline([Diagnostics.Process]$process, [string]$logPath, [long]$originalLength) {
    $names = @('AppInitialized','SettingsInitialized','SettingsFirstRender',
        'FirstInputPriorityDispatch','OverlayFirstDrawing')
    $watch = [Diagnostics.Stopwatch]::StartNew()
    do {
        if ($process.HasExited) { throw 'App exited before startup timeline completed.' }
        $log = Read-NewLog $logPath $originalLength
        $values = [ordered]@{}
        foreach ($name in $names) {
            $match = [regex]::Match($log, "BenchmarkTimeline $name ([0-9]+(?:\.[0-9]+)?) ms\.")
            if ($match.Success) {
                $values[$name] = [double]::Parse($match.Groups[1].Value,
                    [Globalization.CultureInfo]::InvariantCulture)
            }
        }
        if ($values.Count -eq $names.Count) { return [pscustomobject]$values }
        Start-Sleep -Milliseconds 25
    } while ($watch.Elapsed.TotalSeconds -lt 5)
    throw "Startup timeline incomplete: $($values.Keys -join ', ')"
}

function Stop-OwnedProcess([Diagnostics.Process]$process) {
    if (-not $process) { return }
    try {
        if (-not $process.HasExited) {
            # WM_CLOSE normally hides Settings instead of exiting. This hard stop applies only
            # to the process launched by this benchmark, never to a pre-existing user process.
            $process.Kill()
            $process.WaitForExit(5000) | Out-Null
        }
    } finally { $process.Dispose() }
}

function Measure-Process([Diagnostics.Process]$process, [int]$run, [string]$label) {
    $sampleCount = [math]::Ceiling($DurationSeconds * 1000 / $SampleIntervalMs)
    $samples = [Collections.Generic.List[object]]::new()
    $clock = [Diagnostics.Stopwatch]::StartNew()
    $process.Refresh()
    $priorCpu = $process.TotalProcessorTime.TotalSeconds
    $priorTime = $clock.Elapsed.TotalSeconds
    for ($index = 0; $index -lt $sampleCount; $index++) {
        Start-Sleep -Milliseconds $SampleIntervalMs
        $process.Refresh()
        if ($process.HasExited) { throw "Process exited during $label run $run." }
        $now = $clock.Elapsed.TotalSeconds
        $cpu = $process.TotalProcessorTime.TotalSeconds
        $delta = $now - $priorTime
        $cpuPercent = if ($delta -gt 0) { 100 * ($cpu - $priorCpu) / ($delta * [Environment]::ProcessorCount) } else { 0 }
        $priorTime = $now
        $priorCpu = $cpu
        $samples.Add([pscustomobject]@{
            Scenario = $label
            Run = $run
            TimestampUtc = [DateTimeOffset]::UtcNow.ToString('o')
            ElapsedSeconds = [math]::Round($now, 3)
            CpuPercentAllLogicalCores = [math]::Round([math]::Max(0, $cpuPercent), 5)
            WorkingSetMB = [math]::Round($process.WorkingSet64 / 1MB, 3)
            PrivateBytesMB = [math]::Round($process.PrivateMemorySize64 / 1MB, 3)
            Handles = $process.HandleCount
            GdiHandles = [CrossGOverlayBenchmark.Native]::GetGuiResources($process.Handle, 0)
            UserHandles = [CrossGOverlayBenchmark.Native]::GetGuiResources($process.Handle, 1)
            ManagedHeapMB = $null
            GcPauseMs = $null
            GpuVramMB = $null
            ContextSwitches = $null
        })
    }
    return $samples
}

$stamp = [DateTimeOffset]::UtcNow.ToString('yyyyMMddTHHmmssZ')
$baseName = if ($Name) { $Name } else { $Scenario }
$baseName = $baseName -replace '[^a-zA-Z0-9_-]', '_'
$samplesPath = Join-Path $OutputDirectory "$stamp-$baseName-samples.csv"
$runsPath = Join-Path $OutputDirectory "$stamp-$baseName-runs.json"
$metadataPath = Join-Path $OutputDirectory "$stamp-$baseName-metadata.json"
$runRecords = [Collections.Generic.List[object]]::new()
$allSamples = [Collections.Generic.List[object]]::new()
$existing = @(Get-Process -Name CrossGOverlay -ErrorAction SilentlyContinue)
if ($Scenario -ne 'Observe' -and $existing.Count -gt 0) {
    throw 'A CrossGOverlay process is already running. Close it before a launch benchmark; the script will not stop an existing instance.'
}

if ($Scenario -eq 'Observe' -and $ProcessId -le 0) { throw 'Observe requires -ProcessId.' }
for ($run = 1; $run -le $Runs; $run++) {
    $owned = $null
    try {
        if ($Scenario -eq 'Observe') {
            $process = Get-LiveProcess $ProcessId
            if (-not $process) { throw "PID $ProcessId is not running." }
        } else {
            $logPath = Join-Path $benchmarkRoot "logs\crosshair-$(Get-Date -Format yyyyMMdd).log"
            $logLength = if (Test-Path -LiteralPath $logPath) { (Get-Item -LiteralPath $logPath).Length } else { 0L }
            $launch = [Diagnostics.Stopwatch]::StartNew()
            $owned = Start-Process -FilePath $ExePath -PassThru
            $process = Get-LiveProcess $owned.Id
            if (-not $process) { throw 'Launched process exited immediately.' }
            $firstWindow = Wait-FirstWindow $process
            $firstFrameMs = Wait-FirstFrameLog $process $logPath $logLength
            $logObservedMs = [math]::Round($launch.Elapsed.TotalMilliseconds, 2)
            Wait-StartupCompleteLog $process $logPath $logLength
            $timeline = if ($Scenario -eq 'Startup' -and
                $env:CROSSGOVERLAY_BENCHMARK_TIMELINE -eq '1') {
                Read-Timeline $process $logPath $logLength
            } else { $null }
            $process.Refresh()
            $settingsHandle = $process.MainWindowHandle
            if ($settingsHandle -eq [IntPtr]::Zero -or
                -not [CrossGOverlayBenchmark.Native]::IsWindowVisible($settingsHandle)) {
                throw 'Settings main window was not visible after its first-frame log.'
            }
            $runRecords.Add([pscustomobject]@{
                Scenario = $Scenario; Run = $run; Pid = $process.Id
                FirstHwndMs = $firstWindow.ElapsedMs; SettingsFirstFrameMs = $firstFrameMs
                FirstFrameLogObservedMs = $logObservedMs
                SettingsWindowVisible = $true
                AppInitializedMs = $timeline.AppInitialized
                SettingsInitializedMs = $timeline.SettingsInitialized
                SettingsFirstRenderPreciseMs = $timeline.SettingsFirstRender
                FirstInputPriorityDispatchMs = $timeline.FirstInputPriorityDispatch
                OverlayFirstDrawingMs = $timeline.OverlayFirstDrawing
            })
            if ($Scenario -eq 'IdleHidden') {
                if (-not [CrossGOverlayBenchmark.Native]::PostMessage($settingsHandle, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero)) {
                    throw 'Could not send WM_CLOSE to hide Settings.'
                }
                Start-Sleep -Seconds 5 # Let the tray toast and startup work finish.
                if ($process.HasExited -or [CrossGOverlayBenchmark.Native]::IsWindowVisible($settingsHandle)) {
                    throw 'Settings did not hide while the application remained running.'
                }
            } elseif ($Scenario -eq 'VisibleIdle') {
                Start-Sleep -Seconds 5 # Match the hidden scenario's post-startup settling time.
            }
        }
        if ($Scenario -ne 'Startup') {
            foreach ($sample in (Measure-Process $process $run $baseName)) { $allSamples.Add($sample) }
        }
        # Preserve completed runs even if a later long run is interrupted.
        if ($allSamples.Count -gt 0) { $allSamples | Export-Csv -LiteralPath $samplesPath -NoTypeInformation -Encoding UTF8 }
        $runRecords | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $runsPath -Encoding UTF8
        Write-Host "[Benchmark] $baseName run $run/$Runs complete, PID $($process.Id)."
    } finally {
        if ($owned) { Stop-OwnedProcess $owned }
        elseif ($Scenario -eq 'Observe' -and $process) { $process.Dispose() }
    }
}

if ($allSamples.Count -gt 0) { $allSamples | Export-Csv -LiteralPath $samplesPath -NoTypeInformation -Encoding UTF8 }
$runRecords | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $runsPath -Encoding UTF8
[pscustomobject]@{
    Scenario = $Scenario
    Label = $baseName
    Runs = $Runs
    DurationSecondsPerRun = if ($Scenario -eq 'Startup') { 0 } else { $DurationSeconds }
    SampleIntervalMs = $SampleIntervalMs
    ProcessorCount = [Environment]::ProcessorCount
    ExePath = $ExePath
    BenchmarkDataRoot = if ($Scenario -ne 'Observe') { $benchmarkRoot } else { $null }
    ExeSha256 = (Get-FileHash -LiteralPath $ExePath -Algorithm SHA256).Hash
    DateUtc = [DateTimeOffset]::UtcNow.ToString('o')
    FirstFrameSource = 'CrosshairOverlay.Views.SettingsWindow OnContentRendered log; FirstHwndMs may refer to the splash screen.'
    Note = 'CPU is normalized over all logical processors. Managed heap, GC pause, GPU and context switches require separate instrumentation.'
} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $metadataPath -Encoding UTF8
Write-Host "[Benchmark] Output: $OutputDirectory"
