using System.Diagnostics;
using System.Text.Json;
using Player.App.Services.Storage;
using Player.Core.Library;
using Player.Core.Playback;
using Xunit.Abstractions;

namespace Player.Core.Tests;

public sealed class LargeStorageBoundaryTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _directory = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "player-boundary-" + Guid.NewGuid().ToString("N"))).FullName;
    [Fact]
    public async Task MaximumTabsEntriesQueueAndHistorySurviveSaveBackupAndRestoreWithinProductionLimits()
    {
        var entries = Enumerable.Range(0, 10000).Select(i => new PlaylistEntry(Guid.NewGuid(),
            new MediaTrack(Guid.NewGuid(), $@"C:\Owned\track{i}.flac", "Owned song " + i))).ToArray();
        var playlists = Enumerable.Range(0, 100).Select(i => new PlaylistState(Guid.NewGuid(), "Album " + i, entries.Skip(i * 100).Take(100).ToArray())).ToArray();
        var queue = entries.Select(e => new QueueItem(Guid.NewGuid(), e, e.Id)).ToArray();
        var order = new PlaybackOrderState(RepeatMode.All, true, queue, entries.Select(e => e.Id).ToArray(), entries.Take(100).ToArray(), 99, entries[0].Id);
        var state = new LibraryState(playlists, new(playlists[0].Id, playlists[0].Id, entries[0], 10000000, order));
        var clock = Stopwatch.StartNew();
        var archive = Path.Combine(_directory, "full.zip");
        await using (var store = new SqlitePlayerStore(Path.Combine(_directory, "library.db")))
        {
            await store.SaveAsync(state, true).WaitAsync(TimeSpan.FromSeconds(90));
            var saved = await store.LoadAsync(); Assert.Equal(10000, saved.Playlists.Sum(p => p.Entries.Length));
            Assert.Equal(JsonSerializer.Serialize(state.Session), JsonSerializer.Serialize(saved.Session));
            await BackupBundle.CreateAsync(store, new(Language: "ru"), archive);
            var tooLarge = state with { Session = state.Session with { Order = order with
            { Queue = queue.Select(q => q with { Entry = q.Entry with { Track = q.Entry.Track with { Title = new string('x', 4096) } } }).ToArray() } } };
            await Assert.ThrowsAsync<InvalidDataException>(() => store.SaveAsync(tooLarge, false));
            Assert.Equal(JsonSerializer.Serialize(state.Session), JsonSerializer.Serialize((await store.LoadAsync()).Session));
        }
        var restoredDirectory = Path.Combine(_directory, "restored");
        await BackupBundle.RestoreAsync(restoredDirectory, archive);
        await using var restored = new SqlitePlayerStore(Path.Combine(restoredDirectory, "library.db"));
        var actual = await restored.LoadAsync();
        Assert.Equal(100, actual.Playlists.Length);
        Assert.Equal(entries, actual.Playlists.SelectMany(p => p.Entries).ToArray());
        Assert.Equal(JsonSerializer.Serialize(state.Session), JsonSerializer.Serialize(actual.Session));
        Assert.Equal("ru", new SettingsFile(restoredDirectory).Load().Language);
        output.WriteLine($"Maximum supported dimensions: elapsed={clock.Elapsed.TotalMilliseconds:F1} ms, session UTF-16 units={JsonSerializer.Serialize(state.Session).Length}, archive bytes={new FileInfo(archive).Length}.");
    }
    public void Dispose() => Directory.Delete(_directory, true);
}
