using System.Windows;
using CrosshairOverlay.Core.Abstractions;
using CrosshairOverlay.Core.Models;
using CrosshairOverlay.Views;
using CrosshairOverlay.Views.Dialogs;
using CrosshairOverlay.ViewModels;
using Microsoft.Win32;

namespace CrosshairOverlay.Services.System;

/// <inheritdoc cref="IDialogService"/>
/// <remarks>
/// Tồn tại để ViewModel không phải gọi thẳng <see cref="MessageBox"/> hay
/// <see cref="OpenFileDialog"/> — nhờ vậy ViewModel vẫn unit-test được.
/// </remarks>
public sealed class DialogService : IDialogService
{
    private readonly Func<Window> _settingsWindowFactory;
    private readonly Func<UpdateInfo, Window>? _updateDialogFactory;
    private Window? _settingsWindow;
    private Task? _pendingSettingsShow;
    private Action<Window>? _beforeSettingsReveal;

    /// <param name="settingsWindowFactory">
    /// Do composition root cung cấp. Dịch vụ này không tự dựng cửa sổ Settings được vì cửa sổ
    /// đó lại phụ thuộc ngược vào chính nó.
    /// </param>
    /// <param name="updateDialogFactory">
    /// Hộp thoại cập nhật cần <c>IUpdateService</c>, nên cũng do composition root dựng.
    /// </param>
    public DialogService(Func<Window> settingsWindowFactory, Func<UpdateInfo, Window>? updateDialogFactory = null)
    {
        _settingsWindowFactory = settingsWindowFactory;
        _updateDialogFactory = updateDialogFactory;
    }

    public void ShowSettingsWindow() => _ = ShowSettingsWindowSafelyAsync();

    private async Task ShowSettingsWindowSafelyAsync()
    {
        try { await ShowSettingsWindowAsync(); }
        catch (Exception ex) { global::System.Diagnostics.Trace.WriteLine($"Could not open Settings: {ex}"); }
    }

    /// <summary>Await the saved backdrop before the first visible Settings frame.</summary>
    public Task ShowSettingsWindowAsync(Action<Window>? beforeShow = null)
    {
        var dispatcher = Application.Current.Dispatcher;
        if (App.IsShuttingDown || dispatcher.HasShutdownStarted) return Task.CompletedTask;
        if (!dispatcher.CheckAccess())
        {
            try { return dispatcher.InvokeAsync(() => ShowSettingsWindowAsync(beforeShow)).Task.Unwrap(); }
            catch (InvalidOperationException) { return Task.CompletedTask; }
        }

        if (beforeShow is not null) _beforeSettingsReveal = beforeShow;
        if (_pendingSettingsShow is { IsCompleted: false }) return _pendingSettingsShow;
        return _pendingSettingsShow = ShowSettingsWindowCoreAsync();
    }

    private async Task ShowSettingsWindowCoreAsync()
    {
        if (App.IsShuttingDown || Application.Current.Dispatcher.HasShutdownStarted) return;
        // Tái sử dụng một instance: mở đi mở lại từ tray/hotkey không được đẻ ra nhiều cửa sổ.
        if (_settingsWindow is null)
        {
            var created = _settingsWindowFactory();
            _settingsWindow = created;
            created.Closed += (_, _) =>
            {
                if (ReferenceEquals(_settingsWindow, created)) _settingsWindow = null;
            };
        }

        var window = _settingsWindow;
        // An already visible window needs activation only. Rechecking/redecoding its backdrop
        // on every tray, toast or hotkey click can delay focus and race with a changed file.
        if (window.IsVisible)
        {
            BeforeSettingsReveal(window);
            if (App.IsShuttingDown) return;
            if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
            ActivateSettingsWindow(window);
            return;
        }

        if (window.DataContext is SettingsViewModel viewModel)
            await viewModel.General.PrepareBackgroundForShowAsync();
        if (Application.Current.Dispatcher.HasShutdownStarted || App.IsShuttingDown
            || !ReferenceEquals(_settingsWindow, window)) return;

        if (window.WindowState == WindowState.Minimized)
            window.WindowState = WindowState.Normal;

        // Apply the last hotkey/game preset while still hidden, before WPF reveals Settings.
        (window as SettingsWindow)?.PreparePresetForReveal();

        // Build the selected tab and drain its layout work while Settings is invisible.
        // ApplicationIdle does not acknowledge a DWM present, but the editor will no
        // longer materialize after the window becomes visible.
        var initialShow = window is SettingsWindow && !window.IsLoaded;
        if (initialShow)
        {
            ((SettingsWindow)window).PrepareInitialContent();
            window.ShowActivated = false;
            window.Opacity = 0;
        }

        Core.Diagnostics.StartupProfiler.MarkSettingsShow();
        window.Show();
        if (initialShow)
        {
            window.UpdateLayout();
            var dispatcher = Application.Current.Dispatcher;
            try
            {
                await dispatcher.InvokeAsync(static () => { },
                    global::System.Windows.Threading.DispatcherPriority.ApplicationIdle, App.ShutdownToken);
            }
            catch (OperationCanceledException) when (App.IsShuttingDown || dispatcher.HasShutdownStarted)
            {
                return;
            }
            if (App.IsShuttingDown || dispatcher.HasShutdownStarted ||
                !ReferenceEquals(_settingsWindow, window)) return;
        }

        // Keep Splash visible until the initial Settings layout is ready.
        BeforeSettingsReveal(window);
        if (Application.Current.Dispatcher.HasShutdownStarted || App.IsShuttingDown) return;
        if (initialShow)
        {
            window.Opacity = 1;
            window.ShowActivated = true;
        }
        (window as SettingsWindow)?.MarkSettingsRevealed();
        ActivateSettingsWindow(window);
    }

    private static void ActivateSettingsWindow(Window window)
    {
        window.Activate();
        window.Topmost = true;
        window.Topmost = false;
    }

    private void BeforeSettingsReveal(Window window)
    {
        var callback = _beforeSettingsReveal;
        _beforeSettingsReveal = null;
        callback?.Invoke(window);
    }

    // Cố tình KHÔNG dùng MessageBox của Windows: nút xám mặc định và khung hệ thống phá vỡ
    // hoàn toàn ngôn ngữ thiết kế của phần còn lại.
    public void ShowMessage(string title, string message) => ShowMessage(title, message, isError: false);

    public void ShowMessage(string title, string message, bool isError) =>
        AppDialogWindow.Show(Owner(), title, message, isError ? DialogKind.Error : DialogKind.Info);

    public bool Confirm(string title, string message) =>
        AppDialogWindow.Confirm(Owner(), title, message);

    public bool Confirm(string title, string message, string primaryText, string secondaryText) =>
        AppDialogWindow.Confirm(Owner(), title, message, primaryText, secondaryText);

    public bool CopyToClipboard(string text)
    {
        try
        {
            // Clipboard là tài nguyên dùng chung toàn hệ thống; một ứng dụng khác có thể đang
            // khoá nó và lời gọi sẽ ném lỗi. Không đáng để làm sập bất cứ thứ gì.
            Clipboard.SetText(text);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public void ShowExportCodes(CrosshairExportCodes codes)
    {
        // Hộp thoại chỉ hiển thị dữ liệu đã dựng sẵn nên không cần gì từ DI.
        var window = new ExportCodeWindow(codes) { Owner = Owner() };
        window.ShowDialog();
    }

    public bool ShowUpdate(UpdateInfo update)
    {
        if (_updateDialogFactory is null) return false;

        var window = _updateDialogFactory(update);
        window.Owner = Owner();

        // DialogResult true = script cập nhật đã chạy (xem UpdateDialog).
        return window.ShowDialog() == true;
    }

    public CrosshairProfile? PromptForCrosshairCode()
    {
        // Hộp thoại này chỉ dùng các bộ đọc mã thuần tuý nên không cần gì từ DI.
        var window = new ImportCodeWindow { Owner = Owner() };

        return window.ShowDialog() == true ? window.Result : null;
    }

    public DialogChoice Ask(string title, string message, string primaryText, string secondaryText, string cancelText) =>
        AppDialogWindow.Ask(Owner(), title, message, primaryText, secondaryText, cancelText);

    public string? PickFolder(string title, string? initialDirectory = null)
    {
        var dialog = new OpenFolderDialog { Title = title, Multiselect = false };
        if (!string.IsNullOrWhiteSpace(initialDirectory) && global::System.IO.Directory.Exists(initialDirectory))
            dialog.InitialDirectory = initialDirectory;

        return dialog.ShowDialog(Owner()) == true ? dialog.FolderName : null;
    }

    public string? PickFileToOpen(string filter, string? initialDirectory = null)
    {
        var dialog = new OpenFileDialog
        {
            Filter = filter,
            CheckFileExists = true,
            Multiselect = false,
        };

        if (!string.IsNullOrWhiteSpace(initialDirectory))
            dialog.InitialDirectory = initialDirectory;

        return dialog.ShowDialog(Owner()) == true ? dialog.FileName : null;
    }

    public string? PickFileToSave(string filter, string defaultFileName)
    {
        var dialog = new SaveFileDialog
        {
            Filter = filter,
            FileName = defaultFileName,
            DefaultExt = ".zip",
            AddExtension = true,
            OverwritePrompt = true,
        };
        return dialog.ShowDialog(Owner()) == true ? dialog.FileName : null;
    }

    /// <summary>
    /// Cửa sổ chủ cho hộp thoại. Nếu Settings đang đóng thì trả null — hộp thoại vẫn hiện,
    /// chỉ là không có owner, còn hơn ném exception.
    /// </summary>
    private Window? Owner() =>
        _settingsWindow?.IsVisible == true ? _settingsWindow : null;
}
