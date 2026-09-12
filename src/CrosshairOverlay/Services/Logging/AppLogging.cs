using System.IO;
using System.Text;
using CrosshairOverlay.Core.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace CrosshairOverlay.Services.Logging;

/// <summary>
/// Dựng hệ thống log ghi ra file xoay vòng theo ngày trong
/// <c>%LOCALAPPDATA%\CrosshairOverlay\logs</c>.
/// </summary>
/// <remarks>
/// Mức log điều khiển qua <see cref="LoggingLevelSwitch"/> chứ không cố định lúc tạo, vì có
/// bài toán con-gà-quả-trứng: log phải sẵn sàng TRƯỚC khi đọc được settings.json (bản thân
/// việc đọc file đó cũng cần ghi log), nhưng mức log lại nằm trong chính file ấy. Level switch
/// cho phép chỉnh lại sau khi settings đã nạp xong.
/// </remarks>
public sealed class AppLogging : IDisposable
{
    private const string OutputTemplate =
        "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}";

    private readonly Logger? _logger;
    private readonly LoggingLevelSwitch? _levelSwitch;
    private bool _disposed;

    private AppLogging(ILoggerFactory factory, Logger? logger, LoggingLevelSwitch? levelSwitch)
    {
        Factory = factory;
        _logger = logger;
        _levelSwitch = levelSwitch;
    }

    public ILoggerFactory Factory { get; }

    /// <summary>Đường dẫn file log hiện tại, hoặc null nếu không dựng được log.</summary>
    public string? LogDirectory { get; private init; }

    public static AppLogging Create(IAppPathProvider paths)
    {
        try
        {
            Directory.CreateDirectory(paths.LogsDirectory);

            var levelSwitch = new LoggingLevelSwitch(LogEventLevel.Information);

            var logger = new LoggerConfiguration()
                .MinimumLevel.ControlledBy(levelSwitch)
                .WriteTo.File(
                    Path.Combine(paths.LogsDirectory, "crosshair-.log"),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 7,
                    shared: true,
                    outputTemplate: OutputTemplate,
                    // Ghi kèm BOM UTF-8. Không có BOM thì Notepad cũ và PowerShell 5.1 đọc
                    // file theo bảng mã ANSI, và mọi thông điệp tiếng Việt thành ký tự rác —
                    // đúng lúc người dùng mở log ra để tìm nguyên nhân sự cố.
                    encoding: new UTF8Encoding(encoderShouldEmitUTF8Identifier: true))
                .CreateLogger();

            var factory = LoggerFactory.Create(builder =>
            {
                builder.ClearProviders();
                builder.AddSerilog(logger, dispose: false);
            });

            return new AppLogging(factory, logger, levelSwitch) { LogDirectory = paths.LogsDirectory };
        }
        catch (Exception)
        {
            // Đĩa đầy, thư mục bị khoá quyền… — không ghi được log thì vẫn phải chạy được app.
            return new AppLogging(NullLoggerFactory.Instance, null, null);
        }
    }

    /// <summary>Áp mức log người dùng chọn. Chuỗi không hợp lệ giữ nguyên mức hiện tại.</summary>
    public void SetLevel(string? levelName)
    {
        if (_levelSwitch is null || string.IsNullOrWhiteSpace(levelName)) return;

        if (Enum.TryParse<LogEventLevel>(levelName, ignoreCase: true, out var level))
            _levelSwitch.MinimumLevel = level;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Factory.Dispose();
        _logger?.Dispose();
    }
}
