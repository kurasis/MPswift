using Player.Core.Diagnostics;
using Player.Core.Playback;

namespace Player.Core.Tests;

public sealed class DigitalBoundaryTests
{
    private const int Rate = 8000, Offset = 313, Frames = Rate * 4, Boundary = Rate * 2;
    private static float[] Capture(int shift = 0, bool dropout = false)
    {
        var samples = new float[(Offset + Frames + Math.Max(0, shift) + 100) * 2];
        for (var frame = 0; frame < Frames; frame++)
        {
            var target = Offset + frame + (frame >= Boundary ? shift : 0);
            if (dropout && frame >= Boundary - 3 && frame <= Boundary + 3) continue;
            samples[target * 2] = samples[target * 2 + 1] = AcceptanceSignal.ReferenceSample(frame) / 32768f * .2f;
        }
        return samples;
    }
    [Fact] public void CapturedDelayAndConstantSystemGainAreCalibratedWithoutChangingTheBoundary()
    { var result = DigitalBoundaryAnalyzer.Analyze(Capture(), 2, Rate, Frames, Boundary); Assert.Equal("digital-boundary-passed", result.Status); Assert.Equal(Offset, result.AlignmentOffsetFrames); Assert.InRange(result.FittedGain, .19999, .20001); Assert.Equal(0, result.AfterBoundaryLagFrames); }
    [Theory] [InlineData(1)] [InlineData(-1)] [InlineData(160)]
    public void InsertedOrDroppedFramesFailIndependentBoundaryAlignment(int shift)
    { var result = DigitalBoundaryAnalyzer.Analyze(Capture(shift), 2, Rate, Frames, Boundary); Assert.Equal("digital-boundary-failed", result.Status); Assert.Equal(shift, result.AfterBoundaryLagFrames); }
    [Fact] public void DropoutAtTheBoundaryFailsEvenWhenSubsequentFramesKeepTheirClock()
    { Assert.Equal("digital-boundary-failed", DigitalBoundaryAnalyzer.Analyze(Capture(dropout: true), 2, Rate, Frames, Boundary).Status); }
    [Fact] public void SilenceAndTruncatedCaptureCannotPassWithoutPositiveControl()
    { Assert.Equal("failed-positive-control", DigitalBoundaryAnalyzer.Analyze(new float[Frames * 2], 2, Rate, Frames, Boundary).Status); Assert.Equal("failed-positive-control", DigitalBoundaryAnalyzer.Analyze(Capture().AsSpan(0, Rate * 2), 2, Rate, Frames, Boundary).Status); }
    [Fact] public void CorruptCaptureIsRejected()
    { var samples = Capture(); samples[0] = float.NaN; Assert.Throws<InvalidDataException>(() => DigitalBoundaryAnalyzer.Analyze(samples, 2, Rate, Frames, Boundary)); }
    [Fact] public void RightChannelDropoutFailsEvenWhenLeftChannelProvidesPerfectAlignment()
    {
        var samples = Capture();
        for (var frame = Boundary - 3; frame <= Boundary + 3; frame++) samples[(Offset + frame) * 2 + 1] = 0;
        Assert.Equal("digital-boundary-failed", DigitalBoundaryAnalyzer.Analyze(samples, 2, Rate, Frames, Boundary).Status);
    }
    [Fact] public void ExclusiveOutputMustReopenForNewSourceRateOrChannelLayout()
    {
        var actual = new AudioFormatInfo(48000, 2, "WASAPI"); var exclusive = new AudioOutputSettings(Exclusive: true);
        Assert.True(exclusive.CanReuse("device", "device", actual, new(48000, 6, "FLAC")));
        Assert.False(exclusive.CanReuse("device", "device", actual, new(44100, 2, "FLAC")));
        Assert.False(exclusive.CanReuse("device", "device", actual, new(48000, 1, "WAV")));
        Assert.False(exclusive.CanReuse("other", "device", actual, new(48000, 2, "FLAC")));
        Assert.False(exclusive.CanReuse("device", "device", null, new(48000, 2, "FLAC")));
        Assert.True(new AudioOutputSettings().CanReuse("device", "device", actual, new(44100, 1, "WAV")));
    }
}
