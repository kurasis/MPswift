using System.Collections.Concurrent;
using ManagedBass;

namespace Player.App.Services.Audio;

/// <summary>Keep source pins/callback delegates alive without changing the public integer stream-handle API.</summary>
internal static class NativeStreamPins
{
    private sealed class Entry(SourceReadPins pins)
    {
        public SourceReadPins Pins { get; } = pins;
        public SyncProcedure? Callback { get; set; }
    }
    private static readonly ConcurrentDictionary<int, Entry> Active = new();
    internal static int Count => Active.Count;
    public static void Attach(int handle, SourceReadPins pins)
    {
        var entry = new Entry(pins);
        entry.Callback = (_, channel, _, _) =>
        {
            // Callback-driven WV inputs keep their delegates until the native file callbacks close.
            if ((!pins.UsesCallbacks || pins.InputsClosed) && Active.TryRemove(channel, out var removed)) removed.Pins.Dispose();
        };
        if (!Active.TryAdd(handle, entry)) throw new InvalidOperationException("A native stream already owns source pins.");
        if (Bass.ChannelSetSync(handle, SyncFlags.Free, 0, entry.Callback, 0) != 0) return;
        Active.TryRemove(handle, out _);
        throw new InvalidOperationException("Cannot retain source lifetime: " + Bass.LastError);
    }
    public static bool Free(int handle)
    {
        Active.TryGetValue(handle, out var entry);
        var released = Bass.StreamFree(handle);
        if (released && Active.TryRemove(handle, out var remaining)) remaining.Pins.Dispose();
        GC.KeepAlive(entry); // File callbacks may run before StreamFree returns, even after a FREE sync.
        return released;
    }
    public static void ReleaseAfterEngineFree()
    {
        foreach (var pair in Active)
            if (Active.TryRemove(pair.Key, out var entry)) entry.Pins.Dispose();
    }
}
