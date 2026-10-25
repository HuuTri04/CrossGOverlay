using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using CrosshairOverlay.Services.System;

namespace CrosshairOverlay.Controls;

/// <summary>
/// Short-lived wheel easing for page-level pixel scrollers. No timer runs while idle or hidden.
/// Virtualized item lists retain their own logical/pixel-unit scrolling path.
/// </summary>
public static class SmoothScrollBehavior
{
    private const double PixelsPerNotch = 64d;
    private const double DurationMilliseconds = 200d;
    private const double VisibleOffsetThreshold = 0.5d;
    private static readonly ConditionalWeakTable<ScrollViewer, ScrollState> States = new();

    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(SmoothScrollBehavior),
        new PropertyMetadata(false, OnIsEnabledChanged));

    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);

    // General settings contains many non-virtualized cards over a translucent backdrop.
    // Bound on that one viewer only: fewer layout passes per wheel notch while the image is visible.
    public static readonly DependencyProperty UseBackdropFrameBudgetProperty = DependencyProperty.RegisterAttached(
        "UseBackdropFrameBudget", typeof(bool), typeof(SmoothScrollBehavior), new PropertyMetadata(false));

    public static bool GetUseBackdropFrameBudget(DependencyObject element) =>
        (bool)element.GetValue(UseBackdropFrameBudgetProperty);

    public static void SetUseBackdropFrameBudget(DependencyObject element, bool value) =>
        element.SetValue(UseBackdropFrameBudgetProperty, value);

    private static readonly DependencyPropertyKey IsScrollingPropertyKey =
        DependencyProperty.RegisterAttachedReadOnly(
            "IsScrolling", typeof(bool), typeof(SmoothScrollBehavior),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits));

    public static readonly DependencyProperty IsScrollingProperty = IsScrollingPropertyKey.DependencyProperty;

    public static bool GetIsScrolling(DependencyObject element) => (bool)element.GetValue(IsScrollingProperty);

    internal static double GetEffectiveOffset(ScrollViewer viewer) =>
        States.TryGetValue(viewer, out var state) && state.IsRunning ? state.Target : viewer.VerticalOffset;

    internal static bool TryQueueWheel(ScrollViewer viewer, int delta)
    {
        if (!GetIsEnabled(viewer) || viewer.ScrollableHeight <= 0.5d || delta == 0) return false;
        States.GetValue(viewer, static v => new ScrollState(v)).Queue(delta);
        return true;
    }

    // Kept pure for unit tests and high-resolution wheels that send partial deltas.
    internal static double CalculateTarget(double currentTarget, int delta, double scrollableHeight) =>
        Math.Clamp(currentTarget - delta * PixelsPerNotch / Mouse.MouseWheelDeltaForOneLine,
            0d, Math.Max(0d, scrollableHeight));

    internal static double EaseOutCubic(double progress)
    {
        var remaining = 1d - Math.Clamp(progress, 0d, 1d);
        return 1d - remaining * remaining * remaining;
    }

    internal static TimeSpan FrameIntervalFor(int targetFps, bool backdropVisible) =>
        TimeSpan.FromSeconds(1d / Math.Min(Math.Clamp(targetFps, 60, 144), backdropVisible ? 60 : 144));

    internal static bool HasVisibleMovement(double current, double target) =>
        Math.Abs(target - current) >= VisibleOffsetThreshold;

    private static void OnIsEnabledChanged(DependencyObject element, DependencyPropertyChangedEventArgs args)
    {
        if (element is not ScrollViewer viewer) return;

        viewer.PreviewMouseWheel -= OnPreviewMouseWheel;
        viewer.PreviewMouseDown -= OnPreviewMouseDown;
        viewer.PreviewKeyDown -= OnPreviewKeyDown;
        viewer.Unloaded -= OnUnloaded;
        viewer.IsVisibleChanged -= OnVisibleChanged;

        if ((bool)args.NewValue)
        {
            viewer.PreviewMouseWheel += OnPreviewMouseWheel;
            viewer.PreviewMouseDown += OnPreviewMouseDown;
            viewer.PreviewKeyDown += OnPreviewKeyDown;
            viewer.Unloaded += OnUnloaded;
            viewer.IsVisibleChanged += OnVisibleChanged;
        }
        else if (States.TryGetValue(viewer, out var state)) state.Stop();
    }

    private static void OnPreviewMouseWheel(object sender, MouseWheelEventArgs args)
    {
        if (args.Handled || sender is not ScrollViewer viewer ||
            !WheelScrollAssist.CanScroll(viewer, args.Delta < 0)) return;

        if (TryQueueWheel(viewer, args.Delta)) args.Handled = true;
    }

    private static void OnPreviewMouseDown(object sender, MouseButtonEventArgs args) => Stop(sender);

    private static void OnPreviewKeyDown(object sender, KeyEventArgs args) => Stop(sender);

    private static void OnUnloaded(object sender, RoutedEventArgs args) => Stop(sender);

    private static void OnVisibleChanged(object sender, DependencyPropertyChangedEventArgs args)
    {
        if (args.NewValue is false) Stop(sender);
    }

    private static void Stop(object sender)
    {
        if (sender is ScrollViewer viewer && States.TryGetValue(viewer, out var state)) state.Stop();
    }

    private sealed class ScrollState
    {
        private readonly ScrollViewer _viewer;
        private readonly DispatcherTimer _timer;
        private long _startedAt;
        private double _from;

        public ScrollState(ScrollViewer viewer)
        {
            _viewer = viewer;
            // Input remains ahead of animation ticks if the UI thread is busy.
            _timer = new DispatcherTimer(DispatcherPriority.Background, viewer.Dispatcher)
            {
                Interval = FrameInterval(),
            };
            _timer.Tick += OnTick;
        }

        public bool IsRunning => _timer.IsEnabled;

        public double Target { get; private set; }

        public void Queue(int delta)
        {
            var target = CalculateTarget(IsRunning ? Target : _viewer.VerticalOffset,
                delta, _viewer.ScrollableHeight);
            if (IsRunning && !HasVisibleMovement(Target, target))
            {
                if (!HasVisibleMovement(_viewer.VerticalOffset, target)) Stop();
                return;
            }

            _from = _viewer.VerticalOffset;
            Target = target;
            if (!HasVisibleMovement(_from, Target))
            {
                Stop();
                return;
            }

            _startedAt = Stopwatch.GetTimestamp();
            _viewer.SetValue(IsScrollingPropertyKey, true);
            _timer.Interval = FrameInterval();
            _timer.Start();
        }

        private void OnTick(object? sender, EventArgs args)
        {
            var interval = FrameInterval();
            if (_timer.Interval != interval) _timer.Interval = interval;
            if (!_viewer.IsVisible || _viewer.ScrollableHeight <= 0.5d)
            {
                Stop();
                return;
            }

            Target = Math.Clamp(Target, 0d, _viewer.ScrollableHeight);
            if (!HasVisibleMovement(_viewer.VerticalOffset, Target))
            {
                Stop();
                return;
            }
            var progress = Stopwatch.GetElapsedTime(_startedAt).TotalMilliseconds / DurationMilliseconds;
            if (progress >= 1d)
            {
                ScrollIfVisible(Target);
                Stop();
                return;
            }

            var next = _from + (Target - _from) * EaseOutCubic(progress);
            ScrollIfVisible(next);
            if ((Target <= VisibleOffsetThreshold && _viewer.VerticalOffset <= VisibleOffsetThreshold)
                || (Target >= _viewer.ScrollableHeight - VisibleOffsetThreshold
                    && _viewer.VerticalOffset >= _viewer.ScrollableHeight - VisibleOffsetThreshold))
                Stop();
        }

        private void ScrollIfVisible(double offset)
        {
            if (HasVisibleMovement(_viewer.VerticalOffset, offset))
                _viewer.ScrollToVerticalOffset(offset);
        }

        public void Stop()
        {
            if (!_timer.IsEnabled) return;
            _timer.Stop();
            _viewer.SetValue(IsScrollingPropertyKey, false);
        }

        private TimeSpan FrameInterval() =>
            FrameIntervalFor(UiAnimationFrameRate.TargetFps, GetUseBackdropFrameBudget(_viewer));
    }
}
