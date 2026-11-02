<# Profile isolated Release x64 app, never a game. Raw VMMap maps are gitignored. #>
param(
    [ValidateRange(1,500)][int]$Cycles = 100,
    [ValidateRange(0,86400)][int]$IdleSeconds = 3600,
    [ValidateRange(1,60)][int]$IdleSampleSeconds = 10
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$results = Join-Path $PSScriptRoot 'results'
$data = Join-Path $results 'profiling-app-data'
$raw = Join-Path $results 'vmmap'
$exe = Join-Path $repo 'src\CrosshairOverlay\bin\x64\Release\net8.0-windows\win-x64\CrossGOverlay.exe'
$vmmap = Join-Path $PSScriptRoot 'tools\VMMap\vmmap64.exe'
$runId = [DateTimeOffset]::UtcNow.ToString('yyyyMMddTHHmmssZ')
$prefix = Join-Path $results "memory-profile-$runId"
if (-not (Test-Path $exe) -or -not (Test-Path $vmmap)) { throw 'Release exe or VMMap x64 is missing.' }
if (Get-Process CrossGOverlay -ErrorAction SilentlyContinue) { throw 'Close existing CrossGOverlay first.' }
if (@(Get-Process -ErrorAction SilentlyContinue | Where-Object ProcessName -Match '^DeltaForce').Count) {
    throw 'This app-only profiler will not run while Delta Force is present.'
}
New-Item -ItemType Directory -Path $data,$raw -Force | Out-Null
$settingsPath = Join-Path $data 'settings.json'
if (-not (Test-Path $settingsPath)) {
    $seed = Join-Path $results 'app-data\settings.json'
    if (-not (Test-Path $seed)) { throw "Seed settings missing: $seed" }
    $settings = Get-Content $seed -Raw -Encoding UTF8 | ConvertFrom-Json
    $settings.StartMinimizedToTray = $true
    $settings.ShowOnlyInMatchedGames = $false
    $settings.OverlayEnabled = $true
    $settings.Language = 'en'
    $settings | ConvertTo-Json -Depth 16 | Set-Content $settingsPath -Encoding UTF8
    $presets = Join-Path $data 'presets'
    New-Item -ItemType Directory -Path $presets -Force | Out-Null
    Get-ChildItem (Join-Path $results 'app-data\presets') -File | Copy-Item -Destination $presets
}
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class MemoryProfileNative {
    public delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr param);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc callback, IntPtr param);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] public static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll", SetLastError=true)] public static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr wp, IntPtr lp);
    [DllImport("user32.dll")] public static extern uint GetGuiResources(IntPtr process, uint flags);
    public static IntPtr FindOverlay(int wantedPid) {
        IntPtr result = IntPtr.Zero;
        EnumWindows((hwnd, _) => {
            uint pid; GetWindowThreadProcessId(hwnd, out pid);
            if (pid != wantedPid || !IsWindowVisible(hwnd)) return true;
            long style = GetWindowLongPtr(hwnd, -20).ToInt64();
            if ((style & 0x80020) == 0x80020) { result = hwnd; return false; }
            return true;
        }, IntPtr.Zero);
        return result;
    }
}
'@
function Wait-For([scriptblock]$condition, [string]$message) {
    $watch = [Diagnostics.Stopwatch]::StartNew()
    while ($watch.Elapsed.TotalSeconds -lt 20) {
        if (& $condition) { return }
        Start-Sleep -Milliseconds 100
    }
    throw "Timed out: $message"
}
function Parse-KB($value) {
    if (-not $value) { return 0.0 }
    return [double]::Parse([string]$value,[Globalization.NumberStyles]::AllowThousands,
        [Globalization.CultureInfo]::InvariantCulture)
}
function VMMap-Rows([string]$path) {
    $lines = @(Get-Content -LiteralPath $path)
    $start = -1
    for ($i=0;$i -lt $lines.Count;$i++) {
        if ($lines[$i] -like '"Type","Size"*') { $start=$i; break }
    }
    if ($start -lt 0) { throw "VMMap summary missing: $path" }
    $block=[Collections.Generic.List[string]]::new()
    $block.Add($lines[$start])
    for ($i=$start+1;$i -lt $lines.Count;$i++) {
        if (-not $lines[$i].Trim()) { continue }
        if ($lines[$i] -like '"Address","Type"*') { break }
        $block.Add($lines[$i])
    }
    return @($block | ConvertFrom-Csv)
}
$env:CROSSGOVERLAY_BENCHMARK_ROOT = $data
$snapshots=[Collections.Generic.List[object]]::new()
$samples=[Collections.Generic.List[object]]::new()
$process=$null
$tabNames=[Collections.Generic.List[string]]::new()
function Sample([string]$stage,[int]$cycle=0) {
    $process.Refresh()
    $samples.Add([pscustomobject]@{
        Stage=$stage; Cycle=$cycle; TimestampUtc=[DateTimeOffset]::UtcNow.ToString('o')
        PrivateMB=[math]::Round($process.PrivateMemorySize64/1MB,3)
        WorkingSetMB=[math]::Round($process.WorkingSet64/1MB,3)
        GdiHandles=[MemoryProfileNative]::GetGuiResources($process.Handle,0)
        UserHandles=[MemoryProfileNative]::GetGuiResources($process.Handle,1)
        OverlayVisible=([MemoryProfileNative]::FindOverlay($process.Id) -ne [IntPtr]::Zero)
    })
}
function Snapshot([string]$stage) {
    $path=Join-Path $raw "$runId-$stage.csv"
    $tool=Start-Process -FilePath $vmmap -ArgumentList @('-accepteula','-p',"$($process.Id)",$path) -WindowStyle Hidden -PassThru -Wait
    if ($tool.ExitCode -ne 0 -or -not(Test-Path $path)) { throw "VMMap failed for $stage." }
    $rows=VMMap-Rows $path
    $types=@{}
    foreach($row in $rows){$types[$row.Type]=$row}
    foreach($required in @('Total','Private Data','Managed Heap','Image','Heap')) {
        if(-not $types.ContainsKey($required)){throw "VMMap category missing: $required"}
    }
    $process.Refresh()
    $snapshots.Add([pscustomobject]@{
        Stage=$stage; TimestampUtc=[DateTimeOffset]::UtcNow.ToString('o'); Pid=$process.Id
        VMMapPrivateMB=[math]::Round((Parse-KB $types.Total.Private)/1024,3)
        VMMapCommittedMB=[math]::Round((Parse-KB $types.Total.Committed)/1024,3)
        PrivateDataMB=[math]::Round((Parse-KB $types['Private Data'].Private)/1024,3)
        ManagedHeapPrivateMB=[math]::Round((Parse-KB $types['Managed Heap'].Private)/1024,3)
        ImagePrivateMB=[math]::Round((Parse-KB $types.Image.Private)/1024,3)
        NativeHeapPrivateMB=[math]::Round((Parse-KB $types.Heap.Private)/1024,3)
        ProcessPrivateMB=[math]::Round($process.PrivateMemorySize64/1MB,3)
        WorkingSetMB=[math]::Round($process.WorkingSet64/1MB,3)
        GdiHandles=[MemoryProfileNative]::GetGuiResources($process.Handle,0)
        UserHandles=[MemoryProfileNative]::GetGuiResources($process.Handle,1)
        OverlayVisible=([MemoryProfileNative]::FindOverlay($process.Id) -ne [IntPtr]::Zero)
    })
    Write-Host "[Profiler] VMMap $stage"
}
try {
    $logPath=Join-Path $data ("logs\crosshair-$(Get-Date -Format yyyyMMdd).log")
    $logOffset=if(Test-Path $logPath){(Get-Item $logPath).Length}else{0L}
    $process=Start-Process -FilePath $exe -PassThru
    Wait-For {
        if($process.HasExited){throw 'App exited during startup.'}
        if(-not(Test-Path $logPath)){return $false}
        $stream=[IO.File]::Open($logPath,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::ReadWrite)
        try {
            if($stream.Length -le $logOffset){return $false}
            $stream.Seek($logOffset,[IO.SeekOrigin]::Begin)|Out-Null
            $reader=[IO.StreamReader]::new($stream,[Text.Encoding]::UTF8,$true,4096,$true)
            try {$log=$reader.ReadToEnd()} finally {$reader.Dispose()}
        } finally {$stream.Dispose()}
        if($log -match '\[FTL\] CrosshairOverlay\.App:'){throw 'Fatal startup log.'}
        return $log -match 'CrosshairOverlay\.App:.*?runtime \+ .*?App\.xaml'
    } 'startup completion'
    Start-Sleep -Seconds 5
    if([MemoryProfileNative]::FindOverlay($process.Id) -eq [IntPtr]::Zero){
        throw 'Overlay not visible in start-minimized state.'
    }
    Snapshot '01-overlay-tray'
    $signal=[Threading.EventWaitHandle]::OpenExisting('Global\CrossGOverlay_Activate')
    try {
        $signal.Set()|Out-Null
        Wait-For {$process.Refresh();$process.MainWindowHandle -ne [IntPtr]::Zero} 'Settings open'
        $hwnd=$process.MainWindowHandle
        $element=[System.Windows.Automation.AutomationElement]::FromHandle($hwnd)
        $filter=New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::TabItem)
        $tabs=$element.FindAll([System.Windows.Automation.TreeScope]::Descendants,$filter)
        if($tabs.Count -ne 7){throw "Expected 7 navigation tabs, found $($tabs.Count)."}
        foreach($tab in $tabs){
            $tabNames.Add($tab.Current.Name)
            $pattern=[System.Windows.Automation.SelectionItemPattern]$tab.GetCurrentPattern(
                [System.Windows.Automation.SelectionItemPattern]::Pattern)
            $pattern.Select()
            Start-Sleep -Milliseconds 400
        }
        Start-Sleep -Seconds 2
        Snapshot '02-all-seven-tabs'
        if(-not [MemoryProfileNative]::PostMessage($hwnd,0x0010,[IntPtr]::Zero,[IntPtr]::Zero)){
            throw 'WM_CLOSE failed.'
        }
        Wait-For {-not [MemoryProfileNative]::IsWindowVisible($hwnd)} 'Settings hidden'
        Start-Sleep -Seconds 30
        Snapshot '03-hidden-30s'
        Sample 'before-cycles'
        for($cycle=1;$cycle -le $Cycles;$cycle++){
            $signal.Set()|Out-Null
            Wait-For {[MemoryProfileNative]::IsWindowVisible($hwnd)} "Settings show cycle $cycle"
            if(-not [MemoryProfileNative]::PostMessage($hwnd,0x0010,[IntPtr]::Zero,[IntPtr]::Zero)){
                throw "WM_CLOSE failed on cycle $cycle"
            }
            Wait-For {-not [MemoryProfileNative]::IsWindowVisible($hwnd)} "Settings hide cycle $cycle"
            if($cycle % 10 -eq 0 -or $cycle -eq $Cycles){
                Sample 'cycles' $cycle
                Write-Host "[Profiler] Settings cycles $cycle/$Cycles"
            }
        }
        Start-Sleep -Seconds 5
        Snapshot '05-after-cycles'
        if($IdleSeconds -gt 0){
            $start=[DateTimeOffset]::UtcNow
            Sample 'idle-long'
            while(([DateTimeOffset]::UtcNow-$start).TotalSeconds -lt $IdleSeconds){
                $remaining=$IdleSeconds-([DateTimeOffset]::UtcNow-$start).TotalSeconds
                Start-Sleep -Seconds ([math]::Min($IdleSampleSeconds,[math]::Max(1,[math]::Ceiling($remaining))))
                if($process.HasExited){throw 'App exited during idle.'}
                Sample 'idle-long'
            }
            Snapshot '06-idle-long-end'
        }
    } finally {$signal.Dispose()}
} finally {
    if($snapshots.Count){$snapshots|Export-Csv ($prefix+'-vmmap-summary.csv') -NoTypeInformation -Encoding UTF8}
    if($samples.Count){$samples|Export-Csv ($prefix+'-lifetime.csv') -NoTypeInformation -Encoding UTF8}
    [pscustomobject]@{
        RunId=$runId; ExeSha256=(Get-FileHash $exe -Algorithm SHA256).Hash
        CyclesRequested=$Cycles; IdleSecondsRequested=$IdleSeconds
        TabNames=@($tabNames); VMMapVersion=(Get-Item $vmmap).VersionInfo.FileVersion
        State1='StartMinimizedToTray=true, OverlayEnabled=true, ShowOnlyInMatchedGames=false'
    }|ConvertTo-Json -Depth 4|Set-Content ($prefix+'-metadata.json') -Encoding UTF8
    if($process){
        if(-not $process.HasExited){$process.Kill();$process.WaitForExit(5000)|Out-Null}
        $process.Dispose()
    }
}
Write-Host "[Profiler] Results prefix: $prefix"
