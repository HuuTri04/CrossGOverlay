using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using Microsoft.Extensions.Logging;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace CrosshairOverlay.Services.System;

/// <summary>Windows owns the toast surface, theme, placement and Notification Center history.</summary>
internal sealed class WindowsNotificationService : IDisposable
{
    private readonly ILogger _logger;
    private readonly Action _openSettings;
    private AppNotificationManager? _manager;
    private bool _registered;

    public WindowsNotificationService(ILogger logger, Action openSettings)
    {
        _logger = logger;
        _openSettings = openSettings;
    }

    public void Initialize()
    {
        if (_registered) return;
        try
        {
            var manager = AppNotificationManager.Default;
            if (!AppNotificationManager.IsSupported())
            {
                _logger.LogInformation("Windows App Notifications không được hỗ trợ trong phiên này; dùng thông báo khay native.");
                return;
            }

            // The handler must be attached before Register, including for unpackaged apps.
            manager.NotificationInvoked += OnNotificationInvoked;
            try
            {
                // Register reads the app name and embedded app.ico from the executable.
                // For unpackaged WPF, Windows App SDK sets up COM activation automatically.
                manager.Register();
                _manager = manager;
                _registered = true;
            }
            catch
            {
                manager.NotificationInvoked -= OnNotificationInvoked;
                throw;
            }
            _logger.LogInformation("Đã đăng ký Windows App Notifications.");
        }
        catch (Exception ex)
        {
            // Admin elevation and unavailable Windows App Runtime cannot break the tray.
            _logger.LogWarning(ex, "Không đăng ký được thông báo Windows; dùng thông báo khay native.");
        }
    }

    /// <returns>True when Windows handled the request, including when the user disabled notifications.</returns>
    public bool TryShow(string title, string message)
    {
        if (!_registered || _manager is null) return false;

        try
        {
            // Respect per-app/system notification policy; do not bypass Focus Assist with a popup.
            var setting = _manager.Setting;
            if (setting != AppNotificationSetting.Enabled)
            {
                _logger.LogInformation("Windows đang chặn thông báo cho ứng dụng: {Setting}.", setting);
                return true;
            }

            var builder = new AppNotificationBuilder()
                .AddArgument("action", "open-settings")
                .AddText(title)
                .AddText(message);

            if (GetExistingLogoUri(AppContext.BaseDirectory) is { } logo)
                builder.SetAppLogoOverride(logo);
            else
                _logger.LogWarning("Không có icon.png vật lý trong Resources; gửi thông báo chỉ có chữ.");

            var notification = builder.BuildNotification();
            // A repeat replaces its own entry without discarding unrelated warnings.
            var identity = SHA256.HashData(Encoding.UTF8.GetBytes(title + '\n' + message));
            notification.Tag = "crossgo-" + Convert.ToHexString(identity.AsSpan(0, 8));
            _manager.Show(notification);
            _logger.LogInformation("Đã gửi thông báo tới Windows: {Title} (Tag={Tag}).", title, notification.Tag);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Không gửi được thông báo Windows; dùng thông báo khay native.");
            return false;
        }
    }

    private void OnNotificationInvoked(AppNotificationManager sender, AppNotificationActivatedEventArgs args)
    {
        if (!args.Argument.Contains("action=open-settings", StringComparison.Ordinal)) return;
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.HasShutdownStarted || App.IsShuttingDown) return;
        try
        {
            _ = dispatcher.BeginInvoke(new Action(() =>
            {
                if (!App.IsShuttingDown) _openSettings();
            }));
        }
        catch (InvalidOperationException) { } // Activation arrived during dispatcher shutdown.
    }

    internal static Uri? GetExistingLogoUri(string baseDirectory)
    {
        try
        {
            // Windows cannot resolve a WPF pack URI. This is copied beside the EXE by MSBuild.
            var file = Path.Combine(baseDirectory, "Resources", "icon.png");
            if (!File.Exists(file) || new FileInfo(file).Length <= 8) return null;
            return new Uri(file, UriKind.Absolute);
        }
        catch (Exception)
        {
            // Windows can still show the application icon registered from the EXE.
            return null;
        }
    }

    public void Dispose()
    {
        if (!_registered || _manager is null) return;
        _registered = false;
        try
        {
            _manager.NotificationInvoked -= OnNotificationInvoked;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Không thể gỡ callback thông báo Windows lúc thoát.");
        }
        try
        {
            _manager.Unregister();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Không thể gỡ đăng ký thông báo Windows lúc thoát.");
        }
        _manager = null;
    }
}
