using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace CrosshairOverlay.Controls;

/// <summary>Closes floating dropdowns when their owning Settings content scrolls.</summary>
public static class PopupScrollGuard
{
    // A weak reference cannot keep a recycled tab or its popup alive.
    private static WeakReference<ComboBox>? _openComboBox;
    private static WeakReference<Popup>? _openPopup;

    public static readonly DependencyProperty TrackComboBoxProperty = DependencyProperty.RegisterAttached(
        "TrackComboBox", typeof(bool), typeof(PopupScrollGuard),
        new PropertyMetadata(false, OnTrackComboBoxChanged));

    public static bool GetTrackComboBox(DependencyObject element) =>
        (bool)element.GetValue(TrackComboBoxProperty);

    public static void SetTrackComboBox(DependencyObject element, bool value) =>
        element.SetValue(TrackComboBoxProperty, value);

    public static readonly DependencyProperty TrackPopupProperty = DependencyProperty.RegisterAttached(
        "TrackPopup", typeof(bool), typeof(PopupScrollGuard),
        new PropertyMetadata(false, OnTrackPopupChanged));

    public static bool GetTrackPopup(DependencyObject element) =>
        (bool)element.GetValue(TrackPopupProperty);

    public static void SetTrackPopup(DependencyObject element, bool value) =>
        element.SetValue(TrackPopupProperty, value);

    private static void OnTrackComboBoxChanged(DependencyObject element, DependencyPropertyChangedEventArgs args)
    {
        if (element is not ComboBox combo) return;
        combo.DropDownOpened -= OnComboOpened;
        combo.DropDownClosed -= OnComboClosed;
        combo.Unloaded -= OnComboUnloaded;
        if (args.NewValue is true)
        {
            combo.DropDownOpened += OnComboOpened;
            combo.DropDownClosed += OnComboClosed;
            combo.Unloaded += OnComboUnloaded;
        }
    }

    private static void OnTrackPopupChanged(DependencyObject element, DependencyPropertyChangedEventArgs args)
    {
        if (element is not Popup popup) return;
        popup.Opened -= OnPopupOpened;
        popup.Closed -= OnPopupClosed;
        if (args.NewValue is true)
        {
            popup.Opened += OnPopupOpened;
            popup.Closed += OnPopupClosed;
        }
    }

    private static void OnComboOpened(object? sender, EventArgs args)
    {
        if (sender is ComboBox combo) _openComboBox = new WeakReference<ComboBox>(combo);
    }

    private static void OnComboClosed(object? sender, EventArgs args) => ClearCombo(sender);
    private static void OnComboUnloaded(object sender, RoutedEventArgs args) => ClearCombo(sender);

    private static void ClearCombo(object? sender)
    {
        if (_openComboBox?.TryGetTarget(out var combo) == true && ReferenceEquals(combo, sender))
            _openComboBox = null;
    }

    private static void OnPopupOpened(object? sender, EventArgs args)
    {
        if (sender is Popup popup) _openPopup = new WeakReference<Popup>(popup);
    }

    private static void OnPopupClosed(object? sender, EventArgs args)
    {
        if (_openPopup?.TryGetTarget(out var popup) == true && ReferenceEquals(popup, sender))
            _openPopup = null;
    }

    /// <returns>Whether a dropdown owned by this window was closed.</returns>
    public static bool CloseFor(Window owner)
    {
        var closed = false;
        if (_openComboBox?.TryGetTarget(out var combo) == true &&
            combo.IsDropDownOpen && ReferenceEquals(Window.GetWindow(combo), owner))
        {
            combo.IsDropDownOpen = false;
            closed = true;
        }
        if (_openPopup?.TryGetTarget(out var popup) == true && popup.IsOpen &&
            popup.PlacementTarget is not null &&
            ReferenceEquals(Window.GetWindow(popup.PlacementTarget), owner))
        {
            popup.IsOpen = false;
            closed = true;
        }
        return closed;
    }
}
