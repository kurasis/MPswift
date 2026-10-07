using System.IO.Compression;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Player.App.Services.Storage;
using Player.Core.Library;
using Player.Core.Playback;

namespace Player.Core.Tests;

public sealed class BackupBundleTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "player-bundle-tests-" + Guid.NewGuid().ToString("N"));
    private string Source => Path.Combine(_directory, "source");
    private string Target => Path.Combine(_directory, "target");
    private string Archive => Path.Combine(_directory, "complete.zip");
    private LibraryState _state = null!;
    private readonly PlayerSettings _settings = new(Volume: 23, Muted: true, Language: "ru");
    public BackupBundleTests() { Directory.CreateDirectory(Source); Directory.CreateDirectory(Target); }
    private async Task CreateAsync()
    {
        var entry = new PlaylistEntry(Guid.NewGuid(), new MediaTrack(Guid.NewGuid(), @"C:\Owned\Музыка.wav", "Owned song"));
        var playlist = Guid.NewGuid(); _state = new([new(playlist, "Музыка", [entry, entry with { Id = Guid.NewGuid(), Enabled = false }])], new(playlist, playlist, entry, 5000000));
        await using var store = new SqlitePlayerStore(Path.Combine(Source, "library.db")); await store.LoadAsync(); await store.SaveAsync(_state, true);
        await store.SetRatingAsync(entry.Track.Id, 4);
        var root = new LibraryRoot(Guid.NewGuid(), @"C:\Owned"); await store.PutRootAsync(root);
        await store.UpsertFilesAsync([new(Guid.NewGuid(), root.Id, entry.Track.Path, 123, 456, true, "generation", entry.Track)]);
        File.WriteAllText(Path.Combine(Source, "private-music.wav"), "exclude"); Directory.CreateDirectory(Path.Combine(Source, "Logs"));
        // The source connection is still live in WAL mode when the complete snapshot is taken.
        await BackupBundle.CreateAsync(store, _settings, Archive);
    }
    [Fact]
    public async Task LiveCompleteBackupRestoresDatabaseSettingsIndexAndRatingsAndRetainsCurrentData()
    {
        await CreateAsync();
        var database = Path.Combine(Target, "library.db"); File.WriteAllText(database, "damaged original");
        new SettingsFile(Target).Save(new(Volume: 87));
        var originalSettings = File.ReadAllBytes(Path.Combine(Target, "settings.json"));
        using (var zip = ZipFile.OpenRead(Archive)) Assert.Equal(new[] { "library.db", "manifest.json", "settings.json" }, zip.Entries.Select(e => e.FullName).Order().ToArray());
        await BackupBundle.RestoreAsync(Target, Archive);
        Assert.Equal("damaged original", File.ReadAllText(Directory.GetFiles(Target, "library.db.preserved-*").Single()));
        Assert.Equal(originalSettings, File.ReadAllBytes(Directory.GetFiles(Target, "settings.json.preserved-*").Single()));
        Assert.Equal(JsonSerializer.Serialize(_settings.Validate()), JsonSerializer.Serialize(new SettingsFile(Target).Load()));
        await using var restored = new SqlitePlayerStore(database); var state = await restored.LoadAsync();
        Assert.Equal(_state.Playlists[0].Entries, state.Playlists[0].Entries); Assert.Equal(_state.Session, state.Session);
        Assert.Equal(4, (await restored.GetStatisticsAsync([_state.Session.ActiveEntry!.Track.Id]))[0].Rating);
        Assert.Single((await restored.SearchAsync("Музыка")).Files);
    }
    [Fact]
    public async Task BackupNeverOverwritesAnExistingArchive()
    {
        await CreateAsync(); var before = File.ReadAllBytes(Archive);
        await using var store = new SqlitePlayerStore(Path.Combine(Source, "library.db")); await store.LoadAsync();
        await Assert.ThrowsAsync<IOException>(() => BackupBundle.CreateAsync(store, _settings, Archive)); Assert.Equal(before, File.ReadAllBytes(Archive));
    }
    [Theory]
    [InlineData("checksum")]
    [InlineData("duplicate")]
    [InlineData("traversal")]
    [InlineData("missing")]
    [InlineData("schema")]
    [InlineData("size")]
    [InlineData("settings")]
    [InlineData("database")]
    [InlineData("references")]
    public async Task InvalidArchiveIsRejectedBeforeTouchingCurrentData(string damage)
    {
        await CreateAsync();
        using (var zip = ZipFile.Open(Archive, ZipArchiveMode.Update))
        {
            var manifest = JsonSerializer.Deserialize<BackupBundle.Manifest>(Read(zip.GetEntry("manifest.json")!))!;
            if (damage == "checksum") Rewrite(zip, "settings.json", Read(zip.GetEntry("settings.json")!).Replace("\"Volume\":23", "\"Volume\":24", StringComparison.Ordinal));
            if (damage is "duplicate" or "traversal") { using var writer = new StreamWriter(zip.CreateEntry(damage == "duplicate" ? "settings.json" : "../outside").Open()); writer.Write("reject"); }
            if (damage == "missing") zip.GetEntry("settings.json")!.Delete();
            if (damage == "schema") manifest = manifest with { SchemaVersion = 2 };
            if (damage == "size") manifest = manifest with { Files = manifest.Files.Select(i => i with { Bytes = long.MaxValue }).ToArray() };
            if (damage is "settings" or "database" or "references")
            {
                var name = damage == "settings" ? "settings.json" : "library.db";
                var path = Path.Combine(_directory, name);
                if (damage == "settings") File.WriteAllText(path, "{\"SchemaVersion\":99}");
                else
                {
                    using (var input = zip.GetEntry(name)!.Open()) using (var output = File.Create(path)) input.CopyTo(output);
                    using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString()); connection.Open(); using var command = connection.CreateCommand();
                    command.CommandText = damage == "database" ? "PRAGMA user_version=99" : "PRAGMA foreign_keys=OFF; UPDATE PlaylistEntries SET TrackId='missing'"; command.ExecuteNonQuery();
                }
                var bytes = File.ReadAllBytes(path); zip.GetEntry(name)!.Delete(); using (var output = zip.CreateEntry(name).Open()) output.Write(bytes);
                manifest = manifest with { Files = manifest.Files.Select(i => i.Path == name ? i with { Bytes = bytes.Length, Sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() } : i).ToArray() };
            }
            Rewrite(zip, "manifest.json", JsonSerializer.Serialize(manifest));
        }
        File.WriteAllText(Path.Combine(Target, "library.db"), "preserve db"); new SettingsFile(Target).Save(new(Volume: 81));
        var before = Directory.GetFiles(Target).ToDictionary(p => Path.GetFileName(p)!, File.ReadAllBytes);
        if (damage == "checksum") Assert.Contains("checksum", (await Assert.ThrowsAsync<InvalidDataException>(() => BackupBundle.RestoreAsync(Target, Archive))).Message);
        else await Assert.ThrowsAnyAsync<Exception>(() => BackupBundle.RestoreAsync(Target, Archive));
        foreach (var item in before) Assert.Equal(item.Value, File.ReadAllBytes(Path.Combine(Target, item.Key!)));
        Assert.Empty(Directory.GetDirectories(Target)); Assert.False(File.Exists(Path.Combine(_directory, "outside")));
    }
    [Fact]
    public async Task ActiveStoreOwnershipRejectsRestoreAndLeavesCurrentSessionUsable()
    {
        await CreateAsync(); await using var owner = new SqlitePlayerStore(Path.Combine(Target, "library.db")); var before = await owner.LoadAsync();
        new SettingsFile(Target).Save(new(Volume: 77));
        await Assert.ThrowsAsync<IOException>(() => BackupBundle.RestoreAsync(Target, Archive));
        Assert.Equal(before.Session, (await owner.LoadAsync()).Session); Assert.Equal(77, new SettingsFile(Target).Load().Volume);
    }
    [Fact]
    public async Task OccupiedSettingsPathDoesNotMoveTheCurrentDatabase()
    {
        await CreateAsync(); File.WriteAllText(Path.Combine(Target, "library.db"), "preserve"); Directory.CreateDirectory(Path.Combine(Target, "settings.json"));
        await Assert.ThrowsAsync<IOException>(() => BackupBundle.RestoreAsync(Target, Archive));
        Assert.Equal("preserve", File.ReadAllText(Path.Combine(Target, "library.db"))); Assert.Empty(Directory.GetFiles(Target, "*.preserved-*"));
    }
    [Fact]
    public async Task TruncatedManifestIsRejectedBeforeInstallingUserData()
    {
        await CreateAsync();
        // A valid JSON body with a forged larger central-directory size was previously accepted.
        var bytes = File.ReadAllBytes(Archive);
        var end = bytes.Length - 22;
        while (BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(end, 4)) != 0x06054b50) end--;
        var cursor = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(end + 16, 4)));
        var patched = false;
        while (BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(cursor, 4)) == 0x02014b50)
        {
            var nameLength = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(cursor + 28, 2));
            if (System.Text.Encoding.UTF8.GetString(bytes, cursor + 46, nameLength) == "manifest.json")
            {
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(cursor + 24, 4), 65536u);
                patched = true; break;
            }
            cursor += 46 + nameLength + BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(cursor + 30, 2)) + BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(cursor + 32, 2));
        }
        Assert.True(patched); File.WriteAllBytes(Archive, bytes);
        File.WriteAllText(Path.Combine(Target, "library.db"), "owned original database");
        new SettingsFile(Target).Save(new(Volume: 81));
        var settings = File.ReadAllBytes(Path.Combine(Target, "settings.json"));
        await Assert.ThrowsAnyAsync<InvalidDataException>(() => BackupBundle.RestoreAsync(Target, Archive));
        Assert.Equal("owned original database", File.ReadAllText(Path.Combine(Target, "library.db")));
        Assert.Equal(settings, File.ReadAllBytes(Path.Combine(Target, "settings.json")));
        Assert.Empty(Directory.GetDirectories(Target)); Assert.Empty(Directory.GetFiles(Target, "*.preserved-*"));
    }
    private static string Read(ZipArchiveEntry entry) { using var reader = new StreamReader(entry.Open()); return reader.ReadToEnd(); }
    private static void Rewrite(ZipArchive zip, string name, string value) { zip.GetEntry(name)?.Delete(); using var writer = new StreamWriter(zip.CreateEntry(name).Open()); writer.Write(value); }
    public void Dispose() { SqliteConnection.ClearAllPools(); Directory.Delete(_directory, true); }
}
