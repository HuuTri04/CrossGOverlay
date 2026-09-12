using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CrosshairOverlay.Core.Models;
using CrosshairOverlay.Localization;

namespace CrosshairOverlay.Controls;

/// <summary>
/// Ô nhận tổ hợp phím: bấm vào rồi gõ tổ hợp muốn gán, hoặc bấm nút chuột phụ. Esc xoá gán.
/// </summary>
/// <remarks>
/// Chỉ đọc thao tác khi chính ô này đang có focus — không cài hook bàn phím hay chuột nào.
/// Đây thuần tuý là xử lý sự kiện của WPF bên trong cửa sổ của chính ứng dụng.
/// </remarks>
public sealed class HotkeyInputBox : TextBox
{
    public HotkeyInputBox()
    {
        IsReadOnly = true;
        IsReadOnlyCaretVisible = false;
        IsUndoEnabled = false;
        Cursor = Cursors.Hand;
        ContextMenu = null;
        UpdateText();
    }

    public static readonly DependencyProperty HotkeyKeyProperty = DependencyProperty.Register(
        nameof(HotkeyKey), typeof(Key), typeof(HotkeyInputBox),
        new FrameworkPropertyMetadata(
            Key.None, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnHotkeyChanged));

    public Key HotkeyKey
    {
        get => (Key)GetValue(HotkeyKeyProperty);
        set => SetValue(HotkeyKeyProperty, value);
    }

    public static readonly DependencyProperty HotkeyMouseButtonProperty = DependencyProperty.Register(
        nameof(HotkeyMouseButton), typeof(HotkeyMouseButton), typeof(HotkeyInputBox),
        new FrameworkPropertyMetadata(
            Core.Models.HotkeyMouseButton.None,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            OnHotkeyChanged));

    public HotkeyMouseButton HotkeyMouseButton
    {
        get => (HotkeyMouseButton)GetValue(HotkeyMouseButtonProperty);
        set => SetValue(HotkeyMouseButtonProperty, value);
    }

    public static readonly DependencyProperty HotkeyModifiersProperty = DependencyProperty.Register(
        nameof(HotkeyModifiers), typeof(ModifierKeys), typeof(HotkeyInputBox),
        new FrameworkPropertyMetadata(
            ModifierKeys.None, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnHotkeyChanged));

    public ModifierKeys HotkeyModifiers
    {
        get => (ModifierKeys)GetValue(HotkeyModifiersProperty);
        set => SetValue(HotkeyModifiersProperty, value);
    }

    private static void OnHotkeyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((HotkeyInputBox)d).UpdateText();

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        // Chặn hết: Tab, Space, mũi tên… đều là phím gán được, không để WPF xử lý.
        e.Handled = true;

        // Alt đi kèm khiến WPF báo Key.System, phím thật nằm ở SystemKey.
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (key == Key.Escape)
        {
            ClearAssignment();
            return;
        }

        // Mới chỉ giữ phím bổ trợ thì chưa phải một tổ hợp hoàn chỉnh.
        if (IsModifier(key)) return;

        HotkeyMouseButton = Core.Models.HotkeyMouseButton.None;
        HotkeyModifiers = Keyboard.Modifiers;
        HotkeyKey = key;
    }

    /// <summary>
    /// Nhận nút chuột phụ. Chuột trái/phải cố tình không nhận — gán chúng làm phím tắt toàn cục
    /// sẽ khiến người dùng không click được gì nữa.
    /// </summary>
    protected override void OnPreviewMouseDown(MouseButtonEventArgs e)
    {
        var button = e.ChangedButton switch
        {
            MouseButton.Middle => Core.Models.HotkeyMouseButton.Middle,
            MouseButton.XButton1 => Core.Models.HotkeyMouseButton.XButton1,
            MouseButton.XButton2 => Core.Models.HotkeyMouseButton.XButton2,
            _ => Core.Models.HotkeyMouseButton.None,
        };

        if (button == Core.Models.HotkeyMouseButton.None)
        {
            // Chuột trái chỉ để đưa focus vào ô.
            base.OnPreviewMouseDown(e);
            return;
        }

        e.Handled = true;
        Focus();

        HotkeyKey = Key.None;
        HotkeyModifiers = Keyboard.Modifiers;
        HotkeyMouseButton = button;
    }

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnGotKeyboardFocus(e);
        UpdateText();
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        UpdateText();
    }

    /// <summary>Không đặt tên là Clear() — trùng tên với <see cref="TextBox.Clear"/>.</summary>
    private void ClearAssignment()
    {
        HotkeyModifiers = ModifierKeys.None;
        HotkeyMouseButton = Core.Models.HotkeyMouseButton.None;
        HotkeyKey = Key.None;
    }

    private static bool IsModifier(Key key) => key
        is Key.LeftCtrl or Key.RightCtrl
        or Key.LeftAlt or Key.RightAlt
        or Key.LeftShift or Key.RightShift
        or Key.LWin or Key.RWin
        or Key.System;

    private void UpdateText()
    {
        if (HotkeyKey == Key.None && HotkeyMouseButton == Core.Models.HotkeyMouseButton.None)
        {
            Text = Tr.Get(IsKeyboardFocusWithin ? "Hotkeys_Prompt" : "Hotkeys_Unassigned");
            return;
        }

        Text = KeyNames.Describe(HotkeyModifiers, HotkeyKey, HotkeyMouseButton);
    }
}
