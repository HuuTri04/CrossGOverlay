$ErrorActionPreference='Stop'
$root=Join-Path $PSScriptRoot 'results'
$selection=Import-Csv (Join-Path $root 'preset-switch-20261029T152927Z-selection-calls.csv')
$uiaGdi=Import-Csv (Join-Path $root 'preset-switch-20261029T152927Z-gdi-types.csv')
$internalGdi=Import-Csv (Join-Path $root 'diagnostic-preset-20261029T153709Z-snapshots.csv')
$first=([DateTimeOffset]::Parse($selection[0].TimestampUtc)).AddSeconds(-1)
$last=([DateTimeOffset]::Parse($selection[-1].TimestampUtc)).AddSeconds(1)
$cases=@(
    [pscustomobject]@{Mode='UIAutomation';Start=$first;End=$last;Etl='CrossGOverlay-PresetFanout-20261029T152926Z.etl';Before=[int]($uiaGdi|Where-Object Stage -eq before|Select-Object -First 1 -ExpandProperty Region);After=[int]($uiaGdi|Where-Object Stage -eq after|Select-Object -First 1 -ExpandProperty Region)},
    [pscustomobject]@{Mode='InternalCommand';Start=[DateTimeOffset]::Parse('2026-10-29T15:37:24.5000000Z');End=[DateTimeOffset]::Parse('2026-10-29T15:37:30.0000000Z');Etl='CrossGOverlay-PresetInternalFanout-20261029T153707Z.etl';Before=[int]($internalGdi|Where-Object Stage -eq before|Select-Object -First 1 -ExpandProperty Region);After=[int]($internalGdi|Where-Object Stage -eq after|Select-Object -First 1 -ExpandProperty Region)}
)
function Capture([string]$text,[string]$pattern){
    $match=[regex]::Match($text,$pattern)
    if(-not $match.Success){throw "Missing analyzer field: $pattern"}
    $match.Groups[1].Value
}
$latencies=@($selection|ForEach-Object {[double]$_.UiAutomationCallMs}|Sort-Object)
$rows=foreach($case in $cases){
    $etl=Join-Path $root "etl\$($case.Etl)"
    $analysis=(& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'Analyze-ScrollEtw.ps1') -EtlPath $etl -StartUtc ($case.Start.ToString('o')) -EndUtc ($case.End.ToString('o')) | Out-String)
    if($LASTEXITCODE -ne 0){throw "Analyzer failed: $($case.Mode)"}
    $interval=(Capture $analysis 'AppPresentIntervalP50,P95,P99Ms=([^\r\n]+)').Split(',')
    [pscustomobject]@{
        Mode=$case.Mode;Switches=100;WindowStartUtc=$case.Start.ToString('o');WindowSeconds=[math]::Round(($case.End-$case.Start).TotalSeconds,3)
        UiAutomationCallP50Ms=$(if($case.Mode -eq 'UIAutomation'){$latencies[[int][math]::Ceiling($latencies.Count*.5)-1]}else{$null})
        UiAutomationCallP95Ms=$(if($case.Mode -eq 'UIAutomation'){$latencies[[int][math]::Ceiling($latencies.Count*.95)-1]}else{$null})
        UiAutomationCallP99Ms=$(if($case.Mode -eq 'UIAutomation'){$latencies[[int][math]::Ceiling($latencies.Count*.99)-1]}else{$null})
        UiThreadOnCpuMs=Capture $analysis 'UiMessageThreadOnCpuMs=\d+:([0-9.]+)'
        AppPresent=Capture $analysis 'AppPresent=(\d+)'
        PresentIntervalP50Ms=$interval[0];PresentIntervalP95Ms=$interval[1];PresentIntervalP99Ms=$interval[2]
        MeasureStackSamples=Capture $analysis 'MeasureSamples=(\d+)'
        ArrangeStackSamples=Capture $analysis 'ArrangeSamples=(\d+)'
        PresentGapsAtLeast75Ms=Capture $analysis 'PresentGapsAtLeast75Ms=(\d+)'
        LongGapsWithLayoutStackSample=Capture $analysis 'WithLayoutStackSample=(\d+)'
        GdiRegionBefore=$case.Before;GdiRegionAfter=$case.After
        EtwEventsLost=Capture $analysis 'EventsLost=(\d+)'
        EtlPath=$etl
    }
}
$out=Join-Path $root 'preset-fanout-etw-20261029.csv'
$rows|Export-Csv -LiteralPath $out -NoTypeInformation -Encoding UTF8
$rows|Format-Table Mode,Switches,UiAutomationCallP50Ms,UiAutomationCallP99Ms,UiThreadOnCpuMs,AppPresent,PresentIntervalP99Ms,GdiRegionAfter -AutoSize
Write-Host "Fan-out CSV: $out"
