using Player.Core.Media;

namespace Player.Core.Tests;

public sealed class TrackSegmentTests
{
    [Fact]
    public void SeekIsRelativeToLogicalSegmentAndClamped()
    {
        var segment = new TrackSegment(TimeSpan.FromHours(2), TimeSpan.FromHours(3));
        Assert.Equal(TimeSpan.Zero, segment.ClampRelativeSeek(TimeSpan.FromSeconds(-1)));
        Assert.Equal(TimeSpan.FromHours(3), segment.ToSourcePosition(TimeSpan.FromHours(10)));
        Assert.Equal(TimeSpan.FromHours(2) + TimeSpan.FromSeconds(5), segment.ToSourcePosition(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void UnknownDurationCannotBeUsedAsSeekRange()
    {
        var segment = new TrackSegment(TimeSpan.Zero, null);
        Assert.Null(segment.Duration);
        Assert.Throws<InvalidOperationException>(() => segment.ClampRelativeSeek(TimeSpan.Zero));
    }

    [Fact]
    public void ZeroDurationDoesNotDivideByZero() =>
        Assert.Equal(TimeSpan.Zero, new TrackSegment(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5)).ClampRelativeSeek(TimeSpan.MaxValue));

    [Fact]
    public void RejectsNegativeAndDecreasingBounds()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TrackSegment(TimeSpan.FromTicks(-1), null));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TrackSegment(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void CueFrameConversionIsExactAtWholeSecondsAndDoesNotDrift()
    {
        Assert.Equal(TimeSpan.FromHours(100), TrackSegment.FromCueFrames(100L * 3600 * 75));
        Assert.Equal(TimeSpan.FromTicks(133333), TrackSegment.FromCueFrames(1));
        Assert.Equal(TimeSpan.FromSeconds(1), TrackSegment.FromCueFrames(75));
        Assert.Throws<ArgumentOutOfRangeException>(() => TrackSegment.FromCueFrames(-1));
        Assert.Throws<OverflowException>(() => TrackSegment.FromCueFrames(long.MaxValue));
    }
}
