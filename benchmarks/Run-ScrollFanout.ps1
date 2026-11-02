$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$result = Join-Path $PSScriptRoot 'results\etl\scroll-fanout-run-20261029.log'
"Started $([DateTimeOffset]::UtcNow.ToString('o'))" | Set-Content -LiteralPath $result -Encoding UTF8
foreach ($scenario in @('ScrollIdle', 'ScrollPlain', 'ScrollBackground', 'PresetFanout')) {
    "BEGIN $scenario $([DateTimeOffset]::UtcNow.ToString('o'))" | Add-Content -LiteralPath $result -Encoding UTF8
    try {
        & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'Collect-ElevatedEtw.ps1') -Scenario $scenario -DurationSeconds 30 *>> $result
        if ($LASTEXITCODE -ne 0) { throw "$scenario exited with $LASTEXITCODE" }
        "END $scenario $([DateTimeOffset]::UtcNow.ToString('o'))" | Add-Content -LiteralPath $result -Encoding UTF8
    } catch {
        "ERROR $scenario $($_.Exception.Message)" | Add-Content -LiteralPath $result -Encoding UTF8
    }
}
"FINISHED $([DateTimeOffset]::UtcNow.ToString('o'))" | Add-Content -LiteralPath $result -Encoding UTF8
