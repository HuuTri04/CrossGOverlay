<# App-only GIF activity probe. Counts internal timer ticks; never reads desktop/game pixels. #>
param([ValidateRange(12,60)][int]$WaitSeconds=20)
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
$results=Join-Path $PSScriptRoot 'results'
$root=Join-Path $results 'profiling-app-data\effects\gif'
$exe=Join-Path $repo 'src\CrosshairOverlay\bin\x64\Release\net8.0-windows\win-x64\CrossGOverlay.exe'
$dll=Join-Path (Split-Path $exe) 'CrossGOverlay.dll'
$probe=Join-Path $root 'gif_probe.csv'
if(-not(Test-Path (Join-Path $root 'settings.json'))){throw 'GIF fixture missing.'}
if(-not(Test-Path $exe)){throw 'Release x64 app missing.'}
if(Get-Process CrossGOverlay -ErrorAction SilentlyContinue){throw 'Close existing CrossGOverlay first.'}
if(@(Get-Process -ErrorAction SilentlyContinue|Where-Object ProcessName -Match '^DeltaForce').Count){
    throw 'App-only GIF probe will not run while Delta Force is present.'
}
if(Test-Path $probe){Remove-Item -LiteralPath $probe}
$env:CROSSGOVERLAY_BENCHMARK_ROOT=$root
$env:CROSSGOVERLAY_BENCHMARK_GIF_PROBE='1'
$process=Start-Process -FilePath $exe -PassThru
try {
    Start-Sleep -Seconds $WaitSeconds
    $process.Refresh()
    if($process.HasExited){throw 'App exited before probe completed.'}
    if(-not(Test-Path $probe)){throw 'No GIF timer ticks were recorded; verify active preset and renderer.'}
    $ticks=@(Import-Csv -LiteralPath $probe)
    if($ticks.Count -ne 1 -or [int]$ticks[0].FrameTicks -ne 100){
        throw 'GIF probe did not record the expected 100 timer ticks.'
    }
    $stamp=[DateTimeOffset]::UtcNow.ToString('yyyyMMddTHHmmssZ')
    $csv=Join-Path $results "gif-frame-probe-$stamp.csv"
    Copy-Item -LiteralPath $probe -Destination $csv
    [pscustomobject]@{
        ProbeDateUtc=[DateTimeOffset]::UtcNow.ToString('o')
        ManagedDllSha256=(Get-FileHash $dll -Algorithm SHA256).Hash
        Fixture='Two-frame 64x64 GIF in isolated benchmark root'
        ProbeEnvironment='CROSSGOVERLAY_BENCHMARK_GIF_PROBE=1'
        ResultCsv=$csv
    }|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $results "gif-frame-probe-$stamp-metadata.json") -Encoding UTF8
    Write-Host "[Profiler] $($ticks[0].FrameTicks) GIF timer ticks in $($ticks[0].ElapsedSeconds) seconds; $csv"
} finally {
    if(-not $process.HasExited){$process.Kill();$process.WaitForExit(5000)|Out-Null}
    $process.Dispose()
    Remove-Item Env:\CROSSGOVERLAY_BENCHMARK_GIF_PROBE -ErrorAction SilentlyContinue
    Remove-Item Env:\CROSSGOVERLAY_BENCHMARK_ROOT -ErrorAction SilentlyContinue
}
