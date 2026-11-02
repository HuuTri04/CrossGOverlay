<# Measure the first real routed mouse input after normal startup, outside games. #>
param([ValidateRange(1,100)][int]$Runs=10)
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
$data=Join-Path $PSScriptRoot 'results\profiling-app-data\effects\normal'
$exe=Join-Path $repo 'src\CrosshairOverlay\bin\x64\Release\net8.0-windows\win-x64\CrossGOverlay.exe'
if(-not(Test-Path (Join-Path $data 'settings.json'))){throw 'Run Prepare-EffectFixtures.ps1 first.'}
if(Get-Process CrossGOverlay -ErrorAction SilentlyContinue){throw 'Close existing CrossGOverlay first.'}
if(@(Get-Process -ErrorAction SilentlyContinue|Where-Object ProcessName -Match '^DeltaForce').Count){
    throw 'Input benchmark will not run while Delta Force is present.'
}
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class StartupInputNative {
    [StructLayout(LayoutKind.Sequential)] public struct Point { public int X, Y; }
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
}
'@
function Read-NewLog([string]$path,[long]$offset){
    if(-not(Test-Path $path)){return ''}
    $stream=[IO.File]::Open($path,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::ReadWrite)
    try{
        if($stream.Length -le $offset){return ''}
        $stream.Seek($offset,[IO.SeekOrigin]::Begin)|Out-Null
        $reader=[IO.StreamReader]::new($stream,[Text.Encoding]::UTF8,$true,4096,$true)
        try{return $reader.ReadToEnd()}finally{$reader.Dispose()}
    }finally{$stream.Dispose()}
}
$env:CROSSGOVERLAY_BENCHMARK_ROOT=$data
$env:CROSSGOVERLAY_BENCHMARK_TIMELINE='1'
$stamp=[DateTimeOffset]::UtcNow.ToString('yyyyMMddTHHmmssZ')
$prefix=Join-Path $PSScriptRoot "results\startup-input-$stamp"
$rows=[Collections.Generic.List[object]]::new()
for($run=1;$run -le $Runs;$run++){
    $logPath=Join-Path $data ("logs\crosshair-$(Get-Date -Format yyyyMMdd).log")
    $offset=if(Test-Path $logPath){(Get-Item $logPath).Length}else{0L}
    $p=Start-Process -FilePath $exe -PassThru
    $cursor=[StartupInputNative+Point]::new()
    [StartupInputNative]::GetCursorPos([ref]$cursor)|Out-Null
    try{
        $watch=[Diagnostics.Stopwatch]::StartNew()
        do{
            if($p.HasExited){throw "App exited in run $run."}
            $text=Read-NewLog $logPath $offset
            if($text -match '\[FTL\] CrosshairOverlay\.App:'){throw "Fatal startup in run $run."}
            $render=[regex]::Match($text,'BenchmarkTimeline SettingsFirstRender ([0-9]+(?:\.[0-9]+)?) ms\.')
            $complete=$text -match 'CrosshairOverlay\.App:.*?runtime \+ .*?App\.xaml'
            if($render.Success -and $complete){break}
            Start-Sleep -Milliseconds 20
        }while($watch.Elapsed.TotalSeconds -lt 30)
        if(-not($render.Success -and $complete)){throw "Startup timeline incomplete in run $run."}
        $p.Refresh()
        $window=[System.Windows.Automation.AutomationElement]::FromHandle($p.MainWindowHandle)
        $filter=New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::TabItem)
        $tab=$window.FindAll([System.Windows.Automation.TreeScope]::Descendants,$filter)|
            Where-Object {$_.Current.Name -eq 'General'}|Select-Object -First 1
        if(-not $tab){throw 'General tab not found.'}
        $bounds=$tab.Current.BoundingRectangle
        $x=[int]($bounds.Left+$bounds.Width/2);$y=[int]($bounds.Top+$bounds.Height/2)
        [StartupInputNative]::SetCursorPos($x,$y)|Out-Null
        Start-Sleep -Milliseconds 20
        $sentQpc=[Diagnostics.Stopwatch]::GetTimestamp()
        [StartupInputNative]::mouse_event(0x0002,0,0,0,[UIntPtr]::Zero)
        [StartupInputNative]::mouse_event(0x0004,0,0,0,[UIntPtr]::Zero)
        $input=[regex]::Match('','')
        $inputWatch=[Diagnostics.Stopwatch]::StartNew()
        do{
            $text=Read-NewLog $logPath $offset
            $input=[regex]::Match($text,
                'BenchmarkTimeline FirstMouseInputQpc ([0-9]+) at ([0-9]+(?:\.[0-9]+)?) ms\.')
            if($input.Success){break}
            Start-Sleep -Milliseconds 10
        }while($inputWatch.Elapsed.TotalSeconds -lt 10)
        if(-not $input.Success){throw "No routed first mouse input in run $run."}
        $receivedQpc=[long]::Parse($input.Groups[1].Value)
        $receivedMs=[double]::Parse($input.Groups[2].Value,[Globalization.CultureInfo]::InvariantCulture)
        $renderMs=[double]::Parse($render.Groups[1].Value,[Globalization.CultureInfo]::InvariantCulture)
        if($receivedQpc -lt $sentQpc -or $receivedMs -lt $renderMs){
            throw "First mouse input predates scripted click in run $run."
        }
        $rows.Add([pscustomobject]@{
            Run=$run;SettingsFirstRenderMs=$renderMs;FirstMouseReceivedMs=$receivedMs
            FirstInputAfterRenderMs=[math]::Round($receivedMs-$renderMs,3)
            MouseDispatchLatencyMs=[math]::Round(1000.0*($receivedQpc-$sentQpc)/
                [Diagnostics.Stopwatch]::Frequency,4)
            Method='Synthesized OS mouse click on General tab; no game or debugger.'
        })
        $rows|Export-Csv ($prefix+'-runs.csv') -NoTypeInformation -Encoding UTF8
        Write-Host "[Profiler] Startup first input $run/$Runs complete."
    }finally{
        [StartupInputNative]::SetCursorPos($cursor.X,$cursor.Y)|Out-Null
        if(-not $p.HasExited){$p.Kill();$p.WaitForExit(5000)|Out-Null}
        $p.Dispose()
    }
}
[pscustomobject]@{
    Runs=$Runs;ExeSha256=(Get-FileHash $exe -Algorithm SHA256).Hash
    Interpretation='mouse_event to routed WPF PreviewMouseDown, includes OS scheduling; not display present.'
}|ConvertTo-Json|Set-Content ($prefix+'-metadata.json') -Encoding UTF8
Write-Host "[Profiler] Results prefix: $prefix"
