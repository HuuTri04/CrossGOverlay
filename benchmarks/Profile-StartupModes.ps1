<#
.SYNOPSIS
    Matched 15-run warm startup comparison. Does not label process-cache runs as cold starts
    and does not equate WPF DrawingVisual completion with DWM present.
#>
param([ValidateRange(1,100)][int]$RunsPerMode=15)
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
$results=Join-Path $PSScriptRoot 'results'
$source=Join-Path $results 'profiling-app-data\effects\standard'
$fixtures=Join-Path $results 'profiling-app-data\startup-comparison'
$exe=Join-Path $repo 'src\CrosshairOverlay\bin\x64\Release\net8.0-windows\win-x64\CrossGOverlay.exe'
if(-not(Test-Path (Join-Path $source 'settings.json'))){throw 'Run Prepare-EffectFixtures.ps1 first.'}
if(Get-Process CrossGOverlay -ErrorAction SilentlyContinue){throw 'Close existing CrossGOverlay first.'}
if(@(Get-Process -ErrorAction SilentlyContinue|Where-Object ProcessName -Match '^DeltaForce').Count){
    throw 'App-only startup benchmark will not run while Delta Force is present.'
}
foreach($mode in @('tray','settings')){
    $dest=Join-Path $fixtures $mode
    New-Item -ItemType Directory -Path (Join-Path $dest 'presets'),(Join-Path $dest 'logs') -Force|Out-Null
    Get-ChildItem (Join-Path $source 'presets') -Filter '*.json' -File |
        Copy-Item -Destination (Join-Path $dest 'presets') -Force
    $settings=Get-Content (Join-Path $source 'settings.json') -Raw -Encoding UTF8|ConvertFrom-Json
    $settings.StartMinimizedToTray=($mode -eq 'tray')
    $settings.CustomAppBgPath=$null
    $settings|ConvertTo-Json -Depth 30|Set-Content (Join-Path $dest 'settings.json') -Encoding UTF8
}
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
function Percentile([double[]]$values,[double]$fraction){
    if(-not $values.Count){return $null}
    $sorted=@($values|Sort-Object)
    $index=[math]::Max(0,[math]::Ceiling($sorted.Count*$fraction)-1)
    return [math]::Round($sorted[$index],3)
}
$stamp=[DateTimeOffset]::UtcNow.ToString('yyyyMMddTHHmmssZ')
$prefix=Join-Path $results "startup-modes-$stamp"
$rows=[Collections.Generic.List[object]]::new()
$env:CROSSGOVERLAY_BENCHMARK_TIMELINE='1'
for($run=1;$run -le $RunsPerMode;$run++){
    $order=if($run%2){@('tray','settings')}else{@('settings','tray')}
    foreach($mode in $order){
        $dest=Join-Path $fixtures $mode
        $env:CROSSGOVERLAY_BENCHMARK_ROOT=$dest
        $logPath=Join-Path $dest ("logs\crosshair-$(Get-Date -Format yyyyMMdd).log")
        $offset=if(Test-Path $logPath){(Get-Item $logPath).Length}else{0L}
        $process=Start-Process -FilePath $exe -PassThru
        try{
            $sw=[Diagnostics.Stopwatch]::StartNew()
            do{
                if($process.HasExited){throw "Process exited in $mode run $run."}
                $log=Read-NewLog $logPath $offset
                if($log -match '\[FTL\] CrosshairOverlay\.App:'){
                    throw "Fatal startup in $mode run $run; inspect $logPath"
                }
                $app=[regex]::Match($log,'BenchmarkTimeline AppInitialized ([0-9]+(?:\.[0-9]+)?) ms\.')
                $draw=[regex]::Match($log,'BenchmarkTimeline OverlayFirstDrawing ([0-9]+(?:\.[0-9]+)?) ms\.')
                $settings=[regex]::Match($log,'BenchmarkTimeline SettingsFirstRender ([0-9]+(?:\.[0-9]+)?) ms\.')
                $complete=$log -match 'CrosshairOverlay\.App:.*?runtime \+ .*?App\.xaml'
                if($app.Success -and $draw.Success -and $complete -and
                    ($mode -eq 'tray' -or $settings.Success)){break}
                Start-Sleep -Milliseconds 25
            }while($sw.Elapsed.TotalSeconds -lt 30)
            if(-not($app.Success -and $draw.Success -and $complete) -or
                ($mode -eq 'settings' -and -not $settings.Success)){
                throw "Timeline incomplete in $mode run $run."
            }
            $appMs=[double]::Parse($app.Groups[1].Value,[Globalization.CultureInfo]::InvariantCulture)
            $drawMs=[double]::Parse($draw.Groups[1].Value,[Globalization.CultureInfo]::InvariantCulture)
            $settingsMs=if($settings.Success){
                [double]::Parse($settings.Groups[1].Value,[Globalization.CultureInfo]::InvariantCulture)
            }else{$null}
            $rows.Add([pscustomobject]@{
                Run=$run;Mode=$mode;StartClass='warm';Pid=$process.Id
                AppInitializedMs=$appMs;OverlayFirstDrawingMs=$drawMs
                AppToDrawingMs=[math]::Round($drawMs-$appMs,3)
                SettingsFirstRenderMs=$settingsMs
                DwmPresentMs=$null
            })
            $rows|Export-Csv ($prefix+'-runs.csv') -NoTypeInformation -Encoding UTF8
            Write-Host "[Profiler] Warm startup pair $run/$RunsPerMode, ${mode}: $drawMs ms drawing."
        }finally{
            if(-not $process.HasExited){$process.Kill();$process.WaitForExit(5000)|Out-Null}
            $process.Dispose()
        }
    }
}
$summary=foreach($mode in @('tray','settings')){
    $values=[double[]]@($rows|Where-Object Mode -EQ $mode|ForEach-Object OverlayFirstDrawingMs)
    [pscustomobject]@{
        Mode=$mode;StartClass='warm';Runs=$values.Count
        MedianMs=Percentile $values 0.5;P95Ms=Percentile $values 0.95
        P99Ms=Percentile $values 0.99;MinMs=($values|Measure-Object -Minimum).Minimum
        MaxMs=($values|Measure-Object -Maximum).Maximum
        DwmPresentP95Ms=$null;DwmPresentP99Ms=$null
    }
}
$summary|Export-Csv ($prefix+'-summary.csv') -NoTypeInformation -Encoding UTF8
[pscustomobject]@{
    RunsPerMode=$RunsPerMode;ColdRuns=0
    ExeSha256=(Get-FileHash $exe -Algorithm SHA256).Hash
    ManagedDllSha256=(Get-FileHash (Join-Path (Split-Path $exe) 'CrossGOverlay.dll') -Algorithm SHA256).Hash
    Timing='WPF first DrawingVisual callback, not DWM Present. Repeated launches on same OS boot are warm.'
}|ConvertTo-Json|Set-Content ($prefix+'-metadata.json') -Encoding UTF8
Write-Host "[Profiler] Results prefix: $prefix"
