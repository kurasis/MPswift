using ManagedBass;
using Player.App.Services.Audio;
using Player.Core.Playback;

namespace Player.AudioSmoke;

internal static class StreamLifetimeValidation
{
    public static void PrepareLinks()
    {
        var root = Environment.CurrentDirectory;
        File.Copy(Path.Combine(root, "lifetime.wv"), Path.Combine(root, "linked-lifetime.wv"));
        File.CreateSymbolicLink(Path.Combine(root, "linked-lifetime.wvc"), Path.Combine(root, "lifetime.wvc"));
    }
    public static object Run(string source)
    {
        using var context = new NativeDecodeContext();
        Check(NativeStreamPins.Count == 0, "Unexpected pre-existing source pins.");
        var wav = BassMixerGraph.OpenSource(new AudioRequest(Guid.NewGuid(), source)).Handle;
        try
        {
            MustRefuse(() => { using var writer = File.Open(source, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete); }, "Production WAV stream allowed writes.");
            MustRefuse(() => File.Move(source, source + ".moved"), "Production WAV stream allowed replacement.");
            GC.Collect(); GC.WaitForPendingFinalizers();
            Check(Bass.ChannelSetPosition(wav, 0), "Pinned source seek failed.");
            Check(Bass.ChannelGetData(wav, new float[512], 2048) > 0, "Pinned source PCM failed.");
        }
        finally { Check(NativeStreamPins.Free(wav), "Production source release failed."); }
        using (File.Open(source, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
        Check(NativeStreamPins.Count == 0, "WAV pins were retained after stream release.");

        var wv = Path.Combine(Environment.CurrentDirectory, "lifetime.wv");
        var wvc = Path.ChangeExtension(wv, ".wvc");
        var reference = Path.Combine(Environment.CurrentDirectory, "lifetime-reference.wav");
        var stash = wvc + ".owned-stash";
        File.Move(wvc, stash);
        var missing = BassMixerGraph.OpenSource(new AudioRequest(Guid.NewGuid(), wv)).Handle;
        float lossyError;
        try
        {
            // Appearance after open must not change the decoder's frozen input selection.
            File.Copy(stash, wvc);
            GC.Collect(); GC.WaitForPendingFinalizers();
            lossyError = Compare(missing, reference);
            Check(lossyError > 0.000001f, "A decoder without correction unexpectedly adopted a later sidecar.");
        }
        finally { Check(NativeStreamPins.Free(missing), "Missing-sidecar source release failed."); }
        var corrected = BassMixerGraph.OpenSource(new AudioRequest(Guid.NewGuid(), wv)).Handle;
        float correctedError;
        try
        {
            MustRefuse(() => { using var writer = File.Open(wv, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete); }, "Production WavPack main file allowed writes.");
            MustRefuse(() => { using var writer = File.Open(wvc, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete); }, "Production WavPack correction allowed writes.");
            MustRefuse(() => File.Move(wvc, wvc + ".moved"), "Production WavPack correction allowed replacement.");
            GC.Collect(); GC.WaitForPendingFinalizers();
            correctedError = Compare(corrected, reference);
            Check(correctedError == 0, "Pinned WavPack correction did not produce exact reference PCM.");
        }
        finally { Check(NativeStreamPins.Free(corrected), "Corrected source release failed."); }
        foreach (var path in new[] { wv, wvc }) using (File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
        var renamed = Path.Combine(Environment.CurrentDirectory, "renamed-wavpack.flac");
        File.Copy(wv, renamed); File.Copy(wvc, Path.ChangeExtension(renamed, ".wvc"));
        var renamedHandle = BassMixerGraph.OpenSource(new AudioRequest(Guid.NewGuid(), renamed)).Handle;
        try { Check(Compare(renamedHandle, reference) == 0, "A renamed WavPack input bypassed correction handling."); }
        finally { Check(NativeStreamPins.Free(renamedHandle), "Renamed source release failed."); }
        var external = BassMixerGraph.OpenSource(new AudioRequest(Guid.NewGuid(), wv)).Handle;
        Check(Bass.StreamFree(external), "Direct public integer-handle release failed.");
        var linked = BassMixerGraph.OpenSource(new AudioRequest(Guid.NewGuid(), Path.Combine(Environment.CurrentDirectory, "linked-lifetime.wv"))).Handle;
        try
        {
            MustRefuse(() => { using var writer = File.Open(wvc, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete); }, "A local correction link bypassed target read sharing.");
            Check(Compare(linked, reference) == 0, "Local correction link did not preserve exact PCM.");
        }
        finally { Check(NativeStreamPins.Free(linked), "Linked correction source release failed."); }
        using (File.Open(wvc, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
        Check(NativeStreamPins.Count == 0, "Callback/source pins leaked after release.");
        return new { Status = "production-stream-lifetime-passed", WavWritesAndReplacementDenied = true,
            WavPackMainAndCorrectionWritesDenied = true, CorrectionReplacementDenied = true,
            CallbacksSurviveCollection = true, MissingCorrectionSelectionFrozen = true, RenamedWavPackDetected = true, DirectPublicHandleReleasePreserved = true, LocalCorrectionLinksPreserved = true,
            CorrectedMaximumPcmError = correctedError, UncorrectedMaximumPcmError = lossyError, SourceHandlesReleased = true };
    }
    private static float Compare(int source, string reference)
    {
        var target = Bass.CreateStream(reference, 0, 0, BassFlags.Decode | BassFlags.Float);
        Check(target != 0, "Reference stream failed.");
        try
        {
            Check(Bass.ChannelSetPosition(source, 0), "Source rewind failed.");
            Check(Bass.ChannelGetLength(source) == Bass.ChannelGetLength(target), "Reference length differs.");
            var a = new float[8192]; var b = new float[8192]; float maximum = 0;
            while (true)
            {
                var count = Bass.ChannelGetData(source, a, a.Length * 4);
                var ended = Bass.LastError;
                var other = Bass.ChannelGetData(target, b, b.Length * 4);
                Check(count == other, "Reference block length differs.");
                if (count < 0) { Check(ended == Errors.Ended && Bass.LastError == Errors.Ended, "Reference decode failed."); break; }
                Check(count > 0, "Reference decode stalled.");
                for (var i = 0; i < count / 4; i++)
                { Check(float.IsFinite(a[i]) && float.IsFinite(b[i]), "Nonfinite PCM."); maximum = Math.Max(maximum, Math.Abs(a[i] - b[i])); }
            }
            return maximum;
        }
        finally { Bass.StreamFree(target); }
    }
    private static void MustRefuse(Action action, string message)
    { try { action(); } catch (IOException) { return; } catch (UnauthorizedAccessException) { return; } throw new InvalidOperationException(message); }
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
