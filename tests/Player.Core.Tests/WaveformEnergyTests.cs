using Player.Core.Media;
using Player.Core.Waveforms;
using Player.App.Services.Storage;

namespace Player.Core.Tests;

public sealed class WaveformEnergyTests
{
    [Fact]
    public void TransientPeakDoesNotBecomeFullHeightEnergy()
    {
        var pcm = new float[480]; pcm[0] = 1;
        var accumulator = new WaveformAccumulator(48000, 1, pcm.Length);
        accumulator.Add(pcm); var data = accumulator.Complete();
        Assert.Equal(1, data.Maximum.Single());
        Assert.Equal((float)Math.Sqrt(1.0 / 480), data.Rms!.Single(), 6);
        Assert.Equal(data.Rms!.Single(), WaveformProjection.Create(data, 1).Single().Rms);
    }

    [Fact]
    public void OppositePhaseAndUnequalChannelsKeepMeanSquareEnergyAcrossChunks()
    {
        var accumulator = new WaveformAccumulator(48000, 2, 480);
        var pcm = Enumerable.Range(0, 960).Select(i => i % 2 == 0 ? .75f : -.25f).ToArray();
        accumulator.Add(pcm.AsSpan(0, 198)); accumulator.Add(pcm.AsSpan(198));
        var data = accumulator.Complete();
        Assert.Equal((float)Math.Sqrt((.75 * .75 + .25 * .25) / 2), data.Rms!.Single(), 6);
        Assert.Equal(-.25f, data.Minimum.Single()); Assert.Equal(.75f, data.Maximum.Single());
    }

    [Fact]
    public void FinalPartialBucketAndSilenceUseActualFrameCounts()
    {
        var accumulator = new WaveformAccumulator(1000, 1, 13);
        accumulator.Add(new float[10]); accumulator.Add([.5f, .5f, .5f]);
        var data = accumulator.Complete();
        Assert.Equal(new[] { 0f, .5f }, data.Rms);
        Assert.Equal((float)Math.Sqrt(.25 * 3 / 13), WaveformProjection.Create(data, 1).Single().Rms, 6);
    }

    [Fact]
    public void ProjectionWeightsTimeInsteadOfAveragingBucketsOrPickingMaxima()
    {
        var data = new WaveformData(1000, 1, 10, 11, [-1, -1], [1, 1], [.1f, 1f]);
        Assert.Equal((float)Math.Sqrt((.01 * 10 + 1) / 11), WaveformProjection.Create(data, 1).Single().Rms, 6);
        Assert.Equal(2, WaveformProjection.Create(data, 4096).Length);
    }

    [Fact]
    public void MisalignedCueSlicePreservesEnergyAndConservativePeaks()
    {
        var data = new WaveformData(1000, 1, 10, 20, [-.2f, -.8f], [.2f, .8f], [.2f, .8f]);
        var sliced = data.Slice(new TrackSegment(TimeSpan.FromMilliseconds(5), TimeSpan.FromMilliseconds(15)));
        Assert.Equal((float)Math.Sqrt((.04 + .64) / 2), sliced.Rms!.Single(), 6);
        Assert.Equal(-.8f, sliced.Minimum.Single()); Assert.Equal(.8f, sliced.Maximum.Single()); sliced.Validate();
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(-.1f)]
    [InlineData(.9f)]
    public void InvalidEnergyIsRejected(float rms) => Assert.Throws<InvalidDataException>(() =>
        new WaveformData(1000, 1, 10, 10, [-.5f], [.5f], [rms]).Validate());

    [Fact]
    public void EnergyCacheRoundTripAndOldFormatRefusal()
    {
        var directory = Path.Combine(Path.GetTempPath(), "energy-cache-" + Guid.NewGuid().ToString("N"));
        try
        {
            var cache = new WaveformCache(directory); var key = new string('c', 64);
            var data = new WaveformData(48000, 2, 480, 480, [-1], [1], [.2f]);
            cache.Write(key, data); Assert.Equal(data.Rms, cache.Read(key)!.Rms);
            var path = Path.Combine(directory, key + ".peaks"); var bytes = File.ReadAllBytes(path);
            BitConverter.GetBytes(1).CopyTo(bytes, 4); File.WriteAllBytes(path, bytes);
            Assert.Null(cache.Read(key));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
