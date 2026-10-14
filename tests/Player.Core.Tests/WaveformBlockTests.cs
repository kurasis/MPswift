using Player.Core.Waveforms;

namespace Player.Core.Tests;

public sealed class WaveformBlockTests
{
    [Theory]
    [InlineData(44100, 1)]
    [InlineData(48000, 2)]
    [InlineData(96000, 8)]
    public void BlocksAndPartialBucketsMatchOriginalPerFrameCalculationExactly(int rate, int channels)
    {
        const int frames = 10003;
        var random = new Random(13); var samples = Enumerable.Range(0, frames * channels).Select(i => i % 9 == 0 ? 0f : (float)(random.NextDouble() * 8 - 4)).ToArray();
        var accumulator = new WaveformAccumulator(rate, channels, frames); var offset = 0;
        while (offset < samples.Length)
        {
            var count = Math.Min(samples.Length - offset, random.Next(1, 900) * channels);
            accumulator.Add(samples.AsSpan(offset, count)); offset += count;
        }
        var bucketFrames = rate / 100; var countBuckets = (frames - 1) / bucketFrames + 1;
        var minimum = Enumerable.Repeat(float.PositiveInfinity, countBuckets).ToArray();
        var maximum = Enumerable.Repeat(float.NegativeInfinity, countBuckets).ToArray(); var energy = new double[countBuckets];
        for (var frame = 0; frame < frames; frame++)
            for (var channel = 0; channel < channels; channel++)
            {
                var bucket = frame / bucketFrames; var value = samples[frame * channels + channel];
                minimum[bucket] = Math.Min(minimum[bucket], value); maximum[bucket] = Math.Max(maximum[bucket], value); energy[bucket] += (double)value * value;
            }
        var rms = energy.Select((value, bucket) => (float)Math.Sqrt(value / (Math.Min(bucketFrames, frames - bucket * bucketFrames) * channels))).ToArray();
        var data = accumulator.Complete(); Assert.Equal(frames, data.TotalFrames);
        Assert.Equal(minimum, data.Minimum); Assert.Equal(maximum, data.Maximum); Assert.Equal(rms, data.Rms);
    }
    [Fact]
    public void InvalidSamplesIncompleteFramesAndExcessBucketsRemainRejected()
    {
        foreach (var value in new[] { float.NaN, float.PositiveInfinity, -16.01f, 16.01f })
            Assert.Throws<InvalidDataException>(() => new WaveformAccumulator(1000, 2, 10).Add([0, value]));
        Assert.Throws<InvalidDataException>(() => new WaveformAccumulator(1000, 2, 10).Add([0]));
        Assert.Throws<InvalidDataException>(() => new WaveformAccumulator(1000, 1, 10).Add(new float[11]));
    }
}
