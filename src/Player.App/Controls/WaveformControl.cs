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
    public static readonly DependencyProperty UsePeaksProperty = DependencyProperty.Register(nameof(UsePeaks), typeof(bool), typeof(WaveformControl), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender, GeometryChanged));
    public bool UsePeaks { get => (bool)GetValue(UsePeaksProperty); set => SetValue(UsePeaksProperty, value); }
    public WaveformData? Data { get => (WaveformData?)GetValue(DataProperty); set => SetValue(DataProperty, value); }
    public double Position { get => (double)GetValue(PositionProperty); set => SetValue(PositionProperty, value); }
    public double Duration { get => (double)GetValue(DurationProperty); set => SetValue(DurationProperty, value); }
    public bool CanSeek { get => (bool)GetValue(CanSeekProperty); set => SetValue(CanSeekProperty, value); }
    public event Action<double>? PreviewSeek;
    public event Action<double>? CommitSeek;
    private StreamGeometry? _geometry;
    private StreamGeometry? _peakGeometry;
    private bool _dragging;
    private double _preview;
    public WaveformControl() { Cursor = Cursors.Hand; Focusable = true; }
    protected override AutomationPeer OnCreateAutomationPeer() => new SeekPeer(this);
    private static void GeometryChanged(DependencyObject owner, DependencyPropertyChangedEventArgs args) => ((WaveformControl)owner).ClearGeometry();
    private void ClearGeometry() { _geometry = null; _peakGeometry = null; }
    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo) { ClearGeometry(); base.OnRenderSizeChanged(sizeInfo); }
    protected override void OnRender(DrawingContext drawing)
    {
        drawing.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        if (ActualWidth <= 0 || ActualHeight <= 0 || Data is null) return;
        if (_geometry is null) BuildGeometry(Data);
        var remaining = SystemParameters.HighContrast ? SystemColors.WindowTextBrush : (Brush?)TryFindResource("WaveformRemainingBrush") ?? Brushes.LightGray;
        var played = SystemParameters.HighContrast ? SystemColors.HighlightBrush : (Brush?)TryFindResource("AccentBrush") ?? Brushes.Orange;
        var center = ActualHeight / 2;
        drawing.PushOpacity(0.2); drawing.DrawLine(new Pen(remaining, 1), new Point(0, center), new Point(ActualWidth, center)); drawing.Pop();
        DrawEnvelope(remaining);
        var fraction = Duration > 0 ? Math.Clamp((_dragging ? _preview : Position) / Duration, 0, 1) : 0;
        drawing.PushClip(new RectangleGeometry(new Rect(0, 0, ActualWidth * fraction, ActualHeight)));
        DrawEnvelope(played); drawing.Pop();
        drawing.DrawLine(new Pen(played, 1), new Point(ActualWidth * fraction, 4), new Point(ActualWidth * fraction, Math.Max(4, ActualHeight - 4)));
        if (IsKeyboardFocused) drawing.DrawRectangle(null, new Pen(remaining, 1) { DashStyle = DashStyles.Dot }, new Rect(1, 1, Math.Max(0, ActualWidth - 2), Math.Max(0, ActualHeight - 2)));
        void DrawEnvelope(Brush brush)
        {
            drawing.PushOpacity(SystemParameters.HighContrast ? 0.35 : 0.18);
            drawing.DrawGeometry(brush, null, _peakGeometry); drawing.Pop();
            drawing.DrawGeometry(brush, null, _geometry);
        }
    }
    private void BuildGeometry(WaveformData data)
    {
        var geometry = new StreamGeometry(); var peaks = new StreamGeometry();
        using (var context = geometry.Open())
        using (var peakContext = peaks.Open())
        {
            var columns = WaveformProjection.Create(data, (int)Math.Ceiling(ActualWidth / 2));
            var center = ActualHeight / 2; var half = Math.Max(0, (ActualHeight - 12) / 2);
            for (var column = 0; column < columns.Length; column++)
            {
                var x = ActualWidth * column / columns.Length;
                var width = Math.Max(0.5, ActualWidth / columns.Length - 1);
                var amplitude = Math.Clamp(columns[column].Rms, 0, 1) * half;
                if (UsePeaks) Bar(context, x, width, center - Math.Clamp(columns[column].Maximum, 0, 1) * half, center - Math.Clamp(columns[column].Minimum, -1, 0) * half);
                else if (amplitude > 0) Bar(context, x, width, center - amplitude, center + amplitude);
                Bar(peakContext, x, width, center - Math.Clamp(columns[column].Maximum, 0, 1) * half,
                    center - Math.Clamp(columns[column].Minimum, -1, 0) * half);
            }
        }
        geometry.Freeze(); peaks.Freeze(); _geometry = geometry; _peakGeometry = peaks;
        static void Bar(StreamGeometryContext context, double x, double width, double top, double bottom)
        {
            context.BeginFigure(new Point(x, top), true, true);
            context.LineTo(new Point(x + width, top), true, false); context.LineTo(new Point(x + width, bottom), true, false); context.LineTo(new Point(x, bottom), true, false);
        }
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
