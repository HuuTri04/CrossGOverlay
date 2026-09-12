using System.Text;
using CrosshairOverlay.Core.Abstractions;
using CrosshairOverlay.Interop;
using Microsoft.Extensions.Logging;

namespace CrosshairOverlay.Services.Process;

/// <inheritdoc cref="IRunningApplicationScanner"/>
public sealed class RunningApplicationScanner : IRunningApplicationScanner
{
    private const int MaxPathLength = 1024;

    private readonly ILogger<RunningApplicationScanner> _logger;

    public RunningApplicationScanner(ILogger<RunningApplicationScanner> logger) => _logger = logger;

    public IReadOnlyList<RunningApplication> Scan()
    {
        var ownProcessId = Environment.ProcessId;
        var buffer = new StringBuilder(MaxPathLength);

        // Gộp theo tên tiến trình: Chrome hay Discord đẻ ra hàng chục tiến trình con, người
        // dùng chỉ cần thấy một dòng.
        var byName = new Dictionary<string, RunningApplication>(StringComparer.OrdinalIgnoreCase);

        foreach (var process in global::System.Diagnostics.Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    if (process.Id == ownProcessId) continue;

                    // Điều kiện lọc chính: phải có cửa sổ giao diện.
                    if (process.MainWindowHandle == nint.Zero) continue;

                    var title = process.MainWindowTitle;
                    if (string.IsNullOrWhiteSpace(title)) continue;

                    var name = process.ProcessName + ".exe";
                    if (byName.ContainsKey(name)) continue;

                    byName[name] = new RunningApplication(
                        process.Id, name, title, TryGetPath(process.Id, buffer));
                }
                catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
                {
                    // Tiến trình vừa thoát ngay giữa lúc đang đọc — chuyện bình thường.
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Bỏ qua một tiến trình khi quét.");
                }
            }
        }

        return [.. byName.Values.OrderBy(a => a.ProcessName, StringComparer.CurrentCultureIgnoreCase)];
    }

    /// <summary>
    /// Quyền tối thiểu đủ đọc tên file. Tiến trình chạy quyền admin sẽ từ chối — trả null và
    /// đi tiếp, vì so khớp theo tiêu đề cửa sổ vẫn dùng được.
    /// </summary>
    private static string? TryGetPath(int processId, StringBuilder buffer)
    {
        var handle = NativeMethods.OpenProcess(
            Win32Constants.PROCESS_QUERY_LIMITED_INFORMATION, false, (uint)processId);

        if (handle == 0) return null;

        try
        {
            buffer.Clear();
            buffer.EnsureCapacity(MaxPathLength);
            var size = (uint)buffer.Capacity;

            return NativeMethods.QueryFullProcessImageName(handle, 0, buffer, ref size)
                ? buffer.ToString()
                : null;
        }
        finally
        {
            NativeMethods.CloseHandle(handle);
        }
    }
}
