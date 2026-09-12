using System.Runtime.InteropServices;
using System.Windows;

namespace CrosshairOverlay.Interop;

[StructLayout(LayoutKind.Sequential)]
internal struct RECT
{
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;

    public readonly int Width => Right - Left;
    public readonly int Height => Bottom - Top;

    /// <summary>Đổi sang <see cref="Rect"/> của WPF. Giá trị vẫn là PHYSICAL pixel, chưa chia DPI.</summary>
    public readonly Rect ToRect() => new(Left, Top, Math.Max(0, Width), Math.Max(0, Height));
}

[StructLayout(LayoutKind.Sequential)]
internal struct POINT
{
    public int X;
    public int Y;
}

/// <summary>
/// Biến thể EX của MONITORINFO — có thêm <see cref="szDevice"/>, định danh bền để ghim màn hình.
/// </summary>
[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct MONITORINFOEXW
{
    public int cbSize;
    public RECT rcMonitor;
    public RECT rcWork;
    public uint dwFlags;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
    public string szDevice;

    public static MONITORINFOEXW Create() => new()
    {
        cbSize = Marshal.SizeOf<MONITORINFOEXW>(),
        szDevice = string.Empty,
    };
}

internal enum MonitorDpiType
{
    EffectiveDpi = 0,
    AngularDpi = 1,
    RawDpi = 2,
}
