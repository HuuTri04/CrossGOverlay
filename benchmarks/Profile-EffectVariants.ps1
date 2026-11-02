<# App-only effect measurements. No game process is inspected. #>
param(
    [ValidateSet('all','standard','background','background-60','background-144','background-contrast','gif')]
    [string]$Variant='all',
    [ValidateRange(1,10)][int]$Runs=3,
    [ValidateRange(10,7200)][int]$DurationSeconds=30,
    [ValidateRange(1,60)][int]$SampleSeconds=1
)
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
$results=Join-Path $PSScriptRoot 'results'
$effectRoot=Join-Path $results 'profiling-app-data\effects'
$raw=Join-Path $results 'vmmap'
$exe=Join-Path $repo 'src\CrosshairOverlay\bin\x64\Release\net8.0-windows\win-x64\CrossGOverlay.exe'
$vmmap=Join-Path $PSScriptRoot 'tools\VMMap\vmmap64.exe'
if(-not(Test-Path $exe) -or -not(Test-Path $vmmap)){throw 'Release app or VMMap missing.'}
if(Get-Process CrossGOverlay -ErrorAction SilentlyContinue){throw 'Close existing CrossGOverlay first.'}
if(@(Get-Process -ErrorAction SilentlyContinue|Where-Object ProcessName -Match '^DeltaForce').Count){
    throw 'App-only effect profile will not run while Delta Force is present.'
}
$variants=if($Variant -eq 'all'){@('standard','background','background-contrast','gif')}else{@($Variant)}
$runId=[DateTimeOffset]::UtcNow.ToString('yyyyMMddTHHmmssZ')
$prefix=Join-Path $results "effect-profile-$runId"
$samples=[Collections.Generic.List[object]]::new()
$snapshots=[Collections.Generic.List[object]]::new()
$gpuSamples=[Collections.Generic.List[object]]::new()
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class EffectProfileNative {
    public delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr param);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc callback, IntPtr param);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] public static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern uint GetGuiResources(IntPtr process, uint flags);
    public static bool OverlayVisible(int wantedPid) {
        bool found = false;
        EnumWindows((hwnd, _) => {
            uint pid; GetWindowThreadProcessId(hwnd, out pid);
            if (pid != wantedPid || !IsWindowVisible(hwnd)) return true;
            long style=GetWindowLongPtr(hwnd,-20).ToInt64();
            if ((style & 0x80020)==0x80020) {found=true; return false;}
            return true;
        },IntPtr.Zero);
        return found;
    }
}
'@
function Read-VMMapPrivate([string]$path,[string]$type){
    $lines=Get-Content -LiteralPath $path
    $header=-1
    for($i=0;$i -lt $lines.Count;$i++){if($lines[$i] -like '"Type","Size"*'){$header=$i;break}}
    if($header -lt 0){throw "VMMap header missing in $path"}
    $block=[Collections.Generic.List[string]]::new()
    $block.Add($lines[$header])
    for($i=$header+1;$i -lt $lines.Count;$i++){
        if(-not $lines[$i].Trim()){continue}
        if($lines[$i] -like '"Address","Type"*'){break}
        $block.Add($lines[$i])
    }
    $row=$block|ConvertFrom-Csv|Where-Object Type -EQ $type|Select-Object -First 1
    if(-not $row){throw "VMMap $type missing"}
    return [math]::Round([double]::Parse($row.Private,[Globalization.NumberStyles]::AllowThousands,
        [Globalization.CultureInfo]::InvariantCulture)/1024,3)
}
function Take-Snapshot([Diagnostics.Process]$proc,[string]$name,[int]$run,[string]$stage){
    $path=Join-Path $raw "$runId-$name-$run-$stage.csv"
    $v=Start-Process -FilePath $vmmap -ArgumentList @('-accepteula','-p',"$($proc.Id)",$path) -WindowStyle Hidden -PassThru -Wait
    if($v.ExitCode -ne 0 -or -not(Test-Path $path)){throw "VMMap failed for $name $run $stage"}
    $proc.Refresh()
    $snapshots.Add([pscustomobject]@{
        Variant=$name;Run=$run;Stage=$stage;TimestampUtc=[DateTimeOffset]::UtcNow.ToString('o')
        VMMapPrivateMB=Read-VMMapPrivate $path 'Total'
        PrivateDataMB=Read-VMMapPrivate $path 'Private Data'
        ManagedHeapPrivateMB=Read-VMMapPrivate $path 'Managed Heap'
        NativeHeapPrivateMB=Read-VMMapPrivate $path 'Heap'
        ProcessPrivateMB=[math]::Round($proc.PrivateMemorySize64/1MB,3)
        GdiHandles=[EffectProfileNative]::GetGuiResources($proc.Handle,0)
        UserHandles=[EffectProfileNative]::GetGuiResources($proc.Handle,1)
        OverlayVisible=[EffectProfileNative]::OverlayVisible($proc.Id)
    })
}
function Select-General([Diagnostics.Process]$proc){
    $proc.Refresh()
    if($proc.MainWindowHandle -eq [IntPtr]::Zero){throw 'Settings HWND missing.'}
    $element=[System.Windows.Automation.AutomationElement]::FromHandle($proc.MainWindowHandle)
    $filter=New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::TabItem)
    $tab=$element.FindAll([System.Windows.Automation.TreeScope]::Descendants,$filter)|
        Where-Object {$_.Current.Name -eq 'General'}|Select-Object -First 1
    if(-not $tab){throw 'General tab not found.'}
    $pattern=[System.Windows.Automation.SelectionItemPattern]$tab.GetCurrentPattern(
        [System.Windows.Automation.SelectionItemPattern]::Pattern)
    $pattern.Select()
}
foreach($name in $variants){
    $dir=Join-Path $effectRoot $name
    if(-not(Test-Path (Join-Path $dir 'settings.json'))){
        throw "Fixture missing for $name; run Prepare-EffectFixtures.ps1."
    }
    for($run=1;$run -le $Runs;$run++){
        $env:CROSSGOVERLAY_BENCHMARK_ROOT=$dir
        $proc=Start-Process -FilePath $exe -PassThru
        try{
            Start-Sleep -Seconds 7
            $proc.Refresh()
            if($proc.HasExited){throw "$name run ${run}: app exited."}
            if($name -like 'background*'){Select-General $proc;Start-Sleep -Seconds 5}
            if(-not [EffectProfileNative]::OverlayVisible($proc.Id)){
                throw "$name run ${run}: overlay not visible."
            }
            Take-Snapshot $proc $name $run 'start'
            $clock=[Diagnostics.Stopwatch]::StartNew()
            $proc.Refresh()
            $previousCpu=$proc.TotalProcessorTime.TotalSeconds
            $previousTime=$clock.Elapsed.TotalSeconds
            $nextCheckpointSeconds=60
            while($clock.Elapsed.TotalSeconds -lt $DurationSeconds){
                Start-Sleep -Seconds ([math]::Min($SampleSeconds,
                    [math]::Max(1,[math]::Ceiling($DurationSeconds-$clock.Elapsed.TotalSeconds))))
                $proc.Refresh()
                if($proc.HasExited){throw "$name run ${run}: app exited during sampling."}
                $now=$clock.Elapsed.TotalSeconds
                $cpu=$proc.TotalProcessorTime.TotalSeconds
                $percent=100*($cpu-$previousCpu)/(($now-$previousTime)*[Environment]::ProcessorCount)
                $previousCpu=$cpu;$previousTime=$now
                $samples.Add([pscustomobject]@{
                    Variant=$name;Run=$run;ElapsedSeconds=[math]::Round($now,2)
                    TimestampUtc=[DateTimeOffset]::UtcNow.ToString('o')
                    CpuPercent=[math]::Round([math]::Max(0,$percent),5)
                    PrivateMB=[math]::Round($proc.PrivateMemorySize64/1MB,3)
                    WorkingSetMB=[math]::Round($proc.WorkingSet64/1MB,3)
                    GdiHandles=[EffectProfileNative]::GetGuiResources($proc.Handle,0)
                    UserHandles=[EffectProfileNative]::GetGuiResources($proc.Handle,1)
                })
                if($now -ge $nextCheckpointSeconds){
                    # Keep measured samples if the profiling shell is interrupted.
                    $samples|Export-Csv ($prefix+'-samples.csv') -NoTypeInformation -Encoding UTF8
                    $nextCheckpointSeconds += 60
                }
            }
            try {
                $gpuPaths=@((Get-Counter -ListSet 'GPU Process Memory' -ErrorAction Stop).PathsWithInstances|
                    Where-Object {$_ -match "pid_$($proc.Id)_" -and $_ -match '\\(Shared Usage|Dedicated Usage)$'})
                if($gpuPaths.Count){
                    foreach($snapshot in (Get-Counter -Counter $gpuPaths -SampleInterval 2 -MaxSamples 5 -ErrorAction Stop)){
                        foreach($counter in $snapshot.CounterSamples){
                            $gpuSamples.Add([pscustomobject]@{
                                Variant=$name;Run=$run;TimestampUtc=$snapshot.Timestamp.ToUniversalTime().ToString('o')
                                UsageType=if($counter.Path -match 'Dedicated Usage$'){'Dedicated'}else{'Shared'}
                                Megabytes=[math]::Round($counter.CookedValue/1MB,4)
                                CounterPath=$counter.Path
                            })
                        }
                    }
                }
            } catch {
                Write-Warning "GPU Process Memory counter unavailable for $name run $run."
            }
            if($DurationSeconds -ge 300){Take-Snapshot $proc $name $run 'end'}
            Write-Host "[Profiler] $name run $run/$Runs complete."
        }finally{
            if(-not $proc.HasExited){$proc.Kill();$proc.WaitForExit(5000)|Out-Null}
            $proc.Dispose()
            $samples|Export-Csv ($prefix+'-samples.csv') -NoTypeInformation -Encoding UTF8
            $snapshots|Export-Csv ($prefix+'-vmmap-summary.csv') -NoTypeInformation -Encoding UTF8
            if($gpuSamples.Count){
                $gpuSamples|Export-Csv ($prefix+'-gpu-memory.csv') -NoTypeInformation -Encoding UTF8
            }
        }
    }
}
[pscustomobject]@{
    RunId=$runId;Variants=$variants;Runs=$Runs;DurationSeconds=$DurationSeconds
    ExeSha256=(Get-FileHash $exe -Algorithm SHA256).Hash
    ManagedDllSha256=(Get-FileHash (Join-Path (Split-Path $exe) 'CrossGOverlay.dll') -Algorithm SHA256).Hash
    VMMapVersion=(Get-Item $vmmap).VersionInfo.FileVersion
}|ConvertTo-Json -Depth 4|Set-Content ($prefix+'-metadata.json') -Encoding UTF8
Write-Host "[Profiler] Results prefix: $prefix"
