using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using ManagedBass;
using Player.App.Services.Audio;
using Player.Core.Playback;

namespace Player.AudioSmoke;

internal static class LongFormatValidation
{
    public static object Run(string directory)
    {
        directory = Path.GetFullPath(directory); LocalFileAccess.ValidateDirectory(directory);
        var manifest = Path.Combine(directory, "manifest.json");
        if (new FileInfo(manifest).Length > 65536) throw new InvalidDataException("Long fixture manifest is oversized.");
        using var document = JsonDocument.Parse(File.ReadAllText(manifest));
        if (document.RootElement.GetProperty("schemaVersion").GetInt32() != 1) throw new InvalidDataException("Unknown fixture schema.");
        var results = new List<object>();
        using var context = new NativeDecodeContext();
        foreach (var fixture in document.RootElement.GetProperty("fixtures").EnumerateArray())
        {
            var name = fixture.GetProperty("path").GetString()!;
            if (Path.GetFileName(name) != name) throw new InvalidDataException("Fixture path escapes its directory.");
            var path = LocalFileAccess.ValidateFile(Path.Combine(directory, name)); var hash = Hash(path);
            Check(hash == fixture.GetProperty("sha256").GetString(), "Long fixture hash mismatch.");
            var clock = Stopwatch.StartNew(); var stream = Bass.CreateStream(path, 0, 0, BassFlags.Decode | BassFlags.Float | BassFlags.Prescan);
            Check(stream != 0, "Long fixture open failed: " + Bass.LastError);
            object evidence;
            try
            {
                Check(Bass.ChannelGetInfo(stream, out var info), "Long fixture format unavailable.");
                var bytes = Bass.ChannelGetLength(stream); var duration = Bass.ChannelBytes2Seconds(stream, bytes);
                Check(info.Frequency == fixture.GetProperty("sampleRate").GetInt32() && info.Channels == fixture.GetProperty("channels").GetInt32() && info.ChannelType.ToString() == fixture.GetProperty("codec").GetString(),
                    $"Long format facts differ: {name}; actual {info.Frequency} Hz/{info.Channels} channels/{info.ChannelType}; expected {fixture.GetProperty("sampleRate")}/{fixture.GetProperty("channels")}/{fixture.GetProperty("codec")}; duration {duration:R}.");
                Check(Math.Abs(duration - fixture.GetProperty("durationSeconds").GetDouble()) < .1, "Long duration truncated or inflated.");
                var ranges = new List<object>(); var buffer = new float[4096];
                foreach (var position in new[] { .25, 3600.25, 7199.25 })
                {
                    var seekClock = Stopwatch.StartNew(); var offset = Bass.ChannelSeconds2Bytes(stream, position);
                    Check(Bass.ChannelSetPosition(stream, offset), "Long 64-bit seek failed.");
                    var actual = Bass.ChannelGetPosition(stream);
                    Check(Math.Abs(Bass.ChannelBytes2Seconds(stream, actual) - position) < .01, "Long seek landed on the wrong range.");
                    var read = Bass.ChannelGetData(stream, buffer, buffer.Length * sizeof(float));
                    Check(read == buffer.Length * sizeof(float), "Long bounded range read was short.");
                    float peak = 0; double energy = 0;
                    for (var i = 0; i < read / sizeof(float); i++) { Check(float.IsFinite(buffer[i]), "Non-finite long PCM."); peak = Math.Max(peak, Math.Abs(buffer[i])); energy += buffer[i] * (double)buffer[i]; }
                    Check(peak >= fixture.GetProperty("expectedPeakMinimum").GetSingle() && peak <= fixture.GetProperty("expectedPeakMaximum").GetSingle(), "Long fixture marker/audio was lost.");
                    ranges.Add(new { PositionSeconds = position, DecodedByteOffset = actual, Peak = peak, Rms = Math.Sqrt(energy / (read / 4)), SeekAndReadMilliseconds = seekClock.Elapsed.TotalMilliseconds });
                }
                Check(Bass.ChannelSetPosition(stream, bytes - buffer.Length * sizeof(float)), "Long tail seek failed.");
                var tail = Bass.ChannelGetData(stream, buffer, buffer.Length * sizeof(float));
                Check(tail == buffer.Length * sizeof(float) && Bass.ChannelGetData(stream, buffer, buffer.Length * sizeof(float)) < 0 && Bass.LastError == Errors.Ended, "Long natural end was not reached exactly.");
                evidence = new { File = name, DurationSeconds = duration, DecodedFloatBytes = bytes, ExceedsInt32DecodedRange = bytes > int.MaxValue,
                    info.Frequency, info.Channels, Ranges = ranges, NaturalEnd = true, BoundedBufferBytes = buffer.Length * sizeof(float), ElapsedMilliseconds = clock.Elapsed.TotalMilliseconds };
            }
            finally { Check(Bass.StreamFree(stream), "Long stream disposal failed."); }
            using (var backend = new BassAudioBackend())
            {
                var request = new AudioRequest(Guid.NewGuid(), path); var source = backend.Open(request);
                Check(source.CanSeek && Math.Abs(source.Duration!.Value.TotalSeconds - fixture.GetProperty("durationSeconds").GetDouble()) < .1, "Production long source preparation differs.");
                backend.Seek(TimeSpan.FromSeconds(7199.25)); Check(Math.Abs(backend.ReadPosition().Position.TotalSeconds - 7199.25) < .01, "Production long seek position differs.");
                backend.Stop(); Check(backend.ReadPosition().Position == TimeSpan.Zero, "Long Stop did not reset position."); backend.CloseSource();
            }
            Check(Hash(path) == hash, "Long source file changed."); using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None)) { }
            results.Add(evidence);
        }
        return new { Status = "long-formats-passed", Results = results, ProductionPrepareSeekStop = true, SourcePreservation = true, Output = "not-run; bounded native decode is not two-hour device playback", HugeCompressedApe = "not-run; owned APE tests two-hour duration and >Int32 decoded range, not a compressed file over 4 GiB" };
    }
    private static string Hash(string path) { using var file = File.OpenRead(path); return Convert.ToHexStringLower(SHA256.HashData(file)); }
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
