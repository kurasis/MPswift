using System.Security.Cryptography;
using ManagedBass;
using Player.App.Services.Audio;
using Player.Core.Playback;

namespace Player.AudioSmoke;

internal static class LossyMixerValidation
{
    public static unsafe object Run()
    {
        var fixtureDirectory = Path.Combine(Environment.CurrentDirectory, "tests", "fixtures", "audio");
        var names = new[] { "mp3-cbr.mp3", "mp3-vbr.mp3", "opus.opus" };
        if (names.Any(name => !File.Exists(Path.Combine(fixtureDirectory, name))))
        {
            if (Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true") throw new FileNotFoundException("CI lossy fixtures are required.");
            return new { Status = "not-run", Reason = "Optional repository lossy fixtures are unavailable in this working directory." };
        }
        var results = new List<object>();
        foreach (var name in names)
        {
            var path = Path.Combine(fixtureDirectory, name);
            var hash = Hash(path);
            var reference = new float[144000 * 2]; // Owned three-second stereo fixture at 48 kHz.
            var decoder = Bass.CreateStream(path, 0, 0, BassFlags.Decode | BassFlags.Float | BassFlags.Prescan);
            Check(decoder != 0, "Lossy reference open failed.");
            try
            {
                Check(Bass.ChannelGetInfo(decoder, out var info) && info.Frequency == 48000 && info.Channels == 2, "Lossy reference format differs.");
                Check(Bass.ChannelGetLength(decoder) == reference.Length * sizeof(float), "Known lossy skip/padding did not preserve the independent source frame count.");
                var offset = 0;
                while (offset < reference.Length)
                {
                    var count = Math.Min(8192, reference.Length - offset);
                    fixed (float* target = &reference[offset])
                    {
                        var read = Bass.ChannelGetData(decoder, (nint)target, count * sizeof(float));
                        Check(read > 0 && read % 8 == 0, "Lossy reference read failed."); offset += read / sizeof(float);
                    }
                }
                Check(Bass.ChannelGetData(decoder, new float[2], 8) < 0 && Bass.LastError == Errors.Ended, "Lossy reference did not reach exact EOF.");
            }
            finally { Check(Bass.StreamFree(decoder), "Lossy reference release failed."); }
            float maximum = 0;
            using (var graph = new BassMixerGraph(48000, 2, new()))
            {
                graph.SetVolume(1, false, true);
                var incoming = new AudioRequest(Guid.NewGuid(), path);
                graph.Load(new AudioRequest(Guid.NewGuid(), path));
                Check(graph.PrepareNext(incoming, false), "Lossy next source preparation failed.");
                var output = new float[794]; var offset = 0;
                while (offset < reference.Length * 2)
                {
                    var count = Math.Min(output.Length, reference.Length * 2 - offset);
                    fixed (float* target = output) Check(graph.Render((nint)target, count * sizeof(float)) == count * sizeof(float), "Lossy mixer stalled.");
                    for (var i = 0; i < count; i++)
                    {
                        Check(float.IsFinite(output[i]), "Lossy mixer returned nonfinite PCM.");
                        maximum = Math.Max(maximum, Math.Abs(output[i] - reference[(offset + i) % reference.Length]));
                    }
                    offset += count;
                }
                var position = graph.ReadPosition(0);
                Check(position.Transition?.EntryId == incoming.EntryId && position.Ended, "Lossy boundary timeline or exact EOF differs.");
            }
            Check(maximum < 0.000001f, "Lossy mixer inserted, dropped or changed decoded boundary samples.");
            using (File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None)) { }
            Check(hash == Hash(path), "Lossy boundary check modified media.");
            results.Add(new { File = name, SourceFrames = 144000, CombinedFrames = 288000, MaximumMixerReferenceError = maximum,
                TransitionAndExactEof = true, SourceUnchanged = true, SourceHandlesReleased = true });
        }
        return new { Status = "known-lossy-mixer-profiles-passed", Results = results,
            Scope = "These owned MP3 CBR/VBR and Opus profiles only; reference is actual separately decoded PCM with independently observed skip/padding frame counts",
            AacGapless = "not-claimed: observed AAC profiles include extra decoded frames", EndpointCapture = "not-run" };
    }
    private static string Hash(string path) { using var file = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(file)); }
    private static void Check(bool value, string message) { if (!value) throw new InvalidDataException(message); }
}
