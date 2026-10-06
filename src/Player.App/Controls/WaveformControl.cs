using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Player.Core.Waveforms;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using Player.App.Resources;

namespace Player.App.Controls;

/// <summary>One cached geometry per size/data; playback updates only draw clipping and cursor.</summary>
public sealed class WaveformControl : FrameworkElement
{
    public static readonly DependencyProperty DataProperty = DependencyProperty.Register(nameof(Data), typeof(WaveformData), typeof(WaveformControl), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, GeometryChanged));
    public static readonly DependencyProperty PositionProperty = DependencyProperty.Register(nameof(Position), typeof(double), typeof(WaveformControl), new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty DurationProperty = DependencyProperty.Register(nameof(Duration), typeof(double), typeof(WaveformControl), new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty CanSeekProperty = DependencyProperty.Register(nameof(CanSeek), typeof(bool), typeof(WaveformControl), new PropertyMetadata(false));
    public WaveformData? Data { get => (WaveformData?)GetValue(DataProperty); set => SetValue(DataProperty, value); }
    public double Position { get => (double)GetValue(PositionProperty); set => SetValue(PositionProperty, value); }
    public double Duration { get => (double)GetValue(DurationProperty); set => SetValue(DurationProperty, value); }
    public bool CanSeek { get => (bool)GetValue(CanSeekProperty); set => SetValue(CanSeekProperty, value); }
    public event Action<double>? PreviewSeek;
    public event Action<double>? CommitSeek;
    private StreamGeometry? _geometry;
    private bool _dragging;
    private double _preview;
    public WaveformControl() { Cursor = Cursors.Hand; Focusable = true; }
    protected override AutomationPeer OnCreateAutomationPeer() => new SeekPeer(this);
    private static void GeometryChanged(DependencyObject owner, DependencyPropertyChangedEventArgs args) => ((WaveformControl)owner)._geometry = null;
    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo) { _geometry = null; base.OnRenderSizeChanged(sizeInfo); }
    protected override void OnRender(DrawingContext drawing)
    {
        drawing.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        if (ActualWidth <= 0 || ActualHeight <= 0 || Data is null) return;
        _geometry ??= BuildGeometry(Data);
        var remaining = SystemParameters.HighContrast ? SystemColors.WindowTextBrush : (Brush?)TryFindResource("WaveformRemainingBrush") ?? Brushes.LightGray;
        var played = SystemParameters.HighContrast ? SystemColors.HighlightBrush : (Brush?)TryFindResource("AccentBrush") ?? Brushes.Orange;
        drawing.DrawGeometry(remaining, null, _geometry);
        var fraction = Duration > 0 ? Math.Clamp((_dragging ? _preview : Position) / Duration, 0, 1) : 0;
        drawing.PushClip(new RectangleGeometry(new Rect(0, 0, ActualWidth * fraction, ActualHeight)));
        drawing.DrawGeometry(played, null, _geometry); drawing.Pop();
        drawing.DrawLine(new Pen(played, 1), new Point(ActualWidth * fraction, 0), new Point(ActualWidth * fraction, ActualHeight));
        if (IsKeyboardFocused) drawing.DrawRectangle(null, new Pen(remaining, 1) { DashStyle = DashStyles.Dot }, new Rect(1, 1, Math.Max(0, ActualWidth - 2), Math.Max(0, ActualHeight - 2)));
    }
    private StreamGeometry BuildGeometry(WaveformData data)
    {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            var columns = Math.Min(4096, Math.Max(1, (int)Math.Ceiling(ActualWidth)));
            for (var column = 0; column < columns; column++)
            {
                var first = (int)((long)column * data.Minimum.Length / columns);
                var last = Math.Max(first + 1, (int)((long)(column + 1) * data.Minimum.Length / columns));
                float low = 0, high = 0;
                for (var i = first; i < Math.Min(last, data.Minimum.Length); i++) { low = Math.Min(low, data.Minimum[i]); high = Math.Max(high, data.Maximum[i]); }
                var x = ActualWidth * column / columns; var width = Math.Max(1, ActualWidth / columns);
                var top = ActualHeight * (1 - Math.Clamp(high, 0, 1)) / 2;
                var bottom = ActualHeight * (1 - Math.Clamp(low, -1, 0)) / 2;
                context.BeginFigure(new Point(x, top), true, true);
                context.LineTo(new Point(x + width, top), true, false); context.LineTo(new Point(x + width, bottom), true, false); context.LineTo(new Point(x, bottom), true, false);
            }
        }
        geometry.Freeze(); return geometry;
    }
    private double Map(Point point) => ActualWidth > 0 && Duration > 0 && double.IsFinite(Duration) ? Math.Clamp(point.X / ActualWidth, 0, 1) * Duration : 0;
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (!CanSeek || Duration <= 0) return;
        Focus();
        _dragging = true; CaptureMouse(); _preview = Map(e.GetPosition(this)); PreviewSeek?.Invoke(_preview); InvalidateVisual(); e.Handled = true;
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        var seconds = Map(e.GetPosition(this));
        var time = TimeSpan.FromSeconds(seconds); ToolTip = $"{(long)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}";
        if (_dragging) { _preview = seconds; PreviewSeek?.Invoke(seconds); InvalidateVisual(); }
    }
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    { if (!_dragging) return; _preview = Map(e.GetPosition(this)); FinishDrag(); e.Handled = true; }
    protected override void OnLostMouseCapture(MouseEventArgs e) { FinishDrag(); base.OnLostMouseCapture(e); }
    private void FinishDrag()
    { if (!_dragging) return; _dragging = false; ReleaseMouseCapture(); CommitSeek?.Invoke(_preview); InvalidateVisual(); }
    public void SeekFromAutomation(double value)
    {
        if (!CanSeek) throw new ElementNotEnabledException();
        if (!double.IsFinite(value) || value < 0 || value > Duration) throw new ArgumentOutOfRangeException(nameof(value));
        CommitSeek?.Invoke(value);
    }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (CanSeek && Keyboard.Modifiers == ModifierKeys.None && e.Key is Key.Left or Key.Right or Key.Home or Key.End)
        {
            SeekFromAutomation(e.Key switch { Key.Home => 0, Key.End => Duration, Key.Left => Math.Max(0, Position - 5), _ => Math.Min(Duration, Position + 5) });
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }
    private sealed class SeekPeer(WaveformControl owner) : FrameworkElementAutomationPeer(owner), IRangeValueProvider
    {
        protected override string GetClassNameCore() => "WaveformSeek";
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Slider;
        protected override string GetNameCore() => string.IsNullOrEmpty(base.GetNameCore()) ? Strings.Seek : base.GetNameCore();
        public override object? GetPattern(PatternInterface patternInterface) => patternInterface == PatternInterface.RangeValue ? this : base.GetPattern(patternInterface);
        public bool IsReadOnly => !owner.CanSeek;
        public double LargeChange => 30;
        public double SmallChange => 5;
        public double Maximum => owner.Duration;
        public double Minimum => 0;
        public double Value => owner.Position;
        public void SetValue(double value) => owner.Dispatcher.Invoke(() => owner.SeekFromAutomation(value));
    }
}
