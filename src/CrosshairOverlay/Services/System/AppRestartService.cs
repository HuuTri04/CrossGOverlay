using System.Globalization;
using CrosshairOverlay.Core;
using CrosshairOverlay.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace CrosshairOverlay.Services.System;

/// <inheritdoc cref="IAppRestartService"/>
public sealed class AppRestartService : IAppRestartService
{
    private readonly ILogger<AppRestartService> _logger;
    private readonly Action _shutdown;

    /// <param name="shutdown">Đường thoát duy nhất của ứng dụng, do composition root cung cấp.</param>
    public AppRestartService(ILogger<AppRestartService> logger, Action shutdown)
    {
        _logger = logger;
        _shutdown = shutdown;
    }

    public bool RestartWithFactoryReset(out string? error) => RestartCore(factoryReset: true, out error);

    public bool Restart(out string? error) => RestartCore(factoryReset: false, out error);

    private bool RestartCore(bool factoryReset, out string? error)
    {
        var executable = Environment.ProcessPath;
        if (string.IsNullOrEmpty(executable))
        {
            error = "Không xác định được đường dẫn ứng dụng.";
            return false;
        }

        try
        {
            var startInfo = new global::System.Diagnostics.ProcessStartInfo(executable)
            {
                UseShellExecute = false,
                WorkingDirectory = AppContext.BaseDirectory,
            };
            if (factoryReset) startInfo.ArgumentList.Add(StartupArguments.FactoryResetSwitch);
            startInfo.ArgumentList.Add(StartupArguments.WaitPidSwitch);
            startInfo.ArgumentList.Add(Environment.ProcessId.ToString(CultureInfo.InvariantCulture));

            using var process = global::System.Diagnostics.Process.Start(startInfo);
        }
        catch (Exception ex) when (ex is global::System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            _logger.LogError(ex, "Không khởi động lại được ứng dụng.");
            error = ex.Message;
            return false;
        }

        _logger.LogWarning("Khởi động lại{Reason}: đã mở instance mới, instance này thoát.",
            factoryReset ? " để khôi phục cài đặt gốc" : string.Empty);
        error = null;
        _shutdown();
        return true;
    }
}
