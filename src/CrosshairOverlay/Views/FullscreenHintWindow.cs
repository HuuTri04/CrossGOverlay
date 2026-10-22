using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using CrosshairOverlay.Core.Models;
using CrosshairOverlay.Interop;

namespace CrosshairOverlay.Views;

/// <summary>Short-lived, nonactivating hint; no timer or HWND survives its four-second display.</summary>
internal sealed class FullscreenHintWindow : Window
{
    private readonly DispatcherTimer _dismiss = new(DispatcherPriority.Background)
    {
        Interval = TimeSpan.FromSeconds(4),
    };

    public FullscreenHintWindow(MonitorInfo monitor, string message)
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        IsHitTestVisible = false;
        Topmost = true;
        Width = 440;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = -32000;
        Top = -32000;
        Content = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(235, 24, 28, 38)),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16, 12, 16, 12),
            Child = new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap,
                Foreground = Brushes.White,
                FontSize = 13,
            },
        };
        SourceInitialized += (_, _) =>
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            var style = (long)NativeMethods.GetWindowLongPtr(hwnd, Win32Constants.GWL_EXSTYLE);
            NativeMethods.SetWindowLongPtr(hwnd, Win32Constants.GWL_EXSTYLE,
                (nint)(style | Win32Constants.WS_EX_NOACTIVATE | Win32Constants.WS_EX_TOOLWINDOW));
        };
        Loaded += (_, _) =>
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            var width = (int)Math.Ceiling(ActualWidth * monitor.DpiScaleX);
            var height = (int)Math.Ceiling(ActualHeight * monitor.DpiScaleY);
            NativeMethods.SetWindowPos(hwnd, Win32Constants.HWND_TOPMOST,
                (int)monitor.WorkArea.Right - width - 20, (int)monitor.WorkArea.Top + 20,
                width, height, Win32Constants.SWP_NOACTIVATE | Win32Constants.SWP_NOOWNERZORDER);
        };
        _dismiss.Tick += OnDismiss;
        Closed += (_, _) =>
        {
            _dismiss.Stop();
            _dismiss.Tick -= OnDismiss;
        };
    }

    private void OnDismiss(object? sender, EventArgs e) => Close();

    public void ShowForFourSeconds()
    {
        Show();
        _dismiss.Start();
    }
}
