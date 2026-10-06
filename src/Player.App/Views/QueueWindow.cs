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
        Owner = owner; Title = Strings.Get("QueueTitle"); Width = 580; Height = 420;
        var root = new DockPanel { Margin = new Thickness(12) }; Content = root;
        var actions = new StackPanel { Orientation = Orientation.Horizontal }; DockPanel.SetDock(actions, Dock.Bottom); root.Children.Add(actions);
        var list = new ListBox { ItemsSource = model.Queue, DisplayMemberPath = "Entry.Track.Title" };
        void Button(string title, Action action) { var b = new Button { Content = title, Margin = new Thickness(3) }; b.Click += (_, _) => action(); actions.Children.Add(b); }
        Button(Strings.Get("QueueMoveUp"), () => { if (list.SelectedItem is QueueItem item) model.MoveQueued(item.Id, -1); });
        Button(Strings.Get("QueueMoveDown"), () => { if (list.SelectedItem is QueueItem item) model.MoveQueued(item.Id, 1); });
        Button(Strings.Get("QueueRemove"), () => { if (list.SelectedItem is QueueItem item) model.RemoveQueued(item.Id); });
        Button(Strings.Get("ClearQueue"), () => model.ClearQueueCommand.Execute(null));
        var description = new TextBlock { Text = Strings.Get("QueueDescription"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
        DockPanel.SetDock(description, Dock.Top); root.Children.Add(description); root.Children.Add(list);
    }
}
