using System.Reflection;
using Microsoft.Data.Sqlite;
using Player.App.Services.Storage;
using Player.Core.Library;
using Player.Core.Playback;

namespace Player.Core.Tests;

public sealed class StoreShutdownTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task FailedFileReleaseStillClosesOtherPinsAndCompletesShutdown(int failures)
    {
        var directory = Path.Combine(Path.GetTempPath(), "player-shutdown-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var database = Path.Combine(directory, "library.db");
        var store = new SqlitePlayerStore(database);
        var streams = new List<FailingCloseStream>();
        try
        {
            var state = await store.LoadAsync();
            var entry = new PlaylistEntry(Guid.NewGuid(), new MediaTrack(Guid.NewGuid(), @"C:\Owned Music\track.flac", "Owned saved entry"));
            state = state with { Playlists = [state.Playlists[0] with { Entries = [entry] }] };
            await store.SaveAsync(state, true);
            // Inject failures into actual held-file cleanup, without changing the production constructor/API.
            var pins = (List<DataFileLease>)typeof(SqlitePlayerStore).GetField("_fileLeases", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(store)!;
            for (var index = 0; index < failures; index++)
            {
                var stream = new FailingCloseStream(Path.Combine(directory, $"failure-{index}.db"));
                streams.Add(stream);
                pins.Add((DataFileLease)Activator.CreateInstance(typeof(DataFileLease), BindingFlags.Instance | BindingFlags.NonPublic,
                    binder: null, args: [stream, false], culture: null)!);
            }
            var trailing = Path.Combine(directory, "trailing.db");
            pins.Add(DataFileLease.OpenOrCreate(trailing));
            var shutdown = store.DisposeAsync().AsTask();
            var error = await Assert.ThrowsAnyAsync<Exception>(() => shutdown.WaitAsync(TimeSpan.FromSeconds(10)));
            var errors = error is AggregateException aggregate ? aggregate.Flatten().InnerExceptions.ToArray() : [error];
            Assert.Equal(failures, errors.Length);
            Assert.All(errors, value => Assert.IsType<IOException>(value));
            Assert.All(streams, stream => Assert.Equal(1, stream.CloseAttempts));
            Assert.Same(error, await Assert.ThrowsAnyAsync<Exception>(() => store.DisposeAsync().AsTask()));
            foreach (var path in Directory.GetFiles(directory))
            {
                using var exclusive = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            }
            // On Windows this also detects a retained directory pin after the earlier release error.
            Directory.Move(directory, directory + ".moved");
            Directory.Move(directory + ".moved", directory);
            await using var reopened = new SqlitePlayerStore(database);
            var restored = await reopened.LoadAsync();
            Assert.Equal(state.Playlists[0].Id, restored.Playlists[0].Id);
            Assert.Equal(state.Playlists[0].Entries, restored.Playlists[0].Entries);
        }
        finally
        {
            try { await store.DisposeAsync(); } catch (IOException) { } catch (AggregateException) { }
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, true);
        }
    }

    private sealed class FailingCloseStream(string path) : FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read)
    {
        public int CloseAttempts { get; private set; }
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing) { CloseAttempts++; throw new IOException("Owned file-close failure control."); }
        }
    }
}
