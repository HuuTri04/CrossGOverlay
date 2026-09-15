using System.Runtime.InteropServices;
using CrosshairOverlay.Core.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace CrosshairOverlay.Services.System;

/// <inheritdoc cref="IHardwareInfoService"/>
/// <remarks>
/// <para>
/// Đọc từ registry và API chuẩn của Windows thay vì WMI (<c>System.Management</c>): WMI không có sẵn
/// trong .NET 8 (phải kéo thêm gói NuGet) và mỗi truy vấn tốn vài trăm mili-giây vì phải khởi động
/// dịch vụ COM. Các nguồn dưới đây cho đúng cùng thông tin trong vài mili-giây:
/// </para>
/// <list type="bullet">
/// <item>CPU: <c>ProcessorNameString</c> trong registry — chính là chuỗi <c>Win32_Processor.Name</c> đọc ra.</item>
/// <item>GPU: <c>EnumDisplayDevices</c> — tên bộ điều hợp như <c>Win32_VideoController.Name</c>.</item>
/// <item>RAM: <c>GetPhysicallyInstalledSystemMemory</c> (dung lượng lắp đặt) và <c>GlobalMemoryStatusEx</c>
/// (phần Windows dùng được) — giống dòng "Installed RAM" trong Settings của Windows.</item>
/// <item>Hệ điều hành: tên, phiên bản hiển thị và số build trong registry <c>CurrentVersion</c>.</item>
/// </list>
/// <para>
/// Phần cứng không đổi trong một phiên chạy, nên chỉ đọc MỘT lần (trên thread pool) và dùng lại kết
/// quả cho mọi lần mở cửa sổ Settings.
/// </para>
/// </remarks>
public sealed class HardwareInfoService : IHardwareInfoService
{
    private readonly ILogger<HardwareInfoService> _logger;
    private readonly Lazy<Task<HardwareInfo>> _info;

    public HardwareInfoService(ILogger<HardwareInfoService> logger)
    {
        _logger = logger;
        _info = new Lazy<Task<HardwareInfo>>(() => Task.Run(Read), LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public Task<HardwareInfo> GetAsync() => _info.Value;

    private HardwareInfo Read()
    {
        var started = global::System.Diagnostics.Stopwatch.StartNew();

        var info = new HardwareInfo(
            OsName: Safe(ReadOsName, null),
            OsBuild: Safe(ReadOsBuild, null),
            Is64BitOs: Environment.Is64BitOperatingSystem,
            CpuName: Safe(ReadCpuName, null),
            LogicalProcessors: Environment.ProcessorCount,
            Gpus: Safe(ReadGpuNames, []),
            InstalledRamBytes: Safe(ReadInstalledRam, 0UL),
            UsableRamBytes: Safe(ReadUsableRam, 0UL));

        _logger.LogDebug("Đọc thông tin phần cứng trong {Elapsed} ms.", started.ElapsedMilliseconds);
        return info;
    }

    /// <summary>Mỗi nguồn hỏng riêng lẻ (registry bị chặn, API lỗi) chỉ làm trống đúng một dòng.</summary>
    private T Safe<T>(Func<T> read, T fallback)
    {
        try
        {
            return read();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Không đọc được một mục thông tin phần cứng.");
            return fallback;
        }
    }

    // ------------------------------------------------------------------ hệ điều hành

    private const string CurrentVersionKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion";

    private static string? ReadOsName()
    {
        using var key = Registry.LocalMachine.OpenSubKey(CurrentVersionKey);
        var product = (key?.GetValue("ProductName") as string)?.Trim();
        if (string.IsNullOrEmpty(product)) return null;

        // Windows 11 vẫn ghi "Windows 10 ..." trong ProductName (Microsoft giữ nguyên để tương thích);
        // phân biệt bằng số build: từ 22000 trở lên là Windows 11.
        if (int.TryParse(key?.GetValue("CurrentBuildNumber") as string, out var build) && build >= 22000
            && product.StartsWith("Windows 10", StringComparison.OrdinalIgnoreCase))
        {
            product = "Windows 11" + product["Windows 10".Length..];
        }

        var display = (key?.GetValue("DisplayVersion") as string)?.Trim();
        return string.IsNullOrEmpty(display) ? product : $"{product} {display}";
    }

    private static string? ReadOsBuild()
    {
        using var key = Registry.LocalMachine.OpenSubKey(CurrentVersionKey);
        var build = key?.GetValue("CurrentBuildNumber") as string;
        if (string.IsNullOrEmpty(build)) return Environment.OSVersion.Version.Build.ToString();

        return key?.GetValue("UBR") is int ubr ? $"{build}.{ubr}" : build;
    }

    // ------------------------------------------------------------------ CPU

    private static string? ReadCpuName()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
        var name = key?.GetValue("ProcessorNameString") as string;

        // Một số CPU đệm thêm khoảng trắng; chuẩn hoá về một khoảng.
        return string.IsNullOrWhiteSpace(name)
            ? Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER")
            : string.Join(' ', name.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    // ------------------------------------------------------------------ GPU

    private const uint DisplayDeviceMirroringDriver = 0x00000008;

    private static IReadOnlyList<string> ReadGpuNames()
    {
        var names = new List<string>();
        var device = new DisplayDevice { cb = Marshal.SizeOf<DisplayDevice>() };

        for (uint index = 0; EnumDisplayDevices(null, index, ref device, 0); index++)
        {
            var name = device.DeviceString?.Trim();

            // Bỏ driver "gương" (phần mềm điều khiển từ xa) và bộ điều hợp ảo của Remote Desktop.
            if (!string.IsNullOrEmpty(name)
                && (device.StateFlags & DisplayDeviceMirroringDriver) == 0
                && !name.Contains("Remote Display", StringComparison.OrdinalIgnoreCase)
                && !names.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                names.Add(name);
            }

            device.cb = Marshal.SizeOf<DisplayDevice>();
        }

        return names;
    }

    // ------------------------------------------------------------------ RAM

    private static ulong ReadInstalledRam() =>
        GetPhysicallyInstalledSystemMemory(out var kilobytes) ? kilobytes * 1024UL : 0UL;

    private static ulong ReadUsableRam()
    {
        var status = new MemoryStatusEx { dwLength = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        return GlobalMemoryStatusEx(ref status) ? status.ullTotalPhys : 0UL;
    }

    // ------------------------------------------------------------------ interop

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DisplayDevice
    {
        public int cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public uint StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("user32.dll", EntryPoint = "EnumDisplayDevicesW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayDevices(string? lpDevice, uint iDevNum, ref DisplayDevice lpDisplayDevice, uint dwFlags);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetPhysicallyInstalledSystemMemory(out ulong totalMemoryInKilobytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx lpBuffer);
}
