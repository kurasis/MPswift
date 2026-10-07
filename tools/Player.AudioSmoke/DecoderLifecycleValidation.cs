using ManagedBass;
using Player.App.Services.Audio;

namespace Player.AudioSmoke;

/// <summary>Actual seek/EOF/reopen controls in the already resource-contained security worker.</summary>
internal static class DecoderLifecycleValidation
{
    public static object Run(string source)
    {
        using var lease = LocalReadLease.Open(source);
        using var context = new NativeDecodeContext();
        var buffer = new float[4096]; long total = 0;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var stream = Bass.CreateStream(lease.Path, 0, 0, BassFlags.Decode | BassFlags.Float | BassFlags.Prescan);
            if (stream == 0) throw new InvalidOperationException("Lifecycle open: " + Bass.LastError);
            try
            {
                var length = Bass.ChannelGetLength(stream);
                if (!Bass.ChannelGetInfo(stream, out var info) || length <= 0 || info.Channels <= 0) throw new InvalidDataException("Lifecycle source facts invalid.");
                var alignment = info.Channels * sizeof(float);
                foreach (var fraction in new[] { 0.0, 0.5, 0.95, 0.0 })
                {
                    var position = (long)(length * fraction) / alignment * alignment;
                    if (!Bass.ChannelSetPosition(stream, position)) throw new InvalidOperationException("Lifecycle seek: " + Bass.LastError);
                    var read = Bass.ChannelGetData(stream, buffer, buffer.Length * sizeof(float));
                    if (read <= 0) throw new InvalidDataException("Lifecycle seek produced no data.");
                    Validate(buffer, read);
                    total += read;
                }
                long decoded = 0;
                while (true)
                {
                    var read = Bass.ChannelGetData(stream, buffer, buffer.Length * sizeof(float));
                    if (read < 0 && Bass.LastError == Errors.Ended) break;
                    if (read <= 0) throw new InvalidDataException("Lifecycle decode stalled or failed: " + Bass.LastError);
                    Validate(buffer, read); decoded += read; total += read;
                    if (decoded > 256L * 1024 * 1024) throw new InvalidDataException("Lifecycle decode exceeds fixture bound.");
                }
                if (Bass.ChannelGetData(stream, buffer, buffer.Length * sizeof(float)) >= 0 || Bass.LastError != Errors.Ended)
                    throw new InvalidDataException("Repeated EOF did not report Ended.");
                if (!Bass.ChannelSetPosition(stream, 0) || Bass.ChannelGetData(stream, buffer, buffer.Length * sizeof(float)) <= 0)
                    throw new InvalidDataException("Decoder did not recover after EOF seek.");
            }
            finally { if (!Bass.StreamFree(stream)) throw new InvalidOperationException("Lifecycle free: " + Bass.LastError); }
        }
        return new { Reopens = 3, SeeksPerOpen = 5, RepeatedEofChecked = true, DecodeAfterEofSeek = true, DecodedBytes = total, SourcePinned = true };
    }
    private static void Validate(float[] samples, int bytes)
    {
        if (bytes > samples.Length * sizeof(float) || bytes % sizeof(float) != 0) throw new InvalidDataException("Lifecycle decoder buffer length invalid.");
        for (var i = 0; i < bytes / sizeof(float); i++) if (!float.IsFinite(samples[i])) throw new InvalidDataException("Lifecycle decoder returned a nonfinite sample.");
    }
}
