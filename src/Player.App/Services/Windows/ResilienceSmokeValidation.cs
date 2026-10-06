using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Player.App.Services.Audio;
using Player.App.Services.Library;
using Player.App.Services.Storage;
using Player.Core.Library;
using Player.Core.Media;
using Player.Core.Playback;

namespace Player.App.Services.Windows;

/// <summary>Actual failures in owned directories and a parent-created disposable NTFS VHD.</summary>
public static class ResilienceSmokeValidation
{
    private sealed record OwnedVolume(string Directory, string Token);
    public static async Task<object> RunAsync(string fixture, string output)
    {
        var owned = Path.Combine(output, "g9-owned"); Directory.CreateDirectory(owned);
        var portable = ReadOnlyPortable(owned);
        var tags = await MetadataAsync(fixture, owned);
        var watcher = await WatcherAsync(fixture, owned);
        var marker = JsonSerializer.Deserialize<OwnedVolume>(File.ReadAllText(Path.Combine(Environment.CurrentDirectory, ".player-owned-volume")))!;
        Check(Path.GetPathRoot(marker.Directory) == marker.Directory && File.ReadAllText(Path.Combine(marker.Directory, ".player-volume-token")) == marker.Token,
            "Full-disk checks require the parent-created owned volume marker.");
        var disk = await FullDiskAsync(marker.Directory, fixture);
        return new { Status = "g9-resilience-passed", ReadOnlyPortable = portable, Metadata = tags, WatcherOverflow = watcher, FullDisk = disk,
            PowerLoss = "not-run", Windows = Environment.OSVersion.VersionString };
    }
    private static object ReadOnlyPortable(string owned)
    {
        var app = Path.Combine(owned, "portable"); var data = Directory.CreateDirectory(Path.Combine(app, "Data"));
        File.WriteAllText(Path.Combine(app, "portable.marker"), "Owned validation");
        var user = Path.Combine(owned, "user-fallback"); var original = data.GetAccessControl(); var denied = data.GetAccessControl();
        denied.AddAccessRule(new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!, FileSystemRights.Write, InheritanceFlags.None, PropagationFlags.None, AccessControlType.Deny));
        var prompts = 0; var rejected = false; string? fallback = null;
        try
        {
            data.SetAccessControl(denied);
            try { StorageLocation.Resolve(app, user, _ => { prompts++; return false; }); } catch (UnauthorizedAccessException) { rejected = true; }
            Check(rejected && prompts == 1 && !Directory.Exists(user), "Declined portable fallback wrote per-user data.");
            fallback = StorageLocation.Resolve(app, user, _ => { prompts++; return true; });
            new SettingsFile(fallback).Save(new(Volume: 31));
            Check(new SettingsFile(fallback).Load().Volume == 31 && !File.Exists(Path.Combine(data.FullName, "settings.json")), "Accepted fallback did not use the chosen writable directory.");
        }
        finally
        {
            var restored = new DirectorySecurity(); restored.SetSecurityDescriptorBinaryForm(original.GetSecurityDescriptorBinaryForm(), AccessControlSections.Access); data.SetAccessControl(restored);
        }
        Check(StorageLocation.Resolve(app, user, _ => throw new InvalidOperationException("Unexpected prompt.")) == data.FullName, "Portable location did not recover after permission restoration.");
        return new { ActualDeniedDirectoryAcl = true, ExplicitDeclinePreservedUserDirectory = true, ExplicitAcceptSavedPerUser = true, Choices = prompts, PermissionRecovery = true,
            InteractiveMessageBox = "policy callback exercised; manual dialog interaction remains separate" };
    }
    private static async Task<object> MetadataAsync(string fixture, string owned)
    {
        var directory = Directory.CreateDirectory(Path.Combine(owned, "tags")).FullName;
        var source = Path.Combine(directory, "huge-trailing-tag.wav"); File.Copy(fixture, source);
        // RIFF audio remains valid. The real metadata chunk is too large for safe tag parsing.
        using (var file = new FileStream(source, FileMode.Open, FileAccess.ReadWrite))
        using (var writer = new BinaryWriter(file))
        {
            file.Position = file.Length; writer.Write("id3 "u8); writer.Write((uint)(MetadataReadGuard.MaximumMetadataBytes + 2));
            writer.Write("ID3"u8); writer.Write(new byte[7]); file.SetLength(file.Position + MetadataReadGuard.MaximumMetadataBytes - 8);
            file.Position = 4; writer.Write(checked((uint)(file.Length - 8))); file.Flush(true);
        }
        var before = Hash(source); var diagnostics = new List<string>();
        var track = MediaMetadataReader.Read(source, Guid.NewGuid(), diagnostics.Add);
        Check(track.Title == "huge-trailing-tag" && diagnostics.Count == 1, "Huge tag did not produce bounded metadata fallback.");
        Check(await new ArtworkService().LoadAsync(source, CancellationToken.None) is null, "Huge embedded tag reached artwork parsing.");
        DecodeEvidence decode = await Task.Run(() => { using var session = new BassSmokeSession(); return session.Decode(source); });
        Check(decode.Peak is > 0.01f and < 0.2f && Math.Abs(decode.DurationSeconds - 3) < 0.03, "Metadata failure prevented real audio decode.");
        using (new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.None)) { }
        Check(before == Hash(source), "Huge-tag source changed.");
        var malformed = Path.Combine(directory, "truncated.flac");
        File.WriteAllBytes(malformed, [.. "fLaC"u8, 0x84, 0xff, 0xff, 0xff]);
        diagnostics.Clear(); var fallback = MediaMetadataReader.Read(malformed, Guid.NewGuid(), diagnostics.Add);
        Check(fallback.Title == "truncated" && diagnostics.Count == 1 && await new ArtworkService().LoadAsync(malformed, CancellationToken.None) is null, "Truncated tag did not remain isolated.");
        var compressed = Path.Combine(directory, "unsupported-compressed-tag.wav"); File.Copy(fixture, compressed);
        using (var file = new FileStream(compressed, FileMode.Open, FileAccess.ReadWrite))
        using (var writer = new BinaryWriter(file))
        {
            file.Position = file.Length; writer.Write("id3 "u8); writer.Write(30u);
            writer.Write("ID3"u8); writer.Write(new byte[] { 3, 0, 0, 0, 0, 0, 20 });
            writer.Write("TIT2"u8); writer.Write(new byte[] { 0, 0, 0, 10, 0, 0x80, 0x7f, 0xff, 0xff, 0xff, 0, 0, 0, 0, 0, 0 });
            file.Position = 4; writer.Write(checked((uint)(file.Length - 8))); file.Flush(true);
        }
        var compressedHash = Hash(compressed); diagnostics.Clear(); var compressedTrack = MediaMetadataReader.Read(compressed, Guid.NewGuid(), diagnostics.Add);
        Check(compressedTrack.Title.Length <= 4096 && await new ArtworkService().LoadAsync(compressed, CancellationToken.None) is null && compressedHash == Hash(compressed), "Unsupported compressed ID3 frame escaped metadata isolation.");
        var valid = Path.Combine(directory, "valid.wav"); File.Copy(fixture, valid);
        diagnostics.Clear(); var next = MediaMetadataReader.Read(valid, Guid.NewGuid(), diagnostics.Add);
        Check(next.DurationHint is { TotalSeconds: > 0 } && diagnostics.Count == 0, "Metadata worker failed to recover after a bad file.");
        using (var tagged = TagLib.File.Create(valid)) { tagged.Tag.Title = new string('t', 100000); tagged.Save(); }
        var taggedHash = Hash(valid); diagnostics.Clear(); next = MediaMetadataReader.Read(valid, Guid.NewGuid(), diagnostics.Add);
        Check(next.Title.Length == 4096 && diagnostics.Count == 0 && taggedHash == Hash(valid), "Large valid title was not bounded without source edits.");
        return new { ActualOversizedRiffTagBytes = MetadataReadGuard.MaximumMetadataBytes + 2, FallbackAndArtworkIsolation = true, NativeAudioDecodeUnaffected = true,
            TruncatedFlacRejected = true, UnsupportedCompressedFrameIsolated = true, ValidTitleInputCharacters = 100000, BoundedTitleCharacters = next.Title.Length, SubsequentValidMetadata = true, SourceUnchanged = true, SourceHandlesReleased = true, Decode = decode };
    }
    private static async Task<object> WatcherAsync(string fixture, string owned)
    {
        var rootPath = Directory.CreateDirectory(Path.Combine(owned, "watched")).FullName;
        var keep = Path.Combine(rootPath, "keep.wav"); var missing = Path.Combine(rootPath, "missing.wav"); File.Copy(fixture, keep); File.Copy(fixture, missing);
        await using var store = new SqlitePlayerStore(Path.Combine(owned, "watcher.db")); var state = await store.LoadAsync();
        var root = new LibraryRoot(Guid.NewGuid(), rootPath); await store.PutRootAsync(root);
        var scanner = new LibraryScanner(store); var progress = new InlineProgress();
        await scanner.ScanAsync(root, new Dictionary<string, Guid>(), progress, CancellationToken.None);
        var original = (await store.SearchAsync("")).Files; var kept = original.Single(f => f.Path == keep); var removed = original.Single(f => f.Path == missing);
        var entry = new PlaylistEntry(Guid.NewGuid(), removed.Track); state = state with { Playlists = [state.Playlists[0] with { Entries = [entry] }] };
        await store.SaveAsync(state, true); await store.SetRatingAsync(removed.Track.Id, 4);
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        var hint = new TaskCompletionSource<Guid[]>(TaskCreationOptions.RunContinuationsAsynchronously); var first = 0;
        using var watcher = new LibraryWatcher(ids => hint.TrySetResult(ids))
        { BufferSize = 4096, NotificationCheckpoint = () => { if (Interlocked.Exchange(ref first, 1) == 0) { entered.Set(); release.Wait(TimeSpan.FromSeconds(30)); } } };
        watcher.Watch([root]);
        var trigger = Path.Combine(rootPath, "trigger.wav"); File.WriteAllBytes(trigger, []);
        Check(await Task.Run(() => entered.Wait(TimeSpan.FromSeconds(10))), "Actual watcher callback did not start.");
        try
        {
            await Task.Run(() =>
            {
                for (var i = 0; i < 4000; i++)
                {
                    var path = Path.Combine(rootPath, $"burst-{i:D5}-" + new string('x', 160) + ".tmp");
                    File.WriteAllBytes(path, []); File.Delete(path);
                }
                File.Delete(missing); File.Delete(trigger); File.Copy(fixture, Path.Combine(rootPath, "new.wav"));
            });
        }
        finally { release.Set(); }
        var dirty = await hint.Task.WaitAsync(TimeSpan.FromSeconds(15));
        Check(watcher.OverflowCount > 0 && dirty.Contains(root.Id), "Native watcher overflow was not observed and reconciled.");
        await scanner.ScanAsync(root, new Dictionary<string, Guid>(), progress, CancellationToken.None);
        var reconciled = (await store.SearchAsync("")).Files;
        Check(reconciled.Single(f => f.Path == keep).Id == kept.Id && reconciled.Single(f => f.Path == missing).Id == removed.Id && !reconciled.Single(f => f.Path == missing).Available,
            "Overflow reconciliation lost stable IDs/missing metadata.");
        Check(reconciled.Single(f => Path.GetFileName(f.Path) == "new.wav").Available && (await store.GetStatisticsAsync([removed.Track.Id])).Single().Rating == 4 && (await store.LoadAsync()).Playlists[0].Entries[0].Id == entry.Id,
            "Overflow rescan lost new file, rating or user playlist.");
        File.Copy(fixture, missing); await scanner.ScanAsync(root, new Dictionary<string, Guid>(), progress, CancellationToken.None);
        Check((await store.SearchAsync("")).Files.Single(f => f.Path == missing) is { Available: true } recovered && recovered.Id == removed.Id, "Reappearing file lost identity.");
        for (var i = 0; i < 130; i++) File.Copy(fixture, Path.Combine(rootPath, $"cancel-{i:D3}.wav"));
        using var cancellation = new CancellationTokenSource(); var cancelledAfter = 0;
        try
        {
            await scanner.ScanAsync(root, new Dictionary<string, Guid>(), new InlineProgress(value => { if (value.Seen >= 64) { cancelledAfter = value.Seen; cancellation.Cancel(); } }), cancellation.Token);
            throw new InvalidDataException("Mid-scan cancellation was ignored.");
        }
        catch (OperationCanceledException) { }
        Check(cancelledAfter == 64 && (await store.FindFilesAsync([keep, missing])).All(f => f.Available), "Partial cancelled scan falsely marked existing files missing.");
        await scanner.ScanAsync(root, new Dictionary<string, Guid>(), progress, CancellationToken.None);
        Check((await store.SearchAsync("")).Total == 133 && (await store.FindFilesAsync([keep, missing])).All(f => f.Available), "Scanner failed to resume after a committed partial batch.");
        return new { ActualInternalBufferOverflows = watcher.OverflowCount, BufferBytes = 4096, BurstCreateDeletePairs = 4000, NativeCallbackTemporarilyHeld = true,
            DebouncedRootHint = true, ProductionScannerReconciled = true, StableIdsAndRatingsAndPlaylist = true, MissingAndReappearing = true,
            CancellationAfterCommittedBatch = cancelledAfter, NoFalseMissingFromPartialScan = true, ResumedFiles = 133 };
    }
    private static async Task<object> FullDiskAsync(string volume, string fixture)
    {
        var drive = new DriveInfo(volume);
        Check(drive.DriveFormat == "NTFS" && drive.TotalSize <= 128L * 1024 * 1024, "Full-disk target must be the small owned NTFS validation VHD.");
        var directory = Directory.CreateDirectory(Path.Combine(volume, "owned-player-data")).FullName;
        var database = Path.Combine(directory, "library.db"); var fillPath = Path.Combine(volume, "owned-fill.bin");
        var settings = new SettingsFile(directory); settings.Save(new(Volume: 17)); settings.Save(new(Volume: 29));
        var settingsHash = Hash(Path.Combine(directory, "settings.json")); var backupHash = Hash(Path.Combine(directory, "settings.json.bak"));
        await using var store = new SqlitePlayerStore(database); var original = await store.LoadAsync();
        var track = new MediaTrack(Guid.NewGuid(), fixture, "Committed before disk full");
        original = original with { Playlists = [original.Playlists[0] with { Entries = [new(Guid.NewGuid(), track)] }] }; await store.SaveAsync(original, true);
        var archive = Path.Combine(directory, "full-disk.zip"); var diskError = 0; long filled = 0;
        try
        {
            using (var fill = new FileStream(fillPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                var block = new byte[4096];
                for (var i = 0; i < block.Length; i++) block[i] = (byte)(i % 251);
                try { while (filled < 160L * 1024 * 1024) { fill.Write(block); filled += block.Length; } fill.Flush(true); }
                catch (IOException error) when ((error.HResult & 0xffff) is 112 or 39) { diskError = error.HResult & 0xffff; }
            }
            Check(diskError != 0, "Owned VHD did not produce a real disk-full error.");
            var entries = Enumerable.Range(0, 3000).Select(i => new PlaylistEntry(Guid.NewGuid(), track with { Id = Guid.NewGuid(), Title = new string('x', 4096), Artist = new string('y', 4096) })).ToArray();
            var rejected = false; var sqliteCode = 0;
            try { await store.SaveAsync(original with { Playlists = [original.Playlists[0] with { Entries = entries }] }, true); }
            catch (SqliteException error) when (error.SqliteErrorCode == 13) { rejected = true; sqliteCode = error.SqliteErrorCode; }
            Check(rejected && JsonSerializer.Serialize(await store.LoadAsync()) == JsonSerializer.Serialize(original), "Real SQLITE_FULL lost the last committed playlists/session.");
            var settingsRejected = false;
            try { settings.Save(new(Volume: 83, Output: new(new string('d', 4096)))); }
            catch (IOException error) when ((error.HResult & 0xffff) is 112 or 39) { settingsRejected = true; }
            Check(settingsRejected && settingsHash == Hash(Path.Combine(directory, "settings.json")) && backupHash == Hash(Path.Combine(directory, "settings.json.bak")), "Disk-full settings save lost committed bytes.");
            var backupRejected = false;
            try { await BackupBundle.CreateAsync(store, settings.Load(), archive); }
            catch (Exception error) when (error is SqliteException { SqliteErrorCode: 13 } || error is IOException io && (io.HResult & 0xffff) is 112 or 39) { backupRejected = true; }
            Check(backupRejected && !File.Exists(archive), "Full-disk backup published an incomplete archive.");
            File.Delete(fillPath); settings.Save(new(Volume: 35)); await store.SaveAsync(original, true); await BackupBundle.CreateAsync(store, settings.Load(), archive);
            Check(settings.Load().Volume == 35 && File.Exists(archive) && !Directory.GetFiles(directory, "*.tmp").Any() && !Directory.GetDirectories(directory, ".player-backup-*").Any(), "Storage writers did not recover after freeing space.");
            return new { OwnedNtfsVhd = true, FileSystem = drive.DriveFormat, VolumeBytes = drive.TotalSize, FilledBytes = filled, ActualWindowsDiskFullCode = diskError, ActualSqliteFullCode = sqliteCode,
                TransactionRollbackPreservedCommittedState = true, SettingsAndPreviousBytesPreserved = true, IncompleteBackupNotPublished = true, SaveAndBackupAfterSpaceFreed = true };
        }
        finally { if (File.Exists(fillPath)) File.Delete(fillPath); }
    }
    private sealed class InlineProgress(Action<ScanProgress>? callback = null) : IProgress<ScanProgress> { public void Report(ScanProgress value) => callback?.Invoke(value); }
    private static string Hash(string path) { using var file = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(file)); }
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
}
