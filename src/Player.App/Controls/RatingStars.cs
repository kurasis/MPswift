using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Player.App.Controls;

/// <summary>Five 18-DIP stars with mouse, keyboard and range-value accessibility.</summary>
public sealed class RatingStars : Control
{
    public static readonly DependencyProperty RatingProperty = DependencyProperty.Register(nameof(Rating), typeof(int), typeof(RatingStars),
        new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            OnRatingChanged, (_, value) => Math.Clamp((int)value, 0, 5)));
    private static readonly Geometry Star = CreateStar();
    public int Rating { get => (int)GetValue(RatingProperty); set => SetValue(RatingProperty, value); }
    public RatingStars()
    {
        Width = 110; Height = 28; Focusable = true; Cursor = Cursors.Hand;
        SetResourceReference(ForegroundProperty, "AccentBrush");
    }
    private static Geometry CreateStar()
    {
        var geometry = Geometry.Parse("M9,1 L11.4,6.2 17,6.9 12.9,10.8 14,16.5 9,13.7 4,16.5 5.1,10.8 1,6.9 6.6,6.2 Z");
        geometry.Freeze(); return geometry;
    }
    protected override void OnRender(DrawingContext context)
    {
        base.OnRender(context);
        context.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        var outline = (Brush)FindResource("TextSecondaryBrush");
        for (var index = 0; index < 5; index++)
        {
            context.PushTransform(new TranslateTransform(index * 22 + 2, (ActualHeight - 18) / 2));
            context.DrawGeometry(index < Rating ? Foreground : null, new Pen(index < Rating ? Foreground : outline, 1.2), Star);
            context.Pop();
        }
        if (IsKeyboardFocused) context.DrawRoundedRectangle(null, new Pen(outline, 1), new Rect(0.5, 0.5, ActualWidth - 1, ActualHeight - 1), 4, 4);
    }
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e); Focus();
        var selected = Math.Clamp((int)(e.GetPosition(this).X / 22) + 1, 1, 5);
        SetCurrentValue(RatingProperty, selected == Rating ? 0 : selected); e.Handled = true;
    }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (Keyboard.Modifiers != ModifierKeys.None) return;
        int? next = e.Key switch
        {
            Key.Right or Key.Up => Math.Min(5, Rating + 1),
            Key.Left or Key.Down => Math.Max(0, Rating - 1),
            Key.Home or Key.Delete or Key.Back => 0,
            Key.End => 5,
            Key.Space => Rating == 0 ? 1 : 0,
            _ => null
        };
        if (next is { } value) { SetCurrentValue(RatingProperty, value); e.Handled = true; }
    }
    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e) { base.OnGotKeyboardFocus(e); InvalidateVisual(); }
    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e) { base.OnLostKeyboardFocus(e); InvalidateVisual(); }
    private static void OnRatingChanged(DependencyObject owner, DependencyPropertyChangedEventArgs args)
    {
        if (UIElementAutomationPeer.FromElement((RatingStars)owner) is { } peer)
            peer.RaisePropertyChangedEvent(RangeValuePatternIdentifiers.ValueProperty, (double)(int)args.OldValue, (double)(int)args.NewValue);
    }
    protected override AutomationPeer OnCreateAutomationPeer() => new RatingPeer(this);
    private sealed class RatingPeer(RatingStars owner) : FrameworkElementAutomationPeer(owner), IRangeValueProvider
    {
        protected override string GetClassNameCore() => nameof(RatingStars);
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Slider;
        public override object? GetPattern(PatternInterface pattern) => pattern == PatternInterface.RangeValue ? this : base.GetPattern(pattern);
        public bool IsReadOnly => !owner.IsEnabled;
        public double LargeChange => 1;
        public double SmallChange => 1;
        public double Maximum => 5;
        public double Minimum => 0;
        public double Value => owner.Rating;
        public void SetValue(double value)
        {
            if (!owner.IsEnabled) throw new ElementNotEnabledException();
            if (!double.IsFinite(value) || value < 0 || value > 5 || value != Math.Truncate(value)) throw new ArgumentOutOfRangeException(nameof(value));
            owner.SetCurrentValue(RatingProperty, (int)value);
        }
    }
}
