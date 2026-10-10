using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Player.App.Controls;
using Player.App.Resources;
using Player.App.Services.Windows;
using Player.App.ViewModels;

namespace Player.App.Views;

internal sealed class DesktopPanelWindow : Window
{
    private readonly MainWindow _main;
    private HwndSource? _source;
    private readonly DispatcherTimer _stackTimer;
    private bool _closed, _dragging, _seeking;
    internal TextBlock TrackTitle { get; } = new() { FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis };
    internal Slider PositionSlider { get; } = new() { IsMoveToPointEnabled = true, Minimum = 0, Height = 16, Focusable = false };
    internal Slider VolumeSlider { get; } = new() { IsMoveToPointEnabled = true, Minimum = 0, Maximum = 100, Height = 18, Focusable = false };
    internal Button RestoreButton { get; }
    internal CheckBox ShuffleButton { get; }
    internal nint Handle => new WindowInteropHelper(this).Handle;
    internal Task SeekCompletion { get; private set; } = Task.CompletedTask;
    private PlayerViewModel Model => (PlayerViewModel)DataContext;

    internal DesktopPanelWindow(MainWindow main)
    {
        _main = main;
        SetResourceReference(StyleProperty, typeof(Window));
        LocalizedStrings.Bind(this, TitleProperty, "DesktopPanelTitle");
        Width = Math.Min(800, SystemParameters.WorkArea.Width); Height = 72;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false; ShowActivated = false; Topmost = false;
        // Deliberately unowned: Windows hides owned windows when their owner minimizes.
        var frame = new Border { BorderThickness = new Thickness(1), Padding = new Thickness(10, 5, 8, 5), CornerRadius = new CornerRadius(5) };
        frame.SetResourceReference(Border.BackgroundProperty, "BackgroundBrush"); frame.SetResourceReference(Border.BorderBrushProperty, "AccentBrush"); Content = frame;
        var root = new Grid(); frame.Child = root;
        foreach (var width in new[] { new GridLength(46), new GridLength(1, GridUnitType.Star), new GridLength(300), new GridLength(95), new GridLength(34) }) root.ColumnDefinitions.Add(new() { Width = width });
        var artwork = new Grid { Width = 38, Height = 38, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        var fallback = new Image { Source = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/MPswift;component/Assets/MPswift.png")), Width = 30, Height = 30, Opacity = .8 };
        artwork.Children.Add(fallback); var cover = new Image { Stretch = Stretch.Uniform }; cover.SetBinding(Image.SourceProperty, new Binding("CoverArt")); artwork.Children.Add(cover); root.Children.Add(artwork);
        var description = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) }; Grid.SetColumn(description, 1); root.Children.Add(description);
        TrackTitle.SetBinding(TextBlock.TextProperty, new Binding("Title")); TrackTitle.SetBinding(ToolTipProperty, new Binding("Title")); description.Children.Add(TrackTitle);
        var artist = new TextBlock { FontSize = 10, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 3, 0, 0) }; artist.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush"); artist.SetBinding(TextBlock.TextProperty, new Binding("Artist")); description.Children.Add(artist);
        var transport = new Grid(); Grid.SetColumn(transport, 2); root.Children.Add(transport); transport.RowDefinitions.Add(new() { Height = new GridLength(34) }); transport.RowDefinitions.Add(new());
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center }; actions.SetBinding(IsEnabledProperty, new Binding("CanTransport")); transport.Children.Add(actions);
        Button Command(string key, AppIconKind icon, string command)
        { var button = IconActionButton.Create(key, icon); Compact(button); button.SetBinding(Button.CommandProperty, new Binding(command)); actions.Children.Add(button); return button; }
        Command("Previous", AppIconKind.Previous, "PreviousCommand"); Command("Stop", AppIconKind.Stop, "StopCommand");
        var play = Command("Play", AppIconKind.Play, "PlayPauseCommand"); play.SetBinding(ToolTipProperty, new Binding("PlayPauseLabel")); play.SetBinding(AutomationProperties.NameProperty, new Binding("PlayPauseLabel"));
        var playIcon = (AppIcon)play.Content; var iconStyle = new Style(typeof(AppIcon)); iconStyle.Setters.Add(new Setter(AppIcon.KindProperty, AppIconKind.Play));
        var playing = new DataTrigger { Binding = new Binding("IsPlaying"), Value = true }; playing.Setters.Add(new Setter(AppIcon.KindProperty, AppIconKind.Pause)); iconStyle.Triggers.Add(playing); playIcon.ClearValue(AppIcon.KindProperty); playIcon.Style = iconStyle;
        Command("Next", AppIconKind.Next, "NextCommand");
        ShuffleButton = new CheckBox { Content = new AppIcon { Kind = AppIconKind.Shuffle }, Focusable = false, Margin = new Thickness(5, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        ShuffleButton.SetBinding(ToggleButton.IsCheckedProperty, new Binding("Shuffle") { Mode = BindingMode.TwoWay }); LocalizedStrings.Bind(ShuffleButton, ToolTipProperty, "Shuffle"); LocalizedStrings.Bind(ShuffleButton, AutomationProperties.NameProperty, "Shuffle"); actions.Children.Add(ShuffleButton);
        var seek = new Grid(); Grid.SetRow(seek, 1); transport.Children.Add(seek); seek.ColumnDefinitions.Add(new() { Width = new GridLength(36) }); seek.ColumnDefinitions.Add(new()); seek.ColumnDefinitions.Add(new() { Width = new GridLength(36) });
        var elapsed = new TextBlock { FontSize = 10, VerticalAlignment = VerticalAlignment.Center }; elapsed.SetBinding(TextBlock.TextProperty, new Binding("Elapsed")); seek.Children.Add(elapsed);
        Grid.SetColumn(PositionSlider, 1); seek.Children.Add(PositionSlider); PositionSlider.SetBinding(RangeBase.MaximumProperty, new Binding("DurationSeconds")); PositionSlider.SetBinding(RangeBase.ValueProperty, new Binding("SeekPosition") { Mode = BindingMode.TwoWay }); PositionSlider.SetBinding(IsEnabledProperty, new Binding("CanSeek")); LocalizedStrings.Bind(PositionSlider, AutomationProperties.NameProperty, "Seek");
        AddHandler(Mouse.PreviewMouseUpEvent, new MouseButtonEventHandler((_, args) => { if (args.ChangedButton == MouseButton.Left) FinishSeek(); }), true);
        PositionSlider.AddHandler(Mouse.LostMouseCaptureEvent, new MouseEventHandler((_, _) => FinishSeek()), true);
        var duration = new TextBlock { FontSize = 10, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right }; duration.SetBinding(TextBlock.TextProperty, new Binding("DurationText")); Grid.SetColumn(duration, 2); seek.Children.Add(duration);
        var volume = new StackPanel { Margin = new Thickness(12, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center }; Grid.SetColumn(volume, 3); root.Children.Add(volume);
        var mute = new CheckBox { Content = new AppIcon { Kind = AppIconKind.Volume }, Focusable = false, HorizontalAlignment = HorizontalAlignment.Center }; mute.SetBinding(ToggleButton.IsCheckedProperty, new Binding("Muted") { Mode = BindingMode.TwoWay }); LocalizedStrings.Bind(mute, ToolTipProperty, "Mute"); LocalizedStrings.Bind(mute, AutomationProperties.NameProperty, "Mute"); volume.Children.Add(mute);
        VolumeSlider.SetBinding(RangeBase.ValueProperty, new Binding("Volume") { Mode = BindingMode.TwoWay }); LocalizedStrings.Bind(VolumeSlider, AutomationProperties.NameProperty, "Volume"); volume.Children.Add(VolumeSlider);
        RestoreButton = IconActionButton.Create("DesktopPanelRestore", AppIconKind.Up); Compact(RestoreButton); Grid.SetColumn(RestoreButton, 4); root.Children.Add(RestoreButton); RestoreButton.Click += (_, _) => _main.ShowAndActivate();
        frame.PreviewMouseDown += OnDrag;
        _stackTimer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => KeepBehind(), Dispatcher); _stackTimer.Stop();
        SourceInitialized += (_, _) => { DesktopPanelLayer.Nonactivating(Handle); _source = HwndSource.FromHwnd(Handle); _source?.AddHook(NativeMessage); };
        Closed += (_, _) => { _closed = true; _stackTimer.Stop(); _source?.RemoveHook(NativeMessage); _source = null; DataContext = null; };
    }
    private static void Compact(Button button)
    { button.Focusable = false; button.Width = 28; button.Height = 28; button.Margin = new Thickness(2); }
    internal void Reveal()
    {
        if (_closed) return; Show(); UpdateLayout();
        Place(Model.WindowSettings.DesktopPanelLeft, Model.WindowSettings.DesktopPanelTop); _stackTimer.Start();
    }
    internal void Conceal()
    {
        _stackTimer.Stop();
        if (_seeking && DataContext is PlayerViewModel model) model.SeekPreview = false;
        _seeking = false;
        if (IsVisible) Hide();
    }
    internal void Place(int? left, int? top)
    { DesktopPanelLayer.Fit(Handle, new WindowInteropHelper(_main).Handle, left, top); RememberPlacement(); }
    private void RememberPlacement()
    { if (DataContext is PlayerViewModel model) { var bounds = DesktopPanelLayer.ReadBounds(Handle); model.RememberDesktopPanelPosition(bounds.Left, bounds.Top); } }
    private void OnDrag(object sender, MouseButtonEventArgs args)
    {
        if (args.ChangedButton != MouseButton.Left || args.OriginalSource is not DependencyObject element) return;
        for (var current = element; current is not null; current = current is Visual ? VisualTreeHelper.GetParent(current) : LogicalTreeHelper.GetParent(current))
        {
            if (ReferenceEquals(current, PositionSlider) && PositionSlider.IsEnabled)
            {
                _seeking = true; Model.SeekPreview = true;
                // Track clicks jump immediately; thumb drags finish on release/capture loss.
                Dispatcher.BeginInvoke(() => { if (_seeking && !PositionSlider.IsMouseCaptureWithin) FinishSeek(); }, DispatcherPriority.Input);
            }
            if (current is ButtonBase or Slider) return;
        }
        args.Handled = true;
        if (args.ClickCount == 2) { _main.ShowAndActivate(); return; }
        _dragging = true;
        try { DragMove(); var bounds = DesktopPanelLayer.ReadBounds(Handle); Place(bounds.Left, bounds.Top); }
        catch (Exception error) when (error is IOException or InvalidOperationException)
        { Model.Message = Strings.Get("DesktopPanelUnavailable"); Model.Details = error.Message; }
        finally { _dragging = false; KeepBehind(); }
    }
    private nint NativeMessage(nint window, int message, nint parameter, nint data, ref bool handled)
    {
        if (message == DesktopPanelLayer.MouseActivate) { handled = true; return 3; } // MA_NOACTIVATE: deliver the click without taking foreground focus.
        if (message == DesktopPanelLayer.PositionChanging && data != 0)
        {
            var position = Marshal.PtrToStructure<DesktopPanelLayer.Position>(data);
            if ((position.Flags & DesktopPanelLayer.NoZOrder) == 0) { position.After = DesktopPanelLayer.BottomAnchor(window); position.Flags |= DesktopPanelLayer.NoActivate; Marshal.StructureToPtr(position, data, false); }
        }
        return 0;
    }
    private void KeepBehind()
    {
        if (!IsVisible || _closed || _dragging) return;
        try { DesktopPanelLayer.Behind(Handle); }
        catch (IOException error) { _stackTimer.Stop(); Model.Message = Strings.Get("DesktopPanelUnavailable"); Model.Details = error.Message; }
    }
    private void FinishSeek()
    { if (!_seeking || DataContext is not PlayerViewModel model) return; _seeking = false; model.SeekPreview = false; SeekCompletion = SeekAsync(model, PositionSlider.Value); }
    internal async Task SeekAsync(PlayerViewModel model, double position)
    { try { await model.CommitSeekAsync(position); } catch (Exception error) { model.Message = Strings.ErrorUnexpected; model.Details = error.Message; } }
}
