using System.Runtime.InteropServices;
using Player.App.Services.Storage;
using Player.Core.Waveforms;

namespace Player.AudioSmoke;

internal static class CacheFileSecurityValidation
{
    private static readonly WaveformData Data = new(48000, 1, 480, 480, [-0.2f], [0.2f]);
    private static string Root => Path.Combine(Environment.CurrentDirectory, "cache-file-control");
    private static string Key(int i) => new((char)('a' + i), 64);
    private static string Target(int i) => Path.Combine(Root, "outside-" + i + ".peaks");
    private static string Entry(int i) => Path.Combine(Root, "cache", Key(i) + ".peaks");

    public static void PrepareLinks()
    {
        var cache = new WaveformCache(Path.Combine(Root, "cache"));
        for (var i = 0; i < 3; i++)
        {
            cache.Write(Key(i), Data); File.Move(Entry(i), Target(i));
            File.SetLastWriteTimeUtc(Target(i), new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc));
        }
        File.CreateSymbolicLink(Entry(0), Target(0));
        if (!CreateHardLink(Entry(1), Target(1), 0)) throw new IOException("Cannot create owned cache hardlink control.");
        File.CreateSymbolicLink(Entry(2), Target(2));
    }

    public static object Run()
    {
        var cache = new WaveformCache(Path.Combine(Root, "cache"));
        for (var i = 0; i < 2; i++)
        {
            var bytes = File.ReadAllBytes(Target(i)); var time = File.GetLastWriteTimeUtc(Target(i));
            // Positive control executes the former path-based touch on owned files, then restores their timestamp.
            File.SetLastWriteTimeUtc(Entry(i), time.AddHours(1));
            if (File.GetLastWriteTimeUtc(Target(i)) != time.AddHours(1)) throw new InvalidOperationException("Legacy cache touch control did not follow the linked file.");
            File.SetLastWriteTimeUtc(Target(i), time);
            if (cache.Read(Key(i)) is not null) throw new InvalidOperationException("A linked cache entry was trusted.");
            CheckTarget();
            cache.Write(Key(i), Data);
            if (new FileInfo(Entry(i)).LinkTarget is not null || cache.Read(Key(i)) is null) throw new InvalidOperationException("Cache link regeneration failed.");
            CheckTarget();
            using (File.Open(Entry(i), FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
            void CheckTarget()
            {
                if (!File.ReadAllBytes(Target(i)).AsSpan().SequenceEqual(bytes) || File.GetLastWriteTimeUtc(Target(i)) != time)
                    throw new InvalidOperationException("Cache access changed unrelated file bytes or timestamp.");
            }
        }
        var clearBytes = File.ReadAllBytes(Target(2)); var clearTime = File.GetLastWriteTimeUtc(Target(2));
        cache.Write(Key(3), Data);
        var old = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(Entry(3), old);
        if (cache.Read(Key(3)) is null || File.GetLastWriteTimeUtc(Entry(3)) <= old) throw new InvalidOperationException("Handle-based LRU touch failed.");
        cache.Clear();
        if (cache.GetUsage().Files != 0 || !File.Exists(Target(0)) || !File.Exists(Target(1)) ||
            !File.ReadAllBytes(Target(2)).AsSpan().SequenceEqual(clearBytes) || File.GetLastWriteTimeUtc(Target(2)) != clearTime)
            throw new InvalidOperationException("Cache clearing changed unrelated targets.");
        return new { SymbolicCacheLinkIgnored = true, HardlinkedCacheIgnored = true, OutsideBytesAndTimestampsPreserved = true,
            RegenerationReplacesEntryOnly = true, HandleBasedLruTouch = true, LegacyPathTouchPositiveControl = true, ReleasedReadHandles = true, LinkedEntryClearPreservesTarget = true };
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateHardLinkW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLink(string link, string target, nint security);
}
