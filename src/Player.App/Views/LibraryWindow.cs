using Player.App.Controls;
using Player.App.Resources;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using Player.App.ViewModels;
using Player.Core.Library;

namespace Player.App.Views;

public sealed class LibraryWindow : Window
{
    private readonly PlayerViewModel _model;
    private readonly ObservableCollection<IndexedFile> _files = [];
    private readonly TextBox _search;
    private readonly ListBox _list;
    private readonly TextBlock _status;
    internal Button PreviousButton { get; }
    internal Button NextButton { get; }
    internal TextBox SearchBox => _search;
    internal ComboBox RootsBox { get; }
    internal TextBlock StatusText => _status;
    internal Task SearchCompletion { get; private set; } = Task.CompletedTask;
    private const int PageSize = 100;
    private long _total;

    private CancellationTokenSource? _searchDelay;
    private long _queryGeneration;
    private int _offset;
    private bool _closed;
    public LibraryWindow(Window owner, PlayerViewModel model)
    {
        SetResourceReference(StyleProperty, typeof(Window));
        Owner = owner; _model = model; LocalizedStrings.Bind(this, TitleProperty, "LibraryTitle"); Width = 780; Height = 580; MinWidth = 460; MinHeight = 380;
        var panel = new DockPanel { Margin = new Thickness(12) }; Content = panel;
        var controls = new WrapPanel(); DockPanel.SetDock(controls, Dock.Top); panel.Children.Add(controls);
        Button Button(string key, AppIconKind icon, RoutedEventHandler action) { var b = IconActionButton.Create(key, icon); b.Click += action; controls.Children.Add(b); return b; }
        void Label(string key, Control target)
        {
            var caption = new TextBlock { TextWrapping = TextWrapping.Wrap }; LocalizedStrings.Bind(caption, TextBlock.TextProperty, key);
            var label = new Label { Content = caption, Target = target, Padding = new Thickness(0, 4, 0, 2) };
            AutomationProperties.SetLabeledBy(target, label); LocalizedStrings.Bind(target, AutomationProperties.NameProperty, key);
            DockPanel.SetDock(label, Dock.Top); panel.Children.Add(label);
        }
        Button("AddRoot", AppIconKind.FolderAdd, async (_, _) => { var d = new Microsoft.Win32.OpenFolderDialog(); if (d.ShowDialog(this) == true) { try { await model.AddLibraryRootAsync(d.FolderName); } catch (Exception e) { MessageBox.Show(this, Strings.ErrorUnexpected + "\n\n" + e.Message, Strings.Get("LibraryTitle")); } } });
        Button("RefreshRoots", AppIconKind.Refresh, (_, _) => model.ScanRoots()); Button("CancelScan", AppIconKind.Stop, (_, _) => model.CancelScan());
        Button("AddSelected", AppIconKind.Add, (_, _) => model.AddLibraryTracks(_list!.SelectedItems.Cast<IndexedFile>().Select(f => f.Track)));
        PreviousButton = Button("PreviousPage", AppIconKind.Previous, async (sender, _) => { if (sender is not Button { IsEnabled: true }) return; _offset = Math.Max(0, _offset - PageSize); await SearchAsync(); });
        NextButton = Button("NextPage", AppIconKind.Next, async (sender, _) => { if (sender is not Button { IsEnabled: true }) return; _offset += PageSize; await SearchAsync(); });
        PreviousButton.IsEnabled = NextButton.IsEnabled = false;
        _search = new TextBox { Margin = new Thickness(0, 0, 0, 8), MaxLength = 4096 }; Label("LibrarySearch", _search); LocalizedStrings.Bind(_search, ToolTipProperty, "LibrarySearch"); DockPanel.SetDock(_search, Dock.Top); panel.Children.Add(_search);
        _search.TextChanged += async (_, _) => { _searchDelay?.Cancel(); _searchDelay?.Dispose(); _searchDelay = new(); var token = _searchDelay.Token; try { await Task.Delay(200, token); _offset = 0; await SearchAsync(); } catch (OperationCanceledException) { } };
        _status = AccessibleStatus.Create(); _status.Margin = new Thickness(0, 6, 0, 6); DockPanel.SetDock(_status, Dock.Bottom); panel.Children.Add(_status);
        var roots = new ComboBox { ItemsSource = model.LibraryRoots, DisplayMemberPath = "Path", Margin = new Thickness(0, 0, 0, 8) }; RootsBox = roots; Label("LibraryRoots", roots); DockPanel.SetDock(roots, Dock.Top); panel.Children.Add(roots);
        Button("DisableRoot", AppIconKind.Delete, async (_, _) => { if (roots.SelectedItem is LibraryRoot root) await model.DisableLibraryRootAsync(root); });
        _list = new ListBox { ItemsSource = _files, DisplayMemberPath = "Track.Title", SelectionMode = SelectionMode.Extended }; LocalizedStrings.Bind(_list, AutomationProperties.NameProperty, "LibraryResults"); panel.Children.Add(_list);
        VirtualizingPanel.SetIsVirtualizing(_list, true); VirtualizingPanel.SetVirtualizationMode(_list, VirtualizationMode.Recycling);
        ScrollViewer.SetCanContentScroll(_list, true);
        model.PropertyChanged += OnModelChanged;
        Loaded += async (_, _) => await SearchAsync(); Closed += (_, _) => { _closed = true; model.PropertyChanged -= OnModelChanged; _searchDelay?.Cancel(); };
    }
    private async void OnModelChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) { if (e.PropertyName == nameof(PlayerViewModel.IsScanning) && !_model.IsScanning) await SearchAsync(); }
    private Task SearchAsync() => SearchCompletion = SearchCoreAsync();
    private async Task SearchCoreAsync()
    {
        try
        {
            await LoadPageAsync(_search.Text, _offset);
        }
        catch (Exception e) { if (!_closed) { AccessibleStatus.Update(_status, Strings.ErrorUnexpected); _model.Details = e.Message; } }
    }
    internal async Task<LibraryPage?> LoadPageAsync(string query, int offset)
    {
        if (_model.LibraryIndex is null || _closed) return null;
        var generation = ++_queryGeneration;
        PreviousButton.IsEnabled = NextButton.IsEnabled = false;
        try
        {
            var page = await _model.LibraryIndex.SearchAsync(query, offset, PageSize);
            if (_closed || generation != _queryGeneration) return null;
            while (page.Offset > (page.Total == 0 ? 0 : (page.Total - 1) / PageSize * PageSize))
            {
                var lastOffset = page.Total == 0 ? 0 : checked((int)((page.Total - 1) / PageSize * PageSize));
                page = await _model.LibraryIndex.SearchAsync(query, lastOffset, PageSize);
                if (_closed || generation != _queryGeneration) return null;
            }
            _offset = page.Offset; _total = page.Total;
            _files.Clear(); foreach (var file in page.Files) _files.Add(file);
            AccessibleStatus.Update(_status, string.Format(Strings.Culture, Strings.Get("LibraryPage"), page.Total,
                page.Files.Length == 0 ? 0 : page.Offset + 1, page.Files.Length == 0 ? 0 : page.Offset + page.Files.Length, _model.ScanStatus));
            return page;
        }
        catch (Exception) when (_closed || generation != _queryGeneration) { return null; }
        finally
        {
            if (!_closed && generation == _queryGeneration)
            { PreviousButton.IsEnabled = _offset > 0; NextButton.IsEnabled = (long)_offset + PageSize < _total; }
        }
    }
    internal ListBox ResultList => _list;
}
