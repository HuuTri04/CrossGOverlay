using CrosshairOverlay.Core.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace CrosshairOverlay.Services.System;

/// <inheritdoc cref="IStartupService"/>
public sealed class StartupService : IStartupService
{
    /// <remarks>
    /// Dùng HKEY_CURRENT_USER chứ không phải HKEY_LOCAL_MACHINE: ghi vào HKCU không cần quyền
    /// admin, khớp với việc app chạy ở mức <c>asInvoker</c>.
    /// </remarks>
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    private const string ValueName = "CrosshairOverlay";

    private readonly ILogger<StartupService> _logger;

    public StartupService(ILogger<StartupService> logger) => _logger = logger;

    /// <summary>
    /// Registry là nguồn chân lý, không phải settings.json — người dùng có thể tắt mục khởi
    /// động qua Task Manager mà app không hề hay biết.
    /// </summary>
    public bool IsEnabled
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
                var value = key?.GetValue(ValueName) as string;
                if (string.IsNullOrWhiteSpace(value)) return false;

                // So khớp cả đường dẫn: mục cũ trỏ tới bản cài ở chỗ khác thì coi như chưa bật.
                var current = GetExecutablePath();
                return current is not null
                    && value.Contains(current, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Không đọc được khoá khởi động cùng Windows.");
                return false;
            }
        }
    }

    public bool SetEnabled(bool enabled)
    {
        var path = GetExecutablePath();
        if (path is null)
        {
            _logger.LogWarning("Không xác định được đường dẫn file thực thi.");
            return false;
        }

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                ?? Registry.CurrentUser.CreateSubKey(RunKeyPath);

            if (key is null)
            {
                _logger.LogWarning("Không mở được khoá Run của registry.");
                return false;
            }

            if (enabled)
            {
                // Bọc dấu nháy: đường dẫn có dấu cách sẽ bị Windows cắt thành nhiều tham số.
                key.SetValue(ValueName, $"\"{path}\"", RegistryValueKind.String);
                _logger.LogInformation("Đã bật khởi động cùng Windows.");
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
                _logger.LogInformation("Đã tắt khởi động cùng Windows.");
            }

            return true;
        }
        catch (Exception ex)
        {
            // Chính sách nhóm có thể chặn ghi khoá Run. Báo thất bại chứ không ném exception —
            // đây là một tuỳ chọn phụ, không đáng làm hỏng cả phiên làm việc.
            _logger.LogWarning(ex, "Không ghi được khoá khởi động cùng Windows.");
            return false;
        }
    }

    private static string? GetExecutablePath()
    {
        // Environment.ProcessPath trỏ tới file .exe apphost, đúng thứ cần đưa vào khoá Run.
        var path = Environment.ProcessPath;
        return string.IsNullOrWhiteSpace(path) ? null : path;
    }
}
