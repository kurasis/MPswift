using Player.App.Services.Storage;
using Microsoft.Data.Sqlite;

namespace Player.Core.Tests;

public sealed class DataFilePolicyTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "mp-data-policy-" + Guid.NewGuid().ToString("N"));
    public DataFilePolicyTests() => Directory.CreateDirectory(_root);
    [Fact]
    public async Task PinnedSqliteDependencyPositiveControlWritesThroughAFileLink()
    {
        var outside = Path.Combine(_root, "outside.db");
        await using (var store = new SqlitePlayerStore(outside)) { await store.LoadAsync(); await store.CheckpointAsync(); }
        var alias = Path.Combine(_root, "alias.db"); File.CreateSymbolicLink(alias, outside);
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = alias, Mode = SqliteOpenMode.ReadWrite, Pooling = false }.ToString()))
        {
            connection.Open(); using var command = connection.CreateCommand();
            command.CommandText = "UPDATE Playlists SET Name='Owned link control'"; Assert.Equal(1, command.ExecuteNonQuery());
        }
        await using var reopened = new SqlitePlayerStore(outside);
        Assert.Equal("Owned link control", (await reopened.LoadAsync()).Playlists[0].Name);
    }
    [Fact]
    public async Task ReadOnlyOwnedSnapshotUsesProductionLoaderWithoutChangingDatabaseBytes()
    {
        var path = Path.Combine(_root, "snapshot.db");
        await using (var store = new SqlitePlayerStore(path)) { await store.LoadAsync(); await store.CheckpointAsync(); }
        var bytes = File.ReadAllBytes(path);
        using var pin = DataFileLease.OpenExisting(path);
        await using (var validator = new SqlitePlayerStore(path) { ReadOnlyValidation = true }) Assert.Single((await validator.LoadAsync()).Playlists);
        Assert.Equal(bytes, File.ReadAllBytes(path));
    }
    [Theory]
    [InlineData("library.db")]
    [InlineData("library.db-wal")]
    [InlineData("library.db-shm")]
    public async Task LinkedMutableDatabaseFilesAreRejectedBeforeOpeningSqlite(string name)
    {
        var directory = Directory.CreateDirectory(Path.Combine(_root, "working")).FullName;
        var outside = Path.Combine(_root, "outside"); File.WriteAllText(outside, "owned outside sentinel");
        File.CreateSymbolicLink(Path.Combine(directory, name), outside);
        await using var store = new SqlitePlayerStore(Path.Combine(directory, "library.db"));
        await Assert.ThrowsAsync<IOException>(() => store.LoadAsync());
        Assert.Equal("owned outside sentinel", File.ReadAllText(outside));
    }
    [Theory]
    [InlineData("settings.json")]
    [InlineData("settings.json.bak")]
    public void LinkedSettingsAreRejectedAndBothFilesPreserved(string name)
    {
        var directory = Directory.CreateDirectory(Path.Combine(_root, "working")).FullName;
        var outside = Path.Combine(_root, "outside.json"); File.WriteAllText(outside, "{\"Volume\":21}");
        File.CreateSymbolicLink(Path.Combine(directory, name), outside);
        var settings = new SettingsFile(directory);
        Assert.Throws<IOException>(() => settings.Load());
        Assert.Throws<IOException>(() => settings.Save(new(Volume: 80)));
        Assert.Equal("{\"Volume\":21}", File.ReadAllText(outside)); Assert.Equal(outside, new FileInfo(Path.Combine(directory, name)).LinkTarget);
    }
    public void Dispose() => Directory.Delete(_root, true);
}
