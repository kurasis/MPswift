using System.Windows;
using Player.App.Services.Audio;
using Player.App.ViewModels;
using Player.App.Views;
using Player.App.Services.Library;
using Player.App.Services.Windows;
using Player.Core.Playback;
using Player.App.Services.Storage;
using Player.App.Services.Waveforms;
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
        string directory;
        try { directory = smoke ? StorageLocation.Prepare(Path.Combine(Environment.CurrentDirectory, "artifacts", "smoke", "stage-c-data")) : StorageLocation.Resolve(); }
        catch (Exception error) { MessageBox.Show(error.Message, "Storage unavailable"); Shutdown(1); return; }
        PlayerViewModel model;
        try { model = CreateModel(directory); }
        catch (Exception error) { ReportStartupFailure(smoke, error); Shutdown(1); return; }
        var window = new MainWindow
        {
            DataContext = model
        };
        MainWindow = window;
        try { await model.InitializeAsync(); }
        catch (Exception error)
        {
            await model.DisposeAsync();
            if (!smoke && error is not (NewerDatabaseSchemaException or PlayerStoreInUseException) &&
                MessageBox.Show("Saved data could not be read. Original files will be preserved. Restore a database backup?\n\n" + error.Message, "Saved data unavailable", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "SQLite backup (*.db)|*.db" };
                if (dialog.ShowDialog() == true)
                {
                    try
                    {
                        var backup = LocalFileAccess.ValidateFile(dialog.FileName);
                        await Task.Run(() => DatabaseRecovery.Restore(Path.Combine(directory, "library.db"), backup));
                        model = CreateModel(directory); window.DataContext = model; await model.InitializeAsync();
                    }
                    catch (Exception restoreError) { ReportStartupFailure(false, restoreError); Shutdown(1); return; }
                }
                else { Shutdown(1); return; }
            }
            else { ReportStartupFailure(smoke, error); Shutdown(1); return; }
        }
        window.Width = Math.Min(model.WindowSettings.WindowWidth, SystemParameters.WorkArea.Width);
        window.Height = Math.Min(model.WindowSettings.WindowHeight, SystemParameters.WorkArea.Height);
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
    private static void ReportStartupFailure(bool smoke, Exception error)
    {
        if (smoke)
        {
            var output = Path.Combine(Environment.CurrentDirectory, "artifacts", "smoke"); Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, "ui.json"), JsonSerializer.Serialize(new { Status = "ui-smoke-failed", error.Message, error.StackTrace }));
        }
        else MessageBox.Show("Saved data could not be read. Original files have been preserved.\n\n" + error.Message, "Saved data unavailable", MessageBoxButton.OK, MessageBoxImage.Error);
    }
    public PlayerViewModel CreateModel(string directory)
    {
        var settings = new SettingsFile(directory);
        var options = settings.Load();
        var player = new SerializedAudioPlayer(() => new BassAudioBackend());
        return new PlayerViewModel(player, new PlaybackCoordinator(player), new MediaImportService(), new FileDialogService(), Dispatcher,
            new SqlitePlayerStore(Path.Combine(directory, "library.db")), settings,
            new BassWaveformService(new WaveformCache(Path.Combine(directory, "Cache", "Waveforms"), options.WaveformCacheMiB * 1024L * 1024)));
    }
}
