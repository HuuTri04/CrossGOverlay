<#
.SYNOPSIS
    Dung file .ico da kich thuoc tu mot anh PNG nguon.

.DESCRIPTION
    Vi sao can script nay: mot file PNG chi doi duoi thanh .ico KHONG phai la file ICO.
    Trinh bien dich C# se tu choi voi loi "CS7065: Icon stream is not in the expected format",
    va System.Drawing.Icon cung khong doc duoc. ICO la mot container rieng: header ICONDIR,
    bang ICONDIRENTRY, roi du lieu tung khung.

    Script ghi cac khung duoi dang DIB 32-bit (BGRA) thay vi nhung PNG vao trong ICO. DIB duoc
    moi phien ban Windows va moi trinh bien dich chap nhan; khung PNG thi mot so cong cu cu
    khong doc duoc.

.EXAMPLE
    .\build\make-icon.ps1 -Source src\CrosshairOverlay\images\app.source.png `
                          -Destination src\CrosshairOverlay\images\app.ico
#>

[CmdletBinding()]
param(
    [string] $Source = "src\CrosshairOverlay\images\app.source.png",
    [string] $Destination = "src\CrosshairOverlay\images\app.ico",
    [int[]]  $Sizes = @(16, 24, 32, 48, 64, 128, 256)
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $Source)) { throw "Khong tim thay anh nguon: $Source" }
$Source = (Resolve-Path $Source).Path

$code = @"
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

public static class IcoBuilder
{
    public static void Build(string sourcePath, string destPath, int[] sizes)
    {
        using (var source = Image.FromFile(sourcePath))
        {
            var frames = new List<byte[]>();
            foreach (var size in sizes) frames.Add(BuildFrame(source, size));

            using (var fs = File.Create(destPath))
            using (var bw = new BinaryWriter(fs))
            {
                bw.Write((short)0);               // reserved
                bw.Write((short)1);               // type = icon
                bw.Write((short)sizes.Length);

                int offset = 6 + 16 * sizes.Length;
                for (int i = 0; i < sizes.Length; i++)
                {
                    int s = sizes[i];
                    bw.Write((byte)(s >= 256 ? 0 : s));  // 0 nghia la 256
                    bw.Write((byte)(s >= 256 ? 0 : s));
                    bw.Write((byte)0);                   // so mau trong bang mau
                    bw.Write((byte)0);                   // reserved
                    bw.Write((short)1);                  // planes
                    bw.Write((short)32);                 // bit per pixel
                    bw.Write(frames[i].Length);
                    bw.Write(offset);
                    offset += frames[i].Length;
                }

                foreach (var frame in frames) bw.Write(frame);
            }
        }
    }

    private static byte[] BuildFrame(Image source, int size)
    {
        using (var scaled = new Bitmap(size, size, PixelFormat.Format32bppArgb))
        {
            using (var g = Graphics.FromImage(scaled))
            {
                g.Clear(Color.Transparent);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.DrawImage(source, new Rectangle(0, 0, size, size));
            }
            return BuildDib(scaled);
        }
    }

    private static byte[] BuildDib(Bitmap bmp)
    {
        int w = bmp.Width, h = bmp.Height;
        int xorStride = w * 4;
        int andStride = ((w + 31) / 32) * 4;   // moi dong mat na can can chinh 4 byte
        int xorSize = xorStride * h;
        int andSize = andStride * h;

        using (var ms = new MemoryStream())
        using (var bw = new BinaryWriter(ms))
        {
            bw.Write(40);                  // BITMAPINFOHEADER.biSize
            bw.Write(w);
            bw.Write(h * 2);               // cao gap doi: phan mau + phan mat na
            bw.Write((short)1);
            bw.Write((short)32);
            bw.Write(0);                   // khong nen
            bw.Write(xorSize + andSize);
            bw.Write(0); bw.Write(0); bw.Write(0); bw.Write(0);

            var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                var row = new byte[xorStride];
                for (int y = h - 1; y >= 0; y--)   // DIB luu tu duoi len tren
                {
                    Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, xorStride);
                    bw.Write(row);
                }
            }
            finally { bmp.UnlockBits(data); }

            // Mat na toan 0: alpha cua anh 32-bit da lo phan trong suot.
            bw.Write(new byte[andSize]);
            bw.Flush();
            return ms.ToArray();
        }
    }
}
"@

Add-Type -TypeDefinition $code -Language CSharp -ReferencedAssemblies System.Drawing

$destFull = [IO.Path]::GetFullPath((Join-Path (Get-Location) $Destination))
[IcoBuilder]::Build($Source, $destFull, $Sizes)

$bytes = [IO.File]::ReadAllBytes($destFull)
$frames = [BitConverter]::ToUInt16($bytes, 4)

Write-Host ("Da tao {0}" -f $destFull) -ForegroundColor Green
Write-Host ("  {0:N0} bytes, {1} khung: {2}" -f $bytes.Length, $frames, ($Sizes -join ', '))
