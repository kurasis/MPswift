namespace Player.App.Services.Audio;

/// <summary>Owner-thread endpoint index cache, invalidated by process-wide notifications or a failed identity check.</summary>
internal sealed class DeviceSelectionCache(Func<int> resolve)
{
    private int _index = -1;
    private long _revision = long.MinValue;
    internal long Resolutions { get; private set; }
    public int Resolve(long revision)
    {
        if (_index < 0 || _revision != revision)
        { var index = resolve(); _index = index; _revision = revision; Resolutions++; }
        return _index;
    }
    public void Invalidate() { _index = -1; _revision = long.MinValue; }
}
