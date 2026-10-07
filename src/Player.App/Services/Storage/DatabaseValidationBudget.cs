using System.Diagnostics;
using Microsoft.Data.Sqlite;

namespace Player.App.Services.Storage;

/// <summary>Bound SQLite VM work during validation and queued operations; remove the callback on scope exit.</summary>
internal sealed class DatabaseValidationBudget : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly SQLitePCL.delegate_progress _progress;
    public DatabaseValidationBudget(SqliteConnection connection, long maximumInstructions = 500_000_000, TimeSpan? maximumTime = null)
    {
        _connection = connection;
        var deadline = Stopwatch.GetTimestamp() + (long)((maximumTime ?? TimeSpan.FromMinutes(1)).TotalSeconds * Stopwatch.Frequency);
        long instructions = 0;
        _progress = _ => { instructions += 1000; return instructions >= maximumInstructions || Stopwatch.GetTimestamp() >= deadline ? 1 : 0; };
        SQLitePCL.raw.sqlite3_progress_handler(connection.Handle!, 1000, _progress, null);
    }
    public void Dispose() => SQLitePCL.raw.sqlite3_progress_handler(_connection.Handle!, 0, null, null);
}
