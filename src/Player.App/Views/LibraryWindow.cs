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
    private readonly System.Windows.Threading.DispatcherTimer _scanTimer;
    private CancellationTokenSource? _searchDelay;
    private long _queryGeneration;
    private int _offset;
    private bool _closed;
    public LibraryWindow(Window owner, PlayerViewModel model)
    {
        Owner = owner; _model = model; Title = "Local music library"; Width = 780; Height = 580;
        var panel = new DockPanel { Margin = new Thickness(12) }; Content = panel;
        var controls = new WrapPanel(); DockPanel.SetDock(controls, Dock.Top); panel.Children.Add(controls);
        void Button(string text, RoutedEventHandler action) { var b = new Button { Content = text, Margin = new Thickness(3) }; b.Click += action; controls.Children.Add(b); }
        Button("Add root", async (_, _) => { var d = new Microsoft.Win32.OpenFolderDialog(); if (d.ShowDialog(this) == true) { try { await model.AddLibraryRootAsync(d.FolderName); } catch (Exception e) { MessageBox.Show(this, e.Message); } } });
        Button("Refresh roots", (_, _) => model.ScanRoots()); Button("Cancel scan", (_, _) => model.CancelScan());
        Button("Add selected to playlist", (_, _) => model.AddLibraryTracks(_list!.SelectedItems.Cast<IndexedFile>().Select(f => f.Track)));
        Button("Previous page", async (_, _) => { _offset = Math.Max(0, _offset - 100); await SearchAsync(); }); Button("Next page", async (_, _) => { _offset += 100; await SearchAsync(); });
        _search = new TextBox { Margin = new Thickness(0, 8, 0, 8), ToolTip = "Literal Unicode library search", MaxLength = 4096 }; DockPanel.SetDock(_search, Dock.Top); panel.Children.Add(_search);
        _search.TextChanged += async (_, _) => { _searchDelay?.Cancel(); _searchDelay?.Dispose(); _searchDelay = new(); var token = _searchDelay.Token; try { await Task.Delay(200, token); _offset = 0; await SearchAsync(); } catch (OperationCanceledException) { } };
        _status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 6) }; DockPanel.SetDock(_status, Dock.Bottom); panel.Children.Add(_status);
        var roots = new ComboBox { ItemsSource = model.LibraryRoots, DisplayMemberPath = "Path", Margin = new Thickness(0, 0, 0, 8) }; DockPanel.SetDock(roots, Dock.Top); panel.Children.Add(roots);
        Button("Disable selected root", async (_, _) => { if (roots.SelectedItem is LibraryRoot root) await model.DisableLibraryRootAsync(root); });
        _list = new ListBox { ItemsSource = _files, DisplayMemberPath = "Track.Title", SelectionMode = SelectionMode.Extended }; panel.Children.Add(_list);
        _scanTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _scanTimer.Tick += async (_, _) => { if (!model.IsScanning && model.ScanStatus != _lastScan) { _lastScan = model.ScanStatus; await SearchAsync(); } }; _scanTimer.Start();
        Loaded += async (_, _) => await SearchAsync(); Closed += (_, _) => { _closed = true; _scanTimer.Stop(); _searchDelay?.Cancel(); };
    }
    private string _lastScan = "";
    private async Task SearchAsync()
    {
        if (_model.LibraryIndex is null || _closed) return;
        var generation = ++_queryGeneration;
        try
        {
            var page = await _model.LibraryIndex.SearchAsync(_search.Text, _offset);
            if (_closed || generation != _queryGeneration) return;
            _files.Clear(); foreach (var file in page.Files) _files.Add(file);
            _status.Text = $"{page.Total} results · {_offset + 1}–{_offset + page.Files.Length}. {_model.ScanStatus}";
        }
        catch (Exception e) { if (!_closed) _status.Text = e.Message; }
    }
}
