using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Player.App.Controls;

/// <summary>Short interruptible pixel scrolling; recycling and native keyboard/touch/scrollbar input remain enabled.</summary>
public sealed class SmoothPlaylistListBox : ListBox
{
    private ScrollViewer? _viewer;
    private double _from, _target;
    private long _started;
    private TimeSpan _lastRenderingTime;
    private bool _animating;
    internal bool IsScrollAnimating => _animating;
    internal bool? ScrollAnimationOverride { get; set; }
    private bool AnimationsEnabled => ScrollAnimationOverride ?? SystemParameters.ClientAreaAnimation;
    public SmoothPlaylistListBox()
    {
        Unloaded += (_, _) => CancelScrollAnimation();
        IsVisibleChanged += (_, _) => { if (!IsVisible) CancelScrollAnimation(); };
    }
    public override void OnApplyTemplate()
    {
        CancelScrollAnimation();
        base.OnApplyTemplate();
        _viewer = GetTemplateChild("PART_ScrollViewer") as ScrollViewer;
    }
    protected override void OnPreviewMouseWheel(MouseWheelEventArgs e)
    {
        if (e.Handled || Keyboard.Modifiers != ModifierKeys.None || _viewer is null ||
            SystemParameters.WheelScrollLines == 0 || _viewer.ScrollableHeight <= 0)
        { base.OnPreviewMouseWheel(e); return; }
        var step = SystemParameters.WheelScrollLines < 0 ? _viewer.ViewportHeight : SystemParameters.WheelScrollLines * 48d;
        ScrollWheelBy(-e.Delta / 120d * step);
        e.Handled = true;
    }
    internal void ScrollWheelBy(double pixels)
    {
        if (_viewer is null || !double.IsFinite(pixels)) return;
        _from = _viewer.VerticalOffset;
        _target = Math.Clamp((_animating ? _target : _from) + pixels, 0, _viewer.ScrollableHeight);
        _started = Stopwatch.GetTimestamp();
        if (!AnimationsEnabled || !IsVisible || Math.Abs(_target - _from) < .5)
        { CancelScrollAnimation(); _viewer.ScrollToVerticalOffset(_target); return; }
        if (_animating) return;
        _animating = true;
        CompositionTarget.Rendering += OnScrollFrame;
    }
    private void OnScrollFrame(object? sender, EventArgs args)
    {
        if (_viewer is null || !IsVisible) { CancelScrollAnimation(); return; }
        // Rendering can be raised more than once per composed frame; advance once per frame at the display's own rate.
        if (args is RenderingEventArgs rendering)
        {
            if (rendering.RenderingTime == _lastRenderingTime) return;
            _lastRenderingTime = rendering.RenderingTime;
        }
        ApplyScrollFrame(Stopwatch.GetElapsedTime(_started).TotalMilliseconds);
    }
    internal void ApplyScrollFrame(double elapsedMilliseconds)
    {
        if (!_animating || _viewer is null) return;
        var progress = Math.Clamp(elapsedMilliseconds / 180, 0, 1);
        if (!AnimationsEnabled) progress = 1;
        var target = Math.Min(_target, _viewer.ScrollableHeight);
        _viewer.ScrollToVerticalOffset(_from + (target - _from) * (1 - Math.Pow(1 - progress, 3)));
        if (progress >= 1) CancelScrollAnimation();
    }
    internal void CancelScrollAnimation()
    {
        if (!_animating) return;
        CompositionTarget.Rendering -= OnScrollFrame;
        _animating = false;
    }
    protected override void OnSelectionChanged(SelectionChangedEventArgs e) { CancelScrollAnimation(); base.OnSelectionChanged(e); }
    protected override void OnPreviewKeyDown(KeyEventArgs e) { CancelScrollAnimation(); base.OnPreviewKeyDown(e); }
    protected override void OnPreviewMouseDown(MouseButtonEventArgs e) { CancelScrollAnimation(); base.OnPreviewMouseDown(e); }
    protected override void OnPreviewTouchDown(TouchEventArgs e) { CancelScrollAnimation(); base.OnPreviewTouchDown(e); }
    protected override void OnDragOver(DragEventArgs e) { CancelScrollAnimation(); base.OnDragOver(e); }
    protected override void OnItemsChanged(System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        // Rows appended by a streaming import do not move the content being scrolled to.
        if (e.Action != System.Collections.Specialized.NotifyCollectionChangedAction.Add) CancelScrollAnimation();
        base.OnItemsChanged(e);
    }
}
