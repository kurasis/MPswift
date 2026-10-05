using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using Player.App.Services.Storage;
using Player.Core.Library;
using Player.Core.Playback;
using Player.Core.Waveforms;

namespace Player.Core.Tests;

public sealed class StorageAndWaveformTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "player-tests-" + Guid.NewGuid().ToString("N"));
    public StorageAndWaveformTests() => Directory.CreateDirectory(_directory);
    private string Database => Path.Combine(_directory, "library.db");
    private static PlaylistEntry Entry(string title = "Музыка '); DROP TABLE Tracks; --") => new(Guid.NewGuid(), new MediaTrack(Guid.NewGuid(), @"C:\Music\song.wav", title));
    private static LibraryState State(params PlaylistEntry[] entries)
    { var id = Guid.NewGuid(); return new([new(id, "Default ' %_", entries)], new(id, id, entries.FirstOrDefault(), TimeSpan.FromSeconds(1).Ticks)); }
    [Fact]
    public async Task DatabaseRoundTripPreservesUnicodeDuplicatesEnabledOrderAndDetachedSession()
    {
        var entry = Entry(); var duplicate = entry with { Id = Guid.NewGuid(), Enabled = false };
        var state = State(duplicate, entry);
        var detached = entry with { Id = Guid.NewGuid() }; state = state with { Session = state.Session with { ActiveEntry = detached } };
        await using (var store = new SqlitePlayerStore(Database)) { await store.LoadAsync(); await store.SaveAsync(state, true); }
        await using var reopened = new SqlitePlayerStore(Database);
        var restored = await reopened.LoadAsync();
        Assert.Equal(state.Playlists[0].Id, restored.Playlists[0].Id);
        Assert.Equal(state.Playlists[0].Name, restored.Playlists[0].Name);
        Assert.Equal(state.Playlists[0].Entries, restored.Playlists[0].Entries);
        Assert.Equal(state.Session, restored.Session);
        Assert.False(restored.Playlists[0].Entries[0].Enabled);
        Assert.Equal(entry.Track.Id, restored.Playlists[0].Entries[0].Track.Id);
    }
    [Fact]
    public async Task SecondWriterIsRejectedWithoutChangingTheActiveLibrary()
    {
        await using var first = new SqlitePlayerStore(Database); await first.LoadAsync();
        var state = State(Entry()); await first.SaveAsync(state, true);
        await using var second = new SqlitePlayerStore(Database);
        await Assert.ThrowsAsync<PlayerStoreInUseException>(() => second.LoadAsync());
        Assert.Equal(state.Playlists[0].Entries, (await first.LoadAsync()).Playlists[0].Entries);
    }
    [Fact]
    public async Task SessionOnlySaveDoesNotReplacePlaylistRows()
    {
        await using var store = new SqlitePlayerStore(Database); await store.LoadAsync();
        var original = State(Entry()); await store.SaveAsync(original, true);
        var update = original with { Session = original.Session with { PositionTicks = TimeSpan.FromSeconds(4).Ticks } };
        await store.SaveAsync(update with { Playlists = [original.Playlists[0] with { Entries = [] }] }, false);
        var restored = await store.LoadAsync();
        Assert.Single(restored.Playlists[0].Entries); Assert.Equal(update.Session, restored.Session);
    }
    [Fact]
    public async Task FailedMultiEntrySaveRollsBackAllTablesAndSession()
    {
        await using var store = new SqlitePlayerStore(Database); await store.LoadAsync();
        var original = State(Entry()); await store.SaveAsync(original, true);
        using (var connection = new SqliteConnection("Data Source=" + Database))
        {
            connection.Open(); using var command = connection.CreateCommand();
            command.CommandText = "CREATE TRIGGER RejectDisabled BEFORE INSERT ON PlaylistEntries WHEN NEW.Enabled=0 BEGIN SELECT RAISE(ABORT,'injected constraint'); END";
            command.ExecuteNonQuery();
        }
        var failed = State(Entry() with { Enabled = false });
        await Assert.ThrowsAsync<SqliteException>(() => store.SaveAsync(failed, true));
        var restored = await store.LoadAsync();
        Assert.Equal(original.Playlists[0].Entries, restored.Playlists[0].Entries);
        Assert.Equal(original.Session, restored.Session);
    }
    [Fact]
    public async Task LiveWalBackupHasLatestCommittedRowsAndCanBeReopened()
    {
        await using var store = new SqlitePlayerStore(Database); await store.LoadAsync();
        var state = State(Entry()); await store.SaveAsync(state, true);
        var backup = Path.Combine(_directory, "backup.db"); await store.BackupAsync(backup);
        await using var restored = new SqlitePlayerStore(backup);
        Assert.Equal(state.Playlists[0].Entries, (await restored.LoadAsync()).Playlists[0].Entries);
        await Assert.ThrowsAsync<IOException>(() => store.BackupAsync(backup));
    }
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task UnknownSchemaNeverRecreatesOrDowngradesUserData(int version)
    {
        using (var connection = new SqliteConnection("Data Source=" + Database))
        { connection.Open(); using var command = connection.CreateCommand(); command.CommandText = "CREATE TABLE Sentinel(Value TEXT); INSERT INTO Sentinel VALUES('preserve'); PRAGMA user_version=" + version; command.ExecuteNonQuery(); }
        var before = SHA256.HashData(File.ReadAllBytes(Database));
        await using var store = new SqlitePlayerStore(Database);
        if (version == 0) await Assert.ThrowsAsync<InvalidDataException>(() => store.LoadAsync());
        else await Assert.ThrowsAsync<NewerDatabaseSchemaException>(() => store.LoadAsync());
        Assert.Equal(before, SHA256.HashData(File.ReadAllBytes(Database)));
    }
    [Fact]
    public async Task CorruptDatabaseRemainsByteForByteIntact()
    {
        File.WriteAllText(Database, "preserve this corrupt database");
        var before = File.ReadAllBytes(Database);
        await using var store = new SqlitePlayerStore(Database);
        await Assert.ThrowsAsync<SqliteException>(() => store.LoadAsync());
        Assert.Equal(before, File.ReadAllBytes(Database));
    }
    [Fact]
    public async Task ExplicitBackupRestoreValidatesFirstAndPreservesOriginalCorruptBytes()
    {
        var entry = Entry(); var state = State(entry); var backup = Path.Combine(_directory, "backup.db");
        await using (var store = new SqlitePlayerStore(Database)) { await store.LoadAsync(); await store.SaveAsync(state, true); await store.BackupAsync(backup); }
        File.WriteAllText(Database, "preserve corrupted original");
        DatabaseRecovery.Restore(Database, backup);
        Assert.Equal("preserve corrupted original", File.ReadAllText(Directory.GetFiles(_directory, "library.db.preserved-*").Single()));
        await using var restored = new SqlitePlayerStore(Database);
        Assert.Equal(entry, (await restored.LoadAsync()).Playlists[0].Entries[0]);
    }
    [Fact]
    public void SettingsRoundTripClampsRangesAndKeepsPreviousGoodBackup()
    {
        var file = new SettingsFile(_directory);
        file.Save(new PlayerSettings(Volume: 25, Muted: true)); file.Save(new PlayerSettings(Volume: 150, WaveformCacheMiB: 1));
        Assert.Equal(100, file.Load().Volume); Assert.Equal(16, file.Load().WaveformCacheMiB);
        Assert.Contains("25", File.ReadAllText(Path.Combine(_directory, "settings.json.bak")));
        File.WriteAllText(Path.Combine(_directory, "settings.json"), "{\"SchemaVersion\":2,\"Volume\":20}");
        Assert.Throws<InvalidDataException>(file.Load);
        Assert.Contains("\"SchemaVersion\":2", File.ReadAllText(Path.Combine(_directory, "settings.json")));
    }
    [Fact]
    public void OppositePhaseChannelsRetainPeaksAcrossChunkBoundaries()
    {
        var accumulator = new WaveformAccumulator(48000, 2, 960);
        var samples = Enumerable.Range(0, 1920).Select(i => i % 2 == 0 ? 0.5f : -0.5f).ToArray();
        accumulator.Add(samples.AsSpan(0, 204)); accumulator.Add(samples.AsSpan(204));
        var result = accumulator.Complete();
        Assert.Equal(new[] { -0.5f, -0.5f }, result.Minimum); Assert.Equal(new[] { 0.5f, 0.5f }, result.Maximum);
    }
    [Fact]
    public void MultiHourAccumulatorHasBoundedStorageAndCoarserResolution()
    {
        var accumulator = new WaveformAccumulator(192000, 8, 192000L * 60 * 60 * 8);
        accumulator.Add(new float[8]); var data = accumulator.Complete();
        Assert.True(data.FramesPerBucket > 1920); Assert.Single(data.Minimum);
        Assert.Throws<InvalidDataException>(() => accumulator.Add([float.NaN, 0, 0, 0, 0, 0, 0, 0]));
    }
    [Fact]
    public void CacheRoundTripRejectsCorruptionAndOversizedCountBeforeAllocation()
    {
        var cache = new WaveformCache(_directory); var key = WaveformCache.Fingerprint("C:/Музыка.wav", 123, 456, "decoder1");
        var data = new WaveformData(48000, 2, 480, 960, [-0.2f, -0.5f], [0.2f, 0.5f]);
        cache.Write(key, data); var restored = cache.Read(key)!;
        Assert.Equal(data.Minimum, restored.Minimum); Assert.Equal(data.Maximum, restored.Maximum);
        var path = Path.Combine(_directory, key + ".peaks"); var bytes = File.ReadAllBytes(path);
        bytes[^1] ^= 1; File.WriteAllBytes(path, bytes); Assert.Null(cache.Read(key));
        cache.Write(key, data); bytes = File.ReadAllBytes(path); BitConverter.GetBytes(int.MaxValue).CopyTo(bytes, 68); File.WriteAllBytes(path, bytes);
        Assert.Null(cache.Read(key));
        Assert.NotEqual(key, WaveformCache.Fingerprint("C:/Музыка.wav", 124, 456, "decoder1"));
        Assert.NotEqual(key, WaveformCache.Fingerprint("C:/Музыка.wav", 123, 457, "decoder1"));
        Assert.NotEqual(key, WaveformCache.Fingerprint("C:/Музыка.wav", 123, 456, "decoder2"));
    }
    [Fact]
    public void CacheEvictionAndClearOnlyRemoveDisposablePeaks()
    {
        var cache = new WaveformCache(_directory, 130);
        var data = new WaveformData(48000, 1, 480, 480, [-0.2f], [0.2f]);
        File.WriteAllText(Path.Combine(_directory, "library.db"), "preserve");
        cache.Write(new string('a', 64), data); cache.Write(new string('b', 64), data);
        Assert.Single(Directory.GetFiles(_directory, "*.peaks")); cache.Clear();
        Assert.Empty(Directory.GetFiles(_directory, "*.peaks")); Assert.Equal("preserve", File.ReadAllText(Database));
    }
    [Fact]
    public async Task SessionRestorePreparesAndSeeksWithoutEverStartingPlayback()
    {
        var backend = new RestoreBackend(); var player = new SerializedAudioPlayer(() => backend);
        await using var coordinator = new PlaybackCoordinator(player);
        var entry = Entry(); coordinator.SetEntries([entry]);
        Assert.True(await coordinator.RestoreAsync(entry, TimeSpan.FromSeconds(3)));
        Assert.Equal(PlaybackState.Stopped, player.Snapshot.State); Assert.Equal(TimeSpan.FromSeconds(3), player.Snapshot.Position); Assert.False(backend.Played);
    }
    private sealed class RestoreBackend : IAudioBackend
    {
        private TimeSpan _position;
        public bool Played { get; private set; }
        public AudioSourceInfo Open(string path) => new(TimeSpan.FromSeconds(10), new(48000, 2, "test"), true);
        public void Play() => Played = true;
        public void Pause() { }
        public void Stop() => _position = TimeSpan.Zero;
        public void Seek(TimeSpan position) => _position = position;
        public void SetVolume(double volume, bool muted) { }
        public BackendPosition ReadPosition() => new(_position, false);
        public void CloseSource() { }
        public void Dispose() { }
    }
    public void Dispose() { SqliteConnection.ClearAllPools(); Directory.Delete(_directory, true); }
}
