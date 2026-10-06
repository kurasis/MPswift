using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Player.App.Services.Audio;
using Player.AudioSmoke;
using Player.Core.Playback;

var json = new JsonSerializerOptions { WriteIndented = true };
void Report(object result) => Console.WriteLine(JsonSerializer.Serialize(result, json));

if (args.Length == 0 || args[0] is not ("--generate-fixture" or "--probe" or "--decode" or "--play" or "--formats" or "--extended-formats" or "--missing-wma" or "--engine" or "--engine-play" or "--waveform" or "--mixer" or "--stress") ||
    args.Length != (args[0] == "--probe" ? 1 : 2))
{
    Console.Error.WriteLine("Usage: Player.AudioSmoke --generate-fixture <new.wav> | --probe | --decode <local-file> | --play <local-file> | --formats <fixture-directory> | --engine <local-file> | --engine-play <local-file>");
    return 2;
}

try
{
    if (args[0] == "--generate-fixture")
    {
        Report(new { Status = "generated", Fixture = WavFixture.Generate(args[1]) });
        return 0;
    }
    if (!OperatingSystem.IsWindows() || RuntimeInformation.ProcessArchitecture != Architecture.X64)
    {
        Report(new { Status = "not-run", Reason = "BASS/WASAPI smoke requires Windows x64.", Environment = RuntimeInformation.OSDescription });
        return 3;
    }
    var versions = NativeLibraryBootstrap.LoadAndVerify();
    if (args[0] == "--missing-wma")
    {
        Require(NativeLibraryBootstrap.DecoderValidationErrors.ContainsKey("basswma"), "Missing-WMA check requires an owned copy without BASSWMA.");
        var wav = BassSmokeSession.ValidateSourcePath(Path.Combine(args[1], "pcm16.wav"));
        var wma = BassSmokeSession.ValidateSourcePath(Path.Combine(args[1], "wma2.wma"));
        var wavHash = HashFile(wav); var wmaHash = HashFile(wma); AudioError? unavailable;
        await using (var player = new SerializedAudioPlayer(() => new BassAudioBackend()))
        {
            Require(await player.LoadAsync(new(Guid.NewGuid(), wav), false) && player.Snapshot.CanSeek, "Core WAV failed without optional WMA dependency.");
            Require(!await player.LoadAsync(new(Guid.NewGuid(), wma), false), "WMA unexpectedly loaded without its approved plug-in.");
            unavailable = player.Snapshot.Error;
            Require(unavailable?.Category == AudioErrorCategory.Dependency, "Missing optional WMA was not reported as a dependency error.");
            Require(await player.LoadAsync(new(Guid.NewGuid(), wav), false) && player.Snapshot.State == PlaybackState.Stopped && player.Snapshot.CanSeek, "Core playback did not recover after unavailable WMA.");
        }
        using (new FileStream(wav, FileMode.Open, FileAccess.Read, FileShare.None)) { }
        using (new FileStream(wma, FileMode.Open, FileAccess.Read, FileShare.None)) { }
        Require(wavHash == HashFile(wav) && wmaHash == HashFile(wma), "Optional-dependency test changed source files.");
        Report(new { Status = "missing-optional-wma-passed", CoreWavAvailableBeforeAndAfter = true, WmaError = unavailable, SourceUnchanged = true, SourceHandlesReleased = true,
            Method = "Isolated actual tool copy with the approved WMA DLL omitted; MF fallback disabled", WindowsN = "not-run: this is a dependency failure check, not an N OS" });
        return 0;
    }
    if (args[0] == "--probe")
    {
        Report(new { Status = "native-load-passed", Versions = versions, Environment = RuntimeInformation.OSDescription });
        return 0;
    }
    if (args[0] == "--stress") { Report(await StressValidation.RunAsync(args[1])); return 0; }
    if (args[0] == "--extended-formats") { var report = ExtendedFormatValidation.Run(args[1]); Report(report); return report.Status == "extended-formats-passed" ? 0 : 1; }
    if (args[0] == "--mixer") { Report(MixerValidation.Run(args[1])); return 0; }
    if (args[0] == "--waveform") { Report(await WaveformValidation.RunAsync(args[1])); return 0; }
    if (args[0] == "--formats")
    {
        var directory = Path.GetFullPath(args[1]);
        LocalFileAccess.ValidateDirectory(directory);
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "manifest.json")));
        var results = new List<object>();
        foreach (var fixture in manifest.RootElement.GetProperty("fixtures").EnumerateArray())
        {
            var relative = fixture.GetProperty("path").GetString()!;
            if (Path.GetFileName(relative) != relative) throw new InvalidDataException("Invalid fixture path.");
            var file = BassSmokeSession.ValidateSourcePath(Path.Combine(directory, relative));
            var hash = HashFile(file);
            if (hash != fixture.GetProperty("sha256").GetString()) throw new InvalidDataException("Fixture checksum mismatch: " + relative);
            DecodeEvidence evidence;
            using (var session = new BassSmokeSession()) evidence = session.Decode(file);
            if (evidence.SampleRate != fixture.GetProperty("sampleRate").GetInt32() || evidence.Channels != fixture.GetProperty("channels").GetInt32() ||
                evidence.Codec != fixture.GetProperty("expectedBassCodec").GetString() ||
                (fixture.TryGetProperty("expectedBitDepth", out var bits) && evidence.BitDepth != bits.GetInt32()) ||
                Math.Abs(evidence.DurationSeconds - fixture.GetProperty("sourceDurationSeconds").GetDouble()) > fixture.GetProperty("durationToleranceSeconds").GetDouble() ||
                evidence.Peak is < 0.01f or > 0.2f)
                throw new InvalidDataException("Unexpected decoded fixture facts: " + relative + "; " + JsonSerializer.Serialize(evidence));
            using (var exclusive = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.None)) { }
            if (HashFile(file) != hash) throw new InvalidDataException("Fixture source changed.");
            float? losslessError = null;
            float? lossyError = null;
            if (fixture.TryGetProperty("correction", out var correction))
            {
                var companion = correction.GetProperty("path").GetString()!;
                if (Path.GetFileName(companion) != companion || HashFile(Path.Combine(directory, companion)) != correction.GetProperty("sha256").GetString())
                    throw new InvalidDataException("Correction checksum mismatch.");
            }
            if (fixture.TryGetProperty("losslessReference", out var reference))
            {
                var name = reference.GetString()!;
                if (Path.GetFileName(name) != name) throw new InvalidDataException("Invalid reference path.");
                var referencePath = Path.Combine(directory, name);
                if (HashFile(referencePath) != manifest.RootElement.GetProperty("sourceSha256").GetString()) throw new InvalidDataException("Reference checksum mismatch.");
                losslessError = ExtendedFormatValidation.ComparePcm(file, referencePath);
                if (losslessError != 0) throw new InvalidDataException("Lossless fixture PCM differs: " + relative);
                if (HashFile(file) != hash) throw new InvalidDataException("PCM comparison changed source.");
            }
            if (fixture.TryGetProperty("correction", out correction))
            {
                var companion = Path.Combine(directory, correction.GetProperty("path").GetString()!);
                using (new FileStream(companion, FileMode.Open, FileAccess.Read, FileShare.None)) { }
                if (HashFile(companion) != correction.GetProperty("sha256").GetString()) throw new InvalidDataException("Correction file changed during decoding.");
            }
            if (fixture.TryGetProperty("lossyReference", out reference))
            {
                var name = reference.GetString()!;
                if (Path.GetFileName(name) != name || File.Exists(file + "c")) throw new InvalidDataException("Invalid uncorrected hybrid comparison inputs.");
                var referencePath = Path.Combine(directory, name);
                if (HashFile(referencePath) != manifest.RootElement.GetProperty("sourceSha256").GetString()) throw new InvalidDataException("Reference checksum mismatch.");
                lossyError = ExtendedFormatValidation.ComparePcm(file, referencePath);
                if (lossyError <= 0 || !float.IsFinite(lossyError.Value)) throw new InvalidDataException("Owned uncorrected hybrid control did not expose lossy PCM.");
                if (HashFile(file) != hash) throw new InvalidDataException("Hybrid comparison changed source.");
            }
            using (new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.None)) { }
            if (fixture.TryGetProperty("losslessReference", out reference) || fixture.TryGetProperty("lossyReference", out reference))
            {
                var referencePath = Path.Combine(directory, reference.GetString()!);
                using (new FileStream(referencePath, FileMode.Open, FileAccess.Read, FileShare.None)) { }
                if (HashFile(referencePath) != manifest.RootElement.GetProperty("sourceSha256").GetString()) throw new InvalidDataException("Comparison changed the owned reference.");
            }
            results.Add(new { File = relative, Profile = fixture.GetProperty("profile").GetString(), Status = "decode-seek-end-dispose-passed", SourceSha256 = hash, Decode = evidence, LosslessMaximumError = losslessError, UncorrectedHybridMaximumError = lossyError });
        }
        if (results.Count == 0) throw new InvalidDataException("No format fixtures executed.");
        Report(new { Status = "formats-passed", Environment = RuntimeInformation.OSDescription, Count = results.Count, Results = results, DeviceOutput = "not-run" });
        return 0;
    }
    if (args[0] is "--engine" or "--engine-play" or "--waveform")
    {
        var file = BassSmokeSession.ValidateSourcePath(Path.GetFullPath(args[1]));
        var beforeHash = HashFile(file);
        PlaybackSnapshot final;
        await using (var player = new SerializedAudioPlayer(() => new BassAudioBackend()))
        {
            var request = new AudioRequest(Guid.NewGuid(), file);
            Require(await player.LoadAsync(request, false), "Production engine load failed: " + player.Snapshot.Error?.Detail);
            Require(player.Snapshot.State == PlaybackState.Stopped && player.Snapshot.CanSeek, "Prepared state invalid.");
            Require(await player.SeekAsync(TimeSpan.FromSeconds(1)), "Production seek failed.");
            Require(Math.Abs(player.Snapshot.Position.TotalSeconds - 1) < 0.01, "Prepared seek position invalid.");
            if (args[0] == "--engine-play")
            {
                Require(await player.PlayAsync(), "Production play failed: " + player.Snapshot.Error?.Detail);
                await Task.Delay(600);
                Require(player.Snapshot.State == PlaybackState.Playing && player.Snapshot.Position > TimeSpan.FromSeconds(1), "Playback did not advance.");
                Require(await player.PauseAsync(), "Production pause failed.");
                var paused = player.Snapshot.Position;
                await Task.Delay(200);
                Require(player.Snapshot.State == PlaybackState.Paused && player.Snapshot.Position == paused, "Paused position advanced.");
                Require(await player.SeekAsync(TimeSpan.FromSeconds(0.5)), "Paused seek failed.");
                Require(player.Snapshot.State == PlaybackState.Paused, "Seek lost paused state.");
                Require(await player.SetVolumeAsync(0.25, true), "Production mute command failed.");
                Require(await player.SetVolumeAsync(0.25, false), "Production unmute command failed.");
                Require(await player.PlayAsync(), "Production resume failed.");
                await Task.Delay(350);
                Require(player.Snapshot.Position > TimeSpan.FromSeconds(0.5), "Resumed playback did not advance.");
            }
            else
            {
                var replacements = Enumerable.Range(0, 40).Select(_ => player.LoadAsync(new AudioRequest(Guid.NewGuid(), file), false)).ToArray();
                await Task.WhenAll(replacements);
                Require(player.Snapshot.State == PlaybackState.Stopped && player.Snapshot.CanSeek, "Rapid preparation lost latest state.");
            }
            Require(await player.StopAsync(), "Production stop failed.");
            Require(player.Snapshot.Position == TimeSpan.Zero && player.Snapshot.State == PlaybackState.Stopped, "Stop did not reset source.");
            final = player.Snapshot;
        }
        using (var exclusive = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.None)) { }
        Require(beforeHash == HashFile(file), "Production engine changed source.");
        Report(new { Status = "production-engine-passed", Environment = RuntimeInformation.OSDescription, Snapshot = final,
            Output = args[0] == "--engine-play" ? "shared-api-tested" : "not-run", SourceHandleReleased = true, SourceUnchanged = true, AudiblePlayback = "not-manually-verified" });
        return 0;
    }
    var path = BassSmokeSession.ValidateSourcePath(Path.GetFullPath(args[1]));
    var before = HashFile(path);
    DecodeEvidence decoded;
    OutputEvidence? output = null;
    using (var session = new BassSmokeSession())
    {
        decoded = session.Decode(path);
        if (args[0] == "--play") output = session.ExerciseSharedOutput();
    }
    // The source must be preserved and closed after native disposal.
    using (var exclusive = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None)) { }
    if (before != HashFile(path)) throw new InvalidDataException("Source checksum changed.");
    if (File.Exists(path + ".json"))
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(path + ".json"));
        var expected = manifest.RootElement;
        if (expected.GetProperty("Sha256").GetString() != before ||
            expected.GetProperty("SampleRate").GetInt32() != decoded.SampleRate ||
            expected.GetProperty("Channels").GetInt32() != decoded.Channels ||
            Math.Abs(expected.GetProperty("DurationSeconds").GetDouble() - decoded.DurationSeconds) > expected.GetProperty("DurationToleranceSeconds").GetDouble() ||
            Math.Abs(expected.GetProperty("ExpectedPeak").GetDouble() - decoded.Peak) > expected.GetProperty("PeakTolerance").GetDouble())
            throw new InvalidDataException("Decoded fixture does not match its manifest.");
    }
    Report(new
    {
        Status = output is null ? "decode-passed" : "output-api-passed",
        Environment = RuntimeInformation.OSDescription, Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
        Versions = versions, SourceSha256 = before, Decode = decoded, Output = output,
        SourceUnchanged = true, SourceHandleReleased = true,
        AudiblePlayback = "not-manually-verified", Gapless = "not-run"
    });
    return 0;
}
catch (Exception error)
{
    Report(new { Status = "failed", ErrorType = error.GetType().Name, error.Message, Environment = RuntimeInformation.OSDescription });
    return 1;
}

static string HashFile(string path)
{
    using var stream = File.OpenRead(path);
    return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
}
static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
