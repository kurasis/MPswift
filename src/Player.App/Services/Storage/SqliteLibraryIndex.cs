using System.IO;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Player.Core.Library;
using Player.Core.Playback;

namespace Player.App.Services.Storage;

public sealed partial class SqlitePlayerStore
{
    public Task<LibraryRoot[]> GetRootsAsync() => Queue(() =>
    {
        using var command = Command(Open(), null, "SELECT Id,Path,Enabled FROM LibraryRoots ORDER BY Path"); using var reader = command.ExecuteReader(); var roots = new List<LibraryRoot>();
        while (reader.Read()) { if (roots.Count >= 100) throw new InvalidDataException("Root limit exceeded."); roots.Add(new(Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.GetBoolean(2))); } return roots.ToArray();
    });
    public Task PutRootAsync(LibraryRoot root)
    {
        Player.Core.Media.LocalMediaPath.Parse(root.Path); if (root.Id == Guid.Empty) throw new ArgumentException("Root ID required.");
        return Queue(() => { using var command = Command(Open(), null, "INSERT INTO LibraryRoots(Id,Path,Enabled) VALUES($id,$path,$enabled) ON CONFLICT(Id) DO UPDATE SET Path=excluded.Path,Enabled=excluded.Enabled"); Set(command, ("$id", root.Id.ToString()), ("$path", root.Path), ("$enabled", root.Enabled)); command.ExecuteNonQuery(); return true; });
    }
    public Task<IndexedFile[]> FindFilesAsync(string[] paths)
    {
        if (paths.Length > 64) throw new ArgumentException("Lookup batch exceeds 64 files.");
        return Queue(() => { var result = new List<IndexedFile>(); foreach (var path in paths) { using var command = Command(Open(), null, "SELECT Json,Available FROM MediaIndex WHERE Path=$path"); command.Parameters.AddWithValue("$path", path.ToUpperInvariant()); using var reader = command.ExecuteReader(); if (reader.Read()) { var file = ReadIndex(reader.GetString(0)); result.Add(file with { Available = reader.GetBoolean(1), Track = file.Track with { Available = reader.GetBoolean(1) } }); } } return result.ToArray(); });
    }
    public Task UpsertFilesAsync(IndexedFile[] files)
    {
        if (files.Length > 64) throw new ArgumentException("Write batch exceeds 64 files.");
        foreach (var file in files) { Player.Core.Media.LocalMediaPath.Parse(file.Path); LibraryState.ValidateTrack(file.Track); if (file.Id == Guid.Empty || file.Size < 0 || file.ModifiedUtcTicks < 0 || file.Generation.Length > 100 || file.Track.Id == Guid.Empty) throw new InvalidDataException("Invalid index record."); }
        return Queue(() =>
        {
            var connection = Open(); using var transaction = connection.BeginTransaction();
            using (var size = Command(connection, transaction, "SELECT COUNT(*) FROM MediaIndex"))
                if (Convert.ToInt64(size.ExecuteScalar()) + files.Length > 200000)
                {
                    var newFiles = 0;
                    foreach (var file in files.DistinctBy(f => f.Path, StringComparer.OrdinalIgnoreCase)) { using var existing = Command(connection, transaction, "SELECT 1 FROM MediaIndex WHERE Path=$path"); existing.Parameters.AddWithValue("$path", file.Path.ToUpperInvariant()); if (existing.ExecuteScalar() is null) newFiles++; }
                    if (Convert.ToInt64(size.ExecuteScalar()) + newFiles > 200000) throw new IOException("Index capacity reached; existing library data preserved.");
                }
            using var command = Command(connection, transaction, "INSERT INTO MediaIndex(Id,RootId,Path,Search,Generation,Available,Json) VALUES($id,$root,$path,$search,$generation,$available,$json) ON CONFLICT(Path) DO UPDATE SET RootId=excluded.RootId,Search=excluded.Search,Generation=excluded.Generation,Available=excluded.Available,Json=excluded.Json");
            foreach (var file in files) { Set(command, ("$id", file.Id.ToString()), ("$root", file.RootId.ToString()), ("$path", file.Path.ToUpperInvariant()), ("$search", LibrarySearch.Fields(file.Track)), ("$generation", file.Generation), ("$available", file.Available), ("$json", JsonSerializer.Serialize(file))); command.ExecuteNonQuery(); }
            transaction.Commit(); return true;
        });
    }
    public Task CompleteScanAsync(Guid root, string generation) => Queue(() =>
    {
        // Availability reconciliation does not delete metadata, ratings or user entries.
        using var command = Command(Open(), null, "UPDATE MediaIndex SET Available=0 WHERE RootId=$root AND Generation<>$generation"); Set(command, ("$root", root.ToString()), ("$generation", generation)); command.ExecuteNonQuery(); return true;
    });
    public Task<LibraryPage> SearchAsync(string query, int offset = 0, int limit = 100)
    {
        if (query.Length > 4096 || offset < 0 || limit is < 1 or > 200) throw new ArgumentOutOfRangeException(nameof(limit));
        var normalized = LibrarySearch.Normalize(query);
        return Queue(() =>
        {
            var connection = Open(); long total; using (var count = Command(connection, null, "SELECT COUNT(*) FROM MediaIndex WHERE instr(Search,$query)>0")) { count.Parameters.AddWithValue("$query", normalized); total = (long)count.ExecuteScalar()!; }
            using var command = Command(connection, null, "SELECT Json,Available FROM MediaIndex WHERE instr(Search,$query)>0 ORDER BY Path LIMIT $limit OFFSET $offset"); Set(command, ("$query", normalized), ("$limit", limit), ("$offset", offset));
            using var reader = command.ExecuteReader(); var records = new List<IndexedFile>();
            while (reader.Read()) { var file = ReadIndex(reader.GetString(0)); records.Add(file with { Available = reader.GetBoolean(1), Track = file.Track with { Available = reader.GetBoolean(1) } }); }
            return new LibraryPage(records.ToArray(), total, offset);
        });
    }
    public Task<TrackStatistics[]> GetStatisticsAsync(Guid[] tracks)
    {
        if (tracks.Length > 10000) throw new ArgumentException("Track limit exceeded.");
        return Queue(() => { var records = new List<TrackStatistics>(); foreach (var id in tracks.Distinct()) { using var command = Command(Open(), null, "SELECT Rating,PlayCount,LastPlayed FROM TrackStatistics WHERE TrackId=$id"); command.Parameters.AddWithValue("$id", id.ToString()); using var reader = command.ExecuteReader(); if (reader.Read()) records.Add(new(id, reader.GetInt32(0), reader.GetInt64(1), reader.IsDBNull(2) ? null : reader.GetInt64(2))); } return records.ToArray(); });
    }
    public Task SetRatingAsync(Guid track, int rating)
    {
        if (track == Guid.Empty || rating is < 0 or > 5) throw new ArgumentOutOfRangeException(nameof(rating));
        return Queue(() => { using var command = Command(Open(), null, "INSERT INTO TrackStatistics(TrackId,Rating,PlayCount) VALUES($id,$rating,0) ON CONFLICT(TrackId) DO UPDATE SET Rating=excluded.Rating"); Set(command, ("$id", track.ToString()), ("$rating", rating)); command.ExecuteNonQuery(); return true; });
    }
    public Task RecordListeningAsync(ListeningEvent occurrence)
    {
        if (occurrence.Id == Guid.Empty || occurrence.TrackId == Guid.Empty || occurrence.ListenedTicks < 0 || occurrence.StartedUtcTicks < 0 || occurrence.StartedUtcTicks > DateTime.MaxValue.Ticks) throw new ArgumentException("Invalid occurrence.");
        return Queue(() =>
        {
            var connection = Open(); using var transaction = connection.BeginTransaction();
            using var history = Command(connection, transaction, "INSERT OR IGNORE INTO ListeningHistory(Id,TrackId,Started,Listened,Counted) VALUES($id,$track,$started,$listened,$counted)");
            Set(history, ("$id", occurrence.Id.ToString()), ("$track", occurrence.TrackId.ToString()), ("$started", occurrence.StartedUtcTicks), ("$listened", occurrence.ListenedTicks), ("$counted", occurrence.Counted));
            if (history.ExecuteNonQuery() == 1 && occurrence.Counted)
            { using var stats = Command(connection, transaction, "INSERT INTO TrackStatistics(TrackId,Rating,PlayCount,LastPlayed) VALUES($id,0,1,$time) ON CONFLICT(TrackId) DO UPDATE SET PlayCount=PlayCount+1,LastPlayed=excluded.LastPlayed"); Set(stats, ("$id", occurrence.TrackId.ToString()), ("$time", occurrence.StartedUtcTicks)); stats.ExecuteNonQuery(); }
            Execute(connection, transaction, "DELETE FROM ListeningHistory WHERE Id NOT IN (SELECT Id FROM ListeningHistory ORDER BY Started DESC LIMIT 1000)"); transaction.Commit(); return true;
        });
    }
    public Task ClearListeningAsync() => Queue(() => { Execute(Open(), null, "DELETE FROM ListeningHistory"); return true; });
    public Task RelinkAsync(Guid trackId, string path)
    {
        path = Player.Core.Media.LocalMediaPath.Parse(path).Value;
        return Queue(() =>
        {
            var connection = Open(); using var transaction = connection.BeginTransaction();
            using var read = Command(connection, transaction, "SELECT Json FROM Tracks WHERE Id=$id"); read.Parameters.AddWithValue("$id", trackId.ToString());
            if (read.ExecuteScalar() is string json) { var track = JsonSerializer.Deserialize<MediaTrack>(json)! with { Path = path, Available = true }; using var update = Command(connection, transaction, "UPDATE Tracks SET Json=$json WHERE Id=$id"); Set(update, ("$json", JsonSerializer.Serialize(track)), ("$id", trackId.ToString())); update.ExecuteNonQuery(); }
            using (var indexed = Command(connection, transaction, "SELECT Id,Json FROM MediaIndex WHERE json_extract(Json,'$.Track.Id')=$track"))
            {
                indexed.Parameters.AddWithValue("$track", trackId.ToString()); var rows = new List<IndexedFile>(); using (var reader = indexed.ExecuteReader()) while (reader.Read()) rows.Add(ReadIndex(reader.GetString(1)));
                foreach (var file in rows)
                {
                    var revised = file with { Path = path, Available = true, Track = file.Track with { Path = path, Available = true } };
                    using var update = Command(connection, transaction, "UPDATE MediaIndex SET Path=$path,Search=$search,Available=1,Json=$json WHERE Id=$id");
                    Set(update, ("$path", path.ToUpperInvariant()), ("$search", LibrarySearch.Fields(revised.Track)), ("$json", JsonSerializer.Serialize(revised)), ("$id", file.Id.ToString())); update.ExecuteNonQuery();
                }
            }
            transaction.Commit(); return true;
        });
    }
    private static IndexedFile ReadIndex(string json)
    {
        if (json.Length > 524288) throw new InvalidDataException("Index metadata too large.");
        var file = JsonSerializer.Deserialize<IndexedFile>(json) ?? throw new InvalidDataException("Invalid index metadata.");
        LibraryState.ValidateTrack(file.Track); return file;
    }
    private void AddIndexSchema(SqliteConnection connection, bool migrate)
    {
        if (migrate)
        {
            var backupPath = _path + ".pre-schema2-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss") + "-" + Guid.NewGuid().ToString("N") + ".db";
            using var reserved = new FileStream(backupPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.ReadWrite);
            using var backup = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = backupPath, Mode = SqliteOpenMode.ReadWrite, Pooling = false }.ToString()); backup.Open(); DatabaseSnapshotCopy.Copy(connection, backup);
        }
        using var transaction = connection.BeginTransaction();
        Execute(connection, transaction, """
            ALTER TABLE PlaylistEntries ADD COLUMN AddedUtcTicks INTEGER NOT NULL DEFAULT 0;
            CREATE TABLE LibraryRoots(Id TEXT PRIMARY KEY,Path TEXT NOT NULL UNIQUE,Enabled INTEGER NOT NULL CHECK(Enabled IN(0,1)));
            CREATE TABLE MediaIndex(Id TEXT PRIMARY KEY,RootId TEXT NOT NULL REFERENCES LibraryRoots(Id),Path TEXT NOT NULL UNIQUE,Search TEXT NOT NULL,Generation TEXT NOT NULL,Available INTEGER NOT NULL CHECK(Available IN(0,1)),Json TEXT NOT NULL);
            CREATE INDEX MediaByRootGeneration ON MediaIndex(RootId,Generation);
            CREATE TABLE TrackStatistics(TrackId TEXT PRIMARY KEY,Rating INTEGER NOT NULL CHECK(Rating BETWEEN 0 AND 5),PlayCount INTEGER NOT NULL CHECK(PlayCount>=0),LastPlayed INTEGER);
            CREATE TABLE ListeningHistory(Id TEXT PRIMARY KEY,TrackId TEXT NOT NULL,Started INTEGER NOT NULL,Listened INTEGER NOT NULL,Counted INTEGER NOT NULL CHECK(Counted IN(0,1)));
            CREATE INDEX ListeningByTime ON ListeningHistory(Started);
            PRAGMA user_version=2;
            """);
        if (migrate) MigrationBeforeCommit?.Invoke();
        transaction.Commit();
    }
}
