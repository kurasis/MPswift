using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;
using System.Runtime.InteropServices;
using Microsoft.Data.Sqlite;
using Player.Core.Library;
using Player.Core.Playback;

namespace Player.App.Services.Storage;

/// <summary>One bounded dedicated owner, real synchronous SQLite I/O off the dispatcher.</summary>
public sealed partial class SqlitePlayerStore : IPlayerStore, ILibraryIndexStore
{
    private readonly BlockingCollection<Action> _work = new(64);
    private readonly TaskCompletionSource _exit = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly object _gate = new();
    private readonly string _path;
    private readonly string _defaultPlaylistName;
    private bool _closing;
    private SqliteConnection? _connection;
    private FileStream? _ownership;
    private DataDirectoryLease? _directoryLease;
    private List<DataFileLease>? _fileLeases;
    internal Action? MigrationBeforeCommit { get; init; }
    internal bool ReadOnlyValidation { get; init; }
    public SqlitePlayerStore(string path, string defaultPlaylistName = "Default")
    {
        if (string.IsNullOrWhiteSpace(defaultPlaylistName) || defaultPlaylistName.Length > 200) throw new ArgumentException("Invalid default playlist name.");
        _defaultPlaylistName = defaultPlaylistName;
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
            command.CommandText = "SELECT e.Id, e.Enabled, t.Json, e.AddedUtcTicks FROM PlaylistEntries e JOIN Tracks t ON t.Id=e.TrackId WHERE e.PlaylistId=$id ORDER BY e.EntryOrder";
            command.Parameters.AddWithValue("$id", playlists[i].Id.ToString());
            using var reader = command.ExecuteReader();
            var entries = new List<PlaylistEntry>();
            while (reader.Read())
            {
                if (++total > 10000) throw new InvalidDataException("Saved entry limit exceeded.");
                var json = reader.GetString(2);
                if (json.Length > 524288) throw new InvalidDataException("Oversized track metadata.");
                var track = JsonSerializer.Deserialize<MediaTrack>(json) ?? throw new InvalidDataException("Invalid saved track.");
                LibraryState.ValidateTrack(track);
                track = Player.Core.Media.LegacyTagText.Recover(track);
                entries.Add(new PlaylistEntry(Guid.Parse(reader.GetString(0)), track, reader.GetBoolean(1), reader.GetInt64(3)));
            }
            playlists[i] = playlists[i] with { Entries = entries.ToArray() };
        }
        var session = new SessionState(null, null, null, 0);
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT Json FROM Session WHERE Id=1";
            if (command.ExecuteScalar() is string json)
            {
                if (json.Length > 16 * 1024 * 1024) throw new InvalidDataException("Oversized saved session.");
                session = JsonSerializer.Deserialize<SessionState>(json) ?? throw new InvalidDataException("Invalid saved session.");
            }
        }
        if (playlists.Count == 0) throw new InvalidDataException("Saved database contains no playlist; original preserved.");
        var state = new LibraryState(playlists.ToArray(), session); state.Validate(); return state;
    });

    public Task SaveAsync(LibraryState state, bool playlistsChanged)
    {
        state.Validate();
        var sessionJson = SessionJson.Serialize(state.Session);
        return Queue(() =>
        {
            var connection = Open();
            using var transaction = connection.BeginTransaction();
            if (playlistsChanged)
            {
                Execute(connection, transaction, "DELETE FROM PlaylistEntries; DELETE FROM Playlists; DELETE FROM Tracks;");
                using var tabs = Command(connection, transaction, "INSERT INTO Playlists(Id,Name,TabOrder) VALUES($id,$name,$order)");
                using var tracks = Command(connection, transaction, "INSERT INTO Tracks(Id,Json) VALUES($id,$json) ON CONFLICT(Id) DO UPDATE SET Json=excluded.Json");
                using var entries = Command(connection, transaction, "INSERT INTO PlaylistEntries(Id,PlaylistId,TrackId,EntryOrder,Enabled,AddedUtcTicks) VALUES($id,$playlist,$track,$order,$enabled,$added)");
                for (var i = 0; i < state.Playlists.Length; i++)
                {
                    var tab = state.Playlists[i];
                    Set(tabs, ("$id", tab.Id.ToString()), ("$name", tab.Name), ("$order", i)); tabs.ExecuteNonQuery();
                    for (var j = 0; j < tab.Entries.Length; j++)
                    {
                        var entry = tab.Entries[j];
                        Set(tracks, ("$id", entry.Track.Id.ToString()), ("$json", JsonSerializer.Serialize(entry.Track))); tracks.ExecuteNonQuery();
                        Set(entries, ("$id", entry.Id.ToString()), ("$playlist", tab.Id.ToString()), ("$track", entry.Track.Id.ToString()), ("$order", j), ("$enabled", entry.Enabled), ("$added", entry.AddedUtcTicks)); entries.ExecuteNonQuery();
                    }
                }
            }
            using var session = Command(connection, transaction, "INSERT INTO Session(Id,Json) VALUES(1,$json) ON CONFLICT(Id) DO UPDATE SET Json=excluded.Json");
            session.Parameters.AddWithValue("$json", sessionJson); session.ExecuteNonQuery();
            transaction.Commit(); return true;
        });
    }
    public Task BackupAsync(string destination) => Queue(() =>
    {
        using var lease = DataDirectoryLease.Open(Path.GetDirectoryName(Path.GetFullPath(destination))!);
        destination = Path.Combine(lease.DirectoryPath, Path.GetFileName(destination));
        if (File.Exists(destination)) throw new IOException("Choose a new backup filename; existing files are never overwritten.");
        var connection = Open();
        // Reserve atomically, retaining the name while native SQLite opens/copies the same empty file.
        using var reserved = new FileStream(destination, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.ReadWrite);
        using var backup = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = destination, Mode = SqliteOpenMode.ReadWrite, Pooling = false }.ToString());
        backup.Open(); DatabaseSnapshotCopy.Copy(connection, backup); return true;
    });
    internal Task CheckpointAsync() => Queue(() =>
    {
        using var command = Command(Open(), null, "PRAGMA wal_checkpoint(TRUNCATE)");
        using var reader = command.ExecuteReader();
        if (!reader.Read() || reader.GetInt32(0) != 0) throw new IOException("Owned database checkpoint is busy; original data preserved.");
        return true;
    });
    private SqliteConnection Open()
    {
        if (_connection is not null) return _connection;
        var directoryLease = DataDirectoryLease.Create(Path.GetDirectoryName(_path)!);
        var databasePath = Path.Combine(directoryLease.DirectoryPath, Path.GetFileName(_path));
        FileStream ownership;
        try { ownership = new FileStream(databasePath + ".owner.lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException error) when ((error.HResult & 0xffff) is 32 or 33 || !OperatingSystem.IsWindows() && (error.HResult & 0xffff) == 11) { directoryLease.Dispose(); throw new PlayerStoreInUseException(error); }
        catch { directoryLease.Dispose(); throw; }
        var files = new List<DataFileLease>();
        var existed = false;
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = ReadOnlyValidation ? new Uri(databasePath).AbsoluteUri + "?immutable=1" : databasePath,
            Mode = ReadOnlyValidation ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWrite, Pooling = false, DefaultTimeout = 3 }.ToString());
        try
        {
            var paths = ReadOnlyValidation ? [databasePath] : new[] { databasePath, databasePath + "-wal", databasePath + "-shm" };
            var pinned = new HashSet<string>(StringComparer.Ordinal);
            foreach (var path in paths)
            {
                var file = DataFileLease.OpenExisting(path, writableSharing: !ReadOnlyValidation);
                if (file is not null) { files.Add(file); pinned.Add(path); }
                if (path == databasePath) existed = file is not null;
            }
            if (ReadOnlyValidation && !existed) throw new FileNotFoundException("Owned validation database is missing.");
            if (!ReadOnlyValidation)
                foreach (var path in paths)
                    if (!pinned.Contains(path))
                    {
                        var file = DataFileLease.OpenOrCreate(path); files.Add(file);
                        if (path == databasePath) existed = !file.Created;
                    }
            connection.Open();
            if (OperatingSystem.IsWindows() && !ReadOnlyValidation)
            {
                // These sidecars are pinned without delete sharing until SQLite is closed.
                // Keep normal close-time checkpointing, but avoid Windows deletion retry sleeps.
                // https://www.sqlite.org/c3ref/c_fcntl_begin_atomic_write.html#sqlitefcntlpersistwal
                var persist = 1;
                SqliteException.ThrowExceptionForRC(PersistWalFiles(connection.Handle!, "main", 10, ref persist), connection.Handle);
            }
            DatabaseRecovery.ConfigureReadLimits(connection);
            using var validationBudget = new DatabaseValidationBudget(connection);
            DatabaseRecovery.ValidateSchemaSize(connection);
            using var version = connection.CreateCommand(); version.CommandText = "PRAGMA user_version";
            var schema = Convert.ToInt32(version.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
            if (schema > 2) throw new NewerDatabaseSchemaException(schema);
            if (ReadOnlyValidation && schema != 2) throw new InvalidDataException("Read-only validation requires the migrated schema.");
            if (existed && schema == 0) throw new InvalidDataException("Unrecognized database schema. Original database preserved.");
            using var check = connection.CreateCommand(); check.CommandText = "PRAGMA quick_check";
            if ((string?)check.ExecuteScalar() != "ok") throw new InvalidDataException("Database integrity check failed. Original database preserved.");
            if (existed)
            {
                var required = new[] { "Tracks", "Playlists", "PlaylistEntries", "Session" }.Concat(schema == 2 ? new[] { "LibraryRoots", "MediaIndex", "TrackStatistics", "ListeningHistory" } : []).ToArray();
                using var tables = connection.CreateCommand(); tables.CommandText = "SELECT name FROM sqlite_master WHERE type='table'";
                using var reader = tables.ExecuteReader(); var names = new HashSet<string>(); while (reader.Read()) names.Add(reader.GetString(0));
                if (required.Any(name => !names.Contains(name))) throw new InvalidDataException("Database schema tables are incomplete. Original database preserved.");
            }
            Execute(connection, null, "PRAGMA foreign_keys=ON; PRAGMA busy_timeout=3000;");
            if (!ReadOnlyValidation) Execute(connection, null, "PRAGMA journal_mode=WAL;");
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
                using var initial = Command(connection, transaction, "INSERT INTO Playlists VALUES($id,$name,0)"); initial.Parameters.AddWithValue("$id", Guid.NewGuid().ToString()); initial.Parameters.AddWithValue("$name", _defaultPlaylistName); initial.ExecuteNonQuery();
                transaction.Commit();
            }
            if (schema < 2) AddIndexSchema(connection, existed);
            _connection = connection; _ownership = ownership; _directoryLease = directoryLease; _fileLeases = files; return connection;
        }
        catch { connection.Dispose(); foreach (var file in files) file.Dispose(); ownership.Dispose(); directoryLease.Dispose(); throw; }
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
            if (!_work.TryAdd(() =>
            {
                try { using var budget = new DatabaseValidationBudget(Open()); completion.TrySetResult(action()); }
                catch (Exception error) { completion.TrySetException(error); }
            }))
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
            void Release(IDisposable? resource)
            {
                try { resource?.Dispose(); }
                catch (Exception error) { failure = failure is null ? error : new AggregateException(failure, error); }
            }
            Release(_connection);
            if (_fileLeases is not null) foreach (var file in _fileLeases) Release(file);
            Release(_ownership);
            Release(_directoryLease);
            Release(_work);
        }
        // Reopen is permitted only after both SQLite and the ownership handle are released.
        if (failure is null) _exit.TrySetResult(); else _exit.TrySetException(failure);
    }
    public ValueTask DisposeAsync()
    { lock (_gate) { if (!_closing) { _closing = true; _work.CompleteAdding(); } } return new(_exit.Task); }

    // SQLitePCLRaw's pinned provider uses this same bundled e_sqlite3 library but does not expose file_control.
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.SafeDirectories)]
    [DllImport("e_sqlite3", EntryPoint = "sqlite3_file_control", CallingConvention = CallingConvention.Cdecl)]
    private static extern int PersistWalFiles(SQLitePCL.sqlite3 connection, [MarshalAs(UnmanagedType.LPUTF8Str)] string database,
        int operation, ref int value);
}

public sealed class PlayerStoreInUseException(Exception inner) : IOException("The data directory is already in use. Close the other player instance before opening it.", inner);
