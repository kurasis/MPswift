using Player.App.Services.Storage;
using Player.Core.Waveforms;

namespace Player.Core.Tests;

public sealed class FileContinuityTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "mp-file-continuity-" + Guid.NewGuid().ToString("N"));
    public FileContinuityTests() => Directory.CreateDirectory(_root);

    [Fact]
    public async Task BackupRejectsDanglingFileLinkWithoutCreatingItsTarget()
    {
        var target = Path.Combine(_root, "outside.db");
        var destination = Path.Combine(_root, "backup.db");
        File.CreateSymbolicLink(destination, target);
        await using var store = new SqlitePlayerStore(Path.Combine(_root, "library.db"));
        var original = await store.LoadAsync();

        await Assert.ThrowsAsync<IOException>(() => store.BackupAsync(destination));

        Assert.False(File.Exists(target));
        Assert.Equal(target, new FileInfo(destination).LinkTarget);
        Assert.Equal(original.Playlists[0].Id, (await store.LoadAsync()).Playlists[0].Id);
    }

    [Fact]
    public void LinkedCacheReadDoesNotTouchTargetAndRegenerationReplacesOnlyLink()
    {
        var directory = Path.Combine(_root, "cache");
        var cache = new WaveformCache(directory);
        var key = new string('b', 64);
        var data = new WaveformData(48000, 1, 480, 480, [-0.2f], [0.2f]);
        cache.Write(key, data);
        var path = Path.Combine(directory, key + ".peaks");
        var target = Path.Combine(_root, "outside.peaks");
        File.Move(path, target);
        var bytes = File.ReadAllBytes(target);
        var time = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(target, time);
        time = File.GetLastWriteTimeUtc(target);
        File.CreateSymbolicLink(path, target);

        Assert.Null(cache.Read(key));
        Assert.Equal(time, File.GetLastWriteTimeUtc(target));
        Assert.Equal(bytes, File.ReadAllBytes(target));

        cache.Write(key, data);
        Assert.Null(new FileInfo(path).LinkTarget);
        Assert.NotNull(cache.Read(key));
        Assert.Equal(bytes, File.ReadAllBytes(target));
        Assert.Equal(time, File.GetLastWriteTimeUtc(target));
    }

    [Fact]
    public void CacheReadRefreshesLruWithoutChangingBytesAndEvictsUnaccessedEntry()
    {
        var cache = new WaveformCache(_root, 224);
        var first = new string('a', 64); var second = new string('b', 64); var third = new string('c', 64);
        var data = new WaveformData(48000, 1, 480, 480, [-0.2f], [0.2f]);
        cache.Write(first, data); cache.Write(second, data);
        var firstPath = Path.Combine(_root, first + ".peaks"); var secondPath = Path.Combine(_root, second + ".peaks");
        var bytes = File.ReadAllBytes(firstPath);
        File.SetLastWriteTimeUtc(firstPath, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        File.SetLastWriteTimeUtc(secondPath, new DateTime(2020, 1, 2, 0, 0, 0, DateTimeKind.Utc));

        Assert.NotNull(cache.Read(first));
        Assert.Equal(bytes, File.ReadAllBytes(firstPath));
        cache.Write(third, data);

        Assert.True(File.Exists(firstPath));
        Assert.False(File.Exists(secondPath));
        Assert.NotNull(cache.Read(third));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RejectedBackupRetainsExistingFileOrLinkAndLiveSource(bool link)
    {
        var target = Path.Combine(_root, "foreign.db");
        var destination = link ? Path.Combine(_root, "backup.db") : target;
        var bytes = new byte[] { 1, 2, 3, 4, 5 };
        File.WriteAllBytes(target, bytes);
        if (link) File.CreateSymbolicLink(destination, target);
        await using var store = new SqlitePlayerStore(Path.Combine(_root, "library.db"));
        var original = await store.LoadAsync();

        await Assert.ThrowsAsync<IOException>(() => store.BackupAsync(destination));

        Assert.Equal(bytes, File.ReadAllBytes(target));
        Assert.Equal(original.Playlists[0].Id, (await store.LoadAsync()).Playlists[0].Id);
        if (link) Assert.Equal(target, new FileInfo(destination).LinkTarget);
    }

    public void Dispose() => Directory.Delete(_root, true);
}
