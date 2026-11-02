$ErrorActionPreference='Stop'
$root=Join-Path $PSScriptRoot 'results'
$out=Join-Path $root 'scroll-state-etw-20261029.csv'
$pairs=@(
    @('Idle','20261029T152530Z','20261029T152529Z'),
    @('Plain','20261029T152649Z','20261029T152648Z'),
    @('Background','20261029T152810Z','20261029T152809Z')
)
function Capture([string]$text,[string]$pattern){
    $match=[regex]::Match($text,$pattern)
    if(-not $match.Success){throw "Missing analyzer field: $pattern"}
    $match.Groups[1].Value
}
$rows=foreach($pair in $pairs){
    $mode=$pair[0]
    $prefix="scroll-state-$mode-$($pair[1])"
    $samples=Import-Csv (Join-Path $root "$prefix-samples.csv")
    $first=$samples[0]
    $start=([DateTimeOffset]::Parse($first.TimestampUtc)).AddSeconds(-[double]$first.ElapsedSeconds)
    $end=$start.AddSeconds(30)
    $etl=Join-Path $root "etl\CrossGOverlay-Scroll$mode-$($pair[2]).etl"
    $analysis=(& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'Analyze-ScrollEtw.ps1') -EtlPath $etl -StartUtc ($start.ToString('o')) -EndUtc ($end.ToString('o')) | Out-String)
    if($LASTEXITCODE -ne 0){throw "Analyzer failed: $mode"}
    $present=Capture $analysis 'AppPresent=(\d+)'
    $interval=Capture $analysis 'AppPresentIntervalP50,P95,P99Ms=([^\r\n]+)'
    $parts=if($interval -eq 'N/A'){@('','','')}else{$interval.Split(',')}
    $wheelPath=Join-Path $root "$prefix-wheel-timeline.csv"
    $wheel=if(Test-Path $wheelPath){@(Import-Csv $wheelPath).Count}else{0}
    [pscustomobject]@{
        Mode=$mode;UiTargetFps=60;DurationSeconds=30;StartUtc=$start.ToString('o')
        WheelMessages=$wheel
        CpuMeanPercent=[math]::Round((($samples|ForEach-Object {[double]$_.CpuPercent}|Measure-Object -Average).Average),4)
        UiThreadOnCpuMs=Capture $analysis 'UiMessageThreadOnCpuMs=\d+:([0-9.]+)'
        AppPresent=$present;PresentIntervalP50Ms=$parts[0];PresentIntervalP95Ms=$parts[1];PresentIntervalP99Ms=$parts[2]
        PresentGapsAtLeast75Ms=Capture $analysis 'PresentGapsAtLeast75Ms=(\d+)'
        LongGapsWithLayoutStackSample=Capture $analysis 'WithLayoutStackSample=(\d+)'
        MeasureStackSamples=Capture $analysis 'MeasureSamples=(\d+)'
        ArrangeStackSamples=Capture $analysis 'ArrangeSamples=(\d+)'
        DwmMpoSystemEvents=Capture $analysis 'DwmMpo=(\d+)'
        EtwEventsLost=Capture $analysis 'EventsLost=(\d+)'
        EtlPath=$etl
    }
}
$rows|Export-Csv -LiteralPath $out -NoTypeInformation -Encoding UTF8
$rows|Format-Table Mode,WheelMessages,CpuMeanPercent,UiThreadOnCpuMs,AppPresent,PresentIntervalP99Ms,PresentGapsAtLeast75Ms,LongGapsWithLayoutStackSample -AutoSize
Write-Host "Comparison CSV: $out"
