using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using ManagedBass.Wasapi;
using Player.Core.Diagnostics;
using Player.Core.Media;
using Player.Core.Playback;

namespace Player.App.Services.Audio;

/// <summary>Real production native paths with bounded telemetry. Callback pulling is explicitly separate from endpoint output.</summary>
internal static class StressAcceptanceValidation
{
    private static void Check(bool value, string message) { if (!value) throw new InvalidDataException(message); }
    private static string Hash(string path) { using var file = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(file)).ToLowerInvariant(); }
    public static unsafe object Run(string root, string mode, int durationSeconds, string? deviceId)
    {
        if (durationSeconds is < 60 or > 7200) throw new ArgumentOutOfRangeException(nameof(durationSeconds));
        var versions = NativeLibraryBootstrap.LoadAndVerify();
        AudioAcceptanceValidation.Device? device = null;
        if (mode != "mixer")
        {
            for (var i = 0; i < 1024 && BassWasapi.GetDeviceInfo(i, out var info); i++)
                if (info.IsEnabled && !info.IsInput && !info.IsLoopback && (deviceId is null ? info.IsDefault : info.ID == deviceId))
                { device = new(i, info.ID, info.Name, info.Type.ToString(), true, info.IsDefault, false, false, info.MixFrequency, info.MixChannels); break; }
            if (device is null) return new { Status = "blocked", Mode = mode, Reason = "Requested/default real output is unavailable; no simulated endpoint or fallback was used.", Output = "not-run", TwoHourOutput = "not-run", Versions = versions };
        }
        var first = Path.Combine(root, "owned stress primary.wav"); var second = Path.Combine(root, "owned stress secondary.wav");
        AcceptanceSignal.WriteWave(first, seconds: 3); AcceptanceSignal.WriteWave(second, seconds: 3, frequency: 1301);
        var hashes = new[] { Hash(first), Hash(second) }; var started = DateTimeOffset.UtcNow;
        using var process = Process.GetCurrentProcess();
        using var progress = new StreamWriter(new FileStream(Path.Combine(root, "g12-progress.jsonl"), FileMode.CreateNew, FileAccess.Write, FileShare.Read));
        var stressSamples = new List<ResourceSample>(); var soakSamples = new List<ResourceSample>(); var timings = new List<double>(1000);
        ResourceResult? stress = null, soak = null; object? negotiated = null; var status = "failed"; string? failure = null;
        var stressChanges = 0; var soakTransitions = 0; long renderedFrames = 0; double error = 0, peak = 0, maximumPacingLateness = 0;
        var soakElapsed = 0d; var sourcesReleased = false;
        ResourceSample Sample(Stopwatch watch, List<ResourceSample> target, string phase, int changes)
        {
            process.Refresh(); var value = new ResourceSample(watch.Elapsed.TotalSeconds, process.TotalProcessorTime.TotalSeconds,
                process.HandleCount, process.PrivateMemorySize64, process.WorkingSet64, process.Threads.Count);
            target.Add(value); progress.WriteLine(JsonSerializer.Serialize(new { Phase = phase, Changes = changes, Sample = value })); progress.Flush(); return value;
        }
        AudioRequest Request(int index, bool segment) => new(Guid.NewGuid(), index % 2 == 0 ? first : second,
            segment ? new TrackSegment(TimeSpan.Zero, TimeSpan.FromSeconds(.1)) : null);
        try
        {
            using (var context = new NativeDecodeContext())
            using (var graph = mode == "mixer" ? new BassMixerGraph(48000, 2, new()) : null)
            using (var backend = mode == "mixer" ? null : new BassAudioBackend())
            {
                var buffer = new float[960]; // One 10 ms stereo callback-sized block; no captured-audio accumulation.
                void Render()
                {
                    fixed (float* pointer = buffer) Check(graph!.Render((nint)pointer, buffer.Length * sizeof(float)) == buffer.Length * sizeof(float), "Short native PCM render.");
                    var blockPeak = 0d;
                    foreach (var sample in buffer) { Check(float.IsFinite(sample), "Native PCM is nonfinite."); blockPeak = Math.Max(blockPeak, Math.Abs(sample)); }
                    Check(blockPeak > .001, "Native PCM positive control became silent."); peak = Math.Max(peak, blockPeak); renderedFrames += 480;
                }
                if (graph is not null) { graph.SetVolume(1, false, true); graph.Load(Request(0, true)); }
                if (backend is not null) { backend.SetOutput(new(device!.Id, mode == "exclusive")); backend.SetProcessing(new()); backend.SetVolume(.15, false); }
                var clock = Stopwatch.StartNew();
                for (var i = 0; i < 1050; i++)
                {
                    var cycle = Stopwatch.StartNew(); var next = Request(i + 1, graph is not null);
                    if (graph is not null)
                    {
                        Check(graph.PrepareNext(next, false), "Real native stress could not schedule the next source.");
                        BackendPosition position; var blocks = 0;
                        do { Render(); position = graph.ReadPosition(0); Check(++blocks <= 12, "Native boundary transition failed to arrive."); } while (position.Transition is null);
                        Check(position.Transition.EntryId == next.EntryId && graph.ActiveEntryId == next.EntryId, "Native stress adopted the wrong occurrence.");
                        Render(); Check(graph.ReadPosition(0).Position > TimeSpan.Zero, "Incoming native source did not produce frames.");
                        var frequency = (i + 1) % 2 == 0 ? 997 : 1301;
                        for (var frame = 0; frame < 480; frame++) for (var channel = 0; channel < 2; channel++)
                            error = Math.Max(error, Math.Abs(buffer[frame * 2 + channel] - AcceptanceSignal.Sample(frame, channel, 48000, frequency) / 32768d));
                        Check(error <= .000001, "Incoming source PCM mismatched its owned reference.");
                    }
                    else
                    {
                        backend!.CloseSource(); backend.Open(next); backend.Play();
                        BackendPosition position;
                        do { position = backend.ReadPosition(); if (position.Position > TimeSpan.Zero) break; Thread.Sleep(10); }
                        while (cycle.Elapsed < TimeSpan.FromSeconds(3));
                        Check(position.Position > TimeSpan.Zero, "Real WASAPI source replacement did not advance.");
                        if (i == 0)
                        {
                            Check(BassWasapi.GetInfo(out var info) && info.IsExclusive == (mode == "exclusive"), "Requested real output mode was changed.");
                            negotiated = new { info.Frequency, info.Channels, info.IsExclusive };
                        }
                    }
                    if (i == 49) { clock.Restart(); Sample(clock, stressSamples, "stress-after-warmup", 0); }
                    if (i >= 50)
                    {
                        stressChanges++; timings.Add(cycle.Elapsed.TotalMilliseconds);
                        if (stressChanges % 100 == 0) Sample(clock, stressSamples, "stress", stressChanges);
                    }
                }
                stress = ResourceAnalysis.Analyze(stressSamples, Environment.ProcessorCount);
                Check(stress.GrowthGuardPassed, "Native transition stress exceeded the observed handle/private-memory range guards.");
                var current = new AudioRequest(Guid.NewGuid(), first); var prepared = new AudioRequest(Guid.NewGuid(), first);
                if (graph is not null) { graph.Load(current); Check(graph.PrepareNext(prepared, false), "Soak preparation failed."); }
                else { backend!.Stop(); backend.Open(current); backend.PrepareNext(prepared, false); backend.Play(); }
                clock.Restart(); var phaseFrames = renderedFrames; Sample(clock, soakSamples, "soak", 0);
                var interval = Math.Max(5, Math.Min(60, durationSeconds / 12)); var sampleAt = (double)interval;
                while (clock.Elapsed.TotalSeconds < durationSeconds)
                {
                    BackendPosition position;
                    if (graph is not null)
                    {
                        Render(); position = graph.ReadPosition(0);
                        var due = (renderedFrames - phaseFrames) / 48000d;
                        maximumPacingLateness = Math.Max(maximumPacingLateness, clock.Elapsed.TotalSeconds - due);
                        var remaining = due - clock.Elapsed.TotalSeconds; if (remaining > .001) Thread.Sleep(TimeSpan.FromSeconds(remaining));
                    }
                    else { position = backend!.ReadPosition(); Thread.Sleep(10); }
                    Check(!position.Ended, "Sustained native playback starved before the requested duration.");
                    if (position.Transition is { } adopted)
                    {
                        Check(adopted.EntryId == prepared.EntryId, "Soak adopted the wrong scheduled occurrence."); soakTransitions++;
                        prepared = new AudioRequest(Guid.NewGuid(), first);
                        if (graph is not null) Check(graph.PrepareNext(prepared, false), "Soak rescheduling missed its native boundary.");
                        else backend!.PrepareNext(prepared, false);
                    }
                    if (clock.Elapsed.TotalSeconds >= sampleAt) { Sample(clock, soakSamples, "soak", soakTransitions); sampleAt += interval; }
                }
                soakElapsed = clock.Elapsed.TotalSeconds; Sample(clock, soakSamples, "soak-final", soakTransitions);
                Check(soakTransitions >= durationSeconds / 3 - 1, "Sustained output did not consume the expected repeated source timelines.");
                if (graph is not null) Check(renderedFrames - phaseFrames >= durationSeconds * 48000L * .98, "Paced native render did not consume the requested PCM duration.");
                soak = ResourceAnalysis.Analyze(soakSamples, Environment.ProcessorCount);
                Check(soak.GrowthGuardPassed, "Sustained native playback exceeded the observed handle/private-memory range guards.");
                backend?.Stop();
            }
            for (var i = 0; i < 2; i++)
            { var path = i == 0 ? first : second; Check(Hash(path) == hashes[i], "Stress/soak changed source audio."); using (File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None)) { } }
            sourcesReleased = true; status = mode == "mixer" ? "g12-native-mixer-passed" : "g12-output-workflow-passed";
        }
        catch (Exception exception) { failure = exception.Message; }
        timings.Sort();
        return new { Status = status, Mode = mode, Versions = versions, SelectedDevice = device, NegotiatedOutput = negotiated, StartedUtc = started,
            EndedUtc = DateTimeOffset.UtcNow, RequestedSoakSeconds = durationSeconds, ActualSoakSeconds = soakElapsed, WarmupChanges = 50,
            StressChanges = stressChanges, Stress = stress, StressSamples = stressSamples,
            ChangeP95Milliseconds = timings.Count == 0 ? (double?)null : timings[(int)Math.Ceiling(timings.Count * .95) - 1],
            SoakTransitions = soakTransitions, Soak = soak, SoakSamples = soakSamples, RenderedFrames = mode == "mixer" ? (long?)renderedFrames : null,
            IncomingPcmMaximumError = mode == "mixer" ? (double?)error : null, NativePcmPeak = mode == "mixer" ? (double?)peak : null,
            MaximumPacingLatenessSeconds = mode == "mixer" ? (double?)maximumPacingLateness : null,
            Sources = new[] { new { File = Path.GetFileName(first), Sha256 = hashes[0] }, new { File = Path.GetFileName(second), Sha256 = hashes[1] } },
            SourceHashesAndExclusiveReopen = sourcesReleased, Failure = failure,
            TwoHourOutput = status == "g12-output-workflow-passed" && soakElapsed >= 7200 ? "observed" : "not-run",
            CpuMethod = "Process CPU delta / elapsed monotonic wall time / logical processors; also reported as one-core equivalent. No forced GC.",
            ProcessScope = "Owned headless self-contained apphost/native production path; WPF window, full library and waveform UI are not exercised",
            Output = mode == "mixer" ? "not-run: actual native mixer callback PCM, without WASAPI" : "actual WASAPI consumption; not a listening/capture record",
            ReferenceWindows11Pc = "not-verified", Listening = "not-run", FullAc033Acceptance = "not-claimed", Windows = RuntimeInformation.OSDescription };
    }
}
