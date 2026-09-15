using System.IO;
using CrosshairOverlay.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace CrosshairOverlay.Services.System;

/// <inheritdoc cref="IProcessLauncher"/>
public sealed class ProcessLauncher : IProcessLauncher
{
    private readonly ILogger<ProcessLauncher> _logger;

    public ProcessLauncher(ILogger<ProcessLauncher> logger) => _logger = logger;

    public void Launch(string fileNameOrPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileNameOrPath);

        var startInfo = Path.IsPathRooted(fileNameOrPath)
            ? new global::System.Diagnostics.ProcessStartInfo(fileNameOrPath)
            {
                // Qua shell: game đòi quyền admin sẽ hiện hộp thoại UAC thay vì báo lỗi 740, và thư
                // mục làm việc là thư mục của game — nhiều game không chạy nếu thiếu điều này.
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(fileNameOrPath) ?? string.Empty,
            }
            : new global::System.Diagnostics.ProcessStartInfo(fileNameOrPath)
            {
                // Tìm theo PATH/System32 như Process.Start("notepad.exe"); không có thì ném Win32Exception.
                UseShellExecute = false,
            };

        // Không giữ đối tượng Process: ứng dụng này không theo dõi hay điều khiển tiến trình vừa mở.
        using var process = global::System.Diagnostics.Process.Start(startInfo);
        _logger.LogInformation("Đã mở {App} để test tâm ngắm.", fileNameOrPath);
    }
}
