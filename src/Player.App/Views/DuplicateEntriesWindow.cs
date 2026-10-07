using System.Windows;
using System.Windows.Controls;
using Player.App.Resources;
using Player.Core.Playback;

namespace Player.App.Views;

public sealed class DuplicateEntriesWindow : Window
{
    internal Button RemoveButton { get; }
    public DuplicateEntriesWindow(Window owner, IReadOnlyList<PlaylistEntry> duplicates)
    {
        SetResourceReference(StyleProperty, typeof(Window)); Owner = owner; Title = Strings.Get("RemoveDuplicates");
        Width = 620; Height = 480; MinWidth = 440; MinHeight = 320; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new DockPanel { Margin = new Thickness(20) }; Content = root;
        var description = new TextBlock { Text = string.Format(Strings.Culture, Strings.Get("DuplicatesPreview"), duplicates.Count), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) };
        DockPanel.SetDock(description, Dock.Top); root.Children.Add(description);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        DockPanel.SetDock(buttons, Dock.Bottom); root.Children.Add(buttons);
        var cancel = new Button { Content = Strings.Get("Cancel"), IsCancel = true, Margin = new Thickness(0, 0, 8, 0) }; buttons.Children.Add(cancel); cancel.Click += (_, _) => DialogResult = false;
        RemoveButton = new Button { Content = string.Format(Strings.Culture, Strings.Get("RemoveDuplicatesCount"), duplicates.Count), IsEnabled = duplicates.Count > 0 }; buttons.Children.Add(RemoveButton);
        RemoveButton.Click += (_, _) => DialogResult = true;
        var list = new ListBox { ItemsSource = duplicates.Select(entry => entry.Track.Title + "\n" + entry.Track.Path +
            (entry.Track.Segment is { } segment ? "\n" + PlayerViewTime(segment.Start) + " – " + PlayerViewTime(segment.End) : "")).ToArray() };
        VirtualizingPanel.SetIsVirtualizing(list, true); VirtualizingPanel.SetVirtualizationMode(list, VirtualizationMode.Recycling); ScrollViewer.SetCanContentScroll(list, true);
        System.Windows.Automation.AutomationProperties.SetName(list, Strings.Get("RemoveDuplicates")); root.Children.Add(list);
    }
    private static string PlayerViewTime(TimeSpan? value) => ViewModels.PlayerViewModel.FormatTime(value);
}
