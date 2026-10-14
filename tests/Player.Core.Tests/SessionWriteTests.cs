using Microsoft.Data.Sqlite;
using Player.App.Services.Storage;
using Player.Core.Library;
using Player.Core.Playback;

namespace Player.Core.Tests;

public sealed class SessionWriteTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "mpswift-session-" + Guid.NewGuid().ToString("N"));
    public SessionWriteTests() => Directory.CreateDirectory(_directory);
    [Fact]
    public async Task OptimizedSessionWritePreservesEveryLibraryRowAndReopensLatestSession()
    {
        var path = Path.Combine(_directory, "library.db");
        var entries = Enumerable.Range(0, 10000).Select(i => new PlaylistEntry(Guid.NewGuid(), new(Guid.NewGuid(), @"C:\Music\" + i + ".flac", "Song " + i), i % 2 == 0)).ToArray();
        var tab = Guid.NewGuid(); var state = new LibraryState([new(tab, "Owned library", entries)], new(tab, tab, entries[0], 0));
        await using (var store = new SqlitePlayerStore(path))
        {
            await store.LoadAsync(); await store.SaveAsync(state, true);
            using (var connection = new SqliteConnection("Data Source=" + path))
            {
                connection.Open();
                foreach (var table in new[] { "Tracks", "Playlists", "PlaylistEntries" })
                    foreach (var action in new[] { "INSERT", "UPDATE", "DELETE" })
                    {
                        using var command = connection.CreateCommand();
                        command.CommandText = $"CREATE TRIGGER Guard{table}{action} BEFORE {action} ON {table} BEGIN SELECT RAISE(ABORT,'Unexpected library write'); END";
                        command.ExecuteNonQuery();
                    }
            }
            var detached = entries[0] with { Id = Guid.NewGuid(), Track = entries[0].Track with { Segment = new(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)) } };
            var order = new PlaybackOrder(); order.SetSource(entries); order.Enqueue([entries[1], entries[1]], false); order.Started(entries[0], false);
            state = state with { Session = new(tab, tab, detached, TimeSpan.FromSeconds(47).Ticks, order.Capture()) };
            await ((ISessionPlayerStore)store).SaveSessionAsync(state.Session);
        }
        await using var reopened = new SqlitePlayerStore(path); var loaded = await reopened.LoadAsync();
        Assert.Equal(entries, loaded.Playlists.Single().Entries);
        Assert.Equal(state.Session.ActiveEntry, loaded.Session.ActiveEntry); Assert.Equal(state.Session.PositionTicks, loaded.Session.PositionTicks);
        Assert.Equal(state.Session.Order!.Queue, loaded.Session.Order!.Queue); Assert.Equal(state.Session.Order.History, loaded.Session.Order.History);
    }
    [Theory]
    [InlineData(-1)]
    [InlineData(long.MaxValue)]
    public async Task InvalidSessionNeverOverwritesTheSavedSession(long position)
    {
        await using var store = new SqlitePlayerStore(Path.Combine(_directory, "library.db")); var state = await store.LoadAsync();
        await Assert.ThrowsAsync<InvalidDataException>(() => ((ISessionPlayerStore)store).SaveSessionAsync(state.Session with { PositionTicks = position }));
        Assert.Equal(state.Session, (await store.LoadAsync()).Session);
        var invalid = new PlaylistEntry(Guid.NewGuid(), new(Guid.NewGuid(), "https://example.com/music.flac", "Invalid"));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => ((ISessionPlayerStore)store).SaveSessionAsync(state.Session with { ActiveEntry = invalid }));
        Assert.Equal(state.Session, (await store.LoadAsync()).Session);
    }
    public void Dispose() { SqliteConnection.ClearAllPools(); Directory.Delete(_directory, true); }
}
