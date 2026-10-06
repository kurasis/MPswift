namespace Player.Core.Waveforms;

public sealed record WaveformData(int SampleRate, int Channels, long FramesPerBucket, long TotalFrames, float[] Minimum, float[] Maximum, float[]? Rms = null)
{
    public const int MaximumBuckets = 300000;
    public double DurationSeconds => (double)TotalFrames / SampleRate;
    public WaveformData Slice(Player.Core.Media.TrackSegment segment)
    {
        Validate();
        var start = Math.Clamp((long)Math.Round(segment.Start.TotalSeconds * SampleRate), 0, TotalFrames);
        var end = Math.Clamp((long)Math.Round((segment.End?.TotalSeconds ?? DurationSeconds) * SampleRate), start, TotalFrames);
        if (end <= start) throw new InvalidDataException("Empty waveform segment.");
        var frames = end - start; var count = checked((int)((frames - 1) / FramesPerBucket + 1));
        var min = new float[count]; var max = new float[count];
        var rms = Rms is null ? null : new float[count];
        for (var i = 0; i < count; i++)
        {
            var first = (start + i * FramesPerBucket) / FramesPerBucket;
            var last = Math.Min((end - 1) / FramesPerBucket, (start + (i + 1) * FramesPerBucket - 1) / FramesPerBucket);
            min[i] = float.PositiveInfinity; max[i] = float.NegativeInfinity;
            double energy = 0; long weight = 0;
            for (var j = first; j <= last; j++)
            {
                min[i] = Math.Min(min[i], Minimum[j]); max[i] = Math.Max(max[i], Maximum[j]);
                if (Rms is null) continue;
                var overlap = Math.Min(Math.Min(end, start + (i + 1) * FramesPerBucket), (j + 1) * FramesPerBucket) - Math.Max(start + i * FramesPerBucket, j * FramesPerBucket);
                energy += Rms[j] * (double)Rms[j] * overlap; weight += overlap;
            }
            if (rms is not null) rms[i] = (float)Math.Sqrt(energy / weight);
        }
        return new(SampleRate, Channels, FramesPerBucket, frames, min, max, rms);
    }
    public void Validate()
    {
        if (SampleRate is < 1000 or > 768000 || Channels is < 1 or > 64 || FramesPerBucket < 1 || TotalFrames < 1 ||
            Minimum.Length is < 1 or > MaximumBuckets || Maximum.Length != Minimum.Length ||
            (TotalFrames - 1) / FramesPerBucket + 1 != Minimum.Length || Rms is not null && Rms.Length != Minimum.Length)
            throw new InvalidDataException("Invalid waveform dimensions.");
        for (var i = 0; i < Minimum.Length; i++)
            if (!float.IsFinite(Minimum[i]) || !float.IsFinite(Maximum[i]) || Minimum[i] > Maximum[i] || Minimum[i] < -16 || Maximum[i] > 16)
                throw new InvalidDataException("Invalid waveform peaks.");
        if (Rms is not null)
            for (var i = 0; i < Rms.Length; i++)
                if (!float.IsFinite(Rms[i]) || Rms[i] < 0 || Rms[i] > Math.Max(Math.Abs(Minimum[i]), Math.Abs(Maximum[i])) + 0.00001)
                    throw new InvalidDataException("Invalid waveform energy.");
    }
}

/// <summary>Extrema across all channels, never a signed sum. Memory does not grow with decoded PCM.</summary>
public sealed class WaveformAccumulator
{
    private readonly int _rate;
    private readonly int _channels;
    private readonly long _bucketFrames;
    private readonly float[] _minimum;
    private readonly float[] _maximum;
    private readonly double[] _energy;
    private long _frames;
    public long Frames => _frames;
    public WaveformAccumulator(int sampleRate, int channels, long expectedFrames)
    {
        if (sampleRate is < 1000 or > 768000 || channels is < 1 or > 64 || expectedFrames < 1 || expectedFrames > (long)sampleRate * 86400 * 365)
            throw new ArgumentOutOfRangeException(nameof(expectedFrames));
        _rate = sampleRate; _channels = channels;
        _bucketFrames = Math.Max(Math.Max(1, sampleRate / 100), (expectedFrames - 1) / WaveformData.MaximumBuckets + 1);
        var count = (int)((expectedFrames - 1) / _bucketFrames + 1);
        _minimum = new float[count]; _maximum = new float[count];
        _energy = new double[count];
        Array.Fill(_minimum, float.PositiveInfinity); Array.Fill(_maximum, float.NegativeInfinity);
    }
    public void Add(ReadOnlySpan<float> interleaved)
    {
        if (interleaved.Length % _channels != 0) throw new InvalidDataException("Incomplete PCM frame.");
        for (var frame = 0; frame < interleaved.Length / _channels; frame++)
        {
            var bucket = (int)(_frames / _bucketFrames);
            if (bucket >= _minimum.Length) throw new InvalidDataException("Decoder exceeded the validated waveform range.");
            for (var c = 0; c < _channels; c++)
            {
                var value = interleaved[frame * _channels + c];
                if (!float.IsFinite(value) || value is < -16 or > 16) throw new InvalidDataException("Invalid decoded PCM sample.");
                _minimum[bucket] = Math.Min(_minimum[bucket], value);
                _maximum[bucket] = Math.Max(_maximum[bucket], value);
                _energy[bucket] += (double)value * value;
            }
            _frames++;
        }
    }
    public WaveformData Complete()
    {
        if (_frames == 0) throw new InvalidDataException("No PCM samples decoded.");
        var count = checked((int)((_frames - 1) / _bucketFrames + 1));
        var rms = new float[count];
        for (var i = 0; i < count; i++)
            rms[i] = (float)Math.Sqrt(_energy[i] / (Math.Min(_bucketFrames, _frames - i * _bucketFrames) * _channels));
        var result = new WaveformData(_rate, _channels, _bucketFrames, _frames, _minimum[..count], _maximum[..count], rms);
        result.Validate(); return result;
    }
}

public interface IWaveformService : IAsyncDisposable
{
    Task<WaveformData> AnalyzeAsync(string path, IProgress<double>? progress, CancellationToken cancellationToken, bool refresh = false);
}
