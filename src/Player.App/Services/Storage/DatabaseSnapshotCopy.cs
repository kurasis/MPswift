using System.Data;
using Microsoft.Data.Sqlite;
using SQLitePCL;

namespace Player.App.Services.Storage;

/// <summary>Copy in bounded native steps. An unfinished copy rolls back when its backup handle closes.</summary>
internal static class DatabaseSnapshotCopy
{
    internal const int PagesPerStep = 128;
    internal static readonly TimeSpan MaximumTime = TimeSpan.FromMinutes(5);

    internal static int Copy(SqliteConnection source, SqliteConnection destination, CancellationToken cancellationToken = default,
        TimeSpan? maximumTime = null, TimeProvider? timeProvider = null)
    {
        if (source.State != ConnectionState.Open || destination.State != ConnectionState.Open)
            throw new InvalidOperationException("Snapshot connections must already be open.");
        var limit = maximumTime ?? MaximumTime;
        if (limit <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(maximumTime));
        var clock = timeProvider ?? TimeProvider.System;
        var started = clock.GetTimestamp();
        cancellationToken.ThrowIfCancellationRequested();
        using var backup = raw.sqlite3_backup_init(destination.Handle!, "main", source.Handle!, "main");
        if (backup.IsInvalid) SqliteException.ThrowExceptionForRC(raw.sqlite3_errcode(destination.Handle!), destination.Handle);
        var steps = 0;
        while (true)
        {
            var elapsed = clock.GetElapsedTime(started);
            cancellationToken.ThrowIfCancellationRequested();
            if (elapsed >= limit)
                throw new TimeoutException("SQLite snapshot copy exceeded its time limit; source data and previous backups were preserved.");
            var result = raw.sqlite3_backup_step(backup, PagesPerStep);
            steps++;
            // DONE commits the copy. A single native I/O step cannot be preempted by this soft deadline.
            if (result == raw.SQLITE_DONE) return steps;
            // Retain the provider's immediate BUSY/LOCKED/error behavior; no hidden retry loop.
            SqliteException.ThrowExceptionForRC(result, destination.Handle);
        }
    }
}
