using System.Windows;
using Player.App.Services.Audio;
using Player.App.ViewModels;
using Player.App.Views;
using Player.App.Services.Library;
using Player.App.Services.Windows;
using Player.Core.Playback;
using System.IO;
using System.Text.Json;

namespace Player.App;

public partial class App : Application
{
    private async void OnStartup(object sender, StartupEventArgs e)
    {
        var smoke = e.Args.Length == 3 && e.Args[0] == "--ui-smoke";
        using var validation = smoke ? new UiSmokeValidation() : null;
        if (smoke) ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var model = CreateModel();
        var window = new MainWindow
        {
            DataContext = model
        };
        MainWindow = window;
        window.Show();
        if (smoke)
        {
            var output = Path.Combine(Environment.CurrentDirectory, "artifacts", "smoke");
            Directory.CreateDirectory(output);
            var resultCode = 0;
            object result;
            try
            {
                result = await validation!.RunAsync(window, model, Path.GetFullPath(e.Args[1]), Path.GetFullPath(e.Args[2]), output);
                await window.CloseForValidationAsync();
            }
            catch (Exception error)
            {
                resultCode = 1;
                result = new { Status = "ui-smoke-failed", error.Message, error.StackTrace };
                try { await model.DisposeAsync(); } catch (Exception cleanup) { result = new { Status = "ui-smoke-failed", error.Message, Cleanup = cleanup.Message }; }
            }
            File.WriteAllText(Path.Combine(output, "ui.json"), JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
            Shutdown(resultCode);
        }
        else if (e.Args.Length > 0) await model.AddPathsAsync(e.Args);
    }
    private PlayerViewModel CreateModel()
    {
        var player = new SerializedAudioPlayer(() => new BassAudioBackend());
        return new PlayerViewModel(player, new PlaybackCoordinator(player), new MediaImportService(), new FileDialogService(), Dispatcher);
    }
}
