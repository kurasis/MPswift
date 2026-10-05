namespace Player.Core.Playback;

public enum PlaybackState { Empty, Loading, Stopped, Playing, Paused, Seeking, DeviceUnavailable, Error }
public enum AudioErrorCategory { FileUnavailable, Decoder, Dependency, OutputUnavailable, Unexpected, Busy }
public sealed record AudioError(AudioErrorCategory Category, string ResourceKey, string Detail, int? NativeCode = null);
public sealed class AudioBackendException(AudioErrorCategory category, string detail, int? nativeCode = null)
    : Exception(detail)
{
    public AudioErrorCategory Category { get; } = category;
    public int? NativeCode { get; } = nativeCode;
}

public sealed record AudioFormatInfo(int SampleRate, int Channels, string Codec, int? BitDepth = null);
public sealed record AudioRequest(Guid EntryId, string Path, Player.Core.Media.TrackSegment? Segment = null, ReplayGainTags? ReplayGain = null);
public sealed record AudioSourceInfo(TimeSpan? Duration, AudioFormatInfo Format, bool CanSeek);
public sealed record BackendPosition(TimeSpan Position, bool Ended, AudioFormatInfo? OutputFormat = null, AudioRequest? Transition = null, AudioSourceInfo? TransitionInfo = null);
public sealed record PlaybackSnapshot(long Generation, long Revision, PlaybackState State, Guid? EntryId,
    TimeSpan Position, TimeSpan? Duration, AudioFormatInfo? SourceFormat, AudioFormatInfo? OutputFormat,
    bool CanSeek, double Volume, bool Muted, AudioError? Error = null, bool Ended = false, bool Transitioned = false)
{
    public static PlaybackSnapshot Empty { get; } = new(0, 0, PlaybackState.Empty, null, TimeSpan.Zero, null, null, null, false, 0.5, false);
}

public interface IAudioPlayer : IAsyncDisposable
{
    PlaybackSnapshot Snapshot { get; }
    event Action<PlaybackSnapshot>? SnapshotChanged;
    Task<bool> LoadAsync(AudioRequest request, bool autoPlay, CancellationToken cancellationToken = default);
    Task<bool> PlayAsync();
    Task<bool> PauseAsync();
    Task<bool> StopAsync();
    Task<bool> SeekAsync(TimeSpan position);
    Task<bool> SetVolumeAsync(double volume, bool muted);
}

/// <summary>All calls, including construction/disposal, belong to one engine thread.</summary>
public interface IAudioBackend : IDisposable
{
    AudioSourceInfo Open(string path);
    void Play();
    void Pause();
    void Stop();
    void Seek(TimeSpan position);
    void SetVolume(double volume, bool muted);
    BackendPosition ReadPosition();
    void CloseSource();
}
