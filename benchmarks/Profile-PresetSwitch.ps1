<# UI Automation selects two real presets 500 times. No game or app internals are hooked. #>
param(
    [ValidateRange(2,5000)][int]$Switches=500,
    [ValidateRange(0,3600)][int]$SettlingSeconds=0
)
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
$results=Join-Path $PSScriptRoot 'results'
$data=Join-Path $results 'profiling-app-data\effects\background'
$exe=Join-Path $repo 'src\CrosshairOverlay\bin\x64\Release\net8.0-windows\win-x64\CrossGOverlay.exe'
$vmmap=Join-Path $PSScriptRoot 'tools\VMMap\vmmap64.exe'
$gdiView=Join-Path $PSScriptRoot 'tools\GDIView\GDIView.exe'
$raw=Join-Path $results 'vmmap'
if(-not(Test-Path (Join-Path $data 'settings.json'))){throw 'Run Prepare-EffectFixtures.ps1 first.'}
$presetNames=@(Get-ChildItem (Join-Path $data 'presets') -Filter '*.json' -File|
    ForEach-Object {(Get-Content $_.FullName -Raw -Encoding UTF8|ConvertFrom-Json).Name})
if($presetNames.Count -lt 2){throw 'Fixture needs at least two named presets.'}
if(Get-Process CrossGOverlay -ErrorAction SilentlyContinue){throw 'Close existing CrossGOverlay first.'}
if(@(Get-Process -ErrorAction SilentlyContinue|Where-Object ProcessName -Match '^DeltaForce').Count){
    throw 'App-only preset test will not run while Delta Force is present.'
}
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class PresetProfileNative {
    [DllImport("user32.dll")] public static extern uint GetGuiResources(IntPtr process, uint flags);
}
'@
function Read-Private([string]$path,[string]$type){
    $lines=Get-Content -LiteralPath $path
    $start=-1
    for($i=0;$i -lt $lines.Count;$i++){if($lines[$i] -like '"Type","Size"*'){$start=$i;break}}
    if($start -lt 0){throw "VMMap summary missing: $path"}
    $table=[Collections.Generic.List[string]]::new()
    $table.Add($lines[$start])
    for($i=$start+1;$i -lt $lines.Count;$i++){
        if($lines[$i] -like '"Address","Type"*'){break}
        if($lines[$i].Trim()){$table.Add($lines[$i])}
    }
    $row=$table|ConvertFrom-Csv|Where-Object Type -EQ $type|Select-Object -First 1
    if(-not $row){throw "VMMap category $type missing"}
    [math]::Round([double]::Parse($row.Private,[Globalization.NumberStyles]::AllowThousands,
        [Globalization.CultureInfo]::InvariantCulture)/1024,3)
}
function Snapshot([Diagnostics.Process]$p,[string]$stage){
    $path=Join-Path $raw "$stamp-preset-switch-$stage.csv"
    $v=Start-Process -FilePath $vmmap -ArgumentList @('-accepteula','-p',"$($p.Id)",$path) -WindowStyle Hidden -PassThru -Wait
    if($v.ExitCode -ne 0 -or -not(Test-Path $path)){throw "VMMap failed: $stage"}
    if(Test-Path -LiteralPath $gdiView){
        $gdiRaw=Join-Path $results "gdiview-$stamp-$stage.tmp.csv"
        try{
            $g=Start-Process -FilePath $gdiView -ArgumentList @('/scomma',$gdiRaw) -WindowStyle Hidden -PassThru -Wait
            if($g.ExitCode -ne 0 -or -not(Test-Path -LiteralPath $gdiRaw)){
                throw "GDIView failed: $stage"
            }
            $columns=@('ProcessId','ProcessName','ProcessPath','ProcessUser','Pen','ExtPen',
                'Brush','Bitmap','Font','Palette','Region','DC','MetafileDC',
                'EnhancedMetafileDC','OtherGdi','GdiTotal','AllGdi')
            $row=Get-Content -LiteralPath $gdiRaw | ConvertFrom-Csv -Header $columns |
                Where-Object { $_.ProcessId -eq [string]$p.Id } | Select-Object -First 1
            if(-not $row){throw "GDIView did not list process $($p.Id) at $stage"}
            $gdiTypes.Add([pscustomobject]@{
                Stage=$stage;Pid=$p.Id;Pen=$row.Pen;ExtPen=$row.ExtPen;
                Brush=$row.Brush;Bitmap=$row.Bitmap;Font=$row.Font;
                Palette=$row.Palette;Region=$row.Region;DC=$row.DC;
                MetafileDC=$row.MetafileDC;EnhancedMetafileDC=$row.EnhancedMetafileDC;
                OtherGdi=$row.OtherGdi;GdiTotal=$row.GdiTotal;AllGdi=$row.AllGdi
            })
        }finally{
            Remove-Item -LiteralPath $gdiRaw -ErrorAction SilentlyContinue
        }
    }
    $p.Refresh()
    $gpuShared=$null
    $gpuDedicated=$null
    try{
        $paths=@((Get-Counter -ListSet 'GPU Process Memory' -ErrorAction Stop).PathsWithInstances|
            Where-Object {$_ -match "pid_$($p.Id)_" -and $_ -match '\\(Shared Usage|Dedicated Usage)$'})
        if($paths.Count){
            foreach($sample in (Get-Counter -Counter $paths -MaxSamples 1 -ErrorAction Stop).CounterSamples){
                if($sample.Path -match 'Shared Usage$'){
                    $gpuShared=[math]::Round($sample.CookedValue/1MB,3)
                }elseif($sample.Path -match 'Dedicated Usage$'){
                    $gpuDedicated=[math]::Round($sample.CookedValue/1MB,3)
                }
            }
        }
    }catch{Write-Warning "GPU Process Memory counter unavailable at $stage."}
    [pscustomobject]@{
        Stage=$stage;TimestampUtc=[DateTimeOffset]::UtcNow.ToString('o')
        VMMapPrivateMB=Read-Private $path 'Total'
        ManagedHeapPrivateMB=Read-Private $path 'Managed Heap'
        ProcessPrivateMB=[math]::Round($p.PrivateMemorySize64/1MB,3)
        WorkingSetMB=[math]::Round($p.WorkingSet64/1MB,3)
        GdiHandles=[PresetProfileNative]::GetGuiResources($p.Handle,0)
        UserHandles=[PresetProfileNative]::GetGuiResources($p.Handle,1)
        GpuSharedMB=$gpuShared;GpuDedicatedMB=$gpuDedicated
    }
}
$stamp=[DateTimeOffset]::UtcNow.ToString('yyyyMMddTHHmmssZ')
$prefix=Join-Path $results "preset-switch-$stamp"
$env:CROSSGOVERLAY_BENCHMARK_ROOT=$data
$p=Start-Process -FilePath $exe -PassThru
$snapshots=[Collections.Generic.List[object]]::new()
$latencies=[Collections.Generic.List[object]]::new()
$checkpoints=[Collections.Generic.List[object]]::new()
$gdiTypes=[Collections.Generic.List[object]]::new()
try{
    Start-Sleep -Seconds 8
    $p.Refresh()
    if($p.HasExited -or $p.MainWindowHandle -eq [IntPtr]::Zero){throw 'Settings did not open.'}
    $window=[System.Windows.Automation.AutomationElement]::FromHandle($p.MainWindowHandle)
    $filter=New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::List)
    $lists=$window.FindAll([System.Windows.Automation.TreeScope]::Descendants,$filter)
    $items=$null
    foreach($list in $lists){
        $itemFilter=New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::ListItem)
        $found=$list.FindAll([System.Windows.Automation.TreeScope]::Children,$itemFilter)
        if($found.Count -ge 2 -and
            $presetNames -contains $found.Item(0).Current.Name -and
            $presetNames -contains $found.Item(1).Current.Name){$items=$found;break}
    }
    if(-not $items){throw 'Could not find two selectable presets.'}
    $snapshots.Add((Snapshot $p 'before'))
    for($i=1;$i -le $Switches;$i++){
        $index=$i%2
        $item=$items.Item($index)
        $select=[System.Windows.Automation.SelectionItemPattern]$item.GetCurrentPattern(
            [System.Windows.Automation.SelectionItemPattern]::Pattern)
        $timer=[Diagnostics.Stopwatch]::StartNew()
        $select.Select()
        $timer.Stop()
        if(-not $select.Current.IsSelected){throw "Preset selection $i was not applied."}
        $latencies.Add([pscustomobject]@{
            Switch=$i;Preset=$item.Current.Name;UiAutomationCallMs=[math]::Round($timer.Elapsed.TotalMilliseconds,4)
            TimestampUtc=[DateTimeOffset]::UtcNow.ToString('o')
        })
        if($i%100 -eq 0){
            $p.Refresh()
            $checkpoints.Add([pscustomobject]@{
                Switch=$i;GdiHandles=[PresetProfileNative]::GetGuiResources($p.Handle,0)
                UserHandles=[PresetProfileNative]::GetGuiResources($p.Handle,1)
                PrivateMB=[math]::Round($p.PrivateMemorySize64/1MB,3)
            })
            Write-Host "[Profiler] Preset selections $i/$Switches"
        }
    }
    Start-Sleep -Seconds 5
    $snapshots.Add((Snapshot $p 'after'))
    if($SettlingSeconds -gt 0){
        Start-Sleep -Seconds $SettlingSeconds
        $snapshots.Add((Snapshot $p 'settled'))
    }
}finally{
    if($latencies.Count){$latencies|Export-Csv ($prefix+'-selection-calls.csv') -NoTypeInformation -Encoding UTF8}
    if($checkpoints.Count){$checkpoints|Export-Csv ($prefix+'-checkpoints.csv') -NoTypeInformation -Encoding UTF8}
    if($snapshots.Count){$snapshots|Export-Csv ($prefix+'-vmmap-summary.csv') -NoTypeInformation -Encoding UTF8}
    if($gdiTypes.Count){$gdiTypes|Export-Csv ($prefix+'-gdi-types.csv') -NoTypeInformation -Encoding UTF8}
    if(-not $p.HasExited){$p.Kill();$p.WaitForExit(5000)|Out-Null}
    $p.Dispose()
}
[pscustomobject]@{
    SwitchesRequested=$Switches;SettlingSeconds=$SettlingSeconds
    ExeSha256=(Get-FileHash $exe -Algorithm SHA256).Hash
    ManagedDllSha256=(Get-FileHash (Join-Path (Split-Path $exe) 'CrossGOverlay.dll') -Algorithm SHA256).Hash
    Interpretation='UI Automation Select() duration; not overlay present latency.'
}|ConvertTo-Json|Set-Content ($prefix+'-metadata.json') -Encoding UTF8
Write-Host "[Profiler] Results prefix: $prefix"
