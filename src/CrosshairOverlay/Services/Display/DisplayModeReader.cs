using System.Runtime.InteropServices;
using CrosshairOverlay.Core.Abstractions;

namespace CrosshairOverlay.Services.Display;

/// <inheritdoc cref="IDisplayModeReader"/>
/// <remarks>
/// Dùng <c>EnumDisplaySettings</c> — API chuẩn, trả về đúng chế độ hiển thị đang chạy (độ phân giải
/// thật, tần số quét) trong vài micro-giây. Không cần WinForms (<c>Screen.PrimaryScreen</c>) hay WMI
/// (<c>Win32_VideoController</c> cần thêm gói NuGet và mất vài trăm mili-giây mỗi truy vấn).
/// </remarks>
public sealed class DisplayModeReader : IDisplayModeReader
{
    private const int EnumCurrentSettings = -1;

    public DisplayMode? Read(string? deviceName)
    {
        if (string.IsNullOrWhiteSpace(deviceName)) return null;

        var mode = new DevMode { dmSize = (short)Marshal.SizeOf<DevMode>() };
        if (!EnumDisplaySettings(deviceName, EnumCurrentSettings, ref mode)) return null;
        if (mode.dmPelsWidth <= 0 || mode.dmPelsHeight <= 0) return null;

        // 0 hoặc 1 nghĩa là "mặc định của phần cứng" — không phải một tần số thật.
        var refresh = mode.dmDisplayFrequency > 1 ? mode.dmDisplayFrequency : 0;
        return new DisplayMode(mode.dmPelsWidth, mode.dmPelsHeight, refresh);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DevMode
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public short dmSpecVersion;
        public short dmDriverVersion;
        public short dmSize;
        public short dmDriverExtra;
        public int dmFields;
        public int dmPositionX;
        public int dmPositionY;
        public int dmDisplayOrientation;
        public int dmDisplayFixedOutput;
        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public short dmLogPixels;
        public int dmBitsPerPel;
        public int dmPelsWidth;
        public int dmPelsHeight;
        public int dmDisplayFlags;
        public int dmDisplayFrequency;
        public int dmICMMethod;
        public int dmICMIntent;
        public int dmMediaType;
        public int dmDitherType;
        public int dmReserved1;
        public int dmReserved2;
        public int dmPanningWidth;
        public int dmPanningHeight;
    }

    [DllImport("user32.dll", EntryPoint = "EnumDisplaySettingsW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplaySettings(string lpszDeviceName, int iModeNum, ref DevMode lpDevMode);
}
