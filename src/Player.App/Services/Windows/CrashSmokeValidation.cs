using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Player.App.ViewModels;
using Player.Core.Library;
using Player.Core.Playback;

namespace Player.App.Services.Windows;

/// <summary>Owned diagnostic route: parent kills the actual WPF process while a validation transaction is uncommitted.</summary>
public static class CrashSmokeValidation
{
    public sealed record Checkpoint(PlaylistState[] Playlists, Guid Selected, Guid? Source, Guid? Active, double Position,
        double Volume, bool Muted, QueueItem[] Queue, RepeatMode Repeat, bool Shuffle, string FixtureSha256);
    private static string Hash(string path) { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)); }
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    public static async Task<object> RunAsync(PlayerViewModel model, string phase, string fixture, string directory, string output)
    {
        var signal = Path.Combine(output, "checkpoint.json");
        if (phase == "checkpoint")
        {
            model.CreatePlaylist("Crash checkpoint Музыка"); await model.AddPathsAsync([fixture, fixture]);
            Check(model.Entries.Count == 2, "Crash fixture import failed.");
            model.Entries[1].Enabled = false; model.Entries[0].Rating = 4;
            await model.PrepareAsync(model.Entries[0].Id); await model.CommitSeekAsync(0.5);
            model.Enqueue([model.Entries[0], model.Entries[0]], false);
            model.Repeat = RepeatMode.All; model.Shuffle = true; model.Volume = 23; model.Muted = true;
            // Let queued dispatcher/debounce/rating work settle, then commit an explicit production checkpoint.
            await Task.Delay(1000); await model.SaveNowAsync();
            Check(model.Snapshot.EntryId == model.Entries[0].Id && model.CanSeek && !model.IsPlaying, "Native crash checkpoint not prepared.");
            var expected = new Checkpoint(model.Playlists.Select(p => p.Capture()).ToArray(), model.SelectedPlaylist.Id,
                model.SourcePlaylistId, model.Snapshot.EntryId, model.SeekPosition, model.Volume, model.Muted,
                model.Queue.ToArray(), model.Repeat, model.Shuffle, Hash(fixture));
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.Combine(directory, "library.db"), Pooling = false }.ToString());
            connection.Open(); using var transaction = connection.BeginTransaction(); using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "DELETE FROM PlaylistEntries; UPDATE Playlists SET Name=$name; UPDATE Session SET Json='{}';";
            command.Parameters.AddWithValue("$name", "uncommitted crash damage"); command.ExecuteNonQuery();
            Check(File.Exists(Path.Combine(directory, "library.db-wal")), "Live WAL missing at crash checkpoint.");
            File.WriteAllText(signal + ".pending", JsonSerializer.Serialize(expected)); File.Move(signal + ".pending", signal);
            await Task.Delay(Timeout.InfiniteTimeSpan); // Parent terminates without closing WPF, SQLite or settings normally.
            throw new InvalidOperationException("Checkpoint process unexpectedly resumed.");
        }
        var saved = JsonSerializer.Deserialize<Checkpoint>(File.ReadAllText(signal)) ?? throw new InvalidDataException("Crash checkpoint missing.");
        Check(JsonSerializer.Serialize(saved.Playlists) == JsonSerializer.Serialize(model.Playlists.Select(p => p.Capture()).ToArray()), "Committed playlist IDs/order/duplicates/enabled metadata did not survive termination.");
        Check(saved.Selected == model.SelectedPlaylist.Id && saved.Source == model.SourcePlaylistId && saved.Active == model.Snapshot.EntryId, "Active/source/selected identities changed.");
        Check(Math.Abs(saved.Position - model.SeekPosition) < 0.05 && model.CanSeek && !model.IsPlaying, "Restart position/native preparation/no-autoplay contract failed.");
        Check(saved.Volume == model.Volume && saved.Muted == model.Muted, "Saved volume/mute changed.");
        Check(saved.Repeat == model.Repeat && saved.Shuffle == model.Shuffle && JsonSerializer.Serialize(saved.Queue) == JsonSerializer.Serialize(model.Queue.ToArray()), "Queue occurrence IDs/order/repeat/shuffle changed.");
        Check(model.Entries.All(row => row.Rating == 4), "Shared rating did not survive termination.");
        Check(saved.FixtureSha256 == Hash(fixture), "Source audio changed.");
        using var database = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.Combine(directory, "library.db"), Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        database.Open(); using var verify = database.CreateCommand(); verify.CommandText = "PRAGMA integrity_check";
        Check((string?)verify.ExecuteScalar() == "ok", "SQLite integrity check failed after termination.");
        verify.CommandText = "PRAGMA foreign_key_check"; using (var reader = verify.ExecuteReader()) Check(!reader.Read(), "SQLite references failed after termination.");
        return new { Status = "crash-restart-model-passed", CommittedPlaylistIdentityOrderAndFlags = true, ActiveSourceAndPosition = true,
            QueueRepeatShuffle = true, Ratings = true, VolumeMute = true, NativePreparation = true, NoAutoplay = true,
            SqliteIntegrityAndReferences = true, UncommittedValidationTransactionRolledBack = true, SourceUnchanged = true,
            PositionToleranceSeconds = 0.05, DevicePlayback = "not-run", KillDuringMigrationOrPowerLoss = "not-run" };
    }
}
