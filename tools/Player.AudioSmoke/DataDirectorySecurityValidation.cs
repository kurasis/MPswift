using Player.App.Services.Storage;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Player.Core.Library;
using Player.Core.Waveforms;

namespace Player.AudioSmoke;

internal static class DataDirectorySecurityValidation
{
    public static void PrepareLinks()
    {
        var parent = Path.Combine(Environment.CurrentDirectory, "data-directory-control");
        Directory.CreateDirectory(Path.Combine(parent, "actual", "nested"));
        Directory.CreateSymbolicLink(Path.Combine(parent, "alias"), Path.Combine(parent, "actual"));
    }
    public static object Run()
    {
        var parent = Path.Combine(Environment.CurrentDirectory, "data-directory-control");
        var actual = Path.Combine(parent, "actual"); var alias = Path.Combine(parent, "alias");
        var settings = new SettingsFile(Path.Combine(alias, "nested"));
        settings.Save(new PlayerSettings(Volume: 19)); settings.Save(new PlayerSettings(Volume: 31));
        if (settings.Load().Volume != 31 || settings.LoadBackup().Volume != 19) throw new InvalidOperationException("Linked settings round trip failed.");
        static void MustFail(Action operation)
        {
            try { operation(); } catch (IOException) { return; }
            throw new InvalidOperationException("Pinned data directory replacement was permitted.");
        }
        using (OpenReparseForWrite(alias)) { }
        using (var lease = DataDirectoryLease.Open(Path.Combine(alias, "nested")))
        {
            if (!string.Equals(lease.DirectoryPath, Path.Combine(actual, "nested"), StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Data link did not resolve to its local directory.");
            MustFail(() => Directory.Move(actual, actual + "-moved"));
            MustFail(() => Directory.Move(parent, parent + "-moved"));
            MustFail(() => Directory.Delete(alias));
            MustFail(() => { using var write = OpenReparseForWrite(alias); });
            settings.Save(new PlayerSettings(Volume: 47));
            var cache = new WaveformCache(Path.Combine(alias, "nested", "cache"));
            var key = new string('a', 64); cache.Write(key, new WaveformData(48000, 1, 480, 480, [-0.2f], [0.2f]));
            if (cache.Read(key) is null) throw new InvalidOperationException("Linked waveform cache did not round trip.");
            cache.Clear(); if (cache.GetUsage().Files != 0) throw new InvalidOperationException("Linked waveform cache did not clear.");
        }
        // Positive controls prove denial was caused by the lease, rather than an ACL or inaccessible test path.
        using (OpenReparseForWrite(alias)) { }
        Directory.Delete(alias); Directory.Move(actual, actual + "-moved"); Directory.Move(actual + "-moved", actual);
        Directory.Move(parent, parent + "-moved"); Directory.Move(parent + "-moved", parent);
        if (new SettingsFile(Path.Combine(actual, "nested")).Load().Volume != 47) throw new InvalidOperationException("Settings lost after released-directory controls.");
        var stage = Path.Combine(actual, "stage-clean");
        var stageLease = DataDirectoryLease.Create(stage);
        File.WriteAllText(Path.Combine(stage, "library.db"), "generated database");
        File.WriteAllText(Path.Combine(stage, "library.db.owner.lock"), "generated ownership file");
        MustFail(() => Directory.Move(stage, stage + "-moved"));
        BackupStageCleanup.Clean(stage, stageLease);
        if (Directory.Exists(stage)) throw new InvalidOperationException("Flat staging directory was not removed.");

        var unexpected = Path.Combine(actual, "stage-unexpected");
        var unexpectedLease = DataDirectoryLease.Create(unexpected);
        File.WriteAllText(Path.Combine(unexpected, "library.db"), "generated database");
        var nested = Directory.CreateDirectory(Path.Combine(unexpected, "unexpected-album"));
        var sentinel = Path.Combine(nested.FullName, "keep.txt"); File.WriteAllText(sentinel, "owned sentinel");
        MustFail(() => BackupStageCleanup.Clean(unexpected, unexpectedLease));
        if (File.Exists(Path.Combine(unexpected, "library.db")) || File.ReadAllText(sentinel) != "owned sentinel")
            throw new InvalidOperationException("Staging cleanup traversed unexpected data or missed generated metadata.");
        Directory.Move(unexpected, unexpected + "-moved"); Directory.Move(unexpected + "-moved", unexpected);
        Directory.Move(parent, parent + "-moved"); Directory.Move(parent + "-moved", parent);
        return new { RequestedLinkPinned = true, ReparseWriteOpenBlocked = true, PhysicalDirectoryPinned = true, AncestorPinned = true, LocalDirectoryLinksSupported = true,
            SettingsAndCacheRoundTrip = true, ReplacementWorksAfterDispose = true, StoredDataPreserved = true,
            StagingDirectoryPinned = true, FlatStageRemoved = true, UnexpectedStageDataRetained = true, FailedCleanupHandlesReleased = true };
    }
    private static SafeFileHandle OpenReparseForWrite(string path)
    {
        var handle = CreateFile(path, 0x40000000, 7, 0, 3, 0x02200000, 0);
        if (handle.IsInvalid) { handle.Dispose(); throw new IOException("Cannot open owned reparse object for write."); }
        return handle; // Positive controls open without changing reparse data or file bytes.
    }
    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string path, uint access, uint share, nint security, uint creation, uint flags, nint template);
}
