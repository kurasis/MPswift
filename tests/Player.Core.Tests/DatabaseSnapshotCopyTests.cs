using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using Player.App.Services.Storage;

namespace Player.Core.Tests;

public sealed class DatabaseSnapshotCopyTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "player-snapshot-tests-" + Guid.NewGuid().ToString("N"));
    public DatabaseSnapshotCopyTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public void MultiStepCopyRetainsRowsAndReleasesBothConnections()
    {
        using var source = Open("source.db");
        Fill(source);
        var original = ReadLiveHash(source.DataSource);
        using var destination = Open("copy.db");
        Assert.True(DatabaseSnapshotCopy.Copy(source, destination) > 1);
        Assert.Equal(1024L, Scalar(destination, "SELECT COUNT(*) FROM Payload"));
        Assert.Equal(16L * 1024 * 1024, Scalar(destination, "SELECT SUM(length(Bytes)) FROM Payload"));
        Assert.Equal(1024L, Scalar(destination, "SELECT COUNT(*) FROM Payload WHERE Bytes=zeroblob(16384)"));
        Assert.Equal(original, ReadLiveHash(source.DataSource));
        source.Close(); destination.Close();
        using var exclusiveSource = File.Open(source.DataSource, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        using var exclusiveDestination = File.Open(destination.DataSource, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DeadlineOrCancellationAfterAnActualStepRollsBackIncompleteDestination(bool cancel)
    {
        using var source = Open("source.db"); Fill(source);
        var original = ReadLiveHash(source.DataSource);
        using var destination = Open("copy.db");
        Execute(destination, "CREATE TABLE Retained(Value INTEGER); INSERT INTO Retained VALUES(42)");
        using var cancellation = new CancellationTokenSource();
        var clock = new StepClock(cancel ? cancellation : null);
        if (cancel) Assert.Throws<OperationCanceledException>(() => DatabaseSnapshotCopy.Copy(source, destination, cancellation.Token, timeProvider: clock));
        else Assert.Throws<TimeoutException>(() => DatabaseSnapshotCopy.Copy(source, destination, timeProvider: clock));
        // The third timestamp follows the first real 128-page native step, before the second one.
        Assert.True(clock.Reads >= 3);
        Assert.Equal(42L, Scalar(destination, "SELECT Value FROM Retained"));
        Assert.Equal(0L, Scalar(destination, "SELECT COUNT(*) FROM sqlite_schema WHERE name='Payload'"));
        Assert.Equal(original, ReadLiveHash(source.DataSource));
        Assert.True(DatabaseSnapshotCopy.Copy(source, destination) > 1);
        Assert.Equal(1024L, Scalar(destination, "SELECT COUNT(*) FROM Payload"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BusyOrLockedCopyPreservesNativeErrorAndExistingData(bool lockSource)
    {
        using var source = Open("source.db"); Fill(source);
        using var destination = Open("copy.db");
        Execute(destination, "CREATE TABLE Retained(Value INTEGER); INSERT INTO Retained VALUES(42)");
        using var blocker = Open(lockSource ? "source.db" : "copy.db");
        Execute(blocker, "BEGIN EXCLUSIVE");
        var error = Assert.Throws<SqliteException>(() => DatabaseSnapshotCopy.Copy(source, destination));
        Assert.Contains(error.SqliteErrorCode, new[] { 5, 6 });
        Execute(blocker, "ROLLBACK");
        Assert.Equal(42L, Scalar(destination, "SELECT Value FROM Retained"));
        Assert.Equal(1024L, Scalar(source, "SELECT COUNT(*) FROM Payload"));
        DatabaseSnapshotCopy.Copy(source, destination);
        Assert.Equal(1024L, Scalar(destination, "SELECT COUNT(*) FROM Payload"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FullOrReadOnlyDestinationRejectsCopyWithoutDamagingEitherDatabase(bool readOnly)
    {
        using var source = Open("source.db"); Fill(source);
        using var created = Open("copy.db"); Execute(created, "CREATE TABLE Retained(Value INTEGER); INSERT INTO Retained VALUES(42)");
        if (readOnly) created.Close(); else Execute(created, "PRAGMA max_page_count=64");
        using var destination = readOnly ? new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = created.DataSource, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString()) : null;
        destination?.Open();
        var error = Assert.Throws<SqliteException>(() => DatabaseSnapshotCopy.Copy(source, destination ?? created));
        Assert.Equal(readOnly ? 8 : 13, error.SqliteErrorCode);
        Assert.Equal(42L, Scalar(destination ?? created, "SELECT Value FROM Retained"));
        Assert.Equal(1024L, Scalar(source, "SELECT COUNT(*) FROM Payload"));
    }

    [Fact]
    public void CommittedWalWriteBetweenStepsProducesAConsistentFinalCopy()
    {
        using var source = Open("source.db"); Fill(source); Execute(source, "PRAGMA journal_mode=WAL");
        using var writer = Open("source.db"); using var destination = Open("copy.db");
        var clock = new WriteClock(() => Execute(writer, "UPDATE Payload SET Bytes=zeroblob(32768) WHERE Id=1"));
        Assert.True(DatabaseSnapshotCopy.Copy(source, destination, timeProvider: clock) > 1);
        Assert.Equal(1, clock.Writes);
        Assert.Equal(1024L, Scalar(destination, "SELECT COUNT(*) FROM Payload"));
        Assert.Equal(16L * 1024 * 1024 + 16384, Scalar(destination, "SELECT SUM(length(Bytes)) FROM Payload"));
        Assert.Equal(32768L, Scalar(destination, "SELECT length(Bytes) FROM Payload WHERE Id=1"));
        Assert.Equal(Scalar(source, "SELECT SUM(length(Bytes)) FROM Payload"), Scalar(destination, "SELECT SUM(length(Bytes)) FROM Payload"));
    }

    [Fact]
    public void ClosedConnectionsAndPreCancelledCopyDoNotChangeDestination()
    {
        using var source = Open("source.db"); Fill(source);
        using var destination = Open("copy.db"); Execute(destination, "CREATE TABLE Retained(Value INTEGER); INSERT INTO Retained VALUES(42)");
        Assert.Throws<OperationCanceledException>(() => DatabaseSnapshotCopy.Copy(source, destination, new CancellationToken(true)));
        Assert.Equal(42L, Scalar(destination, "SELECT Value FROM Retained"));
        Assert.Throws<ArgumentOutOfRangeException>(() => DatabaseSnapshotCopy.Copy(source, destination, maximumTime: TimeSpan.Zero));
        source.Close();
        Assert.Throws<InvalidOperationException>(() => DatabaseSnapshotCopy.Copy(source, destination));
        Assert.Equal(42L, Scalar(destination, "SELECT Value FROM Retained"));
    }

    private SqliteConnection Open(string name)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.Combine(_directory, name), Pooling = false, DefaultTimeout = 1 }.ToString());
        connection.Open(); return connection;
    }
    private static byte[] ReadLiveHash(string path)
    {
        // SQLite already owns a writable handle; a test read must permit that existing writer.
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return SHA256.HashData(file);
    }
    private static void Fill(SqliteConnection connection) => Execute(connection, """
        PRAGMA journal_mode=DELETE;
        CREATE TABLE Payload(Id INTEGER PRIMARY KEY,Bytes BLOB);
        WITH RECURSIVE n(x) AS (VALUES(1) UNION ALL SELECT x+1 FROM n WHERE x<1024)
        INSERT INTO Payload SELECT x,zeroblob(16384) FROM n;
        """);
    private static void Execute(SqliteConnection connection, string sql) { using var command = connection.CreateCommand(); command.CommandText = sql; command.ExecuteNonQuery(); }
    private static long Scalar(SqliteConnection connection, string sql) { using var command = connection.CreateCommand(); command.CommandText = sql; return (long)command.ExecuteScalar()!; }
    private sealed class WriteClock(Action write) : TimeProvider
    {
        private int _reads;
        public int Writes { get; private set; }
        public override long GetTimestamp()
        {
            if (++_reads == 3) { write(); Writes++; }
            return TimeProvider.System.GetTimestamp();
        }
    }
    private sealed class StepClock(CancellationTokenSource? cancellation) : TimeProvider
    {
        public int Reads { get; private set; }
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp()
        {
            if (++Reads <= 2) return 0;
            cancellation?.Cancel();
            return cancellation is null ? DatabaseSnapshotCopy.MaximumTime.Ticks : 0;
        }
    }
    public void Dispose() => Directory.Delete(_directory, true);
}
