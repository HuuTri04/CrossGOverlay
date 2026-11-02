<#
.SYNOPSIS
    Run only from an actual Administrator PowerShell. Captures ETW outside games.
    UI workload uses UI Automation; toast workload uses isolated benchmark markers.
    This script produces ETL, not P95/P99 by itself: inspect the trace in WPA.
#>
param([ValidateSet('Ui60','Ui144','ToastAllocation','PresetRegion','PresetGdiEvents','PresetGdiStacks','ScrollIdle','ScrollPlain','ScrollBackground','PresetFanout','PresetInternalFanout')][string]$Scenario,
      [ValidateRange(30,120)][int]$DurationSeconds=30)
$ErrorActionPreference='Stop'
$principal=[Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
if(-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)){
    throw 'ETW requires an elevated Administrator PowerShell; this token is not elevated.'
}
if(@(Get-Process -ErrorAction SilentlyContinue|Where-Object ProcessName -Match '^DeltaForce').Count){
    throw 'The app-only ETW workload will not run while Delta Force is present.'
}
if(Get-Process CrossGOverlay -ErrorAction SilentlyContinue){throw 'Close existing CrossGOverlay first.'}
$status=(& wpr -status 2>&1|Out-String)
if($status -notmatch 'not recording|There are no trace profiles running'){
    throw "Another WPR session may be active: $status"
}
$etlRoot=Join-Path $PSScriptRoot 'results\etl'
New-Item -ItemType Directory -Path $etlRoot -Force|Out-Null
$stamp=[DateTimeOffset]::UtcNow.ToString('yyyyMMddTHHmmssZ')
$etl=Join-Path $etlRoot "CrossGOverlay-$Scenario-$stamp.etl"
$consoleLog=Join-Path $etlRoot "CrossGOverlay-$Scenario-$stamp-console.log"
Start-Transcript -LiteralPath $consoleLog -Force | Out-Null
$started=$false
$startAttempted=$false
try{
    $startAttempted=$true
    if($Scenario -eq 'PresetGdiStacks'){
        $perfView=Join-Path $PSScriptRoot 'tools\PerfView\PerfView.exe'
        if(-not(Test-Path -LiteralPath $perfView)){throw 'Microsoft PerfView is required for GDI creation stacks.'}
        $perfLog=Join-Path $etlRoot "CrossGOverlay-$Scenario-$stamp-perfview.log"
        $provider='Microsoft-Windows-Win32k:0x10000000000:Verbose::@EventIDStacksToEnable=455;@ProcessNameFilter=CrossGOverlay.exe'
        & $perfView /NoGui /AcceptEULA "/LogFile:$perfLog" "/DataFile:$etl" "/OnlyProviders:$provider" start
    }elseif($Scenario -eq 'PresetGdiEvents'){
        # The WPR Handle profile records kernel object handles, not GDI HRGN.
        # Capture the Win32k GdiHandleOperation provider explicitly.
        & logman create trace CrossGOverlayGdi -p Microsoft-Windows-Win32k 0x10000000000 5 -o $etl -ets
    }elseif($Scenario -eq 'ToastAllocation'){
        # VirtualAllocation records commit stacks for the second VMMap 62,504 KiB region.
        & wpr -start VirtualAllocation -filemode
    }elseif($Scenario -eq 'PresetRegion'){
        # Handle and CPU profiles may classify kernel handles and caller stacks.
        # GDI HRGN attribution still requires checking that the events include GDI calls.
        & wpr -start Handle -start CPU.light -filemode
    }elseif($Scenario -in @('ScrollIdle','ScrollPlain','ScrollBackground','PresetFanout','PresetInternalFanout')){
        & wpr -start CPU -start GPU.light -start DesktopComposition.light -filemode
    }else{
        & wpr -start CPU.light -start GPU.light -start DesktopComposition.light -filemode
    }
    if($LASTEXITCODE -ne 0){throw "ETW could not start; exit code $LASTEXITCODE."}
    $started=$true
    if($Scenario -eq 'ToastAllocation'){
        & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'Profile-DiagnosticScenario.ps1') -Scenario toast -Count 10 -SettlingSeconds 5
    }elseif($Scenario -in @('PresetRegion','PresetGdiEvents','PresetGdiStacks')){
        $count=if($Scenario -eq 'PresetGdiStacks'){100}else{1000}
        & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'Profile-DiagnosticScenario.ps1') -Scenario preset -Count $count -SettlingSeconds 5
    }elseif($Scenario -in @('ScrollIdle','ScrollPlain','ScrollBackground')){
        $mode=$Scenario.Substring(6)
        & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'Profile-ScrollStates.ps1') -Mode $mode -DurationSeconds $DurationSeconds
    }elseif($Scenario -eq 'PresetFanout'){
        & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'Profile-PresetSwitch.ps1') -Switches 100 -SettlingSeconds 0
    }elseif($Scenario -eq 'PresetInternalFanout'){
        & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'Profile-DiagnosticScenario.ps1') -Scenario preset -Count 100 -SettlingSeconds 0
    }else{
        $fps=if($Scenario -eq 'Ui60'){60}else{144}
        & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'Profile-UiScroll.ps1') -UiFps $fps -DurationSeconds $DurationSeconds -Runs 1 -TraceRuns 0
    }
    if($LASTEXITCODE -ne 0){throw "Workload failed; exit code $LASTEXITCODE."}
}catch{
    Write-Host "[Profiler] ERROR: $($_.Exception.ToString())"
    throw
}finally{
    if($started){
        if($Scenario -eq 'PresetGdiStacks'){
            & $perfView /NoGui /AcceptEULA "/LogFile:$perfLog" "/DataFile:$etl" /Merge:true /Zip:false stop
            if($LASTEXITCODE -ne 0){Write-Warning "GDI stack ETW stop failed with exit code $LASTEXITCODE."}
        }elseif($Scenario -eq 'PresetGdiEvents'){
            & logman stop CrossGOverlayGdi -ets
            if($LASTEXITCODE -ne 0){Write-Warning "GDI ETW stop failed with exit code $LASTEXITCODE."}
        }else{
            & wpr -stop $etl 'CrossGOverlay elevated app-only diagnostic' -compress
            if($LASTEXITCODE -ne 0){Write-Warning "WPR stop failed with exit code $LASTEXITCODE."}
        }
    }elseif($startAttempted){
        if($Scenario -eq 'PresetGdiStacks' -and $perfView -and (Test-Path -LiteralPath $perfView)){
            & $perfView /NoGui /AcceptEULA "/LogFile:$perfLog" "/DataFile:$etl" abort | Out-Null
        }elseif($Scenario -eq 'PresetGdiEvents'){
            & logman stop CrossGOverlayGdi -ets 2>$null | Out-Null
        }else{
            $current=(& wpr -status 2>&1|Out-String)
            if($current -notmatch 'not recording|There are no trace profiles running'){
                & wpr -cancel | Out-Null
            }
        }
    }
    Stop-Transcript | Out-Null
}
if(-not(Test-Path -LiteralPath $etl)){throw "No ETL output at $etl."}
Write-Host "[Profiler] ETL: $etl"
Write-Host '[Profiler] Inspect process PID/time and VirtualAlloc Commit Stack or CPU/GPU/DWM lanes in WPA.'
