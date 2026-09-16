using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using CrosshairOverlay.Interop;
using CrosshairOverlay.Localization;

namespace CrosshairOverlay.Views.Dialogs;

/// <summary>Mức độ nghiêm trọng, quyết định màu dải nhấn trên đầu thẻ.</summary>
public enum DialogKind
{
    Info,
    Question,
    Warning,
    Error,
}

/// <summary>
/// Hộp thoại thay thế <c>MessageBox</c> của Windows.
/// </summary>
/// <remarks>
/// Là một <see cref="Window"/> trong suốt được đặt TRÙNG KHÍT lên cửa sổ chủ, nên lớp phủ tối
/// chỉ che đúng ứng dụng chứ không che cả màn hình nền. Vị trí đặt bằng <c>SetWindowPos</c> với
/// toạ độ physical pixel lấy từ HWND của cửa sổ chủ — dùng <c>Window.Left/Top</c> sẽ sai khi
/// cửa sổ chủ đang phóng to hoặc nằm trên màn hình có DPI khác.
/// </remarks>
public partial class AppDialogWindow : Window
{
    private AppDialogWindow() => InitializeComponent();

    private bool _result;
    private CrosshairOverlay.Core.Abstractions.DialogChoice _choice = CrosshairOverlay.Core.Abstractions.DialogChoice.Cancel;

    /// <summary>Hộp thoại thông báo, chỉ một nút.</summary>
    public static void Show(Window? owner, string title, string message, DialogKind kind = DialogKind.Info)
    {
        var dialog = Create(owner, title, message, kind, Tr.Get("Dialog_Ok"), secondaryText: null);
        dialog.ShowDialog();
    }

    /// <summary>Hộp thoại xác nhận. Trả về true khi người dùng chọn nút chính.</summary>
    public static bool Confirm(
        Window? owner,
        string title,
        string message,
        string? primaryText = null,
        string? secondaryText = null,
        DialogKind kind = DialogKind.Question)
    {
        var dialog = Create(
            owner,
            title,
            message,
            kind,
            primaryText ?? Tr.Get("Dialog_Yes"),
            secondaryText ?? Tr.Get("Dialog_No"));

        dialog.ShowDialog();
        return dialog._result;
    }

    /// <summary>Hộp thoại ba nút (vd Có / Không / Huỷ). Esc tương đương Huỷ.</summary>
    public static CrosshairOverlay.Core.Abstractions.DialogChoice Ask(
        Window? owner, string title, string message, string primaryText, string secondaryText, string cancelText,
        DialogKind kind = DialogKind.Question)
    {
        var dialog = Create(owner, title, message, kind, primaryText, secondaryText);
        dialog.CancelButton.Content = cancelText;
        dialog.CancelButton.Visibility = Visibility.Visible;
        dialog.CancelButton.IsCancel = true;

        dialog.ShowDialog();
        return dialog._choice;
    }

    private static AppDialogWindow Create(
        Window? owner, string title, string message, DialogKind kind,
        string primaryText, string? secondaryText)
    {
        var dialog = new AppDialogWindow
        {
            TitleText = { Text = title },
            MessageText = { Text = message },
            PrimaryButton = { Content = primaryText },
        };

        if (secondaryText is not null)
        {
            dialog.SecondaryButton.Content = secondaryText;
            dialog.SecondaryButton.Visibility = Visibility.Visible;
        }

        dialog.AccentStripe.Background = dialog.StripeFor(kind);

        // Chỉ gán Owner khi cửa sổ đó đang hiển thị — gán một cửa sổ chưa Show sẽ ném lỗi.
        if (owner is { IsVisible: true })
        {
            dialog.Owner = owner;
            dialog._ownerHandle = new WindowInteropHelper(owner).Handle;
        }

        return dialog;
    }

    private nint _ownerHandle;

    private Brush StripeFor(DialogKind kind) => kind switch
    {
        DialogKind.Warning => Brush("Warning"),
        DialogKind.Error => Brush("Danger"),
        _ => Brush("AccentBright"),
    };

    private Brush Brush(string key) =>
        TryFindResource(key) as Brush ?? Brushes.Gray;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        CoverOwner();
    }

    /// <summary>Phủ trùng khít cửa sổ chủ, hoặc toàn màn hình chính nếu không có chủ.</summary>
    private void CoverOwner()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == 0) return;

        RECT target;

        if (_ownerHandle != 0 && NativeMethods.GetWindowRect(_ownerHandle, out var ownerRect))
        {
            target = ownerRect;
        }
        else
        {
            var monitor = NativeMethods.MonitorFromWindow(handle, Win32Constants.MONITOR_DEFAULTTOPRIMARY);
            var info = MONITORINFOEXW.Create();
            if (!NativeMethods.GetMonitorInfo(monitor, ref info)) return;

            target = info.rcWork;
        }

        NativeMethods.SetWindowPos(
            handle,
            0,
            target.Left,
            target.Top,
            target.Width,
            target.Height,
            Win32Constants.SWP_NOZORDER | Win32Constants.SWP_NOACTIVATE);
    }

    private void OnPrimary(object sender, RoutedEventArgs e)
    {
        _result = true;
        _choice = CrosshairOverlay.Core.Abstractions.DialogChoice.Primary;
        Close();
    }

    private void OnSecondary(object sender, RoutedEventArgs e)
    {
        _result = false;
        _choice = CrosshairOverlay.Core.Abstractions.DialogChoice.Secondary;
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        _result = false;
        _choice = CrosshairOverlay.Core.Abstractions.DialogChoice.Cancel;
        Close();
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        // Esc = huỷ. Với hộp thoại một nút thì Esc cũng đóng, vì không có gì để huỷ.
        if (e.Key != Key.Escape) return;

        _result = false;
        Close();
        e.Handled = true;
    }
}
