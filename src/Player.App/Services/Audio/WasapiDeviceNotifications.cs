using ManagedBass.Wasapi;

namespace Player.App.Services.Audio;

/// <summary>BASSWASAPI has one process-wide callback; leases prevent auxiliary models from replacing/removing it.</summary>
internal sealed class WasapiDeviceNotifications : IDisposable
{
    private static readonly object Gate = new();
    private static readonly WasapiNotifyProcedure Callback = (_, _, _) => Interlocked.Increment(ref _revision);
    private static long _revision;
    private static int _leases;
    private static bool _available;
    private bool _disposed;
    internal static long Revision => Interlocked.Read(ref _revision);
    internal static int ActiveLeases { get { lock (Gate) return _leases; } }
    internal bool Available { get; }
    internal WasapiDeviceNotifications()
    {
        lock (Gate)
        {
            if (_leases == 0) { _available = BassWasapi.SetNotify(Callback); Interlocked.Increment(ref _revision); }
            _leases++; Available = _available;
        }
    }
    public void Dispose()
    {
        lock (Gate)
        {
            if (_disposed) return; _disposed = true;
            if (--_leases == 0)
            {
                if (_available) BassWasapi.SetNotify(null!);
                _available = false; Interlocked.Increment(ref _revision);
            }
        }
        // Callback is statically rooted even if native unregistration fails.
        GC.KeepAlive(Callback);
    }
}
