using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Player.App.Services.Audio;
using Player.App.Services.Library;
using Player.App.Services.Storage;
using Player.App.Services.Waveforms;
using Player.App.ViewModels;
using Player.Core.Library;
using Player.Core.Playback;
using Player.Core.Waveforms;
using Windows.Storage.Streams;

namespace Player.App.Services.Windows;

/// <summary>Inject disposal errors after real native/database release in fresh owned UI-smoke data.</summary>
internal static class ResourceShutdownValidation
{
    public static async Task<object> RunAsync(string fixture, string output)
    {
        static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        var directory = Path.Combine(output, "resource-shutdown-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var cases = new List<object>();
            foreach (var storeFails in new[] { false, true })
            {
                var data = Path.Combine(directory, storeFails ? "two-errors" : "one-error"); Directory.CreateDirectory(data);
                var music = Path.Combine(data, "owned.wav"); File.Copy(fixture, music);
                var database = Path.Combine(data, "library.db");
                var store = new ClosingStore(new SqlitePlayerStore(database), storeFails);
                var waveforms = new ClosingWaveforms(new BassWaveformService(new WaveformCache(Path.Combine(data, "waveforms"))));
                var player = new SerializedAudioPlayer(() => new BassAudioBackend());
                var coordinator = new PlaybackCoordinator(player);
                var model = new PlayerViewModel(player, coordinator, new MediaImportService(), new FileDialogService(),
                    Application.Current.Dispatcher, store, new SettingsFile(data), waveforms);
                try
                {
                    await model.InitializeAsync(); await model.AddPathsAsync([music]);
                    await model.PrepareAsync(model.Entries.Single().Id);
                    await Application.Current.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
                    await model.WaveformCompletion;
                    Check(model.CanSeek && model.Waveform is not null, "Shutdown control did not prepare real native audio/waveform.");
                    var id = model.Entries.Single().Id;
                    Exception? failure = null;
                    try { await model.DisposeAsync(); } catch (Exception error) { failure = error; }
                    var errors = failure is AggregateException aggregate ? aggregate.Flatten().InnerExceptions.ToArray() : failure is null ? [] : new[] { failure };
                    Check(errors.Length == (storeFails ? 2 : 1) && errors.All(error => error is IOException), "Disposal failures were lost or not returned.");
                    Check(waveforms.Closes == 1 && store.Closes == 1, "Earlier release failure skipped database shutdown.");
                    using (var exclusive = File.Open(music, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
                    using (var exclusive = File.Open(database, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
                    await using (var reopened = new SqlitePlayerStore(database))
                        Check((await reopened.LoadAsync()).Playlists.Single().Entries.Single().Id == id, "Shutdown error lost the committed playlist.");
                    Directory.Move(data, data + ".moved"); Directory.Move(data + ".moved", data);
                    cases.Add(new { Errors = errors.Length, ActualNativePreparation = true, ActualWaveform = true,
                        MusicAndDatabaseExclusiveOpen = true, DirectoryPinsReleased = true, SavedEntryReopened = true });
                }
                finally
                {
                    try { await model.DisposeWithoutSavingAsync(); } catch (IOException) { } catch (AggregateException) { }
                    // A failed regression must still release its own native workers/files.
                    await waveforms.Inner.DisposeAsync(); await player.DisposeAsync(); await store.Inner.DisposeAsync();
                }
            }
            var image = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32, null,
                new byte[] { 0, 0, 255, 255, 0, 255, 0, 255, 255, 0, 0, 255, 255, 255, 255, 255 }, 8);
            image.Freeze();
            using var artwork = new InMemoryRandomAccessStream();
            await MediaSessionService.WriteArtworkAsync(artwork, image);
            Check(artwork.Size > 8 && artwork.CanRead, "Closing the output view closed the retained artwork stream.");
            using var input = artwork.GetInputStreamAt(0); using var reader = new DataReader(input);
            await reader.LoadAsync(8); var signature = new byte[8]; reader.ReadBytes(signature);
            Check(signature.AsSpan().SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }), "Retained WinRT artwork did not contain PNG bytes.");
            return new { Status = "resource-shutdown-passed", Cases = cases, WinRtPngOutputClosedAndParentReadable = true,
                Scope = "Real owned native preparation/waveform/database with post-release faults; no device playback or listening claim" };
        }
        finally { Directory.Delete(directory, true); }
    }
    private sealed class ClosingWaveforms(BassWaveformService inner) : IWaveformService
    {
        public BassWaveformService Inner { get; } = inner;
        public int Closes { get; private set; }
        public Task<WaveformData> AnalyzeAsync(string path, IProgress<double>? progress, CancellationToken cancellationToken, bool refresh = false) => Inner.AnalyzeAsync(path, progress, cancellationToken, refresh);
        public async ValueTask DisposeAsync() { Closes++; await Inner.DisposeAsync(); throw new IOException("Owned waveform post-release fault."); }
    }
    private sealed class ClosingStore(SqlitePlayerStore inner, bool fail) : IPlayerStore
    {
        public SqlitePlayerStore Inner { get; } = inner;
        public int Closes { get; private set; }
        public Task<LibraryState> LoadAsync() => Inner.LoadAsync();
        public Task SaveAsync(LibraryState state, bool playlistsChanged) => Inner.SaveAsync(state, playlistsChanged);
        public Task BackupAsync(string destination) => Inner.BackupAsync(destination);
        public async ValueTask DisposeAsync() { Closes++; await Inner.DisposeAsync(); if (fail) throw new IOException("Owned store post-release fault."); }
    }
}
