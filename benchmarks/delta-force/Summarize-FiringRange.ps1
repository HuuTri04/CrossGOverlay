<#
.SYNOPSIS
    Produce per-run and median/SD CSV only from 15 measured 120-second runs.
.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File .\benchmarks\delta-force\Summarize-FiringRange.ps1 `
        -InputCsv .\benchmarks\delta-force\my-runs.csv `
        -OutputCsv .\benchmarks\delta-force\firing-range-results.csv
#>
param(
    [Parameter(Mandatory = $true)][string]$InputCsv,
    [Parameter(Mandatory = $true)][string]$OutputCsv
)

$ErrorActionPreference = 'Stop'
$rows = @(Import-Csv -LiteralPath $InputCsv -Encoding UTF8)
$conditions = @('NoApp', 'OverlayStatic', 'OverlayGif')
if ($rows.Count -ne 15) { throw "Expected exactly 15 runs, found $($rows.Count)." }

function Read-Number($value, [string]$field, [bool]$required) {
    if ([string]::IsNullOrWhiteSpace($value)) {
        if ($required) { throw "$field is missing." }
        return $null
    }
    $number = 0.0
    if (-not [double]::TryParse([string]$value,
            [Globalization.NumberStyles]::Float,
            [Globalization.CultureInfo]::InvariantCulture,
            [ref]$number) -or [double]::IsNaN($number) -or [double]::IsInfinity($number)) {
        throw "$field is not a finite number: $value"
    }
    return $number
}

function Median($values) {
    $sorted = @($values | Sort-Object)
    $mid = [int][math]::Floor($sorted.Count / 2)
    if ($sorted.Count % 2 -eq 1) { return [double]$sorted[$mid] }
    return ([double]$sorted[$mid - 1] + [double]$sorted[$mid]) / 2
}
function StandardDeviation($values) {
    $numbers = @($values)
    $mean = ($numbers | Measure-Object -Average).Average
    $sum = 0.0
    foreach ($number in $numbers) { $sum += [math]::Pow([double]$number - $mean, 2) }
    return [math]::Sqrt($sum / ($numbers.Count - 1))
}
function DeltaPercent([double]$value, [double]$baseline) {
    if ($baseline -eq 0) { return $null }
    return [math]::Round(100 * ($value - $baseline) / $baseline, 4)
}

$metrics = @('AverageFps', 'OnePercentLowFps', 'P95FrameTimeMs', 'P99FrameTimeMs',
    'FrameTimeSpikeCount', 'OverlayCpuMeanPercent', 'OverlayPrivateMB',
    'ManagedHeapMB', 'GcPauseMs', 'GpuVramMB')
$requiredMetrics = @('AverageFps', 'OnePercentLowFps')
$parsed = [Collections.Generic.List[object]]::new()
foreach ($condition in $conditions) {
    $conditionRows = @($rows | Where-Object Condition -EQ $condition)
    if ($conditionRows.Count -ne 5) { throw "$condition must have five runs." }
    $runNumbers = @($conditionRows | ForEach-Object { [int]$_.Run } | Sort-Object)
    if (($runNumbers -join ',') -ne '1,2,3,4,5') { throw "$condition must have runs 1 to 5 exactly once." }
    foreach ($row in $conditionRows) {
        if ($row.Status -ne 'MEASURED') { throw "$condition run $($row.Run) is not MEASURED; no result CSV was written." }
        if ($row.PolicyChecked -ne 'YES') { throw "$condition run $($row.Run) lacks a permitted capture-method check." }
        if ($row.DurationSeconds -ne '120' -or $row.Scene -ne 'Firing Range') {
            throw "$condition run $($row.Run) does not match the 120-second Firing Range protocol."
        }
        if ([string]::IsNullOrWhiteSpace($row.CaptureMethod)) { throw 'CaptureMethod is required.' }
        $values = [ordered]@{}
        foreach ($metric in $metrics) {
            $values[$metric] = Read-Number $row.$metric "$condition run $($row.Run) $metric" ($metric -in $requiredMetrics)
        }
        if ($values.AverageFps -le 0 -or $values.OnePercentLowFps -le 0) {
            throw "$condition run $($row.Run) requires positive FPS values."
        }
        $parsed.Add([pscustomobject]@{ Condition=$condition; Run=[int]$row.Run; CaptureMethod=$row.CaptureMethod; Values=$values })
    }
}

$baseline = @($parsed | Where-Object Condition -EQ 'NoApp')
$baselineMedianFps = Median @($baseline | ForEach-Object { $_.Values.AverageFps })
$baselineMedianLow = Median @($baseline | ForEach-Object { $_.Values.OnePercentLowFps })
$baselineP95 = @($baseline | ForEach-Object { $_.Values.P95FrameTimeMs })
$baselineP99 = @($baseline | ForEach-Object { $_.Values.P99FrameTimeMs })
$baselineMedianP95 = if ($baselineP95 -notcontains $null) { Median $baselineP95 } else { $null }
$baselineMedianP99 = if ($baselineP99 -notcontains $null) { Median $baselineP99 } else { $null }

$output = [Collections.Generic.List[object]]::new()
foreach ($condition in $conditions) {
    $group = @($parsed | Where-Object Condition -EQ $condition)
    foreach ($entry in $group) {
        $item = [ordered]@{
            Condition=$condition; RowType='RUN'; Run=$entry.Run; CaptureMethod=$entry.CaptureMethod
            AverageFps=$entry.Values.AverageFps; OnePercentLowFps=$entry.Values.OnePercentLowFps
            P95FrameTimeMs=$entry.Values.P95FrameTimeMs; P99FrameTimeMs=$entry.Values.P99FrameTimeMs
            FrameTimeSpikeCount=$entry.Values.FrameTimeSpikeCount
            OverlayCpuMeanPercent=$entry.Values.OverlayCpuMeanPercent
            OverlayPrivateMB=$entry.Values.OverlayPrivateMB
            ManagedHeapMB=$entry.Values.ManagedHeapMB; GcPauseMs=$entry.Values.GcPauseMs
            GpuVramMB=$entry.Values.GpuVramMB
            AverageFpsDeltaPct=DeltaPercent $entry.Values.AverageFps $baselineMedianFps
            OnePercentLowDeltaPct=DeltaPercent $entry.Values.OnePercentLowFps $baselineMedianLow
            P95FrameTimeDeltaPct=if ($null -ne $entry.Values.P95FrameTimeMs -and $null -ne $baselineMedianP95) { DeltaPercent $entry.Values.P95FrameTimeMs $baselineMedianP95 } else { $null }
            P99FrameTimeDeltaPct=if ($null -ne $entry.Values.P99FrameTimeMs -and $null -ne $baselineMedianP99) { DeltaPercent $entry.Values.P99FrameTimeMs $baselineMedianP99 } else { $null }
            VariationSD=$null
        }
        $output.Add([pscustomobject]$item)
    }
    foreach ($kind in @('MEDIAN', 'SAMPLE_SD')) {
        $item = [ordered]@{ Condition=$condition; RowType=$kind; Run=''; CaptureMethod=''; }
        foreach ($metric in $metrics) {
            $metricValues = @($group | ForEach-Object { $_.Values[$metric] })
            $item[$metric] = if ($metricValues -contains $null) { $null }
                elseif ($kind -eq 'MEDIAN') { [math]::Round((Median $metricValues), 4) }
                else { [math]::Round((StandardDeviation $metricValues), 4) }
        }
        $item['AverageFpsDeltaPct'] = if ($kind -eq 'MEDIAN') { DeltaPercent $item.AverageFps $baselineMedianFps } else { $null }
        $item['OnePercentLowDeltaPct'] = if ($kind -eq 'MEDIAN') { DeltaPercent $item.OnePercentLowFps $baselineMedianLow } else { $null }
        $item['P95FrameTimeDeltaPct'] = if ($kind -eq 'MEDIAN' -and $null -ne $item.P95FrameTimeMs -and $null -ne $baselineMedianP95) { DeltaPercent $item.P95FrameTimeMs $baselineMedianP95 } else { $null }
        $item['P99FrameTimeDeltaPct'] = if ($kind -eq 'MEDIAN' -and $null -ne $item.P99FrameTimeMs -and $null -ne $baselineMedianP99) { DeltaPercent $item.P99FrameTimeMs $baselineMedianP99 } else { $null }
        $item['VariationSD'] = $null
        $output.Add([pscustomobject]$item)
    }
}
$output | Export-Csv -LiteralPath $OutputCsv -NoTypeInformation -Encoding UTF8
Write-Host "[Benchmark] Wrote $($output.Count) measured rows to $OutputCsv."
