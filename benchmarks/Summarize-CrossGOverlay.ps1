<#
.SYNOPSIS
    Summarize raw benchmark CSV/JSON without changing or inventing missing metrics.
.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File .\benchmarks\Summarize-CrossGOverlay.ps1 `
        -StartupRunsJson .\benchmarks\results\STARTUP-runs.json `
        -SamplesCsv .\benchmarks\results\IDLE-samples.csv `
        -OutputJson .\benchmarks\results\summary.json
#>
param(
    [Parameter(Mandatory = $true)][string]$StartupRunsJson,
    [Parameter(Mandatory = $true)][string]$SamplesCsv,
    [Parameter(Mandatory = $true)][string]$OutputJson,
    [string]$OutputCsv = ''
)

$ErrorActionPreference = 'Stop'
$startup = Get-Content -LiteralPath $StartupRunsJson -Raw -Encoding UTF8 | ConvertFrom-Json
$samples = @(Import-Csv -LiteralPath $SamplesCsv -Encoding UTF8)
if ($startup.Count -eq 0 -or $samples.Count -eq 0) { throw 'Input files contain no benchmark runs.' }
if (@($startup | Where-Object { -not $_.SettingsWindowVisible }).Count -gt 0) {
    throw 'A startup run did not have a visible Settings window.'
}

function As-Double($value) {
    return [double]::Parse([string]$value, [Globalization.CultureInfo]::InvariantCulture)
}
function Average-Of($rows, [string]$property) {
    $numbers = @($rows | ForEach-Object { As-Double $_.$property })
    return [math]::Round(($numbers | Measure-Object -Average).Average, 5)
}
function Max-Of($rows, [string]$property) {
    $numbers = @($rows | ForEach-Object { As-Double $_.$property })
    return [math]::Round(($numbers | Measure-Object -Maximum).Maximum, 5)
}
function Percentile($values, [double]$fraction) {
    $sorted = @($values | Sort-Object)
    return $sorted[[math]::Ceiling($sorted.Count * $fraction) - 1]
}
function Median($values) {
    $sorted = @($values | Sort-Object)
    $middle = [int][math]::Floor($sorted.Count / 2)
    if ($sorted.Count % 2 -eq 1) { return $sorted[$middle] }
    return ($sorted[$middle - 1] + $sorted[$middle]) / 2
}
function Sample-StandardDeviation($values) {
    $numbers = @($values)
    if ($numbers.Count -lt 2) { return $null }
    $mean = ($numbers | Measure-Object -Average).Average
    $sum = 0.0
    foreach ($number in $numbers) { $sum += [math]::Pow([double]$number - $mean, 2) }
    return [math]::Round([math]::Sqrt($sum / ($numbers.Count - 1)), 5)
}

$startupMs = @($startup | ForEach-Object { As-Double $_.SettingsFirstFrameMs })
$groups = @($samples | Group-Object Run | Sort-Object { [int]$_.Name })
$runSummaries = @(foreach ($group in $groups) {
    $rows = @($group.Group)
    $first = $rows[0]
    $last = $rows[-1]
    [pscustomobject]@{
        Run = [int]$group.Name
        Samples = $rows.Count
        CpuMeanPercent = Average-Of $rows 'CpuPercentAllLogicalCores'
        CpuMaxPercent = Max-Of $rows 'CpuPercentAllLogicalCores'
        WorkingSetMeanMB = Average-Of $rows 'WorkingSetMB'
        WorkingSetStartMB = As-Double $first.WorkingSetMB
        WorkingSetEndMB = As-Double $last.WorkingSetMB
        PrivateMeanMB = Average-Of $rows 'PrivateBytesMB'
        PrivateStartMB = As-Double $first.PrivateBytesMB
        PrivateEndMB = As-Double $last.PrivateBytesMB
        HandlesStart = [int]$first.Handles
        HandlesEnd = [int]$last.Handles
        GdiStart = [int]$first.GdiHandles
        GdiEnd = [int]$last.GdiHandles
        UserStart = [int]$first.UserHandles
        UserEnd = [int]$last.UserHandles
    }
})

$result = [pscustomobject]@{
    StartupRunsJson = $StartupRunsJson
    SamplesCsv = $SamplesCsv
    Startup = [pscustomobject]@{
        Runs = $startupMs.Count
        FirstFrameMeanMs = [math]::Round(($startupMs | Measure-Object -Average).Average, 3)
        FirstFrameMedianMs = Median $startupMs
        FirstFrameP95Ms = Percentile $startupMs 0.95
        FirstFrameMinMs = ($startupMs | Measure-Object -Minimum).Minimum
        FirstFrameMaxMs = ($startupMs | Measure-Object -Maximum).Maximum
        ValuesMs = $startupMs
    }
    Samples = [pscustomobject]@{
        Runs = $groups.Count
        Count = $samples.Count
        CpuMeanPercent = Average-Of $samples 'CpuPercentAllLogicalCores'
        CpuMaxPercent = Max-Of $samples 'CpuPercentAllLogicalCores'
        WorkingSetMeanMB = Average-Of $samples 'WorkingSetMB'
        PrivateMeanMB = Average-Of $samples 'PrivateBytesMB'
        PrivateMaxMB = Max-Of $samples 'PrivateBytesMB'
        PerRun = $runSummaries
    }
    NotMeasuredInThisInput = @('CPU wakeups', 'context switches', 'managed heap', 'GC pause',
        'GPU VRAM', 'UI frame time', 'game FPS', 'Release render invalidations')
}
$result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $OutputJson -Encoding UTF8
if (-not $OutputCsv) { $OutputCsv = [IO.Path]::ChangeExtension($OutputJson, '.csv') }
$csvRows = [Collections.Generic.List[object]]::new()
function Add-MetricRows([string]$scenario, [string]$metric, [string]$unit, $values) {
    $numbers = @($values)
    for ($index = 0; $index -lt $numbers.Count; $index++) {
        $csvRows.Add([pscustomobject]@{
            Scenario=$scenario; Metric=$metric; RowType='RUN'; Run=$index + 1
            Value=$numbers[$index]; Unit=$unit; BaselineValue=$null; DeltaPct=$null
        })
    }
    foreach ($summary in @(
        [pscustomobject]@{ Type='MEDIAN'; Value=(Median $numbers) },
        [pscustomobject]@{ Type='SAMPLE_SD'; Value=(Sample-StandardDeviation $numbers) }
    )) {
        $csvRows.Add([pscustomobject]@{
            Scenario=$scenario; Metric=$metric; RowType=$summary.Type; Run=$null
            Value=$summary.Value; Unit=$unit; BaselineValue=$null; DeltaPct=$null
        })
    }
}
Add-MetricRows 'StartupWarm' 'SettingsFirstFrame' 'ms' $startupMs
Add-MetricRows 'IdleHidden' 'CpuMean' '%' @($runSummaries | ForEach-Object { $_.CpuMeanPercent })
Add-MetricRows 'IdleHidden' 'WorkingSetMean' 'MB' @($runSummaries | ForEach-Object { $_.WorkingSetMeanMB })
Add-MetricRows 'IdleHidden' 'PrivateBytesMean' 'MB' @($runSummaries | ForEach-Object { $_.PrivateMeanMB })
$csvRows | Export-Csv -LiteralPath $OutputCsv -NoTypeInformation -Encoding UTF8
Write-Host "[Benchmark] Summary: $OutputJson"
Write-Host "[Benchmark] Per-run/median/variation CSV: $OutputCsv (baseline unavailable)"
