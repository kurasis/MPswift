using Microsoft.Data.Sqlite;
using Player.App.Services.Storage;
using Player.Core.Library;
using Player.Core.Playback;

namespace Player.Core.Tests;

public sealed class IncrementalSaveTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "mpswift-incremental-" + Guid.NewGuid().ToString("N"));
    private string DatabasePath => Path.Combine(_directory, "library.db");
    public IncrementalSaveTests() => Directory.CreateDirectory(_directory);

    private static PlaylistEntry[] Entries(string folder, int count) =>
        Enumerable.Range(0, count).Select(i => new PlaylistEntry(Guid.NewGuid(), new(Guid.NewGuid(), $@"C:\{folder}\{i}.flac", $"{folder} {i}"), AddedUtcTicks: i)).ToArray();
    private void Execute(string sql)
    {
        using var connection = new SqliteConnection("Data Source=" + DatabasePath + ";Pooling=False");
        connection.Open(); using var command = connection.CreateCommand(); command.CommandText = sql; command.ExecuteNonQuery();
    }
    private void GuardEntries(Guid playlist) => Execute($"CREATE TRIGGER Guard{playlist:N} BEFORE DELETE ON PlaylistEntries WHEN OLD.PlaylistId='{playlist}' BEGIN SELECT RAISE(ABORT,'Unchanged playlist rewritten'); END");
    private static void AssertSaved(LibraryState expected, LibraryState loaded)
    {
        Assert.Equal(expected.Playlists.Select(tab => (tab.Id, tab.Name)), loaded.Playlists.Select(tab => (tab.Id, tab.Name)));
        for (var i = 0; i < expected.Playlists.Length; i++) Assert.Equal(expected.Playlists[i].Entries, loaded.Playlists[i].Entries);
    }

    [Fact]
    public async Task ChangedPlaylistsAndTracksAreWrittenWithoutRewritingUnchangedRows()
    {
        var first = Guid.NewGuid(); var second = Guid.NewGuid();
        var shared = Entries("Shared", 1)[0];
        var firstEntries = Entries("First", 3000).Append(shared).ToArray();
        var secondEntries = Entries("Second", 3000).Append(shared with { Id = Guid.NewGuid() }).ToArray();
        var state = new LibraryState([new(first, "First", firstEntries), new(second, "Second", secondEntries)], new(first, first, null, 0));
        await using (var store = new SqlitePlayerStore(DatabasePath))
        {
            await store.LoadAsync(); await store.SaveAsync(state, true);
            GuardEntries(second);

            // One row changes in the first playlist only.
            firstEntries = firstEntries.ToArray(); firstEntries[5] = firstEntries[5] with { Enabled = false };
            state = state with { Playlists = [state.Playlists[0] with { Entries = firstEntries }, state.Playlists[1]] };
            await store.SaveAsync(state, true); AssertSaved(state, await store.LoadAsync());

            // Renaming and reordering tabs keeps every entry row.
            GuardEntries(first);
            state = state with { Playlists = [state.Playlists[1] with { Name = "Renamed" }, state.Playlists[0]] };
            await store.SaveAsync(state, true); AssertSaved(state, await store.LoadAsync());

            // Metadata of a track shared by both playlists updates only its track row.
            var revised = shared.Track with { Title = "Revised", Artist = "Artist" };
            PlaylistState Revise(PlaylistState tab) => tab with { Entries = tab.Entries.Select(entry => entry.Track.Id == revised.Id ? entry with { Track = revised } : entry).ToArray() };
            state = state with { Playlists = state.Playlists.Select(Revise).ToArray() };
            await store.SaveAsync(state, true); AssertSaved(state, await store.LoadAsync());
            Execute($"DROP TRIGGER Guard{first:N}");

            // Removing a playlist drops its rows and orphaned tracks; a new playlist is inserted.
            var third = new PlaylistState(Guid.NewGuid(), "Third", Entries("Third", 10));
            state = state with { Playlists = [state.Playlists[0], third] };
            await store.SaveAsync(state, true); AssertSaved(state, await store.LoadAsync());
        }
        using var connection = new SqliteConnection("Data Source=" + DatabasePath + ";Pooling=False"); connection.Open();
        using var count = connection.CreateCommand(); count.CommandText = "SELECT COUNT(*) FROM Tracks";
        Assert.Equal(3011L, (long)count.ExecuteScalar()!);
    }

    [Fact]
    public async Task FailedIncrementalSaveRollsBackAndTheNextSaveRewritesEverything()
    {
        var tab = Guid.NewGuid(); var entries = Entries("Music", 20);
        var state = new LibraryState([new(tab, "Music", entries)], new(tab, tab, null, 0));
        await using var store = new SqlitePlayerStore(DatabasePath);
        await store.LoadAsync(); await store.SaveAsync(state, true);
        Execute("CREATE TRIGGER Reject BEFORE INSERT ON PlaylistEntries BEGIN SELECT RAISE(ABORT,'Rejected'); END");
        var changed = state with { Playlists = [state.Playlists[0] with { Entries = Enumerable.Reverse(entries).ToArray() }] };
        await Assert.ThrowsAsync<SqliteException>(() => store.SaveAsync(changed, true));
        AssertSaved(state, await store.LoadAsync());
        Execute("DROP TRIGGER Reject");
        await store.SaveAsync(changed, true); AssertSaved(changed, await store.LoadAsync());
    }

    [Fact]
    public async Task InvalidStateIsRejectedOnTheDatabaseThreadWithoutWriting()
    {
        var tab = Guid.NewGuid(); var entries = Entries("Music", 5);
        var state = new LibraryState([new(tab, "Music", entries)], new(tab, tab, null, 0));
        await using var store = new SqlitePlayerStore(DatabasePath);
        await store.LoadAsync(); await store.SaveAsync(state, true);
        var invalid = new PlaylistEntry(Guid.NewGuid(), new(Guid.NewGuid(), "https://example.com/a.flac", "Remote"));
        var save = store.SaveAsync(state with { Playlists = [state.Playlists[0] with { Entries = [invalid] }] }, true);
        await Assert.ThrowsAnyAsync<ArgumentException>(() => save);
        AssertSaved(state, await store.LoadAsync());
    }

    public void Dispose() { SqliteConnection.ClearAllPools(); Directory.Delete(_directory, true); }
}
