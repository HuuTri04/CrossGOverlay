<#
.SYNOPSIS
    Runs the opt-in Release x64 stress fixture against isolated app data.
.EXAMPLE
    .\benchmarks\Invoke-Stress.ps1
#>
param(
    [ValidateSet('settings', 'preset', 'preset-multistage', 'pixel', 'pixel-batched', 'all')]
    [string]$Scenario = 'all',
    [string]$ExePath = '',
    [string]$OutputDirectory = ''
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
if (-not $ExePath) {
    $ExePath = Join-Path $repoRoot 'src\CrosshairOverlay\bin\x64\Release\net8.0-windows10.0.19041.0\win-x64\CrossGOverlay.exe'
}
if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path $PSScriptRoot ('results\stress-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
}
$ExePath = [IO.Path]::GetFullPath($ExePath)
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
if (-not (Test-Path -LiteralPath $ExePath -PathType Leaf)) { throw "Release x64 executable missing: $ExePath" }
if (Get-Process CrossGOverlay -ErrorAction SilentlyContinue) {
    throw 'Close the existing CrossGOverlay instance before starting the isolated stress fixture.'
}
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$scenarios = if ($Scenario -eq 'all') { @('settings', 'preset', 'preset-multistage', 'pixel', 'pixel-batched') } else { @($Scenario) }
$summary = [Collections.Generic.List[string]]::new()
$gateFailure = $false
$summary.Add('CrossGOverlay Release x64 stress test - actual measurements')
$summary.Add('Each scenario uses an isolated data directory; delta is after explicit stress-fixture GC minus before.')

foreach ($name in $scenarios) {
    $scenarioRoot = Join-Path $OutputDirectory $name
    $diagnostics = Join-Path $scenarioRoot 'diagnostics'
    New-Item -ItemType Directory -Path $diagnostics -Force | Out-Null
    $env:CROSSGOVERLAY_BENCHMARK_ROOT = $scenarioRoot
    $env:CROSSGOVERLAY_STRESS_SCENARIO = $name
    $env:CROSSGOVERLAY_BENCHMARK_AUTO_EXIT = '1'
    $env:CROSSGOVERLAY_BENCHMARK_NO_TRAY_TOAST = '1'
    $child = $null
    try {
        $child = Start-Process -FilePath $ExePath -PassThru -WindowStyle Hidden
        $ready = Join-Path $diagnostics 'stress-ready.txt'
        $done = Join-Path $diagnostics 'stress-done.txt'
        $errorFile = Join-Path $diagnostics 'stress-error.txt'
        $deadline = (Get-Date).AddSeconds(45)
        while (-not (Test-Path -LiteralPath $ready) -and (Get-Date) -lt $deadline) {
            if ($child.HasExited) { throw "${name}: app exited before fixture was ready (code $($child.ExitCode))." }
            Start-Sleep -Milliseconds 100
            $child.Refresh()
        }
        if (-not (Test-Path -LiteralPath $ready)) { throw "${name}: fixture did not become ready in 45 seconds." }
        [IO.File]::WriteAllText((Join-Path $diagnostics 'stress-start.signal'), 'start')
        $deadline = (Get-Date).AddMinutes(5)
        while (-not (Test-Path -LiteralPath $done) -and (Get-Date) -lt $deadline) {
            if (Test-Path -LiteralPath $errorFile) { throw (Get-Content -LiteralPath $errorFile -Raw) }
            if ($child.HasExited) { throw "${name}: app exited before completing fixture (code $($child.ExitCode))." }
            Start-Sleep -Milliseconds 250
            $child.Refresh()
        }
        if (-not (Test-Path -LiteralPath $done)) { throw "${name}: fixture exceeded five minutes." }
        $rows = @(Import-Csv -LiteralPath (Join-Path $diagnostics "stress-$name.csv"))
        $before = $rows[0]
        $after = $rows[-1]
        $peak = ($rows | Measure-Object -Property PrivateBytes -Maximum).Maximum
        $summary.Add(('{0}: {1} iterations, {2} ms wall, CPU {14}->{15} ms, allocated {16}->{17} bytes, PrivateBytes {3}->{4} (peak {5}), Managed {6}->{7}, GDI {8}->{9}, USER {10}->{11}, handles {12}->{13}' -f
            $name, $after.Iteration, $after.ElapsedMs, $before.PrivateBytes, $after.PrivateBytes,
            $peak, $before.ManagedBytes, $after.ManagedBytes, $before.GdiHandles, $after.GdiHandles,
            $before.UserHandles, $after.UserHandles, $before.ProcessHandles, $after.ProcessHandles,
            $before.CpuMs, $after.CpuMs, $before.AllocatedBytes, $after.AllocatedBytes))
        $assertions = Join-Path $diagnostics 'stress-assertions.txt'
        if (Test-Path -LiteralPath $assertions) {
            foreach ($line in Get-Content -LiteralPath $assertions) {
                $summary.Add("  $line")
                if ($line -match 'FAIL') { $gateFailure = $true }
            }
        }
    }
    catch {
        $summary.Add("${name}: FAILED - $($_.Exception.Message)")
        throw
    }
    finally {
        Remove-Item Env:CROSSGOVERLAY_BENCHMARK_ROOT,Env:CROSSGOVERLAY_STRESS_SCENARIO,Env:CROSSGOVERLAY_BENCHMARK_AUTO_EXIT,Env:CROSSGOVERLAY_BENCHMARK_NO_TRAY_TOAST -ErrorAction SilentlyContinue
        if ($child) {
            if (-not $child.WaitForExit(10000)) { Stop-Process -Id $child.Id -Force }
            $child.Dispose()
        }
        $summary | Set-Content -LiteralPath (Join-Path $OutputDirectory 'StressTest_Report.txt') -Encoding utf8
    }
}
Write-Output (Join-Path $OutputDirectory 'StressTest_Report.txt')
if ($gateFailure) { throw 'One or more stress resource gates failed; see StressTest_Report.txt and the CSV.' }
