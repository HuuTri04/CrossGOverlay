using System.Windows.Input;

namespace CrosshairOverlay.Core.Models;

/// <summary>
/// Đổi <see cref="Key"/> thành tên người đọc được.
/// </summary>
/// <remarks>
/// <c>Key.ToString()</c> cho ra những chuỗi vô nghĩa với người dùng: phím <c>]</c> hiện thành
/// "Oem6", phím <c>[</c> hiện thành "OemOpenBrackets". Tệ hơn, hai phím cùng loại lại ra hai
/// kiểu tên khác nhau, vì nhiều thành viên của enum <see cref="Key"/> dùng chung một giá trị
/// và <c>ToString()</c> lấy tên xuất hiện trước trong bảng.
///
/// <para>
/// Vì lý do trùng giá trị đó, mỗi nhánh <c>case</c> dưới đây chỉ được dùng MỘT tên cho mỗi
/// giá trị — viết cả <c>Key.Oem6</c> lẫn <c>Key.OemCloseBrackets</c> sẽ không biên dịch được.
/// </para>
/// </remarks>
public static class KeyNames
{
    /// <summary>Mô tả đầy đủ một tổ hợp, vd "Alt + ]" hoặc "Ctrl + Mouse 4".</summary>
    public static string Describe(
        ModifierKeys modifiers, Key key, HotkeyMouseButton mouseButton = HotkeyMouseButton.None)
    {
        if (key == Key.None && mouseButton == HotkeyMouseButton.None)
            return Localization.Tr.Get("Hotkeys_Unassigned");

        var parts = new List<string>(5);
        if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");

        parts.Add(mouseButton != HotkeyMouseButton.None ? Label(mouseButton) : Label(key));

        return string.Join(" + ", parts);
    }

    /// <summary>
    /// Tên nút chuột. Dùng cách đánh số quen thuộc với game thủ ("Mouse 4"/"Mouse 5") thay vì
    /// tên API ("XButton1"), vì đó là cách phần mềm chuột và game gọi chúng.
    /// </summary>
    public static string Label(HotkeyMouseButton button) => button switch
    {
        HotkeyMouseButton.Middle => "Mouse 3",
        HotkeyMouseButton.XButton1 => "Mouse 4",
        HotkeyMouseButton.XButton2 => "Mouse 5",
        _ => Localization.Tr.Get("Hotkeys_Unassigned"),
    };

    public static string Label(Key key) => key switch
    {
        Key.None => Localization.Tr.Get("Hotkeys_Unassigned"),

        // Phím ký hiệu: hiện đúng ký tự in trên bàn phím.
        Key.OemOpenBrackets => "[",
        Key.OemCloseBrackets => "]",
        Key.OemSemicolon => ";",
        Key.OemQuotes => "'",
        Key.OemComma => ",",
        Key.OemPeriod => ".",
        Key.OemQuestion => "/",
        Key.OemPipe => "\\",
        Key.OemMinus => "-",
        Key.OemPlus => "=",
        Key.OemTilde => "`",

        // Hàng số trên cùng hiện thành "0".."9" thay vì "D0".."D9".
        >= Key.D0 and <= Key.D9 => ((char)('0' + (key - Key.D0))).ToString(),

        >= Key.NumPad0 and <= Key.NumPad9 => $"Num {key - Key.NumPad0}",
        Key.Add => "Num +",
        Key.Subtract => "Num -",
        Key.Multiply => "Num *",
        Key.Divide => "Num /",
        Key.Decimal => "Num .",

        Key.Return => "Enter",
        Key.Escape => "Esc",
        Key.Back => "Backspace",
        Key.Prior => "Page Up",
        Key.Next => "Page Down",
        Key.Capital => "Caps Lock",
        Key.Snapshot => "Print Screen",
        Key.Space => "Space",

        _ => key.ToString(),
    };
}
