using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Player.App.Services.Storage;
using Player.Core.Library;
using Player.Core.Playback;

namespace Player.Core.Tests;

public sealed class SecurityDatabaseTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ValidationBudgetInterruptsWorkAndRemovesItsCallback(bool useDeadline)
    {
        using var connection = new SqliteConnection("Data Source=:memory:"); connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "WITH RECURSIVE n(x) AS (VALUES(0) UNION ALL SELECT x+1 FROM n WHERE x<100000) SELECT sum(x) FROM n";
        using (new DatabaseValidationBudget(connection, useDeadline ? long.MaxValue : 1000, useDeadline ? TimeSpan.Zero : TimeSpan.FromMinutes(1)))
            Assert.Equal(9, Assert.Throws<SqliteException>(() => command.ExecuteScalar()).SqliteErrorCode);
        Assert.Equal(5_000_050_000L, command.ExecuteScalar());
    }

    [Fact]
    public async Task ExcessiveSchemaIsRejectedBeforeIntegrityScanningWithoutChangingData()
    {
        var directory = Path.Combine(Path.GetTempPath(), "mpswift-schema-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory); var path = Path.Combine(directory, "library.db");
        try
        {
            await using (var store = new SqlitePlayerStore(path)) await store.LoadAsync();
            using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString()))
            {
                connection.Open(); using var transaction = connection.BeginTransaction();
                for (var i = 0; i < 130; i++)
                { using var command = connection.CreateCommand(); command.Transaction = transaction; command.CommandText = $"CREATE TABLE Owned{i}(Value TEXT)"; command.ExecuteNonQuery(); }
                transaction.Commit();
            }
            var before = SHA256.HashData(File.ReadAllBytes(path));
            await using (var store = new SqlitePlayerStore(path)) await Assert.ThrowsAsync<InvalidDataException>(() => store.LoadAsync());
            Assert.Equal(before, SHA256.HashData(File.ReadAllBytes(path)));
            Assert.Empty(Directory.GetFiles(directory, "*.preserved-*"));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task ProductionSaveInterruptsExpensiveUntrustedTriggerAndRetainsCommittedState()
    {
        var directory = Path.Combine(Path.GetTempPath(), "mpswift-trigger-budget-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory); var path = Path.Combine(directory, "library.db");
        try
        {
            LibraryState committed;
            await using (var store = new SqlitePlayerStore(path)) { committed = await store.LoadAsync(); await store.SaveAsync(committed, true); }
            using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString()))
            {
                connection.Open(); using var command = connection.CreateCommand();
                command.CommandText = "CREATE TRIGGER OwnedExpensiveTrigger BEFORE INSERT ON Session BEGIN SELECT sum(x) FROM (WITH RECURSIVE n(x) AS (VALUES(0) UNION ALL SELECT x+1 FROM n WHERE x<1000000000) SELECT x FROM n); END";
                command.ExecuteNonQuery();
            }
            await using (var store = new SqlitePlayerStore(path))
            {
                await store.LoadAsync();
                var changed = committed with { Session = committed.Session with { SelectedPlaylistId = committed.Playlists[0].Id } };
                var error = await Assert.ThrowsAsync<SqliteException>(() => store.SaveAsync(changed, false).WaitAsync(TimeSpan.FromSeconds(90)));
                Assert.Equal(9, error.SqliteErrorCode);
                Assert.Equal(committed.Session, (await store.LoadAsync()).Session);
            }
        }
        finally { Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData("Session")]
    [InlineData("Tracks")]
    public async Task OversizedDatabaseCellsAreRefusedByNativeLimitBeforeManagedStringAllocation(string table)
    {
        var directory = Path.Combine(Path.GetTempPath(), "mpswift-security-db-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory); var path = Path.Combine(directory, "library.db");
        try
        {
            var track = new MediaTrack(Guid.NewGuid(), @"C:\Owned\song.wav", "Owned"); var tab = Guid.NewGuid();
            var state = new LibraryState([new(tab, "Owned", [new(Guid.NewGuid(), track)])], new(tab, null, null, 0));
            await using (var store = new SqlitePlayerStore(path)) { await store.LoadAsync(); await store.SaveAsync(state, true); }
            using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString()))
            {
                connection.Open(); using var command = connection.CreateCommand();
                // Only a disposable test DB; SQLite creates 60 MiB, no user files or unbounded resource load.
                command.CommandText = "UPDATE " + table + " SET Json=printf('%.*c', $bytes, ' ')";
                command.Parameters.AddWithValue("$bytes", 60 * 1024 * 1024); command.ExecuteNonQuery();
            }
            byte[] before; using (var file = File.OpenRead(path)) before = SHA256.HashData(file);
            await using (var store = new SqlitePlayerStore(path))
            {
                var error = await Assert.ThrowsAsync<SqliteException>(() => store.LoadAsync());
                Assert.Equal(18, error.SqliteErrorCode); // SQLITE_TOOBIG, rather than a later managed JSON-size error.
            }
            using (var file = File.OpenRead(path)) Assert.Equal(before, SHA256.HashData(file));
            using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString()))
            {
                connection.Open(); using var command = connection.CreateCommand(); command.CommandText = "UPDATE " + table + " SET Json=$json";
                command.Parameters.AddWithValue("$json", table == "Tracks" ? JsonSerializer.Serialize(track) : JsonSerializer.Serialize(state.Session)); command.ExecuteNonQuery();
            }
            await using (var store = new SqlitePlayerStore(path)) Assert.Equal(state.Session, (await store.LoadAsync()).Session);
        }
        finally { Directory.Delete(directory, true); }
    }
}
