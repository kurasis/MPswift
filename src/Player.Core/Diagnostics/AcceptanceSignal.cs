namespace Player.Core.Diagnostics;

/// <summary>Owned deterministic PCM; acceptance runners never need the user's music or an encoder download.</summary>
public static class AcceptanceSignal
{
    public static short Sample(int frame, int channel, int rate, double frequency) =>
        (short)Math.Round(1000 * Math.Sin(2 * Math.PI * frequency * frame / rate + channel * .17));
    public static short ReferenceSample(int frame)
    {
        var value = unchecked(((uint)frame + 1) * 0x9e3779b9u);
        value ^= value >> 16; value *= 0x85ebca6bu; value ^= value >> 13; value *= 0xc2b2ae35u; value ^= value >> 16;
        return (short)((int)(value % 2001) - 1000);
    }
    public static void WriteWave(string path, int rate = 48000, int channels = 2, int seconds = 3, double frequency = 997, bool reference = false)
    {
        if (rate is < 8000 or > 192000 || channels is < 1 or > 8 || seconds is < 1 or > 30 || !double.IsFinite(frequency) || frequency <= 0 || frequency >= rate / 2)
            throw new ArgumentOutOfRangeException(nameof(rate), "Signal dimensions exceed the bounded acceptance corpus.");
        using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var writer = new BinaryWriter(file, System.Text.Encoding.ASCII, false);
        var bytes = checked(rate * channels * seconds * 2);
        writer.Write("RIFF"u8); writer.Write(bytes + 36); writer.Write("WAVEfmt "u8); writer.Write(16);
        writer.Write((short)1); writer.Write((short)channels); writer.Write(rate); writer.Write(rate * channels * 2);
        writer.Write((short)(channels * 2)); writer.Write((short)16); writer.Write("data"u8); writer.Write(bytes);
        for (var frame = 0; frame < rate * seconds; frame++) for (var channel = 0; channel < channels; channel++) writer.Write(reference ? ReferenceSample(frame) : Sample(frame, channel, rate, frequency));
    }
}
