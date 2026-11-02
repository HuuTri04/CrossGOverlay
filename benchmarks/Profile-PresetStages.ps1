<# Isolated BENCHMARK build only. Never runs against a game or user app data. #>
param(
    [ValidateSet('baseline','no-stats','no-preview','no-bring')][string]$Variant,
    [ValidateRange(10,500)][int]$Count=100,
    [ValidateRange(1,20)][int]$Run=1
)
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
$results=Join-Path $PSScriptRoot 'results'
$exe=Join-Path $results 'preset-ab-build\CrossGOverlay.exe'
$source=Join-Path $results 'profiling-app-data\effects\background'
if(-not(Test-Path -LiteralPath $exe)){throw 'Build the isolated BENCHMARK publish first.'}
if(-not(Test-Path -LiteralPath (Join-Path $source 'settings.json'))){throw 'Benchmark fixture missing.'}
if(Get-Process CrossGOverlay -ErrorAction SilentlyContinue){throw 'Close the existing CrossGOverlay instance first.'}
if(@(Get-Process -ErrorAction SilentlyContinue|Where-Object ProcessName -Match '^DeltaForce').Count){
    throw 'The fixture must run outside Delta Force.'
}
$stamp=[DateTimeOffset]::UtcNow.ToString('yyyyMMddTHHmmssZ')
$prefix="preset-stage-$Variant-run$Run-$stamp"
$data=Join-Path $results "preset-ab-data\$prefix"
New-Item -ItemType Directory -Path (Split-Path $data -Parent) -Force|Out-Null
Copy-Item -LiteralPath $source -Destination $data -Recurse
$control=Join-Path $data 'diagnostics'
New-Item -ItemType Directory -Path $control -Force|Out-Null
foreach($marker in @('ready.txt','start.signal','done.txt','error.txt','progress.txt','preset-stage-events.csv')){
    Remove-Item -LiteralPath (Join-Path $control $marker) -ErrorAction SilentlyContinue
}
$env:CROSSGOVERLAY_BENCHMARK_ROOT=$data
$env:CROSSGOVERLAY_BENCHMARK_SCENARIO='preset-ab'
$env:CROSSGOVERLAY_BENCHMARK_VARIANT=$Variant
$env:CROSSGOVERLAY_BENCHMARK_COUNT=[string]$Count
$proc=$null
function Await-Marker([string]$name,[int]$seconds){
    $path=Join-Path $control $name
    $deadline=[DateTime]::UtcNow.AddSeconds($seconds)
    while([DateTime]::UtcNow -lt $deadline){
        $errorPath=Join-Path $control 'error.txt'
        if(Test-Path -LiteralPath $errorPath){throw (Get-Content -LiteralPath $errorPath -Raw)}
        if(Test-Path -LiteralPath $path){return}
        if($proc.HasExited){throw "Benchmark app exited before $name"}
        Start-Sleep -Milliseconds 100
    }
    throw "Timed out waiting for $name"
}
try{
    $proc=Start-Process -FilePath $exe -PassThru
    Await-Marker 'ready.txt' 30
    Start-Sleep -Seconds 3
    $started=[DateTimeOffset]::UtcNow
    Set-Content -LiteralPath (Join-Path $control 'start.signal') -Value 'go' -Encoding ascii
    Await-Marker 'done.txt' ([math]::Max(60,[math]::Ceiling($Count*0.15+30)))
    $ended=[DateTimeOffset]::UtcNow
    $completed=[int](Get-Content -LiteralPath (Join-Path $control 'done.txt') -Raw).Trim()
    if($completed -ne $Count){throw "Only $completed of $Count switches completed."}
    $events=Join-Path $control 'preset-stage-events.csv'
    if(-not(Test-Path -LiteralPath $events)){throw 'Stage CSV not flushed.'}
    $target=Join-Path $results "$prefix.csv"
    Copy-Item -LiteralPath $events -Destination $target
    [pscustomobject]@{
        Variant=$Variant;Run=$Run;Switches=$Count;StartSignalUtc=$started.ToString('o');FinishedUtc=$ended.ToString('o')
        BenchmarkExeSha256=(Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash
        BenchmarkDllSha256=(Get-FileHash -LiteralPath (Join-Path (Split-Path $exe) 'CrossGOverlay.dll') -Algorithm SHA256).Hash
        ProductionDllSha256=(Get-FileHash -LiteralPath (Join-Path $repo 'src\CrosshairOverlay\bin\x64\Release\net8.0-windows\win-x64\CrossGOverlay.dll') -Algorithm SHA256).Hash
        StageCsv=$target
    }|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $results "$prefix-metadata.json") -Encoding UTF8
    Write-Host "[PresetStage] $Variant run ${Run}: $target"
}finally{
    if($proc){
        if(-not $proc.HasExited){$proc.Kill();$proc.WaitForExit(5000)|Out-Null}
        $proc.Dispose()
    }
    Remove-Item Env:\CROSSGOVERLAY_BENCHMARK_ROOT,Env:\CROSSGOVERLAY_BENCHMARK_SCENARIO,Env:\CROSSGOVERLAY_BENCHMARK_VARIANT,Env:\CROSSGOVERLAY_BENCHMARK_COUNT -ErrorAction SilentlyContinue
}
