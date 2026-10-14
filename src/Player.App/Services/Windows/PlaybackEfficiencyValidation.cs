using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Threading;
using Bass = ManagedBass.Bass;
using Player.App.Services.Audio;
using Player.App.Services.Storage;
using Player.App.Views;
using Player.Core.Library;
using Player.Core.Playback;

namespace Player.App.Services.Windows;

internal static class PlaybackEfficiencyValidation
{
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    internal static async Task<object> RunAsync(MainWindow owner, string fixture, string output)
    {
        float[] pcm; int channels, rate;
        using (var context = new NativeDecodeContext())
        {
            var source = BassMixerGraph.OpenSource(new(Guid.NewGuid(), fixture));
            try
            {
                channels = source.Info.Format.Channels; rate = source.Info.Format.SampleRate;
                var buffer = new float[channels * 16384]; var bytes = Bass.ChannelGetData(source.Handle, buffer, buffer.Length * sizeof(float));
                Check(bytes > 0 && bytes % (channels * sizeof(float)) == 0, "PCM efficiency probe did not decode actual native source frames.");
                pcm = buffer[..(bytes / sizeof(float))];
            }
            finally { NativeStreamPins.Free(source.Handle); }
        }
        static long Legacy(Span<float> samples, int channels, float start, float end)
        {
            var frames = samples.Length / channels; long protectedSamples = 0;
            for (var frame = 0; frame < frames; frame++)
            {
                var gain = start + (end - start) * (frame + 1f) / Math.Max(1, frames);
                for (var channel = 0; channel < channels; channel++)
                {
                    var index = frame * channels + channel; double value = samples[index]; value *= gain;
                    if (!double.IsFinite(value)) { value = 0; protectedSamples++; }
                    else if (value is > 1 or < -1) { value = Math.Clamp(value, -1, 1); protectedSamples++; }
                    samples[index] = (float)value;
                }
            }
            return protectedSamples;
        }
        var reference = pcm.ToArray(); var actual = pcm.ToArray(); var processor = new PcmProcessor(rate, channels, new());
        var protectedReference = Legacy(reference, channels, .37f, .37f); processor.Process(actual, .37f, .37f);
        Check(reference.Select(BitConverter.SingleToInt32Bits).SequenceEqual(actual.Select(BitConverter.SingleToInt32Bits)) && protectedReference == processor.ProtectedSamples,
            "Fast PCM output differs from the original constant-gain processing.");
        object Measure(Action action)
        {
            for (var i = 0; i < 10; i++) action();
            using var process = Process.GetCurrentProcess(); var cpu = process.TotalProcessorTime;
            var before = GC.GetAllocatedBytesForCurrentThread(); var timer = Stopwatch.StartNew();
            for (var i = 0; i < 200; i++) action();
            timer.Stop();
            return new { Blocks = 200, WallMilliseconds = timer.Elapsed.TotalMilliseconds, AllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - before,
                ProcessCpuMilliseconds = (process.TotalProcessorTime - cpu).TotalMilliseconds, IncludesBufferCopy = true };
        }
        var originalPcm = Measure(() => { pcm.CopyTo(reference, 0); Legacy(reference, channels, .37f, .37f); });
        var fastPcm = Measure(() => { pcm.CopyTo(actual, 0); processor.Process(actual, .37f, .37f); });

        var resolutions = 0; var selection = new DeviceSelectionCache(() => { resolutions++; return 3; });
        for (var i = 0; i < 10000; i++) Check(selection.Resolve(1) == 3, "Cached endpoint identity changed.");
        Check(resolutions == 1, "Unchanged device enumeration was not cached."); selection.Resolve(2); selection.Invalidate(); selection.Resolve(2);
        Check(resolutions == 3, "Endpoint notification/retry did not invalidate the cache.");
        var initialLeases = WasapiDeviceNotifications.ActiveLeases; bool notificationsAvailable;
        using (var first = new WasapiDeviceNotifications())
        {
            using var second = new WasapiDeviceNotifications(); notificationsAvailable = second.Available;
            Check(first.Available == second.Available && WasapiDeviceNotifications.ActiveLeases == initialLeases + 2, "Native notification subscriptions replaced one another.");
            first.Dispose(); Check(WasapiDeviceNotifications.ActiveLeases == initialLeases + 1, "Auxiliary subscription removed the remaining owner.");
        }
        Check(WasapiDeviceNotifications.ActiveLeases == initialLeases, "Native notification subscription leaked.");

        var directory = Directory.CreateDirectory(Path.Combine(output, "playback-efficiency-" + Guid.NewGuid().ToString("N"))).FullName;
        var settingsDirectory = Directory.CreateDirectory(Path.Combine(directory, "settings")).FullName;
        var settingsFile = new SettingsFile(settingsDirectory); settingsFile.Save(new(Volume: 20)); settingsFile.Save(new(Volume: 40));
        var path = Path.Combine(settingsDirectory, "settings.json"); var current = File.ReadAllBytes(path); var backup = File.ReadAllBytes(path + ".bak");
        var timestamp = File.GetLastWriteTimeUtc(path); var names = Directory.GetFiles(settingsDirectory).Order().ToArray();
        DataFileLease.FileIdentity? identity;
        using (var lease = DataFileLease.OpenExisting(path)) identity = lease!.Identity;
        var saveTimer = Stopwatch.StartNew();
        for (var i = 0; i < 100; i++) settingsFile.Save(new(Volume: 40));
        saveTimer.Stop();
        using (var lease = DataFileLease.OpenExisting(path)) Check(identity == lease!.Identity, "Unchanged save replaced the current file identity.");
        Check(settingsFile.Writes == 2 && settingsFile.UnchangedSaves == 100 && File.GetLastWriteTimeUtc(path) == timestamp &&
            names.SequenceEqual(Directory.GetFiles(settingsDirectory).Order()) && current.SequenceEqual(File.ReadAllBytes(path)) && backup.SequenceEqual(File.ReadAllBytes(path + ".bak")),
            "Identical settings changed files or backup history.");

        var data = Directory.CreateDirectory(Path.Combine(directory, "model")).FullName; var music = Path.Combine(data, "owned.flac"); File.Copy(fixture, music);
        var sourceHash = SHA256.HashData(File.ReadAllBytes(music));
        var model = ((App)Application.Current).CreateModel(data); var window = new MainWindow { Owner = owner, DataContext = model }; bool closed = false;
        long received, applied, listening; long playCount;
        try
        {
            await model.InitializeAsync(); window.Show(); await model.AddPathsAsync([music]); await model.PrepareAsync(model.Entries[0].Id);
            await model.WaveformCompletion; await model.ArtworkCompletion;
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
            var basis = model.Snapshot; var revision = basis.Revision; var observed = model.ListeningObservations;
            var errorShown = false;
            void ErrorChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
            { if (args.PropertyName == nameof(model.Details) && model.Details == "Owned delivery error") errorShown = true; }
            model.PropertyChanged += ErrorChanged;
            var receivedBefore = model.ReceivedSnapshots; var appliedBefore = model.AppliedSnapshots;
            for (var i = 0; i < 500; i++) model.ReceiveSnapshot(basis with { Revision = ++revision, State = PlaybackState.Playing, Position = TimeSpan.FromMilliseconds(i * 100) });
            model.ReceiveSnapshot(basis with { Revision = ++revision, State = PlaybackState.Error, Error = new(AudioErrorCategory.OutputUnavailable, "ErrorOutputUnavailable", "Owned delivery error") });
            model.ReceiveSnapshot(basis with { Revision = ++revision, State = PlaybackState.Stopped, Ended = true });
            received = model.ReceivedSnapshots - receivedBefore; listening = model.ListeningObservations - observed;
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
            model.PropertyChanged -= ErrorChanged;
            applied = model.AppliedSnapshots - appliedBefore;
            Check(received == 502 && listening == 502 && applied == 3 && model.State == Resources.Strings.Get("StateStopped"),
                "UI delivery lost critical state/statistics or rendered every backlogged position.");
            Check(errorShown, "Critical output error was hidden by position coalescing.");
            await model.DisposeAsync(); window.ExitApplication(); await window.ShutdownCompletion; closed = true;
            await using var reopened = new SqlitePlayerStore(Path.Combine(data, "library.db")); await reopened.LoadAsync();
            playCount = (await reopened.GetStatisticsAsync([model.Entries[0].Entry.Track.Id]))[0].PlayCount;
            Check(playCount == 1, "Raw receipt statistics missed or duplicated the controlled ended occurrence.");
            Check(sourceHash.AsSpan().SequenceEqual(SHA256.HashData(File.ReadAllBytes(music))), "Delivery/processing changed source audio.");
        }
        finally
        {
            if (!closed) { await model.DisposeWithoutSavingAsync(); window.ExitApplication(); await window.ShutdownCompletion; }
        }
        return new { Status = "playback-efficiency-passed", Pcm = new { NativeDecodedSamples = pcm.Length, Original = originalPcm, ConstantGain = fastPcm, BitExact = true, ProtectedSamplesPreserved = true },
            Settings = new { Saves = 100, Writes = 0, saveTimer.Elapsed.TotalMilliseconds, BytesIdentityTimestampAndBackupPreserved = true },
            Devices = new { UnchangedLookups = 10000, InitialResolutions = 1, NotificationAndRetryInvalidation = true, NativeNotificationRegistrationAvailable = notificationsAvailable,
                SharedNativeSubscriptionLifecycle = true, RegistrationFailureUsesLegacyChecks = true, CachedEndpointIdentityCheckedEachPoll = true, PhysicalDefaultChangeAndUnplug = "not-run" },
            Delivery = new { received, applied, listening, playCount, CriticalErrorAndEndPreserved = true, SourceHashPreserved = true },
            Scope = "Native-decoded PCM, actual settings leases, real native notification registration and isolated WPF/SQLite with controlled stamped snapshots. Device changes and overall physical playback CPU remain manual acceptance." };
    }
}
