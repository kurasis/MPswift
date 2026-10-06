using Player.App.Resources;
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
using System.Globalization;
using Player.Core.Integration;

namespace Player.App;

public partial class App : Application
{
    static App() => System.Windows.Forms.Application.SetHighDpiMode(System.Windows.Forms.HighDpiMode.PerMonitorV2);
    private RotatingLog? _log;
    public string DataDirectory { get; private set; } = "";
    public async Task FlushDiagnosticsAsync() { if (_log is not null) await _log.DisposeAsync(); }
    private void WatchDiagnostics(PlayerViewModel model) => model.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(PlayerViewModel.Details) && model.Details.Length > 0) _log?.Record("operation", model.Details); };
    private SingleInstanceService? _instance;
    private TrayService? _tray;
    private MediaSessionService? _media;
    public bool MediaSessionAvailable => _media is not null;
    public string? InstancePipeName => _instance?.PipeName;
    private async void OnStartup(object sender, StartupEventArgs e)
    {
        var uiSmoke = e.Args.Length == 3 && e.Args[0] == "--ui-smoke";
        var crashSmoke = e.Args.Length == 3 && e.Args[0] == "--crash-smoke" && e.Args[1] is "checkpoint" or "verify" or "migration-checkpoint" or "migration-verify" or "failures" && File.Exists(Path.Combine(Environment.CurrentDirectory, ".player-crash-validation"));
        var smoke = uiSmoke || crashSmoke;
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        OpenRequest? request = null;
        try { if (!smoke) request = OpenRequest.ParseArguments(e.Args, Environment.CurrentDirectory); }
        catch (Exception error) { MessageBox.Show(Strings.Get("InvalidArguments") + "\n\n" + error.Message, Player.Core.ProductInfo.Name); Shutdown(2); return; }
        var ready = new TaskCompletionSource<MainWindow>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            _instance = new SingleInstanceService();
            if (!_instance.IsPrimary)
            {
                if (smoke) throw new InvalidOperationException("UI smoke requires an isolated Windows user session.");
                await _instance.ForwardAsync(request!); Shutdown(); return;
            }
            _instance.StartReceiving(async incoming =>
            {
                var active = await ready.Task;
                await Dispatcher.InvokeAsync(async () =>
                {
                    try
                    {
                        await active.RestoreCompletion;
                        active.ShowAndActivate();
                        var current = (PlayerViewModel)active.DataContext;
                        await current.ImportCompletion;
                        var before = current.Entries.Select(row => row.Id).ToHashSet();
                        await current.AddPathsAsync(incoming.Paths);
                        if (incoming.Play)
                        {
                            var added = current.Entries.FirstOrDefault(row => !before.Contains(row.Id));
                            if (added is not null) await current.PlayEntryCommand.ExecuteAsync(added);
                            else if (incoming.Paths.Length == 0) await current.HandleMediaAsync("Play");
                        }
                    }
                    catch (Exception error) { var current = (PlayerViewModel)active.DataContext; current.Message = Strings.ErrorUnexpected; current.Details = error.Message; }
                }).Task.Unwrap();
            });
        }
        catch (Exception error) { MessageBox.Show(Strings.Get("ForwardFailed") + "\n\n" + error.Message, Player.Core.ProductInfo.Name); Shutdown(3); return; }
        using var validation = uiSmoke ? new UiSmokeValidation() : null;
        if (smoke) ShutdownMode = ShutdownMode.OnExplicitShutdown;
        string directory;
        try { directory = smoke ? StorageLocation.Prepare(Path.Combine(Environment.CurrentDirectory, "artifacts", "smoke", crashSmoke ? "stage-g-crash-data" : "stage-c-data")) : StorageLocation.Resolve(); }
        catch (Exception error) { MessageBox.Show(error.Message, Strings.Get("StorageUnavailable")); Shutdown(1); return; }
        var settingsRecovered = false;
        try
        {
            var startupSettings = new SettingsFile(directory);
            var language = startupSettings.LoadWithRecovery(error =>
            {
                if (smoke) return false;
                Strings.SetLanguage(startupSettings.LoadBackup().Language);
                settingsRecovered = MessageBox.Show(Strings.Get("RestoreSettingsPrompt") + "\n\n" + error.Message,
                    Strings.Get("SavedDataUnavailable"), MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
                return settingsRecovered;
            }).Language;
            Strings.SetLanguage(language);
            var culture = CultureInfo.GetCultureInfo(language == "ru" ? "ru-RU" : "en-US");
            CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = culture;
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.DefaultThreadCurrentUICulture = culture;
        }
        catch (Exception error) { ReportStartupFailure(smoke, error); Shutdown(1); return; }
        DataDirectory = directory;
        _log = new RotatingLog(Path.Combine(directory, "Logs"), [Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), directory, AppContext.BaseDirectory]);
        _log.Record("startup", Player.Core.ProductInfo.Version + " · " + System.Runtime.InteropServices.RuntimeInformation.OSDescription);
        PlayerViewModel model;
        try
        {
            var migrationOutput = Path.Combine(Environment.CurrentDirectory, "artifacts", "smoke");
            if (crashSmoke && e.Args[1] == "migration-checkpoint") await MigrationSmokeValidation.SeedAsync(directory, Path.GetFullPath(e.Args[2]));
            if (crashSmoke && e.Args[1] == "migration-verify") MigrationSmokeValidation.AssertRolledBack(directory);
            model = CreateModel(directory, crashSmoke && e.Args[1] == "migration-checkpoint" ? () => MigrationSmokeValidation.PauseBeforeCommit(directory, migrationOutput) : null);
        }
        catch (Exception error) { ReportStartupFailure(smoke, error); Shutdown(1); return; }
        var window = new MainWindow
        {
            DataContext = model
        };
        MainWindow = window;
        WatchDiagnostics(model);
        try { await model.InitializeAsync(); }
        catch (Exception error)
        {
            await model.DisposeAsync();
            if (!smoke && error is not (NewerDatabaseSchemaException or PlayerStoreInUseException) &&
                MessageBox.Show(Strings.Get("RestorePrompt") + "\n\n" + error.Message, Strings.Get("SavedDataUnavailable"), MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                var dialog = new Microsoft.Win32.OpenFileDialog { Filter = Strings.Get("BackupFilter") };
                if (dialog.ShowDialog() == true)
                {
                    try
                    {
                        var backup = LocalFileAccess.ValidateFile(dialog.FileName);
                        if (Path.GetExtension(backup).Equals(".zip", StringComparison.OrdinalIgnoreCase)) await BackupBundle.RestoreAsync(directory, backup);
                        else await Task.Run(() => DatabaseRecovery.Restore(Path.Combine(directory, "library.db"), backup));
                        model = CreateModel(directory); WatchDiagnostics(model); window.DataContext = model; await model.InitializeAsync();
                    }
                    catch (Exception restoreError) { ReportStartupFailure(false, restoreError); Shutdown(1); return; }
                }
                else { Shutdown(1); return; }
            }
            else { ReportStartupFailure(smoke, error); Shutdown(1); return; }
        }
        if (settingsRecovered) model.Message = Strings.Get("SettingsRestored");
        window.Width = Math.Min(model.WindowSettings.WindowWidth, SystemParameters.WorkArea.Width);
        window.Height = Math.Min(model.WindowSettings.WindowHeight, SystemParameters.WorkArea.Height);
        // Clamp saved bounds to the available work area; disconnected monitors cannot strand the window.
        if (!smoke && model.WindowSettings.WindowLeft is { } left && model.WindowSettings.WindowTop is { } top)
        {
            window.Left = Math.Clamp(left, SystemParameters.WorkArea.Left, SystemParameters.WorkArea.Right - window.Width);
            window.Top = Math.Clamp(top, SystemParameters.WorkArea.Top, SystemParameters.WorkArea.Bottom - window.Height);
        }
        window.Show();
        if (!smoke && model.WindowSettings.WindowMaximized) window.WindowState = WindowState.Maximized;
        if (!smoke)
        {
            ShutdownMode = ShutdownMode.OnLastWindowClose;
            _tray = new TrayService(window, model);
        }
        try { _media = new MediaSessionService(window, model); }
        catch (Exception error) { model.Message = Strings.Get("IntegrationUnavailable"); model.Details = error.Message; }
        if (smoke) ready.TrySetResult(window);
        if (smoke)
        {
            var output = Path.Combine(Environment.CurrentDirectory, "artifacts", "smoke");
            Directory.CreateDirectory(output);
            var resultCode = 0;
            object result;
            try
            {
                result = crashSmoke
                    ? e.Args[1] == "failures" ? await ResilienceSmokeValidation.RunAsync(Path.GetFullPath(e.Args[2]), output)
                        : e.Args[1] == "migration-verify" ? await MigrationSmokeValidation.VerifyAsync(model, directory) : await CrashSmokeValidation.RunAsync(model, e.Args[1], Path.GetFullPath(e.Args[2]), directory, output)
                    : await validation!.RunAsync(window, model, Path.GetFullPath(e.Args[1]), Path.GetFullPath(e.Args[2]), output);
                if (result is ResilienceSmokeValidation.Report { Status: not "g9-resilience-passed" }) resultCode = 1;
                await window.CloseForValidationAsync();
            }
            catch (Exception error)
            {
                resultCode = 1;
                result = new { Status = "ui-smoke-failed", error.Message, error.StackTrace };
                try { await model.DisposeAsync(); } catch (Exception cleanup) { result = new { Status = "ui-smoke-failed", error.Message, Cleanup = cleanup.Message }; }
            }
            File.WriteAllText(Path.Combine(output, crashSmoke ? "crash.json" : "ui.json"), JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
            Shutdown(resultCode);
        }
        else if (request is not null)
        {
            var existing = model.Entries.Select(row => row.Id).ToHashSet();
            await model.AddPathsAsync(request.Paths);
            if (request.Play)
            {
                if (request.Paths.Length > 0 && model.Entries.FirstOrDefault(row => !existing.Contains(row.Id)) is { } first) await model.PlayEntryCommand.ExecuteAsync(first);
                else if (request.Paths.Length == 0) await model.HandleMediaAsync("Play");
            }
        }
        if (!smoke) ready.TrySetResult(window);
    }
    public void RebindMedia(MainWindow window, PlayerViewModel model)
    {
        WatchDiagnostics(model); _media?.Dispose(); _media = null;
        try { _media = new MediaSessionService(window, model); } catch (Exception error) { model.Details = error.Message; }
        if (_tray is not null || _restoreTray) { _tray?.Dispose(); _tray = new TrayService(window, model); _restoreTray = false; }
    }
    private bool _restoreTray;
    public void SuspendMedia()
    {
        _media?.Dispose(); _media = null;
        _restoreTray = _tray is not null; _tray?.Dispose(); _tray = null;
    }
    public bool MediaMetadataMatches(PlayerViewModel model) => _media?.MetadataMatches(model) == true;
    protected override void OnExit(ExitEventArgs e)
    {
        _media?.Dispose(); _tray?.Dispose(); _instance?.Dispose(); base.OnExit(e);
    }
    private static void ReportStartupFailure(bool smoke, Exception error)
    {
        if (smoke)
        {
            var output = Path.Combine(Environment.CurrentDirectory, "artifacts", "smoke"); Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, "ui.json"), JsonSerializer.Serialize(new { Status = "ui-smoke-failed", error.Message, error.StackTrace }));
        }
        else MessageBox.Show(Strings.Get("StartupFailure") + "\n\n" + error.Message, Strings.Get("SavedDataUnavailable"), MessageBoxButton.OK, MessageBoxImage.Error);
    }
    public PlayerViewModel CreateModel(string directory, Action? migrationBeforeCommit = null)
    {
        var settings = new SettingsFile(directory);
        var options = settings.Load();
        var player = new SerializedAudioPlayer(() => new BassAudioBackend());
        return new PlayerViewModel(player, new PlaybackCoordinator(player), new MediaImportService(), new FileDialogService(), Dispatcher,
            new SqlitePlayerStore(Path.Combine(directory, "library.db"), Strings.DefaultPlaylist) { MigrationBeforeCommit = migrationBeforeCommit }, settings,
            new BassWaveformService(new WaveformCache(Path.Combine(directory, "Cache", "Waveforms"), options.WaveformCacheMiB * 1024L * 1024)));
    }
}
