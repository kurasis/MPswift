using System.Globalization;

namespace Player.Core.Playback;

public enum ReplayGainMode { Off, Track, Album }
public sealed record ReplayGainTags(double? TrackDb = null, double? AlbumDb = null, double? TrackPeak = null, double? AlbumPeak = null)
{
    public static double? Parse(string? value, bool peak = false)
    {
        value = value?.Trim(); if (value?.EndsWith("dB", StringComparison.OrdinalIgnoreCase) == true) value = value[..^2].Trim();
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) && double.IsFinite(n) && (peak ? n is > 0 and <= 16 : n is >= -60 and <= 30) ? n : null;
    }
    public double Gain(ReplayGainMode mode)
    {
        if (mode == ReplayGainMode.Off) return 1;
        var db = mode == ReplayGainMode.Album ? AlbumDb : TrackDb;
        var peak = mode == ReplayGainMode.Album ? AlbumPeak : TrackPeak;
        var gain = Math.Pow(10, Math.Clamp(db ?? 0, -60, 30) / 20);
        return peak is > 0 ? Math.Min(gain, 1 / peak.Value) : gain;
    }
}
public sealed record AudioProcessingSettings(bool EqualizerEnabled = false, double PreampDb = 0, double[]? Bands = null,
    ReplayGainMode ReplayGain = ReplayGainMode.Off, double CrossfadeSeconds = 0)
{
    public static readonly int[] Centers = [31, 62, 125, 250, 500, 1000, 2000, 4000, 8000, 16000];
    public AudioProcessingSettings Validate()
    {
        if (!Enum.IsDefined(ReplayGain) || !double.IsFinite(PreampDb) || !double.IsFinite(CrossfadeSeconds) || Bands?.Length is not (null or 10) || Bands?.Any(b => !double.IsFinite(b)) == true)
            throw new InvalidDataException("Invalid audio processing settings.");
        return this with { PreampDb = Math.Clamp(PreampDb, -24, 12), CrossfadeSeconds = Math.Clamp(CrossfadeSeconds, 0, 10), Bands = Bands?.Select(b => Math.Clamp(b, -12, 12)).ToArray() ?? new double[10] };
    }
    public static double Overlap(double requested, TimeSpan current, TimeSpan next, bool contiguousCue, bool repeatOne) =>
        contiguousCue || repeatOne ? 0 : Math.Clamp(requested, 0, Math.Min(10, Math.Min(current.TotalSeconds, next.TotalSeconds) / 2));
}

/// <summary>Callback-owned biquad state; precomputed coefficients are swapped outside the render callback.</summary>
public sealed class PcmProcessor
{
    private readonly Filter[] _filters;
    private readonly double _preamp;
    private readonly bool _enabled;
    private readonly int _channels;
    private PcmProcessor? _previous;
    private int _smoothFrames;
    private readonly int _smoothTotal;
    public long ProtectedSamples { get; private set; }
    public PcmProcessor(int rate, int channels, AudioProcessingSettings settings, PcmProcessor? previous = null)
    {
        if (rate < 1000 || channels is < 1 or > 64) throw new ArgumentOutOfRangeException(nameof(rate));
        settings = settings.Validate(); _channels = channels; _enabled = settings.EqualizerEnabled;
        _previous = previous; _smoothTotal = Math.Max(1, rate / 50); _smoothFrames = previous is null ? _smoothTotal : 0;
        // Conservative EQ headroom; final saturation covers untagged overrange sources/overlap.
        var headroom = _enabled ? settings.Bands!.Where(b => b > 0).Sum() : 0;
        _preamp = _enabled ? Math.Pow(10, (settings.PreampDb - headroom) / 20) : 1;
        _filters = AudioProcessingSettings.Centers.Select((f, i) => new Filter(rate, channels, f, settings.Bands![i])).ToArray();
    }
    public void Process(Span<float> samples, float gainStart = 1, float gainEnd = 1)
    {
        var frames = samples.Length / _channels;
        for (var frame = 0; frame < frames; frame++)
        {
            var gain = gainStart + (gainEnd - gainStart) * (frame + 1f) / Math.Max(1, frames);
            for (var c = 0; c < _channels; c++)
            {
                var index = frame * _channels + c; double value = samples[index];
                var input = value; value = FilterSample(value, c);
                if (_previous is { } previous) value = previous.FilterSample(input, c) * (1 - (double)_smoothFrames / _smoothTotal) + value * _smoothFrames / _smoothTotal;
                value *= gain;
                if (!double.IsFinite(value)) { value = 0; ProtectedSamples++; }
                else if (value is > 1 or < -1) { value = Math.Clamp(value, -1, 1); ProtectedSamples++; }
                samples[index] = (float)value;
            }
            if (_previous is not null && ++_smoothFrames >= _smoothTotal) _previous = null;
        }
    }
    private double FilterSample(double value, int channel)
    { if (!_enabled) return value; value *= _preamp; foreach (var filter in _filters) value = filter.Apply(value, channel); return value; }
    private sealed class Filter
    {
        private readonly double b0 = 1, b1, b2, a1, a2;
        private readonly double[] z1, z2;
        public Filter(int rate, int channels, double frequency, double db)
        {
            z1 = new double[channels]; z2 = new double[channels]; if (frequency >= rate * 0.49 || db == 0) return;
            var a = Math.Pow(10, db / 40); var w = 2 * Math.PI * frequency / rate; var alpha = Math.Sin(w) / 2; var denominator = 1 + alpha / a;
            b0 = (1 + alpha * a) / denominator; b1 = -2 * Math.Cos(w) / denominator; b2 = (1 - alpha * a) / denominator; a1 = b1; a2 = (1 - alpha / a) / denominator;
        }
        public double Apply(double x, int c) { var y = b0 * x + z1[c]; z1[c] = b1 * x - a1 * y + z2[c]; z2[c] = b2 * x - a2 * y; return y; }
    }
}

public sealed record AudioDevice(string? Id, string Name, int SampleRate, int Channels);
public sealed record AudioOutputSettings(string? DeviceId = null, bool Exclusive = false)
{
    public bool CanReuse(string selectedDeviceId, string? actualDeviceId, AudioFormatInfo? actual, AudioFormatInfo source) =>
        actual is not null && actualDeviceId == selectedDeviceId &&
        (!Exclusive || actual.SampleRate == source.SampleRate && actual.Channels == Math.Min(2, source.Channels));
}
public interface IAdvancedAudioPlayer
{
    Task<bool> SetProcessingAsync(AudioProcessingSettings settings);
    Task<bool> SetOutputAsync(AudioOutputSettings settings);
    Task<AudioDevice[]> GetDevicesAsync();
    Task<bool> PrepareNextAsync(AudioRequest? request, bool repeatOne = false);
}
public interface IAdvancedAudioBackend
{
    AudioSourceInfo Open(AudioRequest request);
    void SetProcessing(AudioProcessingSettings settings);
    void SetOutput(AudioOutputSettings settings);
    AudioDevice[] GetDevices();
    void PrepareNext(AudioRequest? request, bool repeatOne);
}
