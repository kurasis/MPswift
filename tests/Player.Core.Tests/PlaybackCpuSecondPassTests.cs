using Player.App.Services.Audio;
using Player.Core.Library;
using Player.Core.Playback;

namespace Player.Core.Tests;

public sealed class PlaybackCpuSecondPassTests
{
    [Theory]
    [InlineData(1, 1f, 1f)]
    [InlineData(2, .37f, .37f)]
    [InlineData(8, 0f, 0f)]
    [InlineData(3, -0f, -0f)]
    [InlineData(2, -2f, -2f)]
    [InlineData(2, .2f, .8f)]
    [InlineData(2, float.PositiveInfinity, float.PositiveInfinity)]
    public void BypassAndFallbackMatchLegacyBitsProtectionAndIncompleteFramePolicy(int channels, float start, float end)
    {
        var random = new Random(7);
        var original = Enumerable.Range(0, 4096 * channels + 1).Select(_ => (float)(random.NextDouble() * 6 - 3)).ToArray();
        new float[] { -0f, float.Epsilon, -float.Epsilon, float.MaxValue, -float.MaxValue, float.NaN, float.PositiveInfinity, float.NegativeInfinity }.CopyTo(original, 0);
        var expected = original.ToArray(); long protectedSamples = 0; var frames = expected.Length / channels;
        for (var frame = 0; frame < frames; frame++)
        {
            var gain = start + (end - start) * (frame + 1f) / Math.Max(1, frames);
            for (var channel = 0; channel < channels; channel++)
            {
                var index = frame * channels + channel; double value = expected[index]; value *= gain;
                if (!double.IsFinite(value)) { value = 0; protectedSamples++; }
                else if (value is > 1 or < -1) { value = Math.Clamp(value, -1, 1); protectedSamples++; }
                expected[index] = (float)value;
            }
        }
        var processor = new PcmProcessor(48000, channels, new()); var actual = original.ToArray(); processor.Process(actual, start, end);
        Assert.Equal(expected.Select(BitConverter.SingleToInt32Bits), actual.Select(BitConverter.SingleToInt32Bits));
        Assert.Equal(protectedSamples, processor.ProtectedSamples);
    }
    [Fact]
    public void DeviceCacheInvalidatesOnNotificationsExplicitRetryAndFailedResolution()
    {
        var calls = 0; var selected = 2; var fail = false;
        var cache = new DeviceSelectionCache(() => { calls++; if (fail) throw new IOException("Unavailable"); return selected; });
        for (var i = 0; i < 10000; i++) Assert.Equal(2, cache.Resolve(1));
        Assert.Equal(1, calls); selected = 4; Assert.Equal(4, cache.Resolve(2)); Assert.Equal(2, calls);
        cache.Invalidate(); Assert.Equal(4, cache.Resolve(2)); Assert.Equal(3, calls);
        fail = true; Assert.Throws<IOException>(() => cache.Resolve(3)); Assert.Throws<IOException>(() => cache.Resolve(3));
        fail = false; selected = 6; Assert.Equal(6, cache.Resolve(3)); Assert.Equal(6, calls);
    }
    private static PlaybackSnapshot Position(long revision, double seconds) => PlaybackSnapshot.Empty with
    { Revision = revision, Generation = 1, State = PlaybackState.Playing, EntryId = Guid.Parse("36bcd60a-7f59-4789-8ac0-67712f21edc0"), Position = TimeSpan.FromSeconds(seconds), Duration = TimeSpan.FromMinutes(3) };
    [Fact]
    public void BackloggedPositionUpdatesUseOneDrainButListeningSeesEveryReceipt()
    {
        var queue = new PlaybackSnapshotQueue(); var meter = new ListeningMeter(); var track = Guid.NewGuid(); var events = new List<ListeningEvent>();
        var scheduled = 0;
        for (var i = 0; i < 1000; i++)
        {
            var snapshot = Position(i + 1, i * .1);
            if (queue.Enqueue(snapshot)) scheduled++;
            if (meter.Update(snapshot, track, TimeSpan.FromSeconds(i * .1), DateTime.UnixEpoch.AddSeconds(i * .1)) is { } listening) events.Add(listening);
        }
        Assert.Equal(1, scheduled); Assert.Single(queue.Drain()); Assert.Single(events); Assert.Equal(TimeSpan.FromSeconds(90).Ticks, events[0].ListenedTicks);
        Assert.True(queue.Enqueue(Position(1001, 100))); Assert.Single(queue.Drain());
    }
    [Theory]
    [InlineData("state")][InlineData("generation")][InlineData("entry")][InlineData("duration")][InlineData("format")]
    [InlineData("output")][InlineData("seek")][InlineData("volume")][InlineData("mute")][InlineData("error")]
    [InlineData("ended")][InlineData("transition")][InlineData("backward")][InlineData("jump")]
    public void CriticalUpdatesAndSeekBoundariesAreNeverMerged(string change)
    {
        var first = Position(1, 2); var next = Position(2, 2.1);
        next = change switch
        {
            "state" => next with { State = PlaybackState.Paused }, "generation" => next with { Generation = 2 },
            "entry" => next with { EntryId = Guid.NewGuid() }, "duration" => next with { Duration = TimeSpan.FromMinutes(4) },
            "format" => next with { SourceFormat = new(48000, 2, "FLAC") }, "output" => next with { OutputFormat = new(48000, 2, "WASAPI") },
            "seek" => next with { CanSeek = true }, "volume" => next with { Volume = .2 }, "mute" => next with { Muted = true },
            "error" => next with { Error = new(AudioErrorCategory.OutputUnavailable, "ErrorOutputUnavailable", "Owned error") },
            "ended" => next with { Ended = true }, "transition" => next with { Transitioned = true },
            "backward" => next with { Position = TimeSpan.FromSeconds(1) }, "jump" => next with { Position = TimeSpan.FromSeconds(20) }, _ => throw new Exception()
        };
        var queue = new PlaybackSnapshotQueue(); Assert.True(queue.Enqueue(first)); Assert.False(queue.Enqueue(next));
        Assert.Equal([first, next], queue.Drain()); queue.Clear(); Assert.True(queue.Enqueue(next));
    }
}
