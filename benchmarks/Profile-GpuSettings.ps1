<# Same-process A/B GPU Process Memory: Settings visible, hidden, visible. No game process. #>
param([ValidateRange(3,30)][int]$SamplesPerStage=5,
      [ValidateRange(1,10)][int]$SampleSeconds=2,
      [ValidateRange(0,600)][int]$WarmupSeconds=60,
      [switch]$DisableTrayToast)
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
$results=Join-Path $PSScriptRoot 'results'
$data=Join-Path $results 'profiling-app-data\effects\background'
$exe=Join-Path $repo 'src\CrosshairOverlay\bin\x64\Release\net8.0-windows\win-x64\CrossGOverlay.exe'
$vmmap=Join-Path $PSScriptRoot 'tools\VMMap\vmmap64.exe'
if(-not(Test-Path (Join-Path $data 'settings.json'))){throw 'Run Prepare-EffectFixtures.ps1 first.'}
if(Get-Process CrossGOverlay -ErrorAction SilentlyContinue){throw 'Close existing CrossGOverlay first.'}
if(@(Get-Process -ErrorAction SilentlyContinue|Where-Object ProcessName -Match '^DeltaForce').Count){
    throw 'App-only GPU test will not run while Delta Force is present.'
}
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class GpuProfileNative {
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll", SetLastError=true)] public static extern bool PostMessage(IntPtr hwnd,uint msg,IntPtr wp,IntPtr lp);
    [DllImport("user32.dll")] public static extern uint GetGuiResources(IntPtr process,uint flags);
}
'@
function Wait-Visibility([IntPtr]$hwnd,[bool]$visible){
    $watch=[Diagnostics.Stopwatch]::StartNew()
    while($watch.Elapsed.TotalSeconds -lt 20){
        if([GpuProfileNative]::IsWindowVisible($hwnd) -eq $visible){return}
        Start-Sleep -Milliseconds 100
    }
    throw "Settings visibility did not become $visible."
}
function Sample-Stage([Diagnostics.Process]$proc,[string]$stage){
    for($i=1;$i -le $SamplesPerStage;$i++){
        Start-Sleep -Seconds $SampleSeconds
        $proc.Refresh()
        if($proc.HasExited){throw "App exited during $stage"}
        $paths=@((Get-Counter -ListSet 'GPU Process Memory' -ErrorAction Stop).PathsWithInstances|
            Where-Object {$_ -match "pid_$($proc.Id)_" -and $_ -match '\\(Shared Usage|Dedicated Usage)$'})
        if(-not $paths.Count){throw "GPU Process Memory counters not found for PID $($proc.Id)"}
        $shared=$null;$dedicated=$null
        foreach($counter in (Get-Counter -Counter $paths -MaxSamples 1 -ErrorAction Stop).CounterSamples){
            if($counter.Path -match 'Shared Usage$'){$shared=[math]::Round($counter.CookedValue/1MB,3)}
            elseif($counter.Path -match 'Dedicated Usage$'){$dedicated=[math]::Round($counter.CookedValue/1MB,3)}
        }
        $samples.Add([pscustomobject]@{
            Stage=$stage;Sample=$i;TimestampUtc=[DateTimeOffset]::UtcNow.ToString('o');Pid=$proc.Id;
            SettingsVisible=[GpuProfileNative]::IsWindowVisible($settingsHwnd);
            SharedMB=$shared;DedicatedMB=$dedicated;
            ProcessPrivateMB=[math]::Round($proc.PrivateMemorySize64/1MB,3);
            GdiHandles=[GpuProfileNative]::GetGuiResources($proc.Handle,0)
        })
    }
}
function Snapshot-VMMap([Diagnostics.Process]$proc,[string]$stage){
    if(-not(Test-Path -LiteralPath $vmmap)){return}
    $raw=Join-Path $results 'vmmap'
    New-Item -ItemType Directory -Path $raw -Force | Out-Null
    $path=Join-Path $raw "gpu-settings-$stamp-$stage.csv"
    $tool=Start-Process -FilePath $vmmap -ArgumentList @('-accepteula','-p',"$($proc.Id)",$path) -WindowStyle Hidden -PassThru -Wait
    if($tool.ExitCode -ne 0 -or -not(Test-Path -LiteralPath $path)){throw "VMMap failed at $stage"}
}
$stamp=[DateTimeOffset]::UtcNow.ToString('yyyyMMddTHHmmssZ')
$prefix=Join-Path $results "gpu-settings-$stamp"
$env:CROSSGOVERLAY_BENCHMARK_ROOT=$data
if($DisableTrayToast){$env:CROSSGOVERLAY_BENCHMARK_NO_TRAY_TOAST='1'}
$samples=[Collections.Generic.List[object]]::new()
$proc=Start-Process -FilePath $exe -PassThru
try{
    Start-Sleep -Seconds (8+$WarmupSeconds)
    $proc.Refresh()
    if($proc.HasExited -or $proc.MainWindowHandle -eq [IntPtr]::Zero){throw 'Settings did not open.'}
    $settingsHwnd=$proc.MainWindowHandle
    Wait-Visibility $settingsHwnd $true
    Sample-Stage $proc 'settings-visible-before'
    Snapshot-VMMap $proc 'visible-before'
    if(-not [GpuProfileNative]::PostMessage($settingsHwnd,0x0010,[IntPtr]::Zero,[IntPtr]::Zero)){
        throw 'WM_CLOSE failed.'
    }
    Wait-Visibility $settingsHwnd $false
    Start-Sleep -Seconds 30
    Sample-Stage $proc 'settings-hidden-30s'
    Snapshot-VMMap $proc 'hidden'
    $signal=[Threading.EventWaitHandle]::OpenExisting('Global\CrossGOverlay_Activate')
    try{$signal.Set()|Out-Null}finally{$signal.Dispose()}
    Wait-Visibility $settingsHwnd $true
    Start-Sleep -Seconds 5
    Sample-Stage $proc 'settings-reopened'
    Snapshot-VMMap $proc 'reopened'
}finally{
    if($samples.Count){$samples|Export-Csv ($prefix+'-samples.csv') -NoTypeInformation -Encoding UTF8}
    if(-not $proc.HasExited){$proc.Kill();$proc.WaitForExit(5000)|Out-Null}
    $proc.Dispose()
}
[pscustomobject]@{
    ExeSha256=(Get-FileHash $exe -Algorithm SHA256).Hash;
    ManagedDllSha256=(Get-FileHash (Join-Path (Split-Path $exe) 'CrossGOverlay.dll') -Algorithm SHA256).Hash;
    SamplesPerStage=$SamplesPerStage;SampleSeconds=$SampleSeconds;WarmupSeconds=$WarmupSeconds;
    TrayToastDisabled=[bool]$DisableTrayToast;
    Metric='GPU Process Memory, entire process; not per-HWND or DWM compositing workload.'
}|ConvertTo-Json|Set-Content ($prefix+'-metadata.json') -Encoding UTF8
Write-Host "[Profiler] Results prefix: $prefix"
