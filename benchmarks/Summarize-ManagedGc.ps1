<# 
.SYNOPSIS
    Summarize existing app-only System.Runtime and GPU Process Memory captures.
.NOTES
    This script never launches or attaches to any process, game or profiler.
#>
param(
    [string]$ResultsDirectory = (Join-Path $PSScriptRoot 'results'),
    [string]$OutputCsv = ''
)

$ErrorActionPreference = 'Stop'
if (-not $OutputCsv) { $OutputCsv = Join-Path $ResultsDirectory 'runtime-summary.csv' }
$files = @(Get-ChildItem -LiteralPath $ResultsDirectory -Filter 'managed-gc-*.csv' |
    Where-Object { $_.Name -notlike '*-gpu-memory.csv' } | Sort-Object Name)
if ($files.Count -eq 0) { throw 'No managed GC captures found.' }

$measurements = [Collections.Generic.List[object]]::new()
function Add-Measurement([int]$run, [string]$capture, [string]$metric,
    [double]$value, [string]$unit) {
    $measurements.Add([pscustomobject]@{
        Run=$run; Capture=$capture; Metric=$metric; Value=[math]::Round($value, 5); Unit=$unit
    })
}
function Get-CounterValues($rows, [string]$name) {
    $matched = @($rows | Where-Object { $_.'Counter Name' -eq $name })
    if ($matched.Count -eq 0) { throw "Counter missing: $name" }
    return @($matched | ForEach-Object {
        [double]::Parse($_.'Mean/Increment', [Globalization.CultureInfo]::InvariantCulture)
    })
}
function Mean($values) { return [double](($values | Measure-Object -Average).Average) }

for ($index = 0; $index -lt $files.Count; $index++) {
    $file = $files[$index]
    $run = $index + 1
    $rows = @(Import-Csv -LiteralPath $file.FullName -Encoding UTF8)
    $heap = Get-CounterValues $rows 'GC Heap Size (MB)'
    $allocation = Get-CounterValues $rows 'Allocation Rate (B / 2 sec)'
    $pause = Get-CounterValues $rows 'Time paused by GC (ms / 2 sec)'
    $cpu = Get-CounterValues $rows 'CPU Usage (%)'
    Add-Measurement $run $file.Name 'GcHeapMean' (Mean $heap) 'MB'
    Add-Measurement $run $file.Name 'AllocationRateMean' ((Mean $allocation) / 2 / 1024) 'KiB/s'
    Add-Measurement $run $file.Name 'GcPauseTotal' ([double](($pause | Measure-Object -Sum).Sum)) 'ms'
    Add-Measurement $run $file.Name 'CpuMeanWithProfiler' (Mean $cpu) '%'

    $gpuFile = Join-Path $ResultsDirectory ($file.BaseName + '-gpu-memory.csv')
    if (Test-Path -LiteralPath $gpuFile) {
        $gpuRows = @(Import-Csv -LiteralPath $gpuFile -Encoding UTF8)
        foreach ($type in @('Shared', 'Dedicated')) {
            $values = @($gpuRows | Where-Object UsageType -EQ $type |
                ForEach-Object { [double]$_.Megabytes })
            if ($values.Count -gt 0) {
                Add-Measurement $run (Split-Path $gpuFile -Leaf) ("Gpu" + $type + "Mean") (Mean $values) 'MB'
            }
        }
    }
}

function Median($values) {
    $sorted = @($values | Sort-Object)
    $middle = [int][math]::Floor($sorted.Count / 2)
    if ($sorted.Count % 2 -eq 1) { return [double]$sorted[$middle] }
    return ([double]$sorted[$middle - 1] + [double]$sorted[$middle]) / 2
}
function SampleSd($values) {
    $numbers = @($values)
    if ($numbers.Count -lt 2) { return $null }
    $mean = Mean $numbers
    $sum = 0.0
    foreach ($number in $numbers) { $sum += [math]::Pow([double]$number - $mean, 2) }
    return [math]::Sqrt($sum / ($numbers.Count - 1))
}

$output = [Collections.Generic.List[object]]::new()
foreach ($group in ($measurements | Group-Object Metric | Sort-Object Name)) {
    $values = @($group.Group | ForEach-Object { [double]$_.Value })
    foreach ($row in $group.Group) {
        $output.Add([pscustomobject]@{
            Metric=$group.Name; RowType='RUN'; Run=$row.Run; Capture=$row.Capture
            Value=$row.Value; Unit=$row.Unit; BaselineValue=$null; DeltaPct=$null
        })
    }
    foreach ($summary in @(
        [pscustomobject]@{ Type='MEDIAN'; Value=(Median $values) },
        [pscustomobject]@{ Type='SAMPLE_SD'; Value=(SampleSd $values) }
    )) {
        $output.Add([pscustomobject]@{
            Metric=$group.Name; RowType=$summary.Type; Run=$null; Capture=$null
            Value=if ($null -ne $summary.Value) { [math]::Round($summary.Value, 5) } else { $null }
            Unit=$group.Group[0].Unit; BaselineValue=$null; DeltaPct=$null
        })
    }
}
$output | Export-Csv -LiteralPath $OutputCsv -NoTypeInformation -Encoding UTF8
Write-Host "[Benchmark] Runtime summary: $OutputCsv"
