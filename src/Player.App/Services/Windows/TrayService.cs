using System.ComponentModel;
using System.Windows;
using Player.App.Resources;
using Player.App.ViewModels;
using Player.App.Views;
using Forms = System.Windows.Forms;

namespace Player.App.Services.Windows;

public sealed class TrayService : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private readonly Forms.ContextMenuStrip _menu = new();
    private readonly MainWindow _window;
    private readonly PlayerViewModel _model;
    private readonly Forms.ToolStripItem _play;
    public TrayService(MainWindow window, PlayerViewModel model)
    {
        _window = window; _model = model;
        void Item(string key, Action action) => _menu.Items.Add(Strings.Get(key), null, (_, _) => window.Dispatcher.BeginInvoke(action));
        Item("ShowHide", () => { if (window.IsVisible) window.Hide(); else window.ShowAndActivate(); });
        _play = _menu.Items.Add(model.PlayPauseLabel, null, async (_, _) => await RunAsync(model.PlayPauseCommand.ExecuteAsync(null)));
        Item("Next", () => _ = RunAsync(model.NextCommand.ExecuteAsync(null)));
        Item("Previous", () => _ = RunAsync(model.PreviousCommand.ExecuteAsync(null)));
        _menu.Items.Add(new Forms.ToolStripSeparator()); Item("Exit", window.ExitApplication);
        _icon = new Forms.NotifyIcon { Icon = System.Drawing.SystemIcons.Application, ContextMenuStrip = _menu, Visible = true };
        _icon.DoubleClick += (_, _) => window.Dispatcher.BeginInvoke(window.ShowAndActivate);
        model.PropertyChanged += Changed; Update();
    }
    private async Task RunAsync(Task task) { try { await task; } catch (Exception error) { _model.Message = Strings.ErrorUnexpected; _model.Details = error.Message; } }
    private void Changed(object? sender, PropertyChangedEventArgs args) { if (args.PropertyName is nameof(PlayerViewModel.Title) or nameof(PlayerViewModel.IsPlaying)) Update(); }
    private void Update()
    {
        var text = _model.ProductName + " — " + _model.Title;
        var length = Math.Min(text.Length, 63); if (length < text.Length && char.IsHighSurrogate(text[length - 1])) length--;
        _icon.Text = text[..length]; _play.Text = _model.PlayPauseLabel;
    }
    public void Dispose() { _model.PropertyChanged -= Changed; _icon.Visible = false; _icon.Dispose(); _menu.Dispose(); }
}
