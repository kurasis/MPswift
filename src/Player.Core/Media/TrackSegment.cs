namespace Player.Core.Media;

/// <summary>Absolute source bounds; an absent end means source duration is not yet known.</summary>
public sealed record TrackSegment
{
    public TimeSpan Start { get; }
    public TimeSpan? End { get; }
    public TimeSpan? Duration => End - Start;

    public TrackSegment(TimeSpan start, TimeSpan? end)
    {
        if (start < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(start));
        if (end < start) throw new ArgumentOutOfRangeException(nameof(end));
        Start = start;
        End = end;
    }

    public TimeSpan ClampRelativeSeek(TimeSpan requested)
    {
        if (Duration is not { } duration)
            throw new InvalidOperationException("Seeking requires a validated logical duration.");
        return requested < TimeSpan.Zero ? TimeSpan.Zero : requested > duration ? duration : requested;
    }

    public TimeSpan ToSourcePosition(TimeSpan requested) => Start + ClampRelativeSeek(requested);

    /// <summary>Round once to nearest .NET tick, with ties upward; never accumulate frame deltas.</summary>
    public static TimeSpan FromCueFrames(long frames)
    {
        if (frames < 0) throw new ArgumentOutOfRangeException(nameof(frames));
        var seconds = Math.DivRem(frames, 75, out var remainder);
        var ticks = checked(seconds * TimeSpan.TicksPerSecond + (remainder * TimeSpan.TicksPerSecond + 37) / 75);
        return TimeSpan.FromTicks(ticks);
    }
}
