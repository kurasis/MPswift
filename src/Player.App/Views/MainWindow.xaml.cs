using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Player.App.Resources;
using Player.App.ViewModels;

namespace Player.App.Views;

public partial class MainWindow : Window
{
    private bool _shutdownComplete;
    private bool _shutdownStarted;
    private PlayerViewModel Model => (PlayerViewModel)DataContext;
    public MainWindow()
    {
        InitializeComponent();
        Width = Math.Min(Width, SystemParameters.WorkArea.Width);
        Height = Math.Min(Height, SystemParameters.WorkArea.Height);
    }
    private async void OnTrackDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject element && ItemsControl.ContainerFromElement(PlaylistList, element) is ListBoxItem { DataContext: PlaylistRowViewModel row }
            && !OwnsInput(element)) await Model.PlayEntryCommand.ExecuteAsync(row);
    }
    private void OnRemove(object sender, RoutedEventArgs e) => Model.RemoveEntriesCommand.Execute(PlaylistList.SelectedItems.Cast<PlaylistRowViewModel>().ToArray());
    private void OnHelp(object sender, RoutedEventArgs e) => MessageBox.Show(this, Strings.Get("HelpText"), Strings.Get("Help"), MessageBoxButton.OK, MessageBoxImage.Information);
    private void OnDragOver(object sender, DragEventArgs e) { e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; }
    private async void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths) await Model.AddPathsAsync(paths);
        e.Handled = true;
    }
    private void OnSeekStart(object sender, MouseButtonEventArgs e) => Model.SeekPreview = true;
    private async void OnSeekEnd(object sender, MouseButtonEventArgs e)
    {
        if (!Model.SeekPreview) return;
        Model.SeekPreview = false;
        await Model.CommitSeekAsync(SeekSlider.Value);
    }
    private async void OnSeekCaptureLost(object sender, MouseEventArgs e)
    {
        if (!Model.SeekPreview) return;
        Model.SeekPreview = false;
        await Model.CommitSeekAsync(SeekSlider.Value);
    }
    private async void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.F) { SearchBox.Focus(); SearchBox.SelectAll(); e.Handled = true; return; }
        if (e.Key == Key.Escape) { Model.ClearSearchCommand.Execute(null); e.Handled = true; return; }
        if (e.Key == Key.F1) { OnHelp(sender, e); e.Handled = true; return; }
        if (OwnsInput(e.OriginalSource as DependencyObject)) return;
        if (Keyboard.Modifiers == ModifierKeys.None && e.Key == Key.Space) { await Model.PlayPauseCommand.ExecuteAsync(null); e.Handled = true; }
        else if (PlaylistList.IsKeyboardFocusWithin && e.Key == Key.Enter) { await Model.PlayEntryCommand.ExecuteAsync(Model.SelectedEntry); e.Handled = true; }
        else if (PlaylistList.IsKeyboardFocusWithin && e.Key == Key.Delete) { OnRemove(sender, e); e.Handled = true; }
        else if (PlaylistList.IsKeyboardFocusWithin && e.Key == Key.A && Keyboard.Modifiers == ModifierKeys.Control) { PlaylistList.SelectAll(); e.Handled = true; }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key is Key.Left or Key.Right) { await Model.CommitSeekAsync(Model.SeekPosition + (e.Key == Key.Right ? 5 : -5)); e.Handled = true; }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key is Key.Up or Key.Down) { Model.Volume = Math.Clamp(Model.Volume + (e.Key == Key.Up ? 5 : -5), 0, 100); e.Handled = true; }
    }
    private static bool OwnsInput(DependencyObject? element)
    {
        while (element is not null)
        {
            if (element is TextBoxBase or PasswordBox or ButtonBase or Slider) return true;
            element = element is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D
                ? System.Windows.Media.VisualTreeHelper.GetParent(element)
                : (element as FrameworkContentElement)?.Parent ?? LogicalTreeHelper.GetParent(element);
        }
        return false;
    }
    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_shutdownComplete) return;
        e.Cancel = true;
        if (_shutdownStarted) return;
        _shutdownStarted = true;
        IsEnabled = false;
        try { await Model.DisposeAsync(); _shutdownComplete = true; Close(); }
        catch (Exception error)
        {
            Model.Message = Strings.Get("ErrorUnexpected"); Model.Details = error.Message;
            _shutdownStarted = false; IsEnabled = true;
        }
    }
    public async Task CloseForValidationAsync()
    {
        await Model.DisposeAsync();
        _shutdownComplete = true;
        Close();
    }
}
