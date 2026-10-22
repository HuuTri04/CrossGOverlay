using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using CrosshairOverlay.Interop;
using CrosshairOverlay.Localization;
using CrosshairOverlay.Services.System;

namespace CrosshairOverlay.Views;

/// <summary>Short-lived branded notification shown above the primary taskbar.</summary>
public partial class SystemTrayToastWindow : Window
{
    private static readonly TimeSpan EntranceDuration = TimeSpan.FromMilliseconds(180);
    private static readonly TimeSpan ExitDuration = TimeSpan.FromMilliseconds(150);
    private readonly DispatcherTimer _dismissTimer;
    private readonly Action? _openSettings;
    private ContextMenu? _moreMenu;
    private bool _closing;

    public SystemTrayToastWindow(string title, string message, Action? openSettings = null)
    {
        InitializeComponent();
        TitleText.Text = title;
        BodyText.Text = message;
        _openSettings = openSettings;
        Focusable = false;

        _dismissTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            // 3.35 s visible + 0.15 s fade = closed about 3.5 s after ShowToast.
            Interval = TimeSpan.FromMilliseconds(3350),
        };
        _dismissTimer.Tick += OnDismissTimerTick;
        Closed += OnClosed;

        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            var style = (long)NativeMethods.GetWindowLongPtr(hwnd, Win32Constants.GWL_EXSTYLE);
            NativeMethods.SetWindowLongPtr(hwnd, Win32Constants.GWL_EXSTYLE,
                (nint)(style | Win32Constants.WS_EX_NOACTIVATE | Win32Constants.WS_EX_TOOLWINDOW));
        };
    }

    public void ShowToast()
    {
        // Measure the content offscreen before placing it above the primary taskbar.
        Left = -32000;
        Top = -32000;
        Show();
        UpdateLayout();
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right - Width - 12;
        Top = workArea.Bottom - ActualHeight - 12;

        Opacity = 1;
        BeginAnimation(OpacityProperty,
            UiAnimationFrameRate.Configure(new DoubleAnimation(0, 1, EntranceDuration)
                { FillBehavior = FillBehavior.Stop }));
        SlideTransform.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty,
            UiAnimationFrameRate.Configure(new DoubleAnimation(10, 0, EntranceDuration)
                { FillBehavior = FillBehavior.Stop }));
        _dismissTimer.Start();
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e) => Close();

    private void OnMoreClicked(object sender, RoutedEventArgs e)
    {
        if (_openSettings is null || sender is not Button button) return;

        if (_moreMenu is not null) _moreMenu.IsOpen = false;
        var openItem = new MenuItem { Header = Tr.Get("Tray_Open") };
        openItem.Click += (_, _) =>
        {
            Close();
            _openSettings();
        };
        _moreMenu = new ContextMenu
        {
            PlacementTarget = button,
            Placement = PlacementMode.Top,
        };
        _moreMenu.Items.Add(openItem);
        _moreMenu.IsOpen = true;
    }

    private void OnDismissTimerTick(object? sender, EventArgs e)
    {
        _dismissTimer.Stop();
        if (_closing) return;
        _closing = true;

        var animation = UiAnimationFrameRate.Configure(new DoubleAnimation(1, 0, ExitDuration));
        animation.Completed += (_, _) => Close();
        BeginAnimation(OpacityProperty, animation);
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _closing = true;
        if (_moreMenu is not null) _moreMenu.IsOpen = false;
        _moreMenu = null;
        _dismissTimer.Stop();
        _dismissTimer.Tick -= OnDismissTimerTick;
        Closed -= OnClosed;
    }
}
