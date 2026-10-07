using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Player.Core.Library;

namespace Player.App.Services.Storage;

/// <summary>Complete local backup, published atomically; no music/cache/logs. Validate before replacing any user data.</summary>
public static class BackupBundle
{
    private const long MaximumDatabaseBytes = 2L * 1024 * 1024 * 1024;
    public sealed record Item(string Path, long Bytes, string Sha256);
    public sealed record Manifest(int SchemaVersion, Item[] Files);
    private static string Hash(Stream file)
    {
        var hash = Convert.ToHexString(SHA256.HashData(file)).ToLowerInvariant();
        file.Position = 0;
        return hash;
    }
    public static async Task CreateAsync(IPlayerStore store, PlayerSettings settings, string destination)
    {
        destination = Path.GetFullPath(destination);
        using var destinationLease = DataDirectoryLease.Open(Path.GetDirectoryName(destination)!);
        destination = Path.Combine(destinationLease.DirectoryPath, Path.GetFileName(destination));
        var stage = Path.Combine(Path.GetDirectoryName(destination)!, ".player-backup-" + Guid.NewGuid().ToString("N"));
        var temporary = destination + ".partial-" + Guid.NewGuid().ToString("N");
        var ownsTemporary = false;
        if (File.Exists(destination) || Directory.Exists(destination)) throw new IOException("Choose a new backup filename; existing files are never overwritten.");
        var stageLease = DataDirectoryLease.Create(stage);
        Exception? failure = null;
        try
        {
            await store.BackupAsync(Path.Combine(stage, "library.db"));
            await Task.Run(() =>
            {
                SettingsFile.Export(Path.Combine(stage, "settings.json"), settings);
                using var database = File.OpenRead(Path.Combine(stage, "library.db"));
                using var settingsFile = File.OpenRead(Path.Combine(stage, "settings.json"));
                if (database.Length > MaximumDatabaseBytes) throw new IOException("Backup database exceeds the 2 GiB archive limit; original data preserved.");
                if (settingsFile.Length > 65536) throw new IOException("Backup settings exceed the 64 KiB limit; original data preserved.");
                var files = new[] { database, settingsFile };
                var items = new[] { "library.db", "settings.json" }.Select((name, index) => new Item(name, files[index].Length, Hash(files[index]))).ToArray();
                using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    ownsTemporary = true;
                    using (var zip = new ZipArchive(file, ZipArchiveMode.Create, true))
                    {
                        for (var index = 0; index < items.Length; index++)
                        {
                            using var entry = zip.CreateEntry(items[index].Path, CompressionLevel.Optimal).Open();
                            files[index].CopyTo(entry); // Hash and archive use the same retained read handle.
                        }
                        using var manifest = zip.CreateEntry("manifest.json").Open();
                        JsonSerializer.Serialize(manifest, new Manifest(1, items));
                    }
                    file.Flush(true);
                }
                File.Move(temporary, destination);
            });
        }
        catch (Exception error) { failure = error; throw; }
        finally { FinishStage(stage, stageLease, failure, ownsTemporary ? temporary : null); }
    }
    public static async Task RestoreAsync(string directory, string archive)
    {
        using var directoryLease = DataDirectoryLease.Create(directory);
        directory = directoryLease.DirectoryPath;
        var stage = Path.Combine(directory, ".player-restore-" + Guid.NewGuid().ToString("N"));
        var stageLease = DataDirectoryLease.Create(stage);
        Exception? failure = null;
        try
        {
            await Task.Run(() => ExtractValidated(archive, stage));
            // Actual production loader validates dimensions, IDs, paths and session JSON; migrations affect only this owned copy.
            await using (var validator = new SqlitePlayerStore(Path.Combine(stage, "library.db"))) await validator.LoadAsync();
            await Task.Run(() => Install(directory, stage));
        }
        catch (Exception error) { failure = error; throw; }
        finally { FinishStage(stage, stageLease, failure); }
    }
    private static void FinishStage(string stage, DataDirectoryLease lease, Exception? failure, string? temporary = null)
    {
        var cleanupErrors = new List<Exception>();
        try { if (temporary is not null && File.Exists(temporary)) File.Delete(temporary); }
        catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException) { cleanupErrors.Add(cleanup); }
        try { BackupStageCleanup.Clean(stage, lease); }
        catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException) { cleanupErrors.Add(cleanup); }
        if (cleanupErrors.Count != 0)
        {
            if (failure is not null) cleanupErrors.Insert(0, failure);
            throw new AggregateException("Backup cleanup failed; retained staging data may require review.", cleanupErrors);
        }
    }
    private static void ExtractValidated(string archive, string stage)
    {
        using var archiveLease = DataDirectoryLease.Open(Path.GetDirectoryName(Path.GetFullPath(archive))!);
        archive = Path.Combine(archiveLease.DirectoryPath, Path.GetFileName(archive));
        using var file = File.OpenRead(archive);
        if (file.Length > MaximumDatabaseBytes + 1048576) throw new InvalidDataException("Backup archive is too large.");
        BackupZipDirectory.Validate(file); // Bound central-directory work before ZipArchive materializes its entries.
        using var zip = new ZipArchive(file, ZipArchiveMode.Read);
        var names = new[] { "library.db", "settings.json", "manifest.json" };
        if (zip.Entries.Count != names.Length || names.Any(name => zip.Entries.Count(e => e.FullName == name) != 1)) throw new InvalidDataException("Backup entries are missing, duplicated or unexpected.");
        var manifestEntry = zip.GetEntry("manifest.json")!;
        if (manifestEntry.Length is <= 0 or > 65536) throw new InvalidDataException("Backup manifest size is invalid.");
        var manifestBytes = new byte[checked((int)manifestEntry.Length)];
        using (var input = manifestEntry.Open())
        {
            try { input.ReadExactly(manifestBytes); }
            catch (EndOfStreamException error) { throw new InvalidDataException("Backup manifest is truncated.", error); }
            if (input.ReadByte() >= 0) throw new InvalidDataException("Backup manifest expanded beyond its declared size.");
        }
        Manifest manifest;
        manifest = JsonSerializer.Deserialize<Manifest>(manifestBytes, new JsonSerializerOptions { MaxDepth = 8 }) ?? throw new InvalidDataException("Backup manifest invalid.");
        if (manifest.SchemaVersion != 1 || manifest.Files is null || manifest.Files.Length != 2 || manifest.Files.Any(i => i is null) || names.Take(2).Any(name => manifest.Files.Count(i => i.Path == name) != 1)) throw new InvalidDataException("Backup manifest schema/files invalid.");
        foreach (var item in manifest.Files)
        {
            var entry = zip.GetEntry(item.Path)!;
            var maximum = item.Path == "settings.json" ? 65536 : MaximumDatabaseBytes;
            if (item.Bytes <= 0 || item.Bytes > maximum || entry.Length != item.Bytes || item.Sha256 is null || item.Sha256.Length != 64) throw new InvalidDataException("Backup file size/hash declaration invalid.");
            var path = Path.Combine(stage, item.Path);
            using (var input = entry.Open()) using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                var buffer = new byte[65536]; long copied = 0; int read;
                while ((read = input.Read(buffer)) != 0)
                {
                    copied += read;
                    if (copied > item.Bytes) throw new InvalidDataException("Backup expanded beyond its declared size.");
                    output.Write(buffer, 0, read); hash.AppendData(buffer, 0, read);
                }
                if (copied != item.Bytes) throw new InvalidDataException("Backup file truncated."); output.Flush(true);
                if (Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant() != item.Sha256) throw new InvalidDataException("Backup checksum mismatch.");
            }
        }
        new SettingsFile(stage).Load();
        using var database = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.Combine(stage, "library.db"), Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        database.Open(); DatabaseRecovery.ConfigureReadLimits(database); DatabaseRecovery.Validate(database);
    }
    private static void Install(string directory, string stage)
    {
        var database = Path.Combine(directory, "library.db");
        using var ownership = new FileStream(database + ".owner.lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var id = Guid.NewGuid().ToString("N");
        var originals = new[] { "library.db", "library.db-wal", "library.db-shm", "settings.json", "settings.json.bak" };
        foreach (var name in originals) if (Directory.Exists(Path.Combine(directory, name))) throw new IOException("A data filename is occupied by a directory; originals preserved.");
        var moved = new List<(string Original, string Preserved)>(); var installed = new List<string>();
        try
        {
            foreach (var name in originals)
            {
                var original = Path.Combine(directory, name);
                if (!File.Exists(original)) continue;
                var preserved = name.StartsWith("library.db", StringComparison.Ordinal) ? database + ".preserved-" + id + name["library.db".Length..] : Path.Combine(directory, name + ".preserved-" + id);
                File.Move(original, preserved); moved.Add((original, preserved));
            }
            foreach (var name in new[] { "library.db", "settings.json" })
            {
                var target = Path.Combine(directory, name); File.Move(Path.Combine(stage, name), target); installed.Add(target);
            }
        }
        catch (Exception failure)
        {
            var errors = new List<Exception> { failure };
            foreach (var target in installed.AsEnumerable().Reverse()) try { File.Delete(target); } catch (Exception error) { errors.Add(error); }
            foreach (var (original, preserved) in moved.AsEnumerable().Reverse())
                try { File.Move(preserved, original); } catch (Exception error) { errors.Add(error); }
            if (errors.Count > 1) throw new AggregateException("Restore interrupted; retained originals require recovery before reopening.", errors);
            throw;
        }
    }
}
