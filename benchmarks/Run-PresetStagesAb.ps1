$ErrorActionPreference='Stop'
$log=Join-Path $PSScriptRoot 'results\preset-ab-run-20261029.log'
"Started $([DateTimeOffset]::UtcNow.ToString('o'))"|Set-Content -LiteralPath $log -Encoding UTF8
$schedule=@(
    @('no-stats',1),@('no-preview',1),@('no-bring',1),
    @('no-bring',2),@('baseline',2),@('no-stats',2),@('no-preview',2),
    @('no-preview',3),@('no-stats',3),@('baseline',3),@('no-bring',3)
)
foreach($entry in $schedule){
    $variant=$entry[0];$run=[int]$entry[1]
    "BEGIN $variant run$run $([DateTimeOffset]::UtcNow.ToString('o'))"|Add-Content -LiteralPath $log -Encoding UTF8
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'Profile-PresetStages.ps1') -Variant $variant -Count 100 -Run $run *>> $log
    if($LASTEXITCODE -ne 0){
        "ERROR $variant run$run exit=$LASTEXITCODE"|Add-Content -LiteralPath $log -Encoding UTF8
    }else{
        "END $variant run$run $([DateTimeOffset]::UtcNow.ToString('o'))"|Add-Content -LiteralPath $log -Encoding UTF8
    }
}
"FINISHED $([DateTimeOffset]::UtcNow.ToString('o'))"|Add-Content -LiteralPath $log -Encoding UTF8
