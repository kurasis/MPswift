using System.IO;
using System.Windows;
using System.Windows.Controls;
using Player.App.Controls;
using Player.App.Resources;
using Player.App.Services.Library;
using Player.App.ViewModels;

namespace Player.App.Views;

public sealed class FileInformationWindow : Window
{
    private readonly PlaylistRowViewModel[] _rows;
    private int _index;
    private long _generation;
    private bool _closed;
    private readonly TextBox _path = new() { IsReadOnly = true, Margin = new Thickness(0, 0, 8, 0) };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _position = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0) };
    private readonly StackPanel _general = new(), _v1 = new(), _v2 = new();
    private readonly TextBox _lyrics = new() { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxLength = 65536 };
    private readonly Image _cover = new() { Width = 128, Height = 128, Margin = new Thickness(16), Stretch = System.Windows.Media.Stretch.Uniform };
    private readonly Button _previous, _next;
    public Task LoadCompletion { get; private set; } = Task.CompletedTask;
    internal FileInformationSnapshot? Snapshot { get; private set; }
    internal TabControl Tabs { get; } = new();
    internal TextBox PathBox => _path;

    public FileInformationWindow(Window owner, IReadOnlyList<PlaylistRowViewModel> rows, Guid initialEntryId)
    {
        if (rows.Count == 0) throw new ArgumentException("At least one track is required.", nameof(rows));
        _rows = rows.ToArray(); _index = Math.Max(0, Array.FindIndex(_rows, row => row.Id == initialEntryId));
        SetResourceReference(StyleProperty, typeof(Window)); Owner = owner;
        LocalizedStrings.Bind(this, TitleProperty, "FileInformation"); Width = 800; Height = 660; MinWidth = 560; MinHeight = 440;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new DockPanel { Margin = new Thickness(18) }; Content = root;
        var heading = new TextBlock { Text = Player.Core.ProductInfo.Name, FontSize = 22, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 14) };
        DockPanel.SetDock(heading, Dock.Top); root.Children.Add(heading);
        var pathRow = new DockPanel { Margin = new Thickness(0, 0, 0, 12) }; DockPanel.SetDock(pathRow, Dock.Top); root.Children.Add(pathRow);
        var actions = new StackPanel { Orientation = Orientation.Horizontal }; DockPanel.SetDock(actions, Dock.Right); pathRow.Children.Add(actions);
        void Action(string key, AppIconKind icon, RoutedEventHandler callback) { var button = IconActionButton.Create(key, icon); button.Click += callback; actions.Children.Add(button); }
        Action("CopyPath", AppIconKind.Copy, (_, _) => { try { Clipboard.SetText(_rows[_index].Path); } catch (System.Runtime.InteropServices.ExternalException) { _status.Text = Strings.Get("ClipboardUnavailable"); } });
        Action("ShowFile", AppIconKind.Folder, (_, _) => { try { System.Diagnostics.Process.Start(MainWindow.CreateShowFileStartInfo(_rows[_index].Path)); } catch (Exception error) { _status.Text = error.Message; } });
        _previous = IconActionButton.Create("PreviousFile", AppIconKind.Previous); _next = IconActionButton.Create("NextFile", AppIconKind.Next);
        _previous.Click += (_, _) => Navigate(-1); _next.Click += (_, _) => Navigate(1);
        actions.Children.Add(_previous); actions.Children.Add(_position); actions.Children.Add(_next);
        LocalizedStrings.Bind(_path, System.Windows.Automation.AutomationProperties.NameProperty, "FilePathLabel"); pathRow.Children.Add(_path);
        var footer = new DockPanel { Margin = new Thickness(0, 12, 0, 0) }; DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        var close = new Button { IsCancel = true, IsDefault = true, MinWidth = 110 }; LocalizedStrings.Bind(close, ContentControl.ContentProperty, "Close");
        close.Click += (_, _) => Close(); DockPanel.SetDock(close, Dock.Right); footer.Children.Add(close); footer.Children.Add(_status);
        var general = new DockPanel(); DockPanel.SetDock(_cover, Dock.Right); general.Children.Add(_cover); general.Children.Add(Scroll(_general));
        AddTab("GeneralTab", general); AddTab("LyricsTab", _lyrics); AddTab("ID3v1", Scroll(_v1), literal: true); AddTab("ID3v2", Scroll(_v2), literal: true);
        root.Children.Add(Tabs);
        Loaded += (_, _) => LoadCompletion = LoadAsync(); Closed += (_, _) => { _closed = true; ++_generation; };
    }
    private static ScrollViewer Scroll(FrameworkElement content) => new() { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    private void AddTab(string key, object content, bool literal = false)
    {
        var item = new TabItem { Content = content, Padding = new Thickness(12) };
        if (literal) item.Header = key; else LocalizedStrings.Bind(item, HeaderedContentControl.HeaderProperty, key);
        Tabs.Items.Add(item);
    }
    internal void Navigate(int delta)
    { _index = Math.Clamp(_index + delta, 0, _rows.Length - 1); LoadCompletion = LoadAsync(); }
    private async Task LoadAsync()
    {
        var request = ++_generation; var row = _rows[_index]; Snapshot = null; _cover.Source = null;
        _path.Text = row.Path; _position.Text = $"{_index + 1} / {_rows.Length}";
        _previous.IsEnabled = _index > 0; _next.IsEnabled = _index + 1 < _rows.Length;
        _status.Text = Strings.Get("LoadingFileInformation"); _general.Children.Clear(); _v1.Children.Clear(); _v2.Children.Clear(); _lyrics.Text = "";
        try
        {
            var value = await Task.Run(() => FileInformationReader.Read(row.Entry.Track));
            if (_closed || request != _generation) return;
            Snapshot = value;
            ShowFields(_general, value.General);
            Field(_general, "FormatLabel", value.Description); Field(_general, "DurationLabel", value.Duration.ToString(@"hh\:mm\:ss\.fff"));
            var quality = new List<string>();
            if (value.SampleRate > 0) quality.Add(value.SampleRate.ToString("N0", Strings.Culture) + " " + Strings.Get("HzUnit"));
            if (value.Bitrate > 0) quality.Add(value.Bitrate + " " + Strings.Get("KbpsUnit"));
            if (value.Channels > 0) quality.Add(value.Channels + " " + Strings.Get("ChannelsUnit"));
            if (value.Bits > 0) quality.Add(value.Bits + " " + Strings.Get("BitUnit"));
            Field(_general, "QualityLabel", string.Join(" · ", quality));
            Field(_general, "FileSizeLabel", (value.Bytes / 1048576d).ToString("0.00", Strings.Culture) + " MB (" + value.Bytes.ToString("N0", Strings.Culture) + ")");
            Field(_general, "ModifiedLabel", value.ModifiedUtc.ToLocalTime().ToString("g", Strings.Culture)); Field(_general, "TagTypesLabel", value.TagTypes);
            Field(_general, "Rating", row.Rating == 0 ? Strings.Get("Unrated") : new string('★', row.Rating));
            if (Owner.DataContext is PlayerViewModel { LibraryIndex: { } index })
            {
                var statistics = (await index.GetStatisticsAsync([row.Entry.Track.Id])).FirstOrDefault();
                if (_closed || request != _generation) return;
                Field(_general, "CountedPlays", (statistics?.PlayCount ?? 0).ToString(Strings.Culture));
                Field(_general, "LastPlayed", statistics?.LastPlayedUtcTicks is { } last ? new DateTime(last, DateTimeKind.Utc).ToLocalTime().ToString("g", Strings.Culture) : "—");
            }
            ShowFields(_v1, value.Id3v1); ShowFields(_v2, value.Id3v2); _lyrics.Text = value.Lyrics;
            if (string.IsNullOrWhiteSpace(_lyrics.Text)) _lyrics.Text = Strings.Get("NoLyrics");
            _status.Text = Strings.Get("ReadOnlyInformation");
            var cover = await new ArtworkService().LoadAsync(row.Path, CancellationToken.None);
            if (!_closed && request == _generation) _cover.Source = cover;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or NotSupportedException or TagLib.CorruptFileException or TagLib.UnsupportedFormatException or NotImplementedException or KeyNotFoundException or ObjectDisposedException or Microsoft.Data.Sqlite.SqliteException)
        { if (!_closed && request == _generation) _status.Text = Strings.Get("FileInformationUnavailable") + " " + error.Message; }
    }
    private static void ShowFields(StackPanel panel, IReadOnlyDictionary<string, string> fields)
    {
        if (fields.Count == 0) { var text = new TextBlock { Margin = new Thickness(8) }; LocalizedStrings.Bind(text, TextBlock.TextProperty, "NoTagBlock"); panel.Children.Add(text); return; }
        foreach (var pair in fields) Field(panel, pair.Key, pair.Value);
    }
    private static void Field(StackPanel panel, string key, string value)
    {
        var grid = new Grid { Margin = new Thickness(4, 3, 4, 3) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) }); grid.ColumnDefinitions.Add(new ColumnDefinition());
        var label = new TextBlock { FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap }; LocalizedStrings.Bind(label, TextBlock.TextProperty, key); grid.Children.Add(label);
        var text = new TextBox { Text = string.IsNullOrWhiteSpace(value) ? "—" : value, IsReadOnly = true, BorderThickness = new Thickness(0), Background = System.Windows.Media.Brushes.Transparent, Padding = new Thickness(0), TextWrapping = TextWrapping.Wrap };
        LocalizedStrings.Bind(text, System.Windows.Automation.AutomationProperties.NameProperty, key); Grid.SetColumn(text, 1); grid.Children.Add(text); panel.Children.Add(grid);
    }
}
