<#
.SYNOPSIS
    Capture native VirtualAlloc stacks during the isolated preset stress fixture.
.NOTES
    Run from an elevated PowerShell. The script does not self-elevate and always
    stops its WPR session, including when the strict resource gate fails.
#>
param(
    [string]$OutputDirectory = ''
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path $PSScriptRoot ('results\rc2-native-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
}
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
if (-not $OutputDirectory.StartsWith([IO.Path]::GetFullPath($repoRoot) + [IO.Path]::DirectorySeparatorChar,
        [StringComparison]::OrdinalIgnoreCase)) {
    throw 'OutputDirectory must be inside the repository workspace.'
}

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Run PowerShell as Administrator to enable WPR VirtualAllocation.'
}

New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$etl = Join-Path $OutputDirectory 'CrossGOverlay_RC2_VirtualAllocation.etl'
$stressRoot = Join-Path $OutputDirectory 'stress'
$recording = $false
$stressExit = -1
try {
    & wpr -start VirtualAllocation -filemode
    if ($LASTEXITCODE -ne 0) { throw "WPR start failed with exit code $LASTEXITCODE." }
    $recording = $true

    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'Invoke-Stress.ps1') `
        -Scenario preset-multistage -OutputDirectory $stressRoot
    $stressExit = $LASTEXITCODE
}
finally {
    if ($recording) {
        & wpr -stop $etl
        if ($LASTEXITCODE -ne 0) {
            Write-Error "WPR stop failed with exit code $LASTEXITCODE; check 'wpr -status'."
        }
    }
}

Write-Output "ETL: $etl"
Write-Output "Stress CSV: $stressRoot\preset-multistage\diagnostics\stress-preset-multistage.csv"
if ($stressExit -ne 0) {
    throw "Stress fixture reported a failed resource gate (exit code $stressExit); ETL and CSV were retained."
}
