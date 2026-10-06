using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using ManagedBass;
using ManagedBass.Wasapi;
using Player.Core.Diagnostics;
using Player.Core.Media;
using Player.Core.Playback;

namespace Player.App.Services.Audio;

/// <summary>Explicit G11 route: actual endpoints and bounded loopback capture; probe cannot masquerade as playback.</summary>
internal static class AudioAcceptanceValidation
{
    internal sealed record Device(int Index, string Id, string Name, string Type, bool Enabled, bool Default, bool Input, bool Loopback, int MixRate, int MixChannels);
    private static void Check(bool value, string detail) { if (!value) throw new InvalidDataException(detail); }
    public static object Run(string root, string mode, string? deviceId)
    {
        var versions = NativeLibraryBootstrap.LoadAndVerify();
        var devices = new List<Device>();
        for (var i = 0; i < 1024 && BassWasapi.GetDeviceInfo(i, out var info); i++)
            devices.Add(new(i, info.ID, info.Name, info.Type.ToString(), info.IsEnabled, info.IsDefault, info.IsInput, info.IsLoopback, info.MixFrequency, info.MixChannels));
        var selected = devices.FirstOrDefault(d => d.Enabled && !d.Input && !d.Loopback && (deviceId is null ? d.Default : d.Id == deviceId));
        var began = DateTimeOffset.UtcNow;
        if (mode == "probe") return new { Status = "g11-device-probe-complete", Mode = mode, Versions = versions, Devices = devices,
            Selected = selected, EnabledOutputs = devices.Count(d => d.Enabled && !d.Input && !d.Loopback),
            Output = "not-run", DigitalCapture = "not-run", Listening = "not-run", Windows = RuntimeInformation.OSDescription };
        if (selected is null) return new { Status = "blocked", Mode = mode, Reason = "The requested/default enabled output endpoint is unavailable.",
            Versions = versions, Devices = devices, Output = "not-run", DigitalCapture = "not-run", Listening = "not-run" };
        var phases = new List<object>(); var sources = new Dictionary<string, string>();
        object? digital = null; string status = "g11-output-passed"; string? failure = null; int? nativeCode = null;
        var released = false;
        try
        {
            if (mode == "digital" && (selected.MixRate is < 8000 or > 192000 || selected.MixChannels != 2))
                return new { Status = "blocked", Mode = mode, Selected = selected, Reason = "The digital reference profile requires a supported stereo shared mix format; multichannel capture acceptance is separate.", Output = "not-run" };
            using (var backend = new BassAudioBackend())
            {
                backend.SetOutput(new(deviceId, mode == "exclusive")); backend.SetProcessing(new());
                var rate = mode == "digital" ? selected.MixRate : 48000;
                var primary = Path.Combine(root, "owned output primary.wav"); var secondary = Path.Combine(root, "owned output secondary.wav");
                AcceptanceSignal.WriteWave(primary, rate, 2, 12, 997); AcceptanceSignal.WriteWave(secondary, rate, 2, 12, 1301);
                sources.Add(primary, Hash(primary)); sources.Add(secondary, Hash(secondary));
                backend.Open(new AudioRequest(Guid.NewGuid(), primary)); backend.SetVolume(.15, false); backend.Play();
                Check(BassWasapi.GetInfo(out var negotiated), "Negotiated endpoint information unavailable.");
                Check(negotiated.IsExclusive == (mode == "exclusive"), "Output mode was silently changed.");
                var curve = WasapiVolumeTypes.Device | WasapiVolumeTypes.LinearCurve;
                var master = BassWasapi.GetVolume(curve); var masterMuted = BassWasapi.GetMute(curve);
                Check(float.IsFinite(master) && master is >= 0 and <= 1, "Device master-volume observation unavailable.");
                Check(Advance(backend, 400).Position > TimeSpan.Zero, "Actual output did not consume the prepared source.");
                backend.Pause(); var paused = backend.ReadPosition().Position;
                Thread.Sleep(150); Check(backend.ReadPosition().Position == paused, "Pause advanced the source timeline.");
                backend.Seek(TimeSpan.FromSeconds(.5)); backend.Play();
                Check(Advance(backend, 300).Position > TimeSpan.FromSeconds(.5), "Seek/resume output did not advance.");
                backend.SetVolume(.08, true); Advance(backend, 150); backend.SetVolume(.15, false); Advance(backend, 150);
                for (var i = 0; i < 20; i++)
                {
                    // Match the production owner's close-before-Open, including the formerly repeated reset.
                    backend.CloseSource(); var request = new AudioRequest(Guid.NewGuid(), i % 2 == 0 ? secondary : primary);
                    backend.Open(request); backend.Play(); var position = Advance(backend, 150);
                    Check(position.Position > TimeSpan.Zero, "Running track replacement did not reach the endpoint.");
                    phases.Add(new { Phase = "running-replacement", Iteration = i + 1, EntryId = request.EntryId, PositionSeconds = position.Position.TotalSeconds, position.OutputFormat });
                }
                backend.Pause(); backend.Open(new AudioRequest(Guid.NewGuid(), secondary)); backend.Seek(TimeSpan.FromSeconds(1)); backend.Play();
                Check(Advance(backend, 300).Position > TimeSpan.FromSeconds(1), "Paused source replacement failed.");
                backend.Seek(TimeSpan.FromSeconds(2)); Check(Advance(backend, 300).Position > TimeSpan.FromSeconds(2), "Running seek failed.");
                backend.Stop(); backend.Stop(); Check(backend.ReadPosition().Position == TimeSpan.Zero, "Repeated Stop did not retain zero position.");
                backend.Play(); Check(Advance(backend, 300).Position > TimeSpan.Zero, "Stopped source did not resume.");
                var otherRate = Path.Combine(root, "owned rate change.wav"); AcceptanceSignal.WriteWave(otherRate, 44100, 2, 3, 1301); sources.Add(otherRate, Hash(otherRate));
                var rateSupported = mode != "exclusive" || BassWasapi.CheckFormat(selected.Index, 44100, 2, WasapiInitFlags.Exclusive) >= 0;
                backend.Open(new AudioRequest(Guid.NewGuid(), otherRate));
                if (rateSupported)
                {
                    backend.Play(); var changed = Advance(backend, 300);
                    Check(changed.Position > TimeSpan.Zero, "Rate-changing output did not advance.");
                    Check(BassWasapi.GetInfo(out var afterRate), "Rate-changing endpoint information unavailable.");
                    Check(afterRate.IsExclusive == (mode == "exclusive") && (mode != "exclusive" || afterRate.Frequency == 44100), "Exclusive source-rate change reused the wrong negotiated format.");
                    phases.Add(new { Phase = "rate-change", SourceRate = 44100, NegotiatedRate = afterRate.Frequency, afterRate.IsExclusive });
                }
                else
                {
                    try { backend.Play(); throw new InvalidDataException("Unsupported exclusive format unexpectedly started."); }
                    catch (AudioBackendException error) when (error.Category == AudioErrorCategory.OutputUnavailable)
                    { phases.Add(new { Phase = "unsupported-exclusive-format-refused", error.NativeCode, Detail = error.Message, SharedFallback = false }); }
                }
                backend.Open(new AudioRequest(Guid.NewGuid(), primary)); backend.Play(); Advance(backend, 200);
                backend.SetOutput(new("owned-unavailable-endpoint-" + Guid.NewGuid().ToString("N"), mode == "exclusive"));
                try { backend.Play(); throw new InvalidDataException("Explicit unavailable endpoint silently fell back."); }
                catch (AudioBackendException error) when (error.Category == AudioErrorCategory.OutputUnavailable)
                { phases.Add(new { Phase = "explicit-unavailable-endpoint-refused", Detail = error.Message, AutomaticFallback = false, PhysicalHotplug = "not-run" }); }
                backend.SetOutput(new(deviceId, mode == "exclusive")); backend.Play(); Check(Advance(backend, 300).Position > TimeSpan.Zero, "Explicit output retry failed.");
                var afterMaster = BassWasapi.GetVolume(curve); var afterMuted = BassWasapi.GetMute(curve);
                Check(Math.Abs(afterMaster - master) <= .000001 && afterMuted == masterMuted, "Observed Windows master gain/mute changed during app-gain/transport tests; volume-isolation evidence is inconclusive.");
                phases.Add(new { Phase = "transport-and-gain", Shared = !negotiated.IsExclusive, Exclusive = negotiated.IsExclusive, negotiated.Frequency, negotiated.Channels,
                    MasterVolumeBefore = master, MasterVolumeAfter = afterMaster, SystemMuteBefore = masterMuted, SystemMuteAfter = afterMuted,
                    AppGainAndMuteDidNotChangeSystemVolume = true, PausedAndStoppedReplacement = true, RunningSeek = true, RepeatedStop = true, ExplicitRetry = true });
                backend.Stop();
                if (mode == "digital")
                {
                    var loop = devices.FirstOrDefault(d => d.Enabled && d.Loopback && d.Id == selected.Id);
                    if (loop is null) { status = "blocked"; failure = "No enabled loopback with the selected output endpoint ID; another endpoint was not substituted."; }
                    else
                    {
                        var reference = Path.Combine(root, "owned boundary reference.wav"); AcceptanceSignal.WriteWave(reference, rate, 2, 4, reference: true); sources.Add(reference, Hash(reference));
                        using var capture = new LoopbackCapture(loop.Index, rate, 2);
                        var first = new AudioRequest(Guid.NewGuid(), reference, new TrackSegment(TimeSpan.Zero, TimeSpan.FromSeconds(2)));
                        var second = new AudioRequest(Guid.NewGuid(), reference, new TrackSegment(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4)));
                        backend.Open(first); backend.PrepareNext(second, false); backend.SetVolume(.15, false); backend.Play();
                        var timer = Stopwatch.StartNew(); var transition = false; var ended = false;
                        while (timer.Elapsed < TimeSpan.FromSeconds(7))
                        {
                            var position = backend.ReadPosition(); transition |= position.Transition?.EntryId == second.EntryId;
                            if (position.Ended) { ended = true; break; }
                            Thread.Sleep(10);
                        }
                        Check(transition && ended, "Real output did not adopt/end the scheduled CUE boundary.");
                        Thread.Sleep(200); capture.Stop(); backend.Stop();
                        var samples = capture.Samples(); var file = Path.Combine(root, "loopback.f32");
                        using (var stream = new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.None)) stream.Write(MemoryMarshal.AsBytes(samples.AsSpan()));
                        var analysis = DigitalBoundaryAnalyzer.Analyze(samples, 2, rate, rate * 4, rate * 2);
                        digital = new { Status = analysis.Status, Loopback = loop, SampleFormat = "float32 little endian, interleaved stereo", Rate = rate,
                            CaptureFile = Path.GetFileName(file), CaptureSha256 = Hash(file), CaptureBytes = new FileInfo(file).Length, CapacityBytes = capture.CapacityBytes,
                            CallbackError = capture.CallbackError, ScheduledCueTransition = true, Analysis = analysis,
                            Boundary = "Contiguous segments of one owned PCM/WAV file; other codecs/track-gapless profiles remain separate", OtherSystemAudio = "Not suppressed; interference invalidates reference comparison" };
                        Check(analysis.Status == "digital-boundary-passed", "Digital boundary comparison failed; inspect the saved actual capture.");
                    }
                }
            }
            foreach (var source in sources)
            {
                Check(Hash(source.Key) == source.Value, "Owned source changed during endpoint validation.");
                using (File.Open(source.Key, FileMode.Open, FileAccess.Read, FileShare.None)) { }
            }
            released = true;
        }
        catch (Exception error)
        {
            status = "failed"; failure = error.Message;
            if (error is AudioBackendException native) nativeCode = native.NativeCode;
        }
        return new { Status = status, Mode = mode, Selected = selected, Versions = versions, StartedUtc = began, EndedUtc = DateTimeOffset.UtcNow,
            Phases = phases, Digital = digital, Failure = failure, NativeCode = nativeCode, Sources = sources.Select(s => new { File = Path.GetFileName(s.Key), Sha256 = s.Value }),
            SourcesHashAndExclusiveReopenVerified = released, Windows = RuntimeInformation.OSDescription,
            Listening = "not-manually-verified", PhysicalUnplugDefaultSwitchSleep = "not-run", TwoHourOutputSoak = "not-run",
            Method = "Production BassAudioBackend on one native owner thread; real WASAPI devices. Loopback owns a separate native thread. No simulated output." };
    }
    private static string Hash(string path) { using var file = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(file)).ToLowerInvariant(); }
    private static BackendPosition Advance(BassAudioBackend backend, int milliseconds)
    {
        var timer = Stopwatch.StartNew(); BackendPosition position;
        do { position = backend.ReadPosition(); Thread.Sleep(10); } while (timer.ElapsedMilliseconds < milliseconds);
        return position;
    }
    private sealed class LoopbackCapture : IDisposable
    {
        private readonly WasapiProcedure _callback;
        private readonly float[] _buffer;
        private readonly int _channels;
        private readonly ManualResetEventSlim _finish = new(false);
        private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _written, _error;
        public int CallbackError => Volatile.Read(ref _error);
        public int CapacityBytes => _buffer.Length * sizeof(float);
        public LoopbackCapture(int device, int rate, int channels)
        {
            _channels = channels; _buffer = new float[checked(rate * channels * 12)]; _callback = Record;
            new Thread(() =>
            {
                var initialized = false;
                try
                {
                    Check(BassWasapi.Init(device, rate, channels, WasapiInitFlags.Shared, .1f, 0, _callback), "Open selected endpoint loopback failed: " + Bass.LastError);
                    initialized = true; Check(BassWasapi.Start(), "Start loopback failed: " + Bass.LastError); _ready.TrySetResult(); _finish.Wait();
                }
                catch (Exception error) { _ready.TrySetException(error); _closed.TrySetException(error); }
                finally
                {
                    if (initialized && !BassWasapi.Free()) _closed.TrySetException(new InvalidOperationException("Loopback quiescence/free failed: " + Bass.LastError));
                    _closed.TrySetResult(); GC.KeepAlive(_callback);
                }
            }) { IsBackground = true, Name = "Player owned loopback capture" }.Start();
            try { _ready.Task.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult(); }
            catch { _finish.Set(); throw; }
        }
        private unsafe int Record(nint buffer, int length, nint user)
        {
            try
            {
                var count = length / sizeof(float); var written = Volatile.Read(ref _written);
                if (length < 0 || length % (sizeof(float) * _channels) != 0 || count > _buffer.Length - written)
                { Interlocked.Exchange(ref _error, 1); return 0; }
                new ReadOnlySpan<float>(buffer.ToPointer(), count).CopyTo(_buffer.AsSpan(written, count));
                Volatile.Write(ref _written, written + count); return 1;
            }
            catch { Interlocked.Exchange(ref _error, 2); return 0; }
        }
        public void Stop() { _finish.Set(); _closed.Task.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult(); Check(CallbackError == 0, "Loopback callback overflow or failure."); }
        public float[] Samples() { Check(_closed.Task.IsCompletedSuccessfully, "Capture must be quiescent before reading."); return _buffer.AsSpan(0, Volatile.Read(ref _written)).ToArray(); }
        public void Dispose() { Stop(); _finish.Dispose(); GC.KeepAlive(_callback); }
    }
}
