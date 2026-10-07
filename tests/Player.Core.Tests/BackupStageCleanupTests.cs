using Player.App.Services.Storage;
using Player.Core.Library;

namespace Player.Core.Tests;

public sealed class BackupStageCleanupTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "mp-backup-cleanup-" + Guid.NewGuid().ToString("N"));
    public BackupStageCleanupTests() => Directory.CreateDirectory(_root);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedBackupPreservesUnexpectedStageDataAndTheOriginalFailure(bool nested)
    {
        string? foreign = null;
        var failure = new IOException("Owned test backup failure.");
        await using var store = new FailingStore(path =>
        {
            var directory = Path.GetDirectoryName(path)!;
            if (nested) directory = Directory.CreateDirectory(Path.Combine(directory, "unexpected-album")).FullName;
            foreign = Path.Combine(directory, "keep.txt");
            File.WriteAllText(foreign, "owned sentinel");
            File.WriteAllText(path, "partial generated database");
            throw failure;
        });

        var error = await Assert.ThrowsAnyAsync<Exception>(() => BackupBundle.CreateAsync(store, new(), Path.Combine(_root, "backup.zip")));

        Assert.True(File.Exists(foreign), "Cleanup must preserve unexpected staging entries.");
        Assert.Equal("owned sentinel", File.ReadAllText(foreign!));
        Assert.Contains(failure, error is AggregateException aggregate ? aggregate.InnerExceptions : [error]);
        Assert.False(File.Exists(Path.Combine(_root, "backup.zip")));
        Assert.False(File.Exists(Path.Combine(Directory.GetDirectories(_root).Single(), "library.db")));
    }

    [Fact]
    public void FlatGeneratedMetadataAndMigrationSnapshotsAreCleanedAndHandlesReleased()
    {
        var stage = Path.Combine(_root, "stage");
        var lease = DataDirectoryLease.Create(stage);
        foreach (var name in new[] { "library.db", "library.db-wal", "library.db-shm", "library.db-journal", "library.db.owner.lock", "settings.json", "settings.json.bak",
            "library.db.pre-schema2-20261007120000-" + Guid.NewGuid().ToString("N") + ".db" })
            File.WriteAllText(Path.Combine(stage, name), "generated metadata");

        BackupStageCleanup.Clean(stage, lease);

        Assert.False(Directory.Exists(stage));
        Directory.Move(_root, _root + "-moved"); Directory.Move(_root + "-moved", _root);
    }

    [Fact]
    public void KnownMetadataNameOccupiedByDirectoryIsNeverTraversed()
    {
        var stage = Path.Combine(_root, "stage");
        var lease = DataDirectoryLease.Create(stage);
        var child = Directory.CreateDirectory(Path.Combine(stage, "library.db"));
        var sentinel = Path.Combine(child.FullName, "keep.txt"); File.WriteAllText(sentinel, "owned directory sentinel");

        Assert.Throws<IOException>(() => BackupStageCleanup.Clean(stage, lease));

        Assert.Equal("owned directory sentinel", File.ReadAllText(sentinel));
        Directory.Move(stage, stage + "-moved"); // Failed cleanup must also release the pin.
    }

    [Fact]
    public void CleanupUnlinksGeneratedFileNameAndPreservesItsTarget()
    {
        var target = Path.Combine(_root, "outside.txt"); File.WriteAllText(target, "owned outside sentinel");
        var stage = Path.Combine(_root, "stage");
        var lease = DataDirectoryLease.Create(stage);
        File.CreateSymbolicLink(Path.Combine(stage, "library.db"), target);

        BackupStageCleanup.Clean(stage, lease);

        Assert.False(Directory.Exists(stage));
        Assert.Equal("owned outside sentinel", File.ReadAllText(target));
    }

    [Fact]
    public void UnrecognizedMigrationLookalikeIsRetained()
    {
        var stage = Path.Combine(_root, "stage");
        var lease = DataDirectoryLease.Create(stage);
        var sentinel = Path.Combine(stage, "library.db.pre-schema2-unknown.db"); File.WriteAllText(sentinel, "owned unknown snapshot");

        Assert.Throws<IOException>(() => BackupStageCleanup.Clean(stage, lease));

        Assert.Equal("owned unknown snapshot", File.ReadAllText(sentinel));
    }

    private sealed class FailingStore(Action<string> backup) : IPlayerStore
    {
        public Task<LibraryState> LoadAsync() => throw new NotSupportedException();
        public Task SaveAsync(LibraryState state, bool playlistsChanged) => throw new NotSupportedException();
        public Task BackupAsync(string destination) { backup(destination); return Task.CompletedTask; }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    public void Dispose() => Directory.Delete(_root, true);
}
