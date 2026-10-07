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
        public int FreeNotified;
        public Exception? CleanupFailure;
    }
    private static readonly ConcurrentDictionary<int, Entry> Active = new();
    internal static int Count => Active.Count;
    public static void Attach(int handle, SourceReadPins pins)
    {
        var entry = new Entry(pins);
        void Complete()
        {
            if (Volatile.Read(ref entry.FreeNotified) == 0 || pins.UsesCallbacks && !pins.InputsClosed) return;
            if (!Active.TryRemove(new KeyValuePair<int, Entry>(handle, entry))) return;
            try { pins.Dispose(); } catch (Exception error) { entry.CleanupFailure = error; }
        }
        pins.FileClosed = Complete;
        entry.Callback = (_, _, _, _) => { Volatile.Write(ref entry.FreeNotified, 1); Complete(); };
        if (!Active.TryAdd(handle, entry)) throw new InvalidOperationException("A native stream already owns source pins.");
        if (Bass.ChannelSetSync(handle, SyncFlags.Free, 0, entry.Callback, 0) != 0) return;
        Active.TryRemove(new KeyValuePair<int, Entry>(handle, entry)); pins.FileClosed = null;
        throw new InvalidOperationException("Cannot retain source lifetime: " + Bass.LastError);
    }
    public static bool Free(int handle)
    {
        Active.TryGetValue(handle, out var entry);
        var released = Bass.StreamFree(handle);
        // A FREE callback can remove this entry before a concurrent decoder reuses the integer handle.
        if (released && entry is not null && Active.TryRemove(new KeyValuePair<int, Entry>(handle, entry))) entry.Pins.Dispose();
        GC.KeepAlive(entry); // File callbacks may run before StreamFree returns, even after a FREE sync.
        if (entry?.CleanupFailure is { } failure) throw new System.IO.IOException("Source pin cleanup failed.", failure);
        return released;
    }
    public static void ReleaseAfterEngineFree()
    {
        foreach (var pair in Active)
            if (Active.TryRemove(pair)) pair.Value.Pins.Dispose();
    }
}
