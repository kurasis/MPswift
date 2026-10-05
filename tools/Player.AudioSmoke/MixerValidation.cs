using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Player.App.Services.Audio;
using Player.Core.Media;
using Player.Core.Playback;

namespace Player.AudioSmoke;

internal static class MixerValidation
{
    public static unsafe object Run(string fixture)
    {
        var directory = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(fixture))!, "stage-d-mixer"); Directory.CreateDirectory(directory);
        const int rate = 48000, frames = 48000; var source = new short[frames * 2];
        for (var i = 0; i < frames; i++) { var value = (short)Math.Round(Math.Sin(2 * Math.PI * 997 * i / rate) * 12000); source[i * 2] = value; source[i * 2 + 1] = (short)-value; }
        var left = Path.Combine(directory, "left.wav"); var right = Path.Combine(directory, "right.wav"); var whole = Path.Combine(directory, "whole.wav");
        Write(left, source[..frames], rate); Write(right, source[frames..], rate); Write(whole, source, rate);
        var hashes = new[] { left, right, whole }.Select(Hash).ToArray();
        using (var context = new NativeDecodeContext())
        {
            float[] Capture(AudioRequest first, AudioRequest second, double fade = 0)
            {
                using var graph = new BassMixerGraph(rate, 2, new(CrossfadeSeconds: fade)); graph.SetVolume(1, false, true);
                var firstInfo = graph.Load(first); Require(graph.PrepareNext(second, false), "Next source could not be scheduled.");
                var output = new float[source.Length]; var offset = 0;
                while (offset < output.Length)
                {
                    var count = Math.Min(794, output.Length - offset); // Cross the split inside a render block.
                    fixed (float* p = &output[offset]) { var got = graph.Render((nint)p, count * sizeof(float)); Require(got == count * sizeof(float), "Short persistent mixer read."); }
                    offset += count;
                }
                var position = graph.ReadPosition(0); Require(position.Transition?.EntryId == second.EntryId, "Incoming item was not adopted on the audible timeline."); Require(position.Ended, "Split sources did not reach bounded EOF.");
                return output;
            }
            var output = Capture(new(Guid.NewGuid(), left), new(Guid.NewGuid(), right));
            var maximumError = output.Select((v, i) => Math.Abs(v - source[i] / 32768f)).Max(); Require(maximumError < 0.000001, "Split lossless signal has a gap, duplicate or boundary error.");
            var cue = Capture(new(Guid.NewGuid(), whole, new(TimeSpan.Zero, TimeSpan.FromSeconds(0.5))), new(Guid.NewGuid(), whole, new(TimeSpan.FromSeconds(0.5), TimeSpan.FromSeconds(1))));
            var cueError = cue.Select((v, i) => Math.Abs(v - source[i] / 32768f)).Max(); Require(cueError < 0.000001, "Contiguous CUE output differs from source.");
            WriteCapture(Path.Combine(directory, "lossless-capture.f32"), output); WriteCapture(Path.Combine(directory, "cue-capture.f32"), cue);
            // Real overlap, canceled by seeking the incoming item after adoption.
            using var graph = new BassMixerGraph(rate, 2, new(CrossfadeSeconds: 0.2)); graph.SetVolume(1, false, true);
            var a = new AudioRequest(Guid.NewGuid(), left); var b = new AudioRequest(Guid.NewGuid(), right); graph.Load(a); Require(graph.PrepareNext(b, false), "Crossfade scheduling failed.");
            var buffer = new float[32000]; fixed (float* p = buffer) Require(graph.Render((nint)p, buffer.Length * sizeof(float)) == buffer.Length * sizeof(float), "Crossfade read failed.");
            var incoming = graph.ReadPosition(0); Require(incoming.Transition?.EntryId == b.EntryId && incoming.Position > TimeSpan.Zero, "Crossfade UI item did not switch at incoming start.");
            Require(buffer.Skip(29000).Any(v => Math.Abs(v) > 0.01), "Crossfade overlap is silent."); graph.Seek(TimeSpan.FromSeconds(0.1));
            fixed (float* p = buffer) Require(graph.Render((nint)p, 400 * sizeof(float)) == 400 * sizeof(float), "Seek after overlap failed.");
            Require(graph.ReadPosition(0).Transition is null && graph.ActiveEntryId == b.EntryId, "Seeking overlap retained the outgoing item.");
            foreach (var path in new[] { left, right, whole }) using (File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) { }
            for (var i = 0; i < hashes.Length; i++) Require(hashes[i] == Hash(new[] { left, right, whole }[i]), "Mixer changed source audio.");
            return new { Status = "mixer-passed", Environment = RuntimeInformation.OSDescription, SplitFrames = frames, SampleRate = rate, Channels = 2,
                LosslessMaximumSampleError = maximumError, CueMaximumSampleError = cueError, AddedOrDuplicatedFrames = 0,
                PersistentGraph = true, PreparedNext = true, CrossfadeIncomingTimeline = true, SeekCancelsOverlap = true, SourceUnchanged = true,
                Capture = "stage-d-mixer/lossless-capture.f32", CueCapture = "stage-d-mixer/cue-capture.f32", CaptureEncoding = "IEEE float32 LE interleaved stereo",
                CapturePath = "production mixer callback PCM; no endpoint/system latency", WasapiOutputCapture = "not-run", Listening = "not-run", LossyGapless = "not-claimed", FixtureLicense = "CC0-1.0" };
        }
    }
    private static void Write(string path, short[] samples, int rate)
    {
        using var file = new FileStream(path, FileMode.Create, FileAccess.Write); using var w = new BinaryWriter(file, Encoding.ASCII);
        w.Write("RIFF"u8); w.Write(36 + samples.Length * 2); w.Write("WAVEfmt "u8); w.Write(16); w.Write((short)1); w.Write((short)2); w.Write(rate); w.Write(rate * 4); w.Write((short)4); w.Write((short)16); w.Write("data"u8); w.Write(samples.Length * 2); foreach (var s in samples) w.Write(s);
    }
    private static void WriteCapture(string path, float[] samples) { using var file = new FileStream(path, FileMode.Create, FileAccess.Write); file.Write(MemoryMarshal.AsBytes(samples.AsSpan())); }
    private static string Hash(string path) { using var file = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(file)); }
    private static void Require(bool ok, string message) { if (!ok) throw new InvalidDataException(message); }
}
