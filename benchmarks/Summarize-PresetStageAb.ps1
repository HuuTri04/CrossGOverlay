$ErrorActionPreference='Stop'
$root=Join-Path $PSScriptRoot 'results'
$culture=[Globalization.CultureInfo]::InvariantCulture
function Number([object]$value){[double]::Parse([string]$value,$culture)}
function Quantile([double[]]$values,[double]$fraction){
    if(-not $values.Count){return $null}
    $ordered=@($values|Sort-Object)
    [math]::Round($ordered[[int][math]::Ceiling($ordered.Count*$fraction)-1],4)
}
function Median([double[]]$values){Quantile $values 0.5}
$files=Get-ChildItem $root -Filter 'preset-stage-*-run*-*.csv' -File|
    Where-Object Name -Match '^preset-stage-(baseline|no-stats|no-preview|no-bring)-run([1-3])-\d{8}T\d{6}Z\.csv$'|
    Sort-Object LastWriteTime
$selected=$files|Group-Object {if($_.Name -match '^preset-stage-(.+)-run([1-3])-'){"$($matches[1])-$($matches[2])"}}|
    ForEach-Object {$_.Group|Select-Object -Last 1}
$switches=[Collections.Generic.List[object]]::new()
foreach($file in $selected){
    $file.Name -match '^preset-stage-(.+)-run([1-3])-'|Out-Null
    $variant=$matches[1];$run=[int]$matches[2]
    foreach($group in (Import-Csv $file.FullName|Group-Object SwitchId)){
        $events=@{};foreach($event in $group.Group){$events[$event.Stage]=$event}
        function Delta([string]$a,[string]$b){
            if(-not $events.ContainsKey($a) -or -not $events.ContainsKey($b)){return $null}
            [math]::Round((Number $events[$b].ElapsedMs)-(Number $events[$a].ElapsedMs),6)
        }
        $switches.Add([pscustomobject]@{
            Variant=$variant;Run=$run;SwitchId=[int]$group.Name
            T1ToT2StartMs=Delta 'T1.RequestReceived' 'T2.SetActiveStart'
            T2StartToT3Ms=Delta 'T2.SetActiveStart' 'T3.BeforeActiveChanged'
            T3ToT2EndMs=Delta 'T3.BeforeActiveChanged' 'T2.SetActiveEnd'
            BootstrapperMs=Delta 'T4a.Bootstrapper.Start' 'T4a.Bootstrapper.End'
            SettingsVmMs=Delta 'T4b.SettingsViewModel.Start' 'T4b.SettingsViewModel.End'
            StatsMs=Delta 'T4c.StatsService.Start' 'T4c.StatsService.End'
            BringQueueMs=Delta 'T4d.BringQueued' 'T4d.BringStart'
            BringExecutionMs=Delta 'T4d.BringStart' 'T4d.BringEnd'
            T2EndToT5Ms=Delta 'T2.SetActiveEnd' 'T5.OverlayOnRender'
            T1ToT5Ms=Delta 'T1.RequestReceived' 'T5.OverlayOnRender'
            Rendered=([bool]$events.ContainsKey('T5.OverlayOnRender'))
            StageCsv=$file.FullName
        })
    }
}
$raw=Join-Path $root 'preset-stage-ab-switches-20261029.csv'
$switches|Export-Csv -LiteralPath $raw -NoTypeInformation -Encoding UTF8
$perRun=foreach($group in ($switches|Group-Object Variant,Run)){
    $rows=@($group.Group);$first=$rows[0]
    $rendered=@($rows|Where-Object Rendered)
    $get={param($name) [double[]]@($rows|ForEach-Object {if($null -ne $_.$name){[double]$_.$name}})}
    $lat=&$get 'T1ToT5Ms';$set=&$get 'T3ToT2EndMs';$boot=&$get 'BootstrapperMs'
    $settings=&$get 'SettingsVmMs';$stats=&$get 'StatsMs';$bring=&$get 'BringQueueMs'
    $request=&$get 'T1ToT2StartMs';$core=&$get 'T2StartToT3Ms';$render=&$get 'T2EndToT5Ms'
    [pscustomobject]@{
        Variant=$first.Variant;Run=$first.Run;Switches=$rows.Count;Rendered=$rendered.Count
        T1ToT5P50Ms=Quantile $lat .5;T1ToT5P95Ms=Quantile $lat .95;T1ToT5P99Ms=Quantile $lat .99
        T3ToT2EndP50Ms=Quantile $set .5;T3ToT2EndP95Ms=Quantile $set .95
        T1ToT2StartP50Ms=Quantile $request .5;T2StartToT3P50Ms=Quantile $core .5
        T2EndToT5P50Ms=Quantile $render .5
        BootstrapperP50Ms=Quantile $boot .5;SettingsVmP50Ms=Quantile $settings .5
        StatsP50Ms=Quantile $stats .5;BringQueueP50Ms=Quantile $bring .5
        RenderBeforeSetActiveEnd=@($rows|Where-Object {$null -ne $_.T2EndToT5Ms -and $_.T2EndToT5Ms -lt 0}).Count
    }
}
$runCsv=Join-Path $root 'preset-stage-ab-runs-20261029.csv'
$perRun|Export-Csv -LiteralPath $runCsv -NoTypeInformation -Encoding UTF8
$baseline=@($perRun|Where-Object Variant -eq baseline)
$baselineP50=Median ([double[]]@($baseline|ForEach-Object {[double]$_.T1ToT5P50Ms}))
$ab=foreach($group in ($perRun|Group-Object Variant)){
    $rows=@($group.Group)
    $p50=Median ([double[]]@($rows|ForEach-Object {[double]$_.T1ToT5P50Ms}))
    $p95=Median ([double[]]@($rows|ForEach-Object {[double]$_.T1ToT5P95Ms}))
    [pscustomobject]@{
        Variant=$group.Name;Runs=$rows.Count;SwitchesTotal=($rows|Measure-Object Switches -Sum).Sum
        RenderedTotal=($rows|Measure-Object Rendered -Sum).Sum
        MedianOfRunT1ToT5P50Ms=$p50;MedianOfRunT1ToT5P95Ms=$p95
        DeltaP50FromBaselineMs=if($null -ne $baselineP50){[math]::Round($p50-$baselineP50,4)}else{$null}
        MedianOfRunT3ToT2EndP50Ms=Median ([double[]]@($rows|ForEach-Object {[double]$_.T3ToT2EndP50Ms}))
        MedianOfRunT1ToT2StartP50Ms=Median ([double[]]@($rows|ForEach-Object {[double]$_.T1ToT2StartP50Ms}))
        MedianOfRunT2StartToT3P50Ms=Median ([double[]]@($rows|ForEach-Object {[double]$_.T2StartToT3P50Ms}))
        MedianOfRunT2EndToT5P50Ms=Median ([double[]]@($rows|ForEach-Object {[double]$_.T2EndToT5P50Ms}))
        MedianOfRunBootstrapperP50Ms=Median ([double[]]@($rows|ForEach-Object {[double]$_.BootstrapperP50Ms}))
        MedianOfRunSettingsVmP50Ms=Median ([double[]]@($rows|ForEach-Object {[double]$_.SettingsVmP50Ms}))
        MedianOfRunStatsP50Ms=Median ([double[]]@($rows|Where-Object {$null -ne $_.StatsP50Ms}|ForEach-Object {[double]$_.StatsP50Ms}))
        MedianOfRunBringQueueP50Ms=Median ([double[]]@($rows|Where-Object {$null -ne $_.BringQueueP50Ms}|ForEach-Object {[double]$_.BringQueueP50Ms}))
        RenderBeforeSetActiveEndTotal=($rows|Measure-Object RenderBeforeSetActiveEnd -Sum).Sum
    }
}
$abCsv=Join-Path $root 'preset-stage-ab-summary-20261029.csv'
$ab|Export-Csv -LiteralPath $abCsv -NoTypeInformation -Encoding UTF8
$ab|Format-Table Variant,Runs,SwitchesTotal,RenderedTotal,MedianOfRunT1ToT5P50Ms,MedianOfRunT1ToT5P95Ms,DeltaP50FromBaselineMs -AutoSize
Write-Host "Raw=$raw`nRuns=$runCsv`nSummary=$abCsv"
