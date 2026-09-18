using System.Windows;
using CrosshairOverlay.Core.Abstractions;
using CrosshairOverlay.Core.Models;
using CrosshairOverlay.Views;
using CrosshairOverlay.Views.Dialogs;
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

    public void ShowSettingsWindow()
    {
        // Tái sử dụng một instance: mở đi mở lại từ tray/hotkey không được đẻ ra nhiều cửa sổ.
        if (_settingsWindow is null)
        {
            _settingsWindow = _settingsWindowFactory();
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
            _settingsWindow.Show();
        }
        else
        {
            if (_settingsWindow.WindowState == WindowState.Minimized)
                _settingsWindow.WindowState = WindowState.Normal;

            _settingsWindow.Show();
        }

        _settingsWindow.Activate();
        _settingsWindow.Topmost = true;
        _settingsWindow.Topmost = false;
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

    /// <summary>
    /// Cửa sổ chủ cho hộp thoại. Nếu Settings đang đóng thì trả null — hộp thoại vẫn hiện,
    /// chỉ là không có owner, còn hơn ném exception.
    /// </summary>
    private Window? Owner() =>
        _settingsWindow?.IsVisible == true ? _settingsWindow : Application.Current?.MainWindow;
}
