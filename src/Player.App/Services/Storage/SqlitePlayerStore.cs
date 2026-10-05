using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Player.Core.Library;
using Player.Core.Playback;

namespace Player.App.Services.Storage;

/// <summary>One bounded dedicated owner, real synchronous SQLite I/O off the dispatcher.</summary>
public sealed class SqlitePlayerStore : IPlayerStore
{
    private readonly BlockingCollection<Action> _work = new(64);
    private readonly TaskCompletionSource _exit = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly object _gate = new();
    private readonly string _path;
    private bool _closing;
    private SqliteConnection? _connection;
    private FileStream? _ownership;
    public SqlitePlayerStore(string path)
    {
        _path = Path.GetFullPath(path);
        new Thread(Run) { IsBackground = true, Name = "Player database" }.Start();
    }
    public Task<LibraryState> LoadAsync() => Queue(() =>
    {
        var connection = Open();
        var playlists = new List<PlaylistState>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT Id, Name FROM Playlists ORDER BY TabOrder";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                if (playlists.Count >= 100) throw new InvalidDataException("Saved tab limit exceeded.");
                playlists.Add(new PlaylistState(Guid.Parse(reader.GetString(0)), reader.GetString(1), []));
            }
        }
        var total = 0;
        for (var i = 0; i < playlists.Count; i++)
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT e.Id, e.Enabled, t.Json FROM PlaylistEntries e JOIN Tracks t ON t.Id=e.TrackId WHERE e.PlaylistId=$id ORDER BY e.EntryOrder";
            command.Parameters.AddWithValue("$id", playlists[i].Id.ToString());
            using var reader = command.ExecuteReader();
            var entries = new List<PlaylistEntry>();
            while (reader.Read())
            {
                if (++total > 10000) throw new InvalidDataException("Saved entry limit exceeded.");
                var json = reader.GetString(2);
                if (json.Length > 524288) throw new InvalidDataException("Oversized track metadata.");
                var track = JsonSerializer.Deserialize<MediaTrack>(json) ?? throw new InvalidDataException("Invalid saved track.");
                entries.Add(new PlaylistEntry(Guid.Parse(reader.GetString(0)), track, reader.GetBoolean(1)));
            }
            playlists[i] = playlists[i] with { Entries = entries.ToArray() };
        }
        var session = new SessionState(null, null, null, 0);
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT Json FROM Session WHERE Id=1";
            if (command.ExecuteScalar() is string json)
            {
                if (json.Length > 1048576) throw new InvalidDataException("Oversized saved session.");
                session = JsonSerializer.Deserialize<SessionState>(json) ?? throw new InvalidDataException("Invalid saved session.");
            }
        }
        if (playlists.Count == 0) throw new InvalidDataException("Saved database contains no playlist; original preserved.");
        var state = new LibraryState(playlists.ToArray(), session); state.Validate(); return state;
    });

    public Task SaveAsync(LibraryState state, bool playlistsChanged)
    {
        state.Validate();
        return Queue(() =>
        {
            var connection = Open();
            using var transaction = connection.BeginTransaction();
            if (playlistsChanged)
            {
                Execute(connection, transaction, "DELETE FROM PlaylistEntries; DELETE FROM Playlists; DELETE FROM Tracks;");
                using var tabs = Command(connection, transaction, "INSERT INTO Playlists(Id,Name,TabOrder) VALUES($id,$name,$order)");
                using var tracks = Command(connection, transaction, "INSERT INTO Tracks(Id,Json) VALUES($id,$json) ON CONFLICT(Id) DO UPDATE SET Json=excluded.Json");
                using var entries = Command(connection, transaction, "INSERT INTO PlaylistEntries(Id,PlaylistId,TrackId,EntryOrder,Enabled) VALUES($id,$playlist,$track,$order,$enabled)");
                for (var i = 0; i < state.Playlists.Length; i++)
                {
                    var tab = state.Playlists[i];
                    Set(tabs, ("$id", tab.Id.ToString()), ("$name", tab.Name), ("$order", i)); tabs.ExecuteNonQuery();
                    for (var j = 0; j < tab.Entries.Length; j++)
                    {
                        var entry = tab.Entries[j];
                        Set(tracks, ("$id", entry.Track.Id.ToString()), ("$json", JsonSerializer.Serialize(entry.Track))); tracks.ExecuteNonQuery();
                        Set(entries, ("$id", entry.Id.ToString()), ("$playlist", tab.Id.ToString()), ("$track", entry.Track.Id.ToString()), ("$order", j), ("$enabled", entry.Enabled)); entries.ExecuteNonQuery();
                    }
                }
            }
            using var session = Command(connection, transaction, "INSERT INTO Session(Id,Json) VALUES(1,$json) ON CONFLICT(Id) DO UPDATE SET Json=excluded.Json");
            session.Parameters.AddWithValue("$json", JsonSerializer.Serialize(state.Session)); session.ExecuteNonQuery();
            transaction.Commit(); return true;
        });
    }
    public Task BackupAsync(string destination) => Queue(() =>
    {
        if (File.Exists(destination)) throw new IOException("Choose a new backup filename; existing files are never overwritten.");
        var connection = Open();
        using var backup = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = destination, Mode = SqliteOpenMode.ReadWriteCreate, Pooling = false }.ToString());
        backup.Open(); connection.BackupDatabase(backup); return true;
    });
    private SqliteConnection Open()
    {
        if (_connection is not null) return _connection;
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        FileStream ownership;
        try { ownership = new FileStream(_path + ".owner.lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException error) when ((error.HResult & 0xffff) is 32 or 33 || !OperatingSystem.IsWindows() && (error.HResult & 0xffff) == 11) { throw new PlayerStoreInUseException(error); }
        var existed = File.Exists(_path);
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = _path, Mode = existed ? SqliteOpenMode.ReadWrite : SqliteOpenMode.ReadWriteCreate, Pooling = false, DefaultTimeout = 3 }.ToString());
        try
        {
            connection.Open();
            using var version = connection.CreateCommand(); version.CommandText = "PRAGMA user_version";
            var schema = Convert.ToInt32(version.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
            if (schema > 1) throw new NewerDatabaseSchemaException(schema);
            if (existed && schema == 0) throw new InvalidDataException("Unrecognized database schema. Original database preserved.");
            using var check = connection.CreateCommand(); check.CommandText = "PRAGMA quick_check";
            if ((string?)check.ExecuteScalar() != "ok") throw new InvalidDataException("Database integrity check failed. Original database preserved.");
            Execute(connection, null, "PRAGMA foreign_keys=ON; PRAGMA busy_timeout=3000; PRAGMA journal_mode=WAL;");
            if (!existed)
            {
                using var transaction = connection.BeginTransaction();
                Execute(connection, transaction, """
                    CREATE TABLE Tracks(Id TEXT PRIMARY KEY, Json TEXT NOT NULL);
                    CREATE TABLE Playlists(Id TEXT PRIMARY KEY, Name TEXT NOT NULL, TabOrder INTEGER NOT NULL UNIQUE);
                    CREATE TABLE PlaylistEntries(Id TEXT PRIMARY KEY, PlaylistId TEXT NOT NULL REFERENCES Playlists(Id), TrackId TEXT NOT NULL REFERENCES Tracks(Id), EntryOrder INTEGER NOT NULL, Enabled INTEGER NOT NULL CHECK(Enabled IN (0,1)), UNIQUE(PlaylistId,EntryOrder));
                    CREATE INDEX EntriesByTrack ON PlaylistEntries(TrackId);
                    CREATE TABLE Session(Id INTEGER PRIMARY KEY CHECK(Id=1), Json TEXT NOT NULL);
                    PRAGMA user_version=1;
                    """);
                using var initial = Command(connection, transaction, "INSERT INTO Playlists VALUES($id,'Default',0)"); initial.Parameters.AddWithValue("$id", Guid.NewGuid().ToString()); initial.ExecuteNonQuery();
                transaction.Commit();
            }
            _connection = connection; _ownership = ownership; return connection;
        }
        catch { connection.Dispose(); ownership.Dispose(); throw; }
    }
    private static SqliteCommand Command(SqliteConnection connection, SqliteTransaction? transaction, string sql)
    { var command = connection.CreateCommand(); command.Transaction = transaction; command.CommandText = sql; return command; }
    private static void Execute(SqliteConnection connection, SqliteTransaction? transaction, string sql)
    { using var command = Command(connection, transaction, sql); command.ExecuteNonQuery(); }
    private static void Set(SqliteCommand command, params (string Name, object Value)[] values)
    { command.Parameters.Clear(); foreach (var (name, value) in values) command.Parameters.AddWithValue(name, value); }
    private Task<T> Queue<T>(Func<T> action)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            if (_closing) throw new ObjectDisposedException(nameof(SqlitePlayerStore));
            if (!_work.TryAdd(() => { try { completion.TrySetResult(action()); } catch (Exception error) { completion.TrySetException(error); } }))
                completion.TrySetException(new IOException("Database work queue is full. Retry after the current operation."));
        }
        return completion.Task;
    }
    private void Run()
    {
        Exception? failure = null;
        try { foreach (var action in _work.GetConsumingEnumerable()) action(); }
        catch (Exception error) { failure = error; }
        finally
        {
            try { _connection?.Dispose(); } catch (Exception error) { failure = error; }
            try { _ownership?.Dispose(); } catch (Exception error) { failure = failure is null ? error : new AggregateException(failure, error); }
            _work.Dispose();
        }
        // Reopen is permitted only after both SQLite and the ownership handle are released.
        if (failure is null) _exit.TrySetResult(); else _exit.TrySetException(failure);
    }
    public ValueTask DisposeAsync()
    { lock (_gate) { if (!_closing) { _closing = true; _work.CompleteAdding(); } } return new(_exit.Task); }
}

public sealed class PlayerStoreInUseException(Exception inner) : IOException("The data directory is already in use. Close the other player instance before opening it.", inner);
