using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Win32.SafeHandles;
using System.Security.AccessControl;
using Player.App.Services.Storage;

namespace MPswift.TagRepair;

internal sealed record FileResult(string Path, string Status, string[] Changes, string? Backup = null, string? AudioSha256 = null, string? Error = null);

internal static class FileRepair
{
    internal const string BackupDirectoryName = "MPswift.TagRepair.Backups";
    internal static FileResult Process(string root, string path, bool apply, int cueCodePage, CancellationToken cancellation = default,
        Action<Stream, Stream>? commit = null)
    {
        if (apply && !OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Writing is supported only on Windows; preview remains available.");
        using var directory = DataDirectoryLease.Open(System.IO.Path.GetDirectoryName(path)!);
        var physical = System.IO.Path.Combine(directory.DirectoryPath, System.IO.Path.GetFileName(path));
        var relative = System.IO.Path.GetRelativePath(root, physical);
        if (relative == ".." || relative.StartsWith(".." + System.IO.Path.DirectorySeparatorChar, StringComparison.Ordinal) || System.IO.Path.IsPathRooted(relative))
            throw new IOException("Resolved file is outside the selected folder.");
        using var source = OpenSource(physical, apply);
        var extension = System.IO.Path.GetExtension(physical).ToLowerInvariant();
        byte[]? cue = null; TagEdit? tags = null; AudioFingerprint? audio = null; string[] changes;
        if (extension == ".cue")
        {
            if (source.Length > 4 * 1024 * 1024) throw new InvalidDataException("CUE exceeds 4 MiB.");
            var before = new byte[(int)source.Length]; source.ReadExactly(before);
            cue = TextRepair.Cue(before, physical, cueCodePage);
            changes = before.AsSpan().SequenceEqual(cue) ? [] : ["CUE text -> UTF-8 (FILE and INDEX lines preserved)"];
        }
        else
        {
            tags = TagEditor.Read(source, physical);
            changes = tags.Changes.Select(key => key + ": " + tags.Before[key] + " -> " + tags.After[key]).ToArray();
            if (TagEditor.NeedsUnicodeRewrite(source, physical)) changes = [.. changes, "MP3 tags -> ID3v2.4 / UTF-8; remove legacy ID3v1 copy"];
            if (changes.Length > 0) audio = AudioFingerprint.Read(source, extension);
        }
        if (changes.Length == 0) return new(path, "unchanged", []);
        if (!apply) return new(path, "preview", changes, AudioSha256: audio?.Sha256);
        cancellation.ThrowIfCancellationRequested();
        // Pin the selected root and the one flat backup directory through commit.
        // Open follows links for other storage users; reject them explicitly here.
        using var rootDirectory = DataDirectoryLease.Open(root);
        var backupFolder = System.IO.Path.Combine(rootDirectory.DirectoryPath, BackupDirectoryName);
        Directory.CreateDirectory(backupFolder);
        using var backups = DataDirectoryLease.Open(backupFolder);
        if ((File.GetAttributes(backupFolder) & FileAttributes.ReparsePoint) != 0 ||
            !backups.DirectoryPath.Equals(backupFolder, StringComparison.OrdinalIgnoreCase))
            throw new IOException("The backup folder must not be a directory link; original retained.");
        var nonce = Guid.NewGuid().ToString("N"); var temporary = physical + ".mpswift-" + nonce + ".tmp";
        var name = System.IO.Path.GetFileName(physical);
        if (name.Length > 120) name = name[..120];
        var backup = System.IO.Path.Combine(backups.DirectoryPath, name + ".mpswift-" + nonce + ".bak");
        using var prepared = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
        try
        {
            DataFileLease.Check(prepared.SafeFileHandle);
            if (OperatingSystem.IsWindows()) PrivateAccess(temporary);
            source.Position = 0; var originalHash = SHA256.HashData(source); source.Position = 0;
            if (cue is not null) prepared.Write(cue);
            else
            {
                source.CopyTo(prepared); TagEditor.Rewrite(prepared, physical, tags!);
                if (AudioFingerprint.Read(prepared, extension) != audio) throw new InvalidDataException("Audio payload changed; original retained.");
            }
            prepared.Flush(true); prepared.Position = 0; var preparedHash = SHA256.HashData(prepared);
            cancellation.ThrowIfCancellationRequested();
            using var retained = new FileStream(backup, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
            DataFileLease.Check(retained.SafeFileHandle);
            if (OperatingSystem.IsWindows()) CopyAccess(physical, backup);
            source.Position = 0; source.CopyTo(retained); retained.Flush(true); retained.Position = 0;
            if (!SHA256.HashData(retained).AsSpan().SequenceEqual(originalHash)) throw new IOException("Backup verification failed; original retained.");
            // A private flat sidecar maps duplicate basenames to their source;
            // retain it with the exact backup even if a later write rolls back.
            using (var mapping = new FileStream(backup + ".json", FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            {
                DataFileLease.Check(mapping.SafeFileHandle);
                if (OperatingSystem.IsWindows()) PrivateAccess(backup + ".json");
                System.Text.Json.JsonSerializer.Serialize(mapping, new { SourceRelativePath = relative, OriginalSha256 = Convert.ToHexStringLower(originalHash) });
                mapping.Flush(true);
            }
            cancellation.ThrowIfCancellationRequested(); DataFileLease.Check(source.SafeFileHandle);
            try
            {
                // Write to the exact pinned original object; no path reopening or rename race.
                // Finish or roll back this phase even when Ctrl+C requests cancellation.
                source.Position = prepared.Position = 0;
                if (commit is null) prepared.CopyTo(source); else commit(prepared, source);
                source.SetLength(prepared.Length); source.Flush(true); source.Position = 0;
                if (!SHA256.HashData(source).AsSpan().SequenceEqual(preparedHash)) throw new IOException("Written bytes differ from verified candidate.");
            }
            catch (Exception writeError) when (writeError is IOException or UnauthorizedAccessException)
            {
                try
                {
                    source.Position = retained.Position = 0; retained.CopyTo(source); source.SetLength(retained.Length); source.Flush(true); source.Position = 0;
                    if (!SHA256.HashData(source).AsSpan().SequenceEqual(originalHash)) throw new IOException("Restored bytes differ from original.");
                }
                catch (Exception restoreError) when (restoreError is IOException or UnauthorizedAccessException)
                { throw new IOException("Write and rollback failed. Restore the verified backup: " + backup, new AggregateException(writeError, restoreError)); }
                throw new IOException("Write failed; original restored. Verified backup: " + backup, writeError);
            }
            return new(path, "repaired", changes, backup, audio?.Sha256);
        }
        finally
        {
            var identity = DataFileLease.Identify(prepared.SafeFileHandle); prepared.Dispose();
            if (!DataFileLease.DeleteIfSame(temporary, identity)) throw new IOException("Temporary object changed; unknown replacement retained.");
        }
    }

    private static FileStream OpenSource(string path, bool writable)
    {
        if (writable && (File.GetAttributes(path) & FileAttributes.Encrypted) != 0) throw new IOException("Encrypted sources are not modified.");
        if (!OperatingSystem.IsWindows())
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("File links are skipped.");
            return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        }
        var handle = CreateFile(path, writable ? 0xc0000000u : 0x80000000u, 1, 0, 3, 0x00200000, 0);
        if (handle.IsInvalid) { var error = Marshal.GetLastPInvokeError(); handle.Dispose(); throw new IOException("Cannot lock source file; close the player/editor and check file permissions.", new Win32Exception(error)); }
        try { DataFileLease.Check(handle); return new FileStream(handle, writable ? FileAccess.ReadWrite : FileAccess.Read); }
        catch { handle.Dispose(); throw; }
    }
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static void PrivateAccess(string ownedCopy)
    {
        using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        var user = identity.User ?? throw new IOException("Cannot identify the current user.");
        var security = new FileSecurity(); security.SetAccessRuleProtection(true, false);
        security.AddAccessRule(new FileSystemAccessRule(user, FileSystemRights.FullControl, AccessControlType.Allow));
        FileSystemAclExtensions.SetAccessControl(new FileInfo(ownedCopy), security);
    }
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static void CopyAccess(string source, string ownedCopy)
    {
        var security = FileSystemAclExtensions.GetAccessControl(new FileInfo(source), AccessControlSections.Access);
        security.SetAccessRuleProtection(true, true);
        FileSystemAclExtensions.SetAccessControl(new FileInfo(ownedCopy), security);
    }
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string path, uint access, uint share, nint security, uint creation, uint flags, nint template);
}
