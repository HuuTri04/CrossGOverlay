<#
.SYNOPSIS
    Ký số (Authenticode) CrossGOverlay.exe và CrossGOverlay.dll trong thư mục publish.

.DESCRIPTION
    ĐỌC KỸ TRƯỚC KHI DÙNG — ký số giải quyết được gì và KHÔNG giải quyết được gì:

      * Chứng chỉ TỰ KÝ (-SelfSigned): chỉ dùng để thử quy trình trên máy mình.
        KHÔNG làm SmartScreen hết chặn, và máy người khác vẫn coi file là không đáng tin,
        vì họ không có chứng chỉ gốc của bạn.

      * Chứng chỉ OV (Organization Validation) do CA cấp: bỏ được dòng "Nhà phát hành không
        xác định". SmartScreen vẫn có thể cảnh báo cho tới khi file tích luỹ đủ uy tín qua
        lượt tải.

      * Chứng chỉ EV (Extended Validation): được SmartScreen tin ngay từ lượt tải đầu tiên.
        Đây là thứ duy nhất loại bỏ cảnh báo tức thì.

    Luôn đóng dấu thời gian (timestamp). Không có nó, chữ ký hết hiệu lực ngay khi chứng chỉ
    hết hạn; có nó thì chữ ký vẫn hợp lệ vĩnh viễn với những bản đã ký trước lúc hết hạn.

.EXAMPLE
    # Thử quy trình bằng chứng chỉ tự ký
    .\build\sign.ps1 -SelfSigned

.EXAMPLE
    # Ký bằng chứng chỉ thật
    .\build\sign.ps1 -PfxPath C:\certs\company.pfx -PfxPassword (Read-Host -AsSecureString)
#>

[CmdletBinding()]
param(
    # Thu muc publish (ky CrossGOverlay.exe va CrossGOverlay.dll ben trong), hoac duong dan mot file cu the.
    [string] $Path = "src\CrosshairOverlay\bin\Release\net8.0-windows\win-x64\publish",
    [string] $PfxPath,
    [securestring] $PfxPassword,
    [switch] $SelfSigned,
    [string] $TimestampUrl = "http://timestamp.digicert.com"
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $Path)) {
    throw "Khong tim thay: $Path`nChay 'dotnet publish -c Release' truoc."
}

$Path = (Resolve-Path $Path).Path

# Ban phat hanh dang THU MUC: ma cua ung dung nam trong CrossGOverlay.dll, CrossGOverlay.exe chi la file
# khoi chay. Ky ca hai — SmartScreen xet file .exe, con chu ky tren .dll cho biet ma ben trong khong bi sua.
$files = if (Test-Path $Path -PathType Container) {
    @("CrossGOverlay.exe", "CrossGOverlay.dll") | ForEach-Object {
        $f = Join-Path $Path $_
        if (-not (Test-Path $f)) { throw "Thu muc publish thieu $_ : $Path" }
        $f
    }
} else {
    @($Path)
}

Write-Host "File can ky:"
$files | ForEach-Object { Write-Host "  $_" }

# ---------------------------------------------------------------- lay chung chi

$certificate = $null

if ($SelfSigned) {
    Write-Warning "Dang dung chung chi TU KY. Chi hop le tren may nay, KHONG bo duoc SmartScreen."

    $subject = "CN=CrosshairOverlay Dev"
    $certificate = Get-ChildItem Cert:\CurrentUser\My |
        Where-Object { $_.Subject -eq $subject -and $_.NotAfter -gt (Get-Date) } |
        Select-Object -First 1

    if (-not $certificate) {
        Write-Host "Tao chung chi tu ky moi..."
        $certificate = New-SelfSignedCertificate `
            -Type CodeSigningCert `
            -Subject $subject `
            -CertStoreLocation Cert:\CurrentUser\My `
            -NotAfter (Get-Date).AddYears(3)
    }
}
elseif ($PfxPath) {
    if (-not (Test-Path $PfxPath)) { throw "Khong tim thay file PFX: $PfxPath" }

    $certificate = if ($PfxPassword) {
        Get-PfxCertificate -FilePath $PfxPath -Password $PfxPassword
    } else {
        Get-PfxCertificate -FilePath $PfxPath
    }
}
else {
    throw "Phai truyen -PfxPath (chung chi that) hoac -SelfSigned (chi de thu quy trinh)."
}

Write-Host "Chung chi: $($certificate.Subject)  (het han $($certificate.NotAfter))"

# ---------------------------------------------------------------- ky

foreach ($file in $files) {
    $result = Set-AuthenticodeSignature `
        -FilePath $file `
        -Certificate $certificate `
        -TimestampServer $TimestampUrl `
        -HashAlgorithm SHA256

    if ($result.Status -ne "Valid") {
        throw "Ky that bai ($file): $($result.Status) - $($result.StatusMessage)"
    }

    Write-Host "Da ky: $file" -ForegroundColor Green
}

# ---------------------------------------------------------------- kiem tra lai

foreach ($file in $files) {
    $check = Get-AuthenticodeSignature -FilePath $file
    Write-Host ""
    Write-Host "File          : $file"
    Write-Host "Trang thai    : $($check.Status)"
    Write-Host "Nguoi ky      : $($check.SignerCertificate.Subject)"
    Write-Host "Dau thoi gian : $(if ($check.TimeStamperCertificate) { 'co' } else { 'KHONG CO' })"

    if (-not $check.TimeStamperCertificate) {
        Write-Warning "Khong dong dau duoc thoi gian. Chu ky se het hieu luc khi chung chi het han."
    }
}
