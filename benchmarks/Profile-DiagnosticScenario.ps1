<#
.SYNOPSIS
    Isolated Release x64 probe for internal preset commands or repeated WPF toasts.
    Never attaches to a game or writes the user's app data.
#>
param(
    [ValidateSet('preset','toast')][string]$Scenario,
    [ValidateRange(2,5000)][int]$Count=1000,
    [ValidateRange(0,300)][int]$SettlingSeconds=30,
    [ValidateSet('normal','no-shadow')][string]$ToastVariant='normal'
)
$ErrorActionPreference='Stop'
if($Scenario -eq 'toast' -and $Count -gt 50){throw 'Toast probe limit is 50 cycles.'}
$repo=Split-Path $PSScriptRoot -Parent
$results=Join-Path $PSScriptRoot 'results'
$data=Join-Path $results 'profiling-app-data\effects\background'
$control=Join-Path $data 'diagnostics'
$exe=Join-Path $repo 'src\CrosshairOverlay\bin\x64\Release\net8.0-windows\win-x64\CrossGOverlay.exe'
$vmmap=Join-Path $PSScriptRoot 'tools\VMMap\vmmap64.exe'
$gdiView=Join-Path $PSScriptRoot 'tools\GDIView\GDIView.exe'
$raw=Join-Path $results 'vmmap'
if(-not(Test-Path (Join-Path $data 'settings.json'))){throw 'Run Prepare-EffectFixtures.ps1 first.'}
if(-not(Test-Path $exe) -or -not(Test-Path $vmmap) -or -not(Test-Path $gdiView)){
    throw 'Release x64 executable, VMMap and GDIView are required.'
}
if(Get-Process CrossGOverlay -ErrorAction SilentlyContinue){throw 'Close existing CrossGOverlay first.'}
if(@(Get-Process -ErrorAction SilentlyContinue|Where-Object ProcessName -Match '^DeltaForce').Count){
    throw 'App-only diagnostics will not run while Delta Force is present.'
}
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class ScenarioNative {
    [DllImport("user32.dll")] public static extern uint GetGuiResources(IntPtr process, uint flags);
}
'@
function Wait-File([string]$path,[int]$seconds){
    $sw=[Diagnostics.Stopwatch]::StartNew()
    while($sw.Elapsed.TotalSeconds -lt $seconds){
        if(Test-Path -LiteralPath (Join-Path $control 'error.txt')){
            throw (Get-Content -LiteralPath (Join-Path $control 'error.txt') -Raw)
        }
        if(Test-Path -LiteralPath $path){return}
        if($proc.HasExited){throw "App exited before marker $path"}
        Start-Sleep -Milliseconds 100
    }
    throw "Timed out waiting for $path"
}
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
function Get-GpuShared([int]$pidValue){
    try{
        $paths=@((Get-Counter -ListSet 'GPU Process Memory' -ErrorAction Stop).PathsWithInstances|
            Where-Object {$_ -match "pid_${pidValue}_" -and $_ -match '\\Shared Usage$'})
        if(-not $paths.Count){return $null}
        $sum=0.0
        foreach($sample in (Get-Counter -Counter $paths -MaxSamples 1 -ErrorAction Stop).CounterSamples){
            $sum+=$sample.CookedValue
        }
        return [math]::Round($sum/1MB,3)
    }catch{return $null}
}
function Snapshot([Diagnostics.Process]$process,[string]$stage){
    $vmPath=Join-Path $raw "$stamp-$Scenario-$stage.csv"
    $v=Start-Process -FilePath $vmmap -ArgumentList @('-accepteula','-p',"$($process.Id)",$vmPath) -WindowStyle Hidden -PassThru -Wait
    if($v.ExitCode -ne 0 -or -not(Test-Path $vmPath)){throw "VMMap failed at $stage"}
    $gdiPath=Join-Path $results "gdiview-$stamp-$stage.tmp.csv"
    try{
        $g=Start-Process -FilePath $gdiView -ArgumentList @('/scomma',$gdiPath) -WindowStyle Hidden -PassThru -Wait
        if($g.ExitCode -ne 0 -or -not(Test-Path $gdiPath)){throw "GDIView failed at $stage"}
        $columns=@('ProcessId','ProcessName','ProcessPath','ProcessUser','Pen','ExtPen',
            'Brush','Bitmap','Font','Palette','Region','DC','MetafileDC',
            'EnhancedMetafileDC','OtherGdi','GdiTotal','AllGdi')
        $gdi=Get-Content -LiteralPath $gdiPath|ConvertFrom-Csv -Header $columns|
            Where-Object {$_.ProcessId -eq [string]$process.Id}|Select-Object -First 1
        if(-not $gdi){throw "GDIView did not list PID $($process.Id)"}
    }finally{Remove-Item -LiteralPath $gdiPath -ErrorAction SilentlyContinue}
    $process.Refresh()
    [pscustomobject]@{
        Scenario=$Scenario;Count=$Count;Stage=$stage;TimestampUtc=[DateTimeOffset]::UtcNow.ToString('o')
        Pid=$process.Id;VMMapPrivateMB=Read-Private $vmPath 'Total'
        VMMapPrivateDataMB=Read-Private $vmPath 'Private Data'
        VMMapManagedHeapMB=Read-Private $vmPath 'Managed Heap'
        ProcessPrivateMB=[math]::Round($process.PrivateMemorySize64/1MB,3)
        WorkingSetMB=[math]::Round($process.WorkingSet64/1MB,3)
        GpuSharedMB=Get-GpuShared $process.Id
        GdiHandles=[ScenarioNative]::GetGuiResources($process.Handle,0)
        UserHandles=[ScenarioNative]::GetGuiResources($process.Handle,1)
        Pen=$gdi.Pen;Brush=$gdi.Brush;Bitmap=$gdi.Bitmap;Font=$gdi.Font
        Region=$gdi.Region;DC=$gdi.DC;AllGdi=$gdi.AllGdi
    }
}
$stamp=[DateTimeOffset]::UtcNow.ToString('yyyyMMddTHHmmssZ')
$prefix=Join-Path $results "diagnostic-$Scenario-$stamp"
New-Item -ItemType Directory -Path $control,$raw -Force|Out-Null
foreach($name in @('ready.txt','start.signal','progress.txt','done.txt','error.txt')){
    Remove-Item -LiteralPath (Join-Path $control $name) -ErrorAction SilentlyContinue
}
$env:CROSSGOVERLAY_BENCHMARK_ROOT=$data
$env:CROSSGOVERLAY_BENCHMARK_SCENARIO=$Scenario
$env:CROSSGOVERLAY_BENCHMARK_COUNT=[string]$Count
if($Scenario -eq 'toast'){$env:CROSSGOVERLAY_BENCHMARK_TOAST_VARIANT=$ToastVariant}
$snapshots=[Collections.Generic.List[object]]::new()
$checkpoints=[Collections.Generic.List[object]]::new()
$proc=Start-Process -FilePath $exe -PassThru
try{
    Wait-File (Join-Path $control 'ready.txt') 30
    Start-Sleep -Seconds 5
    $snapshots.Add((Snapshot $proc 'before'))
    Set-Content -LiteralPath (Join-Path $control 'start.signal') -Value 'go' -Encoding ascii
    $last=0
    $deadline=[DateTime]::UtcNow.AddSeconds([math]::Max(90,$Count*0.4+30))
    while(-not(Test-Path (Join-Path $control 'done.txt')) -and [DateTime]::UtcNow -lt $deadline){
        if(Test-Path (Join-Path $control 'error.txt')){
            throw (Get-Content (Join-Path $control 'error.txt') -Raw)
        }
        if($proc.HasExited){throw 'App exited during diagnostic scenario.'}
        $progressPath=Join-Path $control 'progress.txt'
        $current=0
        if(Test-Path $progressPath){
            [int]::TryParse((Get-Content -LiteralPath $progressPath -Raw).Trim(),[ref]$current)|Out-Null
        }
        if($current -ge $last+100 -or ($Scenario -eq 'toast' -and $current -ge $last+10)){
            $proc.Refresh()
            $checkpoints.Add([pscustomobject]@{
                Scenario=$Scenario;SwitchOrToast=$current
                GdiHandles=[ScenarioNative]::GetGuiResources($proc.Handle,0)
                UserHandles=[ScenarioNative]::GetGuiResources($proc.Handle,1)
                ProcessPrivateMB=[math]::Round($proc.PrivateMemorySize64/1MB,3)
                GpuSharedMB=Get-GpuShared $proc.Id
            })
            $last=$current
        }
        Start-Sleep -Milliseconds 250
    }
    Wait-File (Join-Path $control 'done.txt') 5
    $completed=[int](Get-Content -LiteralPath (Join-Path $control 'done.txt') -Raw).Trim()
    if($completed -ne $Count){throw "Completed $completed of $Count operations."}
    $snapshots.Add((Snapshot $proc 'after'))
    if($SettlingSeconds -gt 0){
        Start-Sleep -Seconds $SettlingSeconds
        $snapshots.Add((Snapshot $proc 'settled'))
    }
}finally{
    if($snapshots.Count){$snapshots|Export-Csv ($prefix+'-snapshots.csv') -NoTypeInformation -Encoding UTF8}
    if($checkpoints.Count){$checkpoints|Export-Csv ($prefix+'-checkpoints.csv') -NoTypeInformation -Encoding UTF8}
    if(-not $proc.HasExited){$proc.Kill();$proc.WaitForExit(5000)|Out-Null}
    $proc.Dispose()
}
[pscustomobject]@{
    Scenario=$Scenario;Count=$Count;SettlingSeconds=$SettlingSeconds
    ToastVariant=if($Scenario -eq 'toast'){$ToastVariant}else{'n/a'}
    ExeSha256=(Get-FileHash $exe -Algorithm SHA256).Hash
    ManagedDllSha256=(Get-FileHash (Join-Path (Split-Path $exe) 'CrossGOverlay.dll') -Algorithm SHA256).Hash
    Note='Benchmark-only app data and internal command path. GPU counter covers entire process, not one HWND.'
}|ConvertTo-Json|Set-Content ($prefix+'-metadata.json') -Encoding UTF8
Write-Host "[Profiler] Results prefix: $prefix"
