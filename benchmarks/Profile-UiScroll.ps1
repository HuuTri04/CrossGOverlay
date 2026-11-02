<# WPF Settings scrolling with a synthetic background, outside all games. #>
param(
    [ValidateSet(60,144)][int]$UiFps,
    [ValidateRange(30,300)][int]$DurationSeconds=120,
    [ValidateRange(1,10)][int]$Runs=3,
    [ValidateRange(0,10)][int]$TraceRuns=1
)
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
$results=Join-Path $PSScriptRoot 'results'
$data=Join-Path $results "profiling-app-data\effects\background-$UiFps"
$exe=Join-Path $repo 'src\CrosshairOverlay\bin\x64\Release\net8.0-windows\win-x64\CrossGOverlay.exe'
$etlRoot=Join-Path $results 'etl'
if(-not(Test-Path (Join-Path $data 'settings.json'))){throw 'Run Prepare-EffectFixtures.ps1 first.'}
if(Get-Process CrossGOverlay -ErrorAction SilentlyContinue){throw 'Close existing CrossGOverlay first.'}
if(@(Get-Process -ErrorAction SilentlyContinue|Where-Object ProcessName -Match '^DeltaForce').Count){
    throw 'UI scrolling benchmark will not run with Delta Force.'
}
New-Item -ItemType Directory -Path $etlRoot -Force|Out-Null
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class ScrollProfileNative {
    [DllImport("user32.dll",SetLastError=true)] public static extern bool PostMessage(IntPtr h,uint m,IntPtr w,IntPtr l);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x,int y);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out Point p);
    [StructLayout(LayoutKind.Sequential)] public struct Point {public int X,Y;}
}
'@
$stamp=[DateTimeOffset]::UtcNow.ToString('yyyyMMddTHHmmssZ')
$prefix=Join-Path $results "ui-scroll-$UiFps-$stamp"
$samples=[Collections.Generic.List[object]]::new()
$runResults=[Collections.Generic.List[object]]::new()
for($run=1;$run -le $Runs;$run++){
    $env:CROSSGOVERLAY_BENCHMARK_ROOT=$data
    $proc=Start-Process -FilePath $exe -PassThru
    $wprStarted=$false
    $runResult=$null
    $cursor=New-Object ScrollProfileNative+Point
    [ScrollProfileNative]::GetCursorPos([ref]$cursor)|Out-Null
    try{
        Start-Sleep -Seconds 8
        $proc.Refresh()
        if($proc.HasExited -or $proc.MainWindowHandle -eq [IntPtr]::Zero){
            throw "Settings not ready in UI run $run."
        }
        $window=[System.Windows.Automation.AutomationElement]::FromHandle($proc.MainWindowHandle)
        $tabFilter=New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::TabItem)
        $general=$window.FindAll([System.Windows.Automation.TreeScope]::Descendants,$tabFilter)|
            Where-Object {$_.Current.Name -eq 'General'}|Select-Object -First 1
        if(-not $general){throw 'General tab not found.'}
        $select=[System.Windows.Automation.SelectionItemPattern]$general.GetCurrentPattern(
            [System.Windows.Automation.SelectionItemPattern]::Pattern)
        $select.Select()
        Start-Sleep -Seconds 3
        $scrollFilter=New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::IsScrollPatternAvailableProperty,$true)
        $scrollables=$window.FindAll([System.Windows.Automation.TreeScope]::Descendants,$scrollFilter)
        $viewer=$null
        foreach($candidate in $scrollables){
            $pattern=[System.Windows.Automation.ScrollPattern]$candidate.GetCurrentPattern(
                [System.Windows.Automation.ScrollPattern]::Pattern)
            if($pattern.Current.VerticallyScrollable -and $pattern.Current.VerticalViewSize -lt 100){
                $viewer=$candidate;break
            }
        }
        if(-not $viewer){throw 'No vertical ScrollViewer exposed by UI Automation.'}
        $scroll=[System.Windows.Automation.ScrollPattern]$viewer.GetCurrentPattern(
            [System.Windows.Automation.ScrollPattern]::Pattern)
        $bounds=$viewer.Current.BoundingRectangle
        $x=[int]($bounds.Left+$bounds.Width*0.45)
        $y=[int]($bounds.Top+$bounds.Height*0.5)
        [ScrollProfileNative]::SetCursorPos($x,$y)|Out-Null
        $initialPercent=$scroll.Current.VerticalScrollPercent
        # WPR's built-in CPU/GPU/DesktopComposition profiles provide an ETL for
        # later frame analysis. This script does not claim UI frame time from CPU samples.
        $etl=Join-Path $etlRoot "ui-scroll-$UiFps-$stamp-run$run.etl"
        if($run -le $TraceRuns){
            try{
                & wpr -start CPU.light -start GPU.light -start DesktopComposition.light -filemode | Out-Null
                if($LASTEXITCODE -eq 0){$wprStarted=$true}
            }catch{Write-Warning "WPR start unavailable: $($_.Exception.Message)"}
        }
        $clock=[Diagnostics.Stopwatch]::StartNew()
        $proc.Refresh()
        $oldCpu=$proc.TotalProcessorTime.TotalSeconds
        $oldTime=0.0
        $lastSample=0.0
        $wheelCount=0
        while($clock.Elapsed.TotalSeconds -lt $DurationSeconds){
            $direction=if(([math]::Floor($wheelCount/12)%2)-eq 0){-120}else{120}
            $wparam=[IntPtr]([long]((([long]$direction -band 0xffff) -shl 16)))
            $lparam=[IntPtr]([long](($y -shl 16) -bor ($x -band 0xFFFF)))
            if(-not [ScrollProfileNative]::PostMessage($proc.MainWindowHandle,0x020A,$wparam,$lparam)){
                throw 'WM_MOUSEWHEEL PostMessage failed.'
            }
            $wheelCount++
            Start-Sleep -Milliseconds 150
            $now=$clock.Elapsed.TotalSeconds
            if($now-$lastSample -ge 1){
                $proc.Refresh()
                if($proc.HasExited){throw 'App exited during scroll workload.'}
                $cpu=$proc.TotalProcessorTime.TotalSeconds
                $percent=100*($cpu-$oldCpu)/(($now-$oldTime)*[Environment]::ProcessorCount)
                $oldCpu=$cpu;$oldTime=$now;$lastSample=$now
                $samples.Add([pscustomobject]@{
                    UiTargetFps=$UiFps;Run=$run;ElapsedSeconds=[math]::Round($now,3)
                    TimestampUtc=[DateTimeOffset]::UtcNow.ToString('o')
                    CpuPercent=[math]::Round([math]::Max(0,$percent),5)
                    PrivateMB=[math]::Round($proc.PrivateMemorySize64/1MB,3)
                    WheelMessagesSent=$wheelCount
                    VerticalScrollPercent=$scroll.Current.VerticalScrollPercent
                })
            }
        }
        Start-Sleep -Seconds 1
        $finalPercent=$scroll.Current.VerticalScrollPercent
        $observed=@($samples|Where-Object {$_.UiTargetFps -eq $UiFps -and $_.Run -eq $run}|
            ForEach-Object {$_.VerticalScrollPercent}|Sort-Object -Unique)
        if($observed.Count -lt 2){throw 'Scroll offset did not change; workload invalid.'}
        $runResult=[pscustomobject]@{
            UiTargetFps=$UiFps;Run=$run;DurationSeconds=$DurationSeconds
            WheelMessagesSent=$wheelCount;InitialScrollPercent=$initialPercent
            FinalScrollPercent=$finalPercent;UniqueObservedOffsets=$observed.Count
            EtlPath=$etl;EtlCaptured=$false;FrameTimeP95Ms=$null;FrameTimeP99Ms=$null
            Note='Frame-time analysis requires supported WPF profiler/WPA.'
        }
        Write-Host "[Profiler] UI scroll $UiFps FPS run $run/$Runs completed."
    }finally{
        if($wprStarted){
            & wpr -stop $etl 'CrossGOverlay UI scrolling on custom background' -compress -skipPdbGen | Out-Null
            if($runResult){$runResult.EtlCaptured=Test-Path $etl}
        }
        if($runResult){$runResults.Add($runResult)}
        [ScrollProfileNative]::SetCursorPos($cursor.X,$cursor.Y)|Out-Null
        if(-not $proc.HasExited){$proc.Kill();$proc.WaitForExit(5000)|Out-Null}
        $proc.Dispose()
        if($samples.Count){$samples|Export-Csv ($prefix+'-samples.csv') -NoTypeInformation -Encoding UTF8}
        if($runResults.Count){$runResults|Export-Csv ($prefix+'-runs.csv') -NoTypeInformation -Encoding UTF8}
    }
}
[pscustomobject]@{
    UiTargetFps=$UiFps;DurationSeconds=$DurationSeconds;Runs=$Runs;TraceRuns=$TraceRuns
    ExeSha256=(Get-FileHash $exe -Algorithm SHA256).Hash
    ManagedDllSha256=(Get-FileHash (Join-Path (Split-Path $exe) 'CrossGOverlay.dll') -Algorithm SHA256).Hash
    Fixture='1600x900 PNG; AppBgBlurRadius=10; General tab'
    SourceCodeScrollTickCapWithBackground=60
    FrameTimeMeaning='P95/P99 blank until an ETL-aware WPF frame analyzer is available.'
}|ConvertTo-Json|Set-Content ($prefix+'-metadata.json') -Encoding UTF8
Write-Host "[Profiler] UI workload CSV prefix: $prefix"
