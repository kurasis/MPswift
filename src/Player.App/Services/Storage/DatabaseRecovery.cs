using System.IO;
using Microsoft.Data.Sqlite;

namespace Player.App.Services.Storage;

public sealed class NewerDatabaseSchemaException(int version) : IOException($"Database schema {version} is newer than this application. Use a compatible version; original database preserved.");

public static class DatabaseRecovery
{
    internal static void ConfigureReadLimits(SqliteConnection connection)
    {
        // Existing sessions allow 16 Mi UTF-16 code units. UTF-8 needs at most 3 bytes per unit;
        // retain 1 MiB for row headers while refusing huge cells before native/managed allocation.
        const int maximumRowBytes = 49 * 1024 * 1024;
        SQLitePCL.raw.sqlite3_limit(connection.Handle!, SQLitePCL.raw.SQLITE_LIMIT_LENGTH, maximumRowBytes);
        SQLitePCL.raw.sqlite3_limit(connection.Handle!, SQLitePCL.raw.SQLITE_LIMIT_SQL_LENGTH, 1024 * 1024);
        SQLitePCL.raw.sqlite3_limit(connection.Handle!, SQLitePCL.raw.SQLITE_LIMIT_COLUMN, 256);
        SQLitePCL.raw.sqlite3_limit(connection.Handle!, SQLitePCL.raw.SQLITE_LIMIT_EXPR_DEPTH, 100);
    }
    /// <summary>Explicit user-selected restore only. Validate first; retain the old main/WAL/SHM files.</summary>
    public static void Restore(string destination, string backup)
    {
        destination = Path.GetFullPath(destination); backup = Path.GetFullPath(backup);
        if (string.Equals(destination, backup, StringComparison.OrdinalIgnoreCase)) throw new IOException("Choose a separate backup file.");
        using var destinationLease = DataDirectoryLease.Open(Path.GetDirectoryName(destination)!);
        using var sourceLease = DataDirectoryLease.Open(Path.GetDirectoryName(backup)!);
        destination = Path.Combine(destinationLease.DirectoryPath, Path.GetFileName(destination));
        backup = Path.Combine(sourceLease.DirectoryPath, Path.GetFileName(backup));
        if (string.Equals(destination, backup, StringComparison.OrdinalIgnoreCase)) throw new IOException("Choose a separate backup file.");
        using var ownership = new FileStream(destination + ".owner.lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        using var source = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = backup, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        source.Open();
        ConfigureReadLimits(source);
        Validate(source);
        var temporary = destination + ".restore-" + Guid.NewGuid().ToString("N");
        var preserved = destination + ".preserved-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss") + "-" + Guid.NewGuid().ToString("N");
        var moved = new List<(string Original, string Preserved)>();
        try
        {
            using (var target = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = temporary, Mode = SqliteOpenMode.ReadWriteCreate, Pooling = false }.ToString()))
            { target.Open(); source.BackupDatabase(target); }
            foreach (var suffix in new[] { "", "-wal", "-shm" })
                if (File.Exists(destination + suffix)) { File.Move(destination + suffix, preserved + suffix); moved.Add((destination + suffix, preserved + suffix)); }
            File.Move(temporary, destination);
        }
        catch
        {
            foreach (var (original, retained) in moved.AsEnumerable().Reverse()) if (!File.Exists(original)) File.Move(retained, original);
            throw;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    internal static void Validate(SqliteConnection source)
    {
        using var budget = new DatabaseValidationBudget(source);
        ValidateSchemaSize(source);
        using var command = source.CreateCommand();
        command.CommandText = "PRAGMA user_version";
        if (Convert.ToInt32(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture) is not (1 or 2)) throw new InvalidDataException("Backup schema is not supported.");
        command.CommandText = "PRAGMA quick_check";
        if ((string?)command.ExecuteScalar() != "ok") throw new InvalidDataException("Backup integrity failed.");
        command.CommandText = "PRAGMA foreign_key_check";
        using (var reader = command.ExecuteReader()) if (reader.Read()) throw new InvalidDataException("Backup has invalid references.");
        command.CommandText = "SELECT COUNT(*) FROM Playlists";
        if (Convert.ToInt64(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture) is < 1 or > 100) throw new InvalidDataException("Backup has invalid playlist dimensions.");
    }
    internal static void ValidateSchemaSize(SqliteConnection connection)
    {
        using var command = connection.CreateCommand(); command.CommandText = "SELECT name, length(sql) FROM sqlite_schema LIMIT 129";
        using var reader = command.ExecuteReader(); var count = 0;
        while (reader.Read())
            if (++count > 128 || reader.GetString(0).Length > 256 || !reader.IsDBNull(1) && reader.GetInt64(1) > 65536)
                throw new InvalidDataException("Database schema exceeds validation bounds; original data preserved.");
    }
}
