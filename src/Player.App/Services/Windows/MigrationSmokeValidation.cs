using System.IO;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Player.App.Services.Storage;
using Player.App.ViewModels;
using Player.Core.Library;
using Player.Core.Playback;

namespace Player.App.Services.Windows;

/// <summary>Marker-guarded validation of an actual production migration, not a separate diagnostic transaction.</summary>
public static class MigrationSmokeValidation
{
    public static async Task SeedAsync(string directory, string fixture)
    {
        var path = Path.Combine(directory, "library.db");
        if (File.Exists(path)) throw new IOException("Migration validation requires a new owned database.");
        var tab = Guid.NewGuid(); var track = new MediaTrack(Guid.NewGuid(), fixture, "Owned migration signal");
        var first = new PlaylistEntry(Guid.NewGuid(), track); var second = new PlaylistEntry(Guid.NewGuid(), track, false);
        var state = new LibraryState([new(tab, "Schema one Музыка", [first, second])], new(tab, tab, first, TimeSpan.FromSeconds(0.5).Ticks));
        await using (var store = new SqlitePlayerStore(path)) { await store.LoadAsync(); await store.SaveAsync(state, true); }
        using (var connection = Connect(path))
        {
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA journal_mode=DELETE; DROP TABLE MediaIndex; DROP TABLE LibraryRoots; DROP TABLE TrackStatistics; DROP TABLE ListeningHistory; ALTER TABLE PlaylistEntries DROP COLUMN AddedUtcTicks; PRAGMA user_version=1;";
            command.ExecuteNonQuery();
        }
        new SettingsFile(directory).Save(new(Volume: 17));
        File.WriteAllText(Path.Combine(directory, "migration-expected.json"), JsonSerializer.Serialize(state));
    }
    public static void PauseBeforeCommit(string directory, string output)
    {
        // This executes on the real SQLite owner, after production DDL and before its Commit.
        File.WriteAllText(Path.Combine(output, "migration-checkpoint.json"), JsonSerializer.Serialize(new { Stage = "production-ddl-before-commit", Database = Path.GetFileName(directory), PendingSchema = 2 }));
        Thread.Sleep(Timeout.Infinite);
        throw new InvalidOperationException("Migration checkpoint unexpectedly resumed.");
    }
    public static void AssertRolledBack(string directory)
    {
        using var connection = Connect(Path.Combine(directory, "library.db")); using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version"; Check(Convert.ToInt32(command.ExecuteScalar()) == 1, "Interrupted migration did not roll back schema version.");
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE name IN ('LibraryRoots','MediaIndex','TrackStatistics','ListeningHistory')";
        Check(Convert.ToInt32(command.ExecuteScalar()) == 0, "Interrupted migration retained partial production tables.");
        command.CommandText = "SELECT COUNT(*) FROM pragma_table_info('PlaylistEntries') WHERE name='AddedUtcTicks'";
        Check(Convert.ToInt32(command.ExecuteScalar()) == 0, "Interrupted ALTER TABLE did not roll back.");
    }
    public static async Task<object> VerifyAsync(PlayerViewModel model, string directory)
    {
        var expected = JsonSerializer.Deserialize<LibraryState>(File.ReadAllText(Path.Combine(directory, "migration-expected.json")))!;
        Check(JsonSerializer.Serialize(model.Playlists.Select(p => p.Capture()).ToArray()) == JsonSerializer.Serialize(expected.Playlists), "Migration changed committed identities/order/duplicates/flags.");
        Check(model.Snapshot.EntryId == expected.Session.ActiveEntry!.Id && model.CanSeek && !model.IsPlaying && Math.Abs(model.SeekPosition - 0.5) < 0.05, "Migration restart failed native preparation/position/no-autoplay.");
        var backups = Directory.GetFiles(directory, "library.db.pre-schema2-*.db");
        Check(backups.Length >= 2, "Each real migration attempt did not retain its schema-one backup.");
        foreach (var path in backups)
        {
            using var connection = Connect(path); using var command = connection.CreateCommand(); command.CommandText = "PRAGMA integrity_check";
            Check((string?)command.ExecuteScalar() == "ok", "Migration backup integrity failed.");
            command.CommandText = "PRAGMA user_version"; Check(Convert.ToInt32(command.ExecuteScalar()) == 1, "Migration backup is not schema one.");
        }
        await model.SaveNowAsync();
        return new { Status = "production-migration-kill-restart-passed", InterruptedActualProductionDdl = true, SchemaAndAlterRolledBackBeforeRestart = true,
            MigrationRetryCompleted = true, SchemaOneBackups = backups.Length, IdentitiesOrderDuplicatesAndFlags = true, NativePreparation = true, NoAutoplay = true,
            PowerLoss = "not-run: forced process termination is not power loss" };
    }
    private static SqliteConnection Connect(string path) { var value = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadWrite, Pooling = false }.ToString()); value.Open(); return value; }
    private static void Check(bool value, string message) { if (!value) throw new InvalidDataException(message); }
}
