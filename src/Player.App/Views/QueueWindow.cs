using Player.App.Controls;
using Player.App.Resources;
using System.Windows;
using System.Windows.Controls;
using Player.App.ViewModels;
using Player.Core.Playback;

namespace Player.App.Views;

public sealed class QueueWindow : Window
{
    public QueueWindow(Window owner, PlayerViewModel model)
    {
        SetResourceReference(StyleProperty, typeof(Window));
        Owner = owner; LocalizedStrings.Bind(this, TitleProperty, "QueueTitle"); Width = 580; Height = 420;
        var root = new DockPanel { Margin = new Thickness(12) }; Content = root;
        var actions = new StackPanel { Orientation = Orientation.Horizontal }; DockPanel.SetDock(actions, Dock.Bottom); root.Children.Add(actions);
        var list = new ListBox { ItemsSource = model.Queue, DisplayMemberPath = "Entry.Track.Title" };
        void Button(string key, AppIconKind icon, Action action) { var b = IconActionButton.Create(key, icon); b.Click += (_, _) => action(); actions.Children.Add(b); }
        Button("QueueMoveUp", AppIconKind.Up, () => { if (list.SelectedItem is QueueItem item) model.MoveQueued(item.Id, -1); });
        Button("QueueMoveDown", AppIconKind.Down, () => { if (list.SelectedItem is QueueItem item) model.MoveQueued(item.Id, 1); });
        Button("QueueRemove", AppIconKind.Delete, () => { if (list.SelectedItem is QueueItem item) model.RemoveQueued(item.Id); });
        Button("ClearQueue", AppIconKind.Delete, () => model.ClearQueueCommand.Execute(null));
        var description = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
        LocalizedStrings.Bind(description, TextBlock.TextProperty, "QueueDescription"); DockPanel.SetDock(description, Dock.Top); root.Children.Add(description); root.Children.Add(list);
    }
}
