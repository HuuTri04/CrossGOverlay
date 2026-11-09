<#
.SYNOPSIS
    Measures process GPU memory with the static Settings backdrop cache on/off.
.NOTES
    Runs the app outside games with isolated settings. Counters cover the whole process,
    including driver/WPF allocations; they cannot assign bytes to one Image or HWND.
#>
param(
    [string]$ExePath = '',
    [string]$FixturePath = '',
    [int]$SamplesPerRun = 5
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
if (-not $ExePath) {
    $ExePath = Join-Path $repo 'src\CrosshairOverlay\bin\Release\net8.0-windows10.0.19041.0\win-x64\CrossGOverlay.exe'
}
if (-not $FixturePath) {
    $FixturePath = Join-Path $PSScriptRoot 'results\profiling-app-data\effects\background\settings.json'
}
if (-not (Test-Path -LiteralPath $ExePath -PathType Leaf)) { throw "EXE missing: $ExePath" }
if (-not (Test-Path -LiteralPath $FixturePath -PathType Leaf)) { throw "Fixture missing: $FixturePath" }
$fixture = Get-Content -LiteralPath $FixturePath -Raw | ConvertFrom-Json
if (-not $fixture.CustomAppBgPath -or -not (Test-Path -LiteralPath $fixture.CustomAppBgPath -PathType Leaf)) {
    throw 'The GPU fixture must contain an existing custom background image.'
}
if (Get-Process CrossGOverlay -ErrorAction SilentlyContinue) { throw 'Close CrossGOverlay before the isolated GPU benchmark.' }
if (@(Get-Process -ErrorAction SilentlyContinue | Where-Object ProcessName -Match '^DeltaForce').Count) {
    throw 'GPU benchmark runs outside Delta Force only.'
}

$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$output = Join-Path $PSScriptRoot "results\gpu-cache-ab-$stamp"
New-Item -ItemType Directory -Path $output -Force | Out-Null
$rows = [Collections.Generic.List[object]]::new()
$runIndex = 0

foreach ($variant in @('off', 'on', 'on', 'off')) {
    $run = ++$runIndex
    $dataRoot = Join-Path $output "app-$variant-$run"
    New-Item -ItemType Directory -Path $dataRoot -Force | Out-Null
    Copy-Item -LiteralPath $FixturePath -Destination (Join-Path $dataRoot 'settings.json')
    $env:CROSSGOVERLAY_BENCHMARK_ROOT = $dataRoot
    $env:CROSSGOVERLAY_BENCHMARK_NO_TRAY_TOAST = '1'
    $env:CROSSGOVERLAY_BENCHMARK_TIMELINE = '1'
    if ($variant -eq 'off') { $env:CROSSGOVERLAY_BENCHMARK_DISABLE_BACKGROUND_CACHE = '1' }
    else { Remove-Item Env:CROSSGOVERLAY_BENCHMARK_DISABLE_BACKGROUND_CACHE -ErrorAction SilentlyContinue }

    $process = $null
    try {
        $process = Start-Process -FilePath $ExePath -PassThru
        Start-Sleep -Seconds 10
        $process.Refresh()
        if ($process.HasExited -or $process.MainWindowHandle -eq [IntPtr]::Zero) {
            throw "Settings did not open for cache=$variant."
        }
        $paths = @((Get-Counter -ListSet 'GPU Process Memory' -ErrorAction Stop).PathsWithInstances | Where-Object {
            $_ -match "pid_$($process.Id)_" -and $_ -match '(Shared Usage|Dedicated Usage)$'
        })
        if ($paths.Count -eq 0) { throw "GPU Process Memory counters unavailable for PID $($process.Id)." }
        for ($sample = 1; $sample -le $SamplesPerRun; $sample++) {
            $process.Refresh()
            $counters = @(Get-Counter -Counter $paths -MaxSamples 1 -ErrorAction Stop).CounterSamples
            $shared = ($counters | Where-Object Path -Match 'Shared Usage$' | Measure-Object CookedValue -Sum).Sum
            $dedicated = ($counters | Where-Object Path -Match 'Dedicated Usage$' | Measure-Object CookedValue -Sum).Sum
            $rows.Add([pscustomobject]@{
                Variant=$variant;Run=$run;Sample=$sample;TimestampUtc=[DateTimeOffset]::UtcNow.ToString('o');
                Pid=$process.Id;SharedMiB=[math]::Round($shared/1MB,3);
                DedicatedMiB=[math]::Round($dedicated/1MB,3);
                PrivateMiB=[math]::Round($process.PrivateMemorySize64/1MB,3)
            })
        }
    }
    finally {
        Remove-Item Env:CROSSGOVERLAY_BENCHMARK_ROOT,Env:CROSSGOVERLAY_BENCHMARK_NO_TRAY_TOAST,Env:CROSSGOVERLAY_BENCHMARK_DISABLE_BACKGROUND_CACHE,Env:CROSSGOVERLAY_BENCHMARK_TIMELINE -ErrorAction SilentlyContinue
        if ($process) {
            if (-not $process.HasExited) { $process.Kill(); $process.WaitForExit(5000) | Out-Null }
            $process.Dispose()
        }
        $rows | Export-Csv -LiteralPath (Join-Path $output 'samples.csv') -NoTypeInformation -Encoding UTF8
    }
}
Write-Output (Join-Path $output 'samples.csv')
