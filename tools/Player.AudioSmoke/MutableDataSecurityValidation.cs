using System.Runtime.InteropServices;
using System.Text;
using Player.App.Services.Storage;

namespace Player.AudioSmoke;

internal static class MutableDataSecurityValidation
{
    public static void PrepareLinks()
    {
        var root = Directory.CreateDirectory(Path.Combine(Environment.CurrentDirectory, "mutable-data-control")).FullName;
        var outside = Path.Combine(root, "outside.txt"); File.WriteAllText(outside, "owned outside sentinel");
        File.CreateSymbolicLink(Path.Combine(root, "symbolic.db"), outside);
        if (!CreateHardLink(Path.Combine(root, "hard.db"), outside, 0)) throw new IOException("Cannot create owned data hardlink control.");
    }
    public static object Run()
    {
        var root = Path.Combine(Environment.CurrentDirectory, "mutable-data-control");
        using var directory = DataDirectoryLease.Open(root);
        static void MustFail(Action action)
        { try { action(); } catch (IOException) { return; } throw new InvalidOperationException("Mutable data control was unexpectedly allowed."); }
        foreach (var name in new[] { "symbolic.db", "hard.db" })
            MustFail(() => { using var pin = DataFileLease.OpenExisting(Path.Combine(root, name), writableSharing: true); });
        if (File.ReadAllText(Path.Combine(root, "outside.txt")) != "owned outside sentinel") throw new InvalidOperationException("Mutable data refusal changed its target.");

        var regular = Path.Combine(root, "regular.db"); File.WriteAllText(regular, "owned regular bytes");
        using (var pin = DataFileLease.OpenExisting(regular, writableSharing: true))
        {
            using (File.OpenWrite(regular)) { } // SQLite must retain write compatibility; this control writes no bytes.
            MustFail(() => File.Move(regular, regular + ".moved"));
        }
        using (var pin = DataFileLease.OpenExisting(regular)) MustFail(() => { using var write = File.OpenWrite(regular); });
        File.Move(regular, regular + ".moved"); File.Move(regular + ".moved", regular);

        var db = Path.Combine(root, "library.db"); var settings = Path.Combine(root, "settings.json");
        File.WriteAllText(db, "old database"); File.WriteAllText(settings, "old settings");
        using var sourceDb = new MemoryStream(Encoding.UTF8.GetBytes("new database")); using var sourceSettings = new MemoryStream(Encoding.UTF8.GetBytes("new settings"));
        var interrupted = false;
        try
        {
            RestoreFileTransaction.Install([(db, sourceDb), (settings, sourceSettings)], [(db, db + ".retained", false), (settings, settings + ".retained", false)], count =>
            {
                if (count != 1) return;
                MustFail(() => File.Delete(db)); MustFail(() => File.Move(db, db + ".moved"));
                MustFail(() => { using var write = File.OpenWrite(db); });
                MustFail(() => { using var write = File.OpenWrite(db + ".retained"); });
                throw new IOException("Owned interruption after first handle-based install.");
            });
        }
        catch (IOException) { interrupted = true; }
        if (!interrupted || File.ReadAllText(db) != "old database" || File.ReadAllText(settings) != "old settings" || Directory.GetFiles(root, "*.restore-*").Length != 0)
            throw new InvalidOperationException("Same-handle rollback did not restore owned originals.");
        RestoreFileTransaction.Install([(db, sourceDb), (settings, sourceSettings)], [(db, db + ".retained", false), (settings, settings + ".retained", false)]);
        if (File.ReadAllText(db) != "new database" || File.ReadAllText(db + ".retained") != "old database") throw new InvalidOperationException("Same-handle commit did not retain originals.");
        using (File.Open(db, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
        DataFileLease.FileIdentity? identity;
        using (var pin = DataFileLease.OpenExisting(regular)) identity = pin!.Identity;
        File.Move(regular, regular + ".owned-moved"); File.WriteAllText(regular, "replacement sentinel");
        if (DataFileLease.DeleteIfSame(regular, identity) || File.ReadAllText(regular) != "replacement sentinel")
            throw new InvalidOperationException("Identity cleanup deleted a replacement.");
        if (!DataFileLease.DeleteIfSame(regular + ".owned-moved", identity) || File.Exists(regular + ".owned-moved"))
            throw new InvalidOperationException("Identity cleanup did not delete its held original object.");
        var archive = Path.Combine(root, "Owned Архив.zip");
        RestoreFileTransaction.PublishNew(archive, stream =>
        {
            stream.WriteByte(42);
            var partial = Directory.GetFiles(root, "*.partial-*").Single();
            MustFail(() => File.Move(partial, partial + ".moved"));
            MustFail(() => { using var writer = File.OpenWrite(partial); });
        });
        if (!File.ReadAllBytes(archive).SequenceEqual(new byte[] { 42 }) || Directory.GetFiles(root, "*.partial-*").Length != 0)
            throw new InvalidOperationException("Same-handle archive publication failed.");
        return new { SymbolicDataRejected = true, HardlinkedDataRejected = true, OutsideBytesPreserved = true,
            WritableSqliteSharingRetained = true, MutableNameReplacementBlocked = true, FrozenSnapshotWritesDenied = true,
            HeldInstalledAndOriginalMutationsDenied = true, SameHandleRollbackPassed = true, SameHandleCommitPassed = true, HandlesReleased = true,
            ReplacementIdentityCleanupRefused = true, OwnedIdentityCleanupPassed = true, SameHandleUnicodePublicationPassed = true };
    }
    [DllImport("kernel32.dll", EntryPoint = "CreateHardLinkW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CreateHardLink(string path, string existing, nint security);
}
