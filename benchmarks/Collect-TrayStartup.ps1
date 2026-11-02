<# Measure first overlay drawing when Settings never opens. Requires timeline env diagnostics. #>
param([ValidateRange(1,100)][int]$Runs=10)
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
$data=Join-Path $PSScriptRoot 'results\profiling-app-data\effects\standard'
$exe=Join-Path $repo 'src\CrosshairOverlay\bin\x64\Release\net8.0-windows\win-x64\CrossGOverlay.exe'
if(-not(Test-Path (Join-Path $data 'settings.json'))){throw 'Run Prepare-EffectFixtures.ps1 first.'}
if(Get-Process CrossGOverlay -ErrorAction SilentlyContinue){throw 'Close existing CrossGOverlay first.'}
if(@(Get-Process -ErrorAction SilentlyContinue|Where-Object ProcessName -Match '^DeltaForce').Count){
    throw 'App-only startup test will not run while Delta Force is present.'
}
$env:CROSSGOVERLAY_BENCHMARK_ROOT=$data
$env:CROSSGOVERLAY_BENCHMARK_TIMELINE='1'
$stamp=[DateTimeOffset]::UtcNow.ToString('yyyyMMddTHHmmssZ')
$output=Join-Path $PSScriptRoot "results\$stamp-TrayStartup.csv"
$rows=[Collections.Generic.List[object]]::new()
for($run=1;$run -le $Runs;$run++){
    $logPath=Join-Path $data ("logs\crosshair-$(Get-Date -Format yyyyMMdd).log")
    $offset=if(Test-Path $logPath){(Get-Item $logPath).Length}else{0L}
    $proc=Start-Process -FilePath $exe -PassThru
    try{
        $watch=[Diagnostics.Stopwatch]::StartNew()
        do{
            if($proc.HasExited){throw "App exited in tray startup run $run."}
            $text=''
            if(Test-Path $logPath){
                $stream=[IO.File]::Open($logPath,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::ReadWrite)
                try{
                    if($stream.Length -gt $offset){
                        $stream.Seek($offset,[IO.SeekOrigin]::Begin)|Out-Null
                        $reader=[IO.StreamReader]::new($stream,[Text.Encoding]::UTF8,$true,4096,$true)
                        try{$text=$reader.ReadToEnd()}finally{$reader.Dispose()}
                    }
                }finally{$stream.Dispose()}
            }
            if($text -match '\[FTL\] CrosshairOverlay\.App:'){throw "Fatal startup in run $run."}
            $app=[regex]::Match($text,'BenchmarkTimeline AppInitialized ([0-9]+(?:\.[0-9]+)?) ms\.')
            $overlay=[regex]::Match($text,'BenchmarkTimeline OverlayFirstDrawing ([0-9]+(?:\.[0-9]+)?) ms\.')
            $complete=$text -match 'CrosshairOverlay\.App:.*?runtime \+ .*?App\.xaml'
            if($app.Success -and $overlay.Success -and $complete){break}
            Start-Sleep -Milliseconds 25
        }while($watch.Elapsed.TotalSeconds -lt 30)
        if(-not($app.Success -and $overlay.Success -and $complete)){
            throw "Tray startup timeline incomplete in run $run."
        }
        $appMs=[double]::Parse($app.Groups[1].Value,[Globalization.CultureInfo]::InvariantCulture)
        $overlayMs=[double]::Parse($overlay.Groups[1].Value,[Globalization.CultureInfo]::InvariantCulture)
        $rows.Add([pscustomobject]@{
            Run=$run;Pid=$proc.Id;AppInitializedMs=$appMs;OverlayFirstDrawingMs=$overlayMs
            AppToOverlayMs=[math]::Round($overlayMs-$appMs,3)
        })
        $rows|Export-Csv $output -NoTypeInformation -Encoding UTF8
        Write-Host "[Profiler] Tray startup $run/$Runs complete."
    }finally{
        if(-not $proc.HasExited){$proc.Kill();$proc.WaitForExit(5000)|Out-Null}
        $proc.Dispose()
    }
}
Write-Host "[Profiler] Tray startup results: $output"
