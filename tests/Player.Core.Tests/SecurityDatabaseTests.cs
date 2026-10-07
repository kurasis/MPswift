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
