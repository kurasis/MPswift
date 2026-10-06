using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Security.Cryptography;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Player.App.Services.Library;
using Player.App.Services.Storage;
using Player.Core.Library;

namespace Player.App.Services.Windows;

/// <summary>Real Windows filesystem permissions/locks and WIC thumbnail failures, in an owned disposable directory.</summary>
public static class StorageArtworkValidation
{
    public static async Task<object> RunAsync(string fixture, string output)
    {
        static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        static string Hash(string path) { using var file = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(file)); }
        var directory = Path.Combine(output, "stage-g-failures-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            var settings = new SettingsFile(directory); settings.Save(new(Volume: 17)); settings.Save(new(Volume: 29));
            var path = Path.Combine(directory, "settings.json"); var before = Hash(path); var backupBefore = Hash(path + ".bak");
            bool lockRejected;
            using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
            { try { settings.Save(new(Volume: 83)); lockRejected = false; } catch (IOException) { lockRejected = true; } }
            Check(lockRejected && Hash(path) == before && Hash(path + ".bak") == backupBefore, "Locked settings replacement lost original/backup bytes.");
            var folder = new DirectoryInfo(directory); var originalAcl = folder.GetAccessControl();
            var deniedAcl = folder.GetAccessControl(); var sid = WindowsIdentity.GetCurrent().User ?? throw new IOException("Windows user SID unavailable.");
            deniedAcl.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.Write, InheritanceFlags.None, PropagationFlags.None, AccessControlType.Deny));
            bool permissionRejected;
            try
            {
                folder.SetAccessControl(deniedAcl);
                try { settings.Save(new(Volume: 91)); permissionRejected = false; } catch (UnauthorizedAccessException) { permissionRejected = true; }
            }
            finally
            {
                // SetAccessControl persists only modified sections; a freshly read descriptor is otherwise a no-op.
                var restoredAcl = new DirectorySecurity();
                restoredAcl.SetSecurityDescriptorBinaryForm(originalAcl.GetSecurityDescriptorBinaryForm(), AccessControlSections.Access);
                folder.SetAccessControl(restoredAcl);
            }
            Check(permissionRejected && Hash(path) == before && Hash(path + ".bak") == backupBefore, "Denied directory write lost saved data.");
            Check(!Directory.GetFiles(directory, "*.tmp").Any(), "Failed settings save left a temporary file.");
            settings.Save(new(Volume: 35)); Check(settings.Load().Volume == 35, "Settings writer unusable after permissions restored.");
            var database = Path.Combine(directory, "library.db"); var archive = Path.Combine(directory, "complete.zip");
            await using (var store = new SqlitePlayerStore(database)) { await store.LoadAsync(); await BackupBundle.CreateAsync(store, settings.Load(), archive); }
            var databaseBefore = Hash(database); before = Hash(path); var restoreRejected = false;
            using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
                try { await BackupBundle.RestoreAsync(directory, archive); } catch (IOException) { restoreRejected = true; }
            Check(restoreRejected && Hash(database) == databaseBefore && Hash(path) == before, "Partial restore failed to roll back moved originals under a real Windows file lock.");
            await using (var reopened = new SqlitePlayerStore(database)) await reopened.LoadAsync();

            var source = Path.Combine(directory, "Owned Музыка.wav"); File.Copy(fixture, source); var sourceBefore = Hash(source);
            var cover = Path.Combine(directory, "cover.png"); var service = new ArtworkService();
            File.WriteAllText(cover, "not an image"); Check(await service.LoadAsync(source, CancellationToken.None) is null, "Malformed cover was decoded.");
            using (var oversized = new FileStream(cover, FileMode.Create, FileAccess.Write)) oversized.SetLength(20 * 1024 * 1024 + 1);
            Check(await service.LoadAsync(source, CancellationToken.None) is null, "Encoded-byte limit was bypassed.");
            // An owned BMP header declares >40 MP; no large image allocation or external source is needed.
            using (var file = File.Create(cover)) using (var writer = new BinaryWriter(file))
            {
                writer.Write((ushort)0x4d42); writer.Write(54); writer.Write(0); writer.Write(54); writer.Write(40);
                writer.Write(10000); writer.Write(10000); writer.Write((ushort)1); writer.Write((ushort)24); writer.Write(new byte[24]);
            }
            Check(await service.LoadAsync(source, CancellationToken.None) is null, "Oversized/truncated image was accepted.");
            var pixels = Enumerable.Repeat(unchecked((int)0xff448855), 16 * 1024).ToArray();
            var bitmap = BitmapSource.Create(16, 1024, 96, 96, PixelFormats.Bgr32, null, pixels, 16 * 4); bitmap.Freeze();
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using (var file = File.Create(cover)) encoder.Save(file);
            var image = await service.LoadAsync(source, CancellationToken.None);
            Check(image is { IsFrozen: true, PixelWidth: > 0 and <= 192, PixelHeight: > 0 and <= 192 }, "Portrait artwork thumbnail exceeded either dimension.");
            Check(ReferenceEquals(image, await service.LoadAsync(source, CancellationToken.None)), "Thumbnail cache missed an unchanged local cover.");
            using var canceled = new CancellationTokenSource(); canceled.Cancel(); var cancellationObserved = false;
            try { await service.LoadAsync(source, canceled.Token); } catch (OperationCanceledException) { cancellationObserved = true; }
            Check(cancellationObserved && await service.LoadAsync(source, CancellationToken.None) is not null, "Cancellation damaged the thumbnail worker.");
            Check(Hash(source) == sourceBefore, "Artwork reader changed source audio.");
            using (File.Open(source, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
            using (File.Open(cover, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
            return new { Status = "storage-artwork-passed", WindowsDirectoryWriteDeniedByAcl = true, LockedSettingsRejected = true,
                OriginalAndPreviousSettingsUnchangedOnFailure = true, WriterRecovered = true, InvalidCoverRejected = true, EncodedLimitBytes = 20 * 1024 * 1024,
                PartialBundleRestoreRolledBackOnLockedSettings = true,
                OversizedTruncatedHeaderRejected = true, DeclaredPixelLimit = 40000000, PortraitThumbnailWidth = image!.PixelWidth, PortraitThumbnailHeight = image.PixelHeight,
                FrozenCacheHit = true, CancellationLeavesWorkerUsable = true, SourceHashUnchanged = true, ReaderHandlesReleased = true,
                DiskFull = "not-run", HugeTagProcessIsolation = "not-run" };
        }
        finally { Directory.Delete(directory, true); }
    }
}
