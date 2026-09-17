<#
.SYNOPSIS
    Dong goi ban phat hanh: publish SACH -> (ky so) -> nen ca thu muc thanh .zip.

.DESCRIPTION
    Ung dung phat hanh dang THU MUC (xem CrosshairOverlay.csproj de biet ly do va so do). File .zip
    tao ra o day chinh la file can dinh kem vao ban phat hanh tren GitHub: tinh nang tu cap nhat chi
    nhan asset .zip, uu tien ten co chu "win-x64".

    Xoa obj\Release va bin\Release truoc khi publish: publish tang dan sau khi sua-hoan-tac nhanh tung
    dong goi nham ma cu ma khong bao loi gi.

    Ket qua: artifacts\CrossGOverlay-<phien ban>-win-x64.zip, ben trong la mot thu muc cung ten.

.EXAMPLE
    .\build\publish.ps1

.EXAMPLE
    # Ky bang chung chi that truoc khi nen
    .\build\publish.ps1 -PfxPath C:\certs\company.pfx -PfxPassword (Read-Host -AsSecureString)
#>

[CmdletBinding()]
param(
    [switch] $SelfSigned,
    [string] $PfxPath,
    [securestring] $PfxPassword
)

$ErrorActionPreference = "Stop"

$repo = Split-Path $PSScriptRoot -Parent
$project = Join-Path $repo "src\CrosshairOverlay"
$csproj = Join-Path $project "CrosshairOverlay.csproj"
$publishDir = Join-Path $project "bin\Release\net8.0-windows\win-x64\publish"

$version = ([xml](Get-Content $csproj -Raw)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $version) { throw "Khong doc duoc <Version> trong $csproj" }

$name = "CrossGOverlay-$version-win-x64"
$artifacts = Join-Path $repo "artifacts"
$stage = Join-Path $artifacts $name
$zip = Join-Path $artifacts "$name.zip"

Write-Host "Phien ban: $version"

# ---------------------------------------------------------------- publish sach

foreach ($dir in @((Join-Path $project "obj\Release"), (Join-Path $project "bin\Release"))) {
    if (Test-Path -LiteralPath $dir) { Remove-Item -LiteralPath $dir -Recurse -Force }
}

& dotnet publish $project -c Release
if ($LASTEXITCODE -ne 0) { throw "dotnet publish that bai (ma $LASTEXITCODE)" }

if (-not (Test-Path (Join-Path $publishDir "CrossGOverlay.exe"))) {
    throw "Publish xong nhung khong thay CrossGOverlay.exe trong $publishDir"
}

# ---------------------------------------------------------------- ky so (tuy chon)

if ($SelfSigned -or $PfxPath) {
    $signArgs = @{ Path = $publishDir }
    if ($SelfSigned) { $signArgs.SelfSigned = $true }
    if ($PfxPath) { $signArgs.PfxPath = $PfxPath }
    if ($PfxPassword) { $signArgs.PfxPassword = $PfxPassword }
    & (Join-Path $PSScriptRoot "sign.ps1") @signArgs
}

# ---------------------------------------------------------------- nen

New-Item -ItemType Directory -Force $artifacts | Out-Null
foreach ($old in @($stage, $zip)) {
    if (Test-Path -LiteralPath $old) { Remove-Item -LiteralPath $old -Recurse -Force }
}

# Chep sang thu muc mang ten phien ban roi nen KEM thu muc goc: nguoi dung giai nen ra mot thu muc
# rieng thay vi 255 file vuong vai ra cho dang dung.
Copy-Item -LiteralPath $publishDir -Destination $stage -Recurse

Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory(
    $stage, $zip, [System.IO.Compression.CompressionLevel]::Optimal, $true)

Remove-Item -LiteralPath $stage -Recurse -Force

$files = (Get-ChildItem -LiteralPath $publishDir -Recurse -File | Measure-Object Length -Sum)
Write-Host ""
Write-Host ("Thu muc publish : {0} file, {1:0.0} MB" -f $files.Count, ($files.Sum / 1MB))
Write-Host ("Goi phat hanh   : {0} ({1:0.0} MB)" -f $zip, ((Get-Item $zip).Length / 1MB)) -ForegroundColor Green
