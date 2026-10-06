using Player.App.Resources;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
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

    private CancellationTokenSource? _searchDelay;
    private long _queryGeneration;
    private int _offset;
    private bool _closed;
    public LibraryWindow(Window owner, PlayerViewModel model)
    {
        Owner = owner; _model = model; Title = Strings.Get("LibraryTitle"); Width = 780; Height = 580;
        var panel = new DockPanel { Margin = new Thickness(12) }; Content = panel;
        var controls = new WrapPanel(); DockPanel.SetDock(controls, Dock.Top); panel.Children.Add(controls);
        void Button(string text, RoutedEventHandler action) { var b = new Button { Content = text, Margin = new Thickness(3) }; b.Click += action; controls.Children.Add(b); }
        Button(Strings.Get("AddRoot"), async (_, _) => { var d = new Microsoft.Win32.OpenFolderDialog(); if (d.ShowDialog(this) == true) { try { await model.AddLibraryRootAsync(d.FolderName); } catch (Exception e) { MessageBox.Show(this, Strings.ErrorUnexpected + "\n\n" + e.Message, Strings.Get("LibraryTitle")); } } });
        Button(Strings.Get("RefreshRoots"), (_, _) => model.ScanRoots()); Button(Strings.Get("CancelScan"), (_, _) => model.CancelScan());
        Button(Strings.Get("AddSelected"), (_, _) => model.AddLibraryTracks(_list!.SelectedItems.Cast<IndexedFile>().Select(f => f.Track)));
        Button(Strings.Get("PreviousPage"), async (_, _) => { _offset = Math.Max(0, _offset - 100); await SearchAsync(); }); Button(Strings.Get("NextPage"), async (_, _) => { _offset += 100; await SearchAsync(); });
        _search = new TextBox { Margin = new Thickness(0, 8, 0, 8), ToolTip = Strings.Get("LibrarySearch"), MaxLength = 4096 }; DockPanel.SetDock(_search, Dock.Top); panel.Children.Add(_search);
        _search.TextChanged += async (_, _) => { _searchDelay?.Cancel(); _searchDelay?.Dispose(); _searchDelay = new(); var token = _searchDelay.Token; try { await Task.Delay(200, token); _offset = 0; await SearchAsync(); } catch (OperationCanceledException) { } };
        _status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 6) }; DockPanel.SetDock(_status, Dock.Bottom); panel.Children.Add(_status);
        var roots = new ComboBox { ItemsSource = model.LibraryRoots, DisplayMemberPath = "Path", Margin = new Thickness(0, 0, 0, 8) }; DockPanel.SetDock(roots, Dock.Top); panel.Children.Add(roots);
        Button(Strings.Get("DisableRoot"), async (_, _) => { if (roots.SelectedItem is LibraryRoot root) await model.DisableLibraryRootAsync(root); });
        _list = new ListBox { ItemsSource = _files, DisplayMemberPath = "Track.Title", SelectionMode = SelectionMode.Extended }; panel.Children.Add(_list);
        VirtualizingPanel.SetIsVirtualizing(_list, true); VirtualizingPanel.SetVirtualizationMode(_list, VirtualizationMode.Recycling);
        ScrollViewer.SetCanContentScroll(_list, true);
        model.PropertyChanged += OnModelChanged;
        Loaded += async (_, _) => await SearchAsync(); Closed += (_, _) => { _closed = true; model.PropertyChanged -= OnModelChanged; _searchDelay?.Cancel(); };
    }
    private async void OnModelChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) { if (e.PropertyName == nameof(PlayerViewModel.IsScanning) && !_model.IsScanning) await SearchAsync(); }
    private async Task SearchAsync()
    {
        try
        {
            await LoadPageAsync(_search.Text, _offset);
        }
        catch (Exception e) { if (!_closed) { _status.Text = Strings.ErrorUnexpected; _model.Details = e.Message; } }
    }
    internal async Task<LibraryPage?> LoadPageAsync(string query, int offset)
    {
        if (_model.LibraryIndex is null || _closed) return null;
        var generation = ++_queryGeneration;
        var page = await _model.LibraryIndex.SearchAsync(query, offset);
        if (_closed || generation != _queryGeneration) return null;
        _files.Clear(); foreach (var file in page.Files) _files.Add(file);
        _status.Text = string.Format(Strings.Culture, Strings.Get("LibraryPage"), page.Total, page.Total == 0 ? 0 : page.Offset + 1, page.Offset + page.Files.Length, _model.ScanStatus);
        return page;
    }
    internal ListBox ResultList => _list;
}
