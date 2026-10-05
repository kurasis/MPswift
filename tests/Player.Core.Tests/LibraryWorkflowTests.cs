using Microsoft.Data.Sqlite;
using Player.App.Services.Storage;
using Player.Core.Library;
using Player.Core.Media;
using Player.Core.Playback;

namespace Player.Core.Tests;

public sealed class LibraryWorkflowTests
{
    private static MediaTrack Track(string name, Guid? id = null) => new(id ?? Guid.NewGuid(), "C:\\Music\\" + name + ".flac", name, "Артист", "Café %_");
    [Fact] public void M3u8RoundTripPreservesDuplicateOrderUnicodeAndRejectsLossyCueExport()
    {
        var a = new PlaylistEntry(Guid.NewGuid(), Track("Песня")); var b = new PlaylistEntry(Guid.NewGuid(), Track("second"));
        var text = PlaylistDocument.ExportM3u8([a, b, a], "C:\\Music\\mix.m3u8"); var parsed = PlaylistDocument.Parse(text, "C:\\Music\\mix.m3u8");
        Assert.Equal(new[] { a.Track.Path, b.Track.Path, a.Track.Path }, parsed.Paths); Assert.Empty(parsed.Diagnostics);
        Assert.Throws<InvalidOperationException>(() => PlaylistDocument.ExportM3u8([a with { Track = a.Track with { Segment = new(TimeSpan.Zero, TimeSpan.FromSeconds(1)) } }], "C:\\Music\\mix.m3u8"));
    }
    [Fact] public void PlsNumericOrderAndOfflineRecursivePolicy()
    {
        var pls = PlaylistDocument.Parse("[playlist]\nFile2=two.flac\nFile1=one.flac\nTitle1=Ignored hint\nFile3=https://invalid/song\nFile4=recursive.m3u8", "C:\\Music\\mix.pls", true);
        Assert.Equal(new[] { "C:\\Music\\one.flac", "C:\\Music\\two.flac" }, pls.Paths); Assert.Equal(2, pls.Diagnostics.Length);
        Assert.Throws<InvalidDataException>(() => PlaylistDocument.Parse("File1=a.wav\nFile1=b.wav", "C:\\Music\\a.pls", true));
    }
    [Fact] public void BomAndExplicitCyrillicFallbackDoNotSilentlyReplaceBytes()
    {
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        var encoding = System.Text.Encoding.GetEncoding(1251, System.Text.EncoderFallback.ExceptionFallback, System.Text.DecoderFallback.ExceptionFallback);
        var bytes = encoding.GetBytes("Музыка.flac"); Assert.Throws<System.Text.DecoderFallbackException>(() => CueSheet.Decode(bytes)); Assert.Equal("Музыка.flac", CueSheet.Decode(bytes, encoding));
    }
    [Fact] public async Task ActualIndexLiteralUnicodePagingMissingReconcileRetainsRatingsAndPlaylists()
    {
        var directory = Path.Combine(Path.GetTempPath(), "library-" + Guid.NewGuid()); Directory.CreateDirectory(directory);
        try
        {
            await using var store = new SqlitePlayerStore(Path.Combine(directory, "player.db")); var root = new LibraryRoot(Guid.NewGuid(), "C:\\Music"); await store.PutRootAsync(root);
            var track = Track("Песня Café %_"); var record = new IndexedFile(Guid.NewGuid(), root.Id, track.Path, 42, 123, true, "old", track);
            await store.UpsertFilesAsync([record]); await store.SetRatingAsync(track.Id, 5);
            var occurrence = new ListeningEvent(Guid.NewGuid(), track.Id, DateTime.UtcNow.Ticks, TimeSpan.FromSeconds(8).Ticks, true); await store.RecordListeningAsync(occurrence); await store.RecordListeningAsync(occurrence);
            var tab = Guid.NewGuid(); await store.SaveAsync(new([new(tab, "Keep", [new(Guid.NewGuid(), track)])], new(tab, tab, null, 0)), true);
            Assert.Single((await store.SearchAsync("пЕСНЯ café %_")).Files); Assert.Empty((await store.SearchAsync("' OR 1=1 --")).Files);
            await store.CompleteScanAsync(root.Id, "new"); var missing = (await store.SearchAsync("%_")).Files.Single(); Assert.False(missing.Available); Assert.False(missing.Track.Available);
            var stat = (await store.GetStatisticsAsync([track.Id])).Single(); Assert.Equal(5, stat.Rating); Assert.Equal(1, stat.PlayCount);
            Assert.Single((await store.LoadAsync()).Playlists[0].Entries);
            await store.UpsertFilesAsync([record with { Generation = "new", ModifiedUtcTicks = 124, Track = track with { Title = "Revised" } }]);
            Assert.Single((await store.SearchAsync("Revised")).Files); Assert.Equal(5, (await store.GetStatisticsAsync([track.Id])).Single().Rating);
        }
        finally { Directory.Delete(directory, true); }
    }
    [Fact] public async Task SchemaOneMigrationCreatesValidBackupAndRetainsStableSession()
    {
        var directory = Path.Combine(Path.GetTempPath(), "migration-" + Guid.NewGuid()); Directory.CreateDirectory(directory); var path = Path.Combine(directory, "player.db");
        try
        {
            var state = LibraryState.CreateDefault(); await using (var store = new SqlitePlayerStore(path)) await store.SaveAsync(state, true);
            using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString()))
            { connection.Open(); using var command = connection.CreateCommand(); command.CommandText = "DROP TABLE MediaIndex; DROP TABLE LibraryRoots; DROP TABLE TrackStatistics; DROP TABLE ListeningHistory; ALTER TABLE PlaylistEntries DROP COLUMN AddedUtcTicks; PRAGMA user_version=1"; command.ExecuteNonQuery(); }
            await using (var migrated = new SqlitePlayerStore(path)) { var read = await migrated.LoadAsync(); Assert.Equal(state.Playlists[0].Id, read.Playlists[0].Id); Assert.Empty(await migrated.GetRootsAsync()); }
            var backup = Assert.Single(Directory.GetFiles(directory, "*.pre-schema2-*.db"));
            using var original = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = backup, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString()); original.Open(); using var check = original.CreateCommand(); check.CommandText = "PRAGMA user_version"; Assert.Equal(1L, check.ExecuteScalar()); check.CommandText = "PRAGMA quick_check"; Assert.Equal("ok", check.ExecuteScalar());
        }
        finally { Directory.Delete(directory, true); }
    }
    [Fact] public async Task HundredThousandIndexRowsReturnOnlyBoundedPagesAndStableIds()
    {
        var directory = Path.Combine(Path.GetTempPath(), "large-index-" + Guid.NewGuid()); Directory.CreateDirectory(directory);
        try
        {
            await using var store = new SqlitePlayerStore(Path.Combine(directory, "player.db")); var root = new LibraryRoot(Guid.NewGuid(), "C:\\Music"); await store.PutRootAsync(root);
            for (var start = 0; start < 100000; start += 64)
            {
                var batch = Enumerable.Range(start, Math.Min(64, 100000 - start)).Select(i => { var track = Track("Item " + i.ToString("D6")); return new IndexedFile(Guid.NewGuid(), root.Id, track.Path, i, 123, true, "scan", track); }).ToArray(); await store.UpsertFilesAsync(batch);
            }
            var page = await store.SearchAsync("Item", 99900); Assert.Equal(100000, page.Total); Assert.Equal(100, page.Files.Length); Assert.Equal("Item 099900", page.Files[0].Track.Title);
            var found = await store.FindFilesAsync([page.Files[0].Path]); Assert.Equal(page.Files[0].Track.Id, found[0].Track.Id); Assert.Empty((await store.SearchAsync("Item", 100000)).Files);
        }
        finally { Directory.Delete(directory, true); }
    }
    [Fact] public void ListeningUsesElapsedPlayingTimeNotSeekDistanceAndCountsOnce()
    {
        var meter = new ListeningMeter(); var entry = Guid.NewGuid(); var track = Guid.NewGuid(); var now = DateTime.UtcNow;
        var snapshot = PlaybackSnapshot.Empty with { EntryId = entry, Duration = TimeSpan.FromSeconds(100), State = PlaybackState.Playing };
        Assert.Null(meter.Update(snapshot, track, TimeSpan.Zero, now));
        Assert.Null(meter.Update(snapshot with { Position = TimeSpan.FromSeconds(90), State = PlaybackState.Paused }, track, TimeSpan.FromSeconds(1), now));
        Assert.Null(meter.Update(snapshot with { State = PlaybackState.Paused }, track, TimeSpan.FromSeconds(100), now));
        Assert.Null(meter.Update(snapshot, track, TimeSpan.FromSeconds(101), now));
        ListeningEvent? counted = null; for (var i = 102; i <= 151; i++) counted ??= meter.Update(snapshot, track, TimeSpan.FromSeconds(i), now);
        Assert.NotNull(counted); Assert.Equal(track, counted.TrackId); Assert.Null(meter.Update(snapshot, track, TimeSpan.FromSeconds(152), now));
    }
}
