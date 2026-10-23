using System.Text.Json;
using Player.Core.Library;
using Player.Core.Playback;

namespace Player.Core.Tests;

public sealed class AlbumSectionsTests
{
    private static MediaTrack Track(string path, string? album = null) => new(Guid.NewGuid(), path, "Song", Album: album);

    [Theory]
    [InlineData(@"C:\Music\Album\one.flac", " Album ", "c:/music/album/two.flac", "album", true)]
    [InlineData(@"C:\Music\Album\one.flac", null, @"C:\Music\Album\two.flac", "   ", true)]
    [InlineData(@"C:\Music\A\one.flac", "Album", @"C:\Music\B\two.flac", "Album", false)]
    [InlineData(@"C:\Music\A\one.flac", "Album", @"C:\Music\A\two.flac", "Other", false)]
    [InlineData(@"C:\Music\A\CD1\one.flac", "Album", @"C:\Music\A\CD2\two.flac", "Album", false)]
    [InlineData("/music/Беларускае/one.flac", "Дні", "/music/Беларускае/two.flac", " Дні ", true)]
    public void SelectionUsesTheSameFolderAndAlbumIdentityAsHeadings(string first, string? album, string second, string? other, bool same)
    {
        Assert.Equal(same, AlbumSections.IsSameAlbum(Track(first, album), Track(second, other)));
        Assert.Equal(same ? 1 : 2, AlbumSections.Create([Track(first, album), Track(second, other)]).Count);
    }

    [Fact]
    public void AlbumSelectionIncludesRepeatedOccurrencesAndCueSegmentsWithoutReordering()
    {
        var first = Track(@"C:\Music\Album\image.flac", "Album") with { Segment = new(TimeSpan.Zero, TimeSpan.FromSeconds(1)) };
        var second = first with { Id = Guid.NewGuid(), Segment = new(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)) };
        var other = Track(@"C:\Music\Other\song.flac", "Album");
        var tracks = new[] { first, other, second, first }; var ids = tracks.Select(track => track.Id).ToArray();
        Assert.Equal([first, second, first], tracks.Where(track => AlbumSections.IsSameAlbum(first, track)));
        Assert.Equal(ids, tracks.Select(track => track.Id));
    }

    [Fact]
    public void NestedDiscsRemainSeparateEvenWithTheSameAlbumTag()
    {
        var sections = AlbumSections.Create([Track(@"C:\Music\Album\CD1\01.flac", "Album"), Track(@"C:\Music\Album\CD1\02.flac", "Album"), Track(@"C:\Music\Album\CD2\01.flac", "Album")]);
        Assert.Equal([0, 2], sections.Select(s => s.StartIndex));
        Assert.Equal([2, 1], sections.Select(s => s.Count));
        Assert.All(sections, s => Assert.Equal("Album", s.Title));
        Assert.EndsWith("CD2", sections[1].Folder);
    }

    [Fact]
    public void RepeatedAlbumsDoNotReorderOrMergeOccurrences()
    {
        var a = Track("/music/one/song.flac", "First");
        var b = Track("/music/one/other.flac", "Second");
        var tracks = new[] { a, a, b, a };
        var ids = tracks.Select(t => t.Id).ToArray();
        var sections = AlbumSections.Create(tracks);
        Assert.Equal([0, 2, 3], sections.Select(s => s.StartIndex));
        Assert.Equal([2, 1, 1], sections.Select(s => s.Count));
        Assert.Equal(ids, tracks.Select(t => t.Id));
    }

    [Fact]
    public void MissingTagsUseFoldersAndSameNamesKeepDistinctLocations()
    {
        var sections = AlbumSections.Create([Track("/music/A/Альбом/song.wav"), Track("/music/B/Альбом/song.wav", "  ")]);
        Assert.Equal(2, sections.Count);
        Assert.All(sections, s => Assert.Equal("Альбом", s.Title));
        Assert.NotEqual(sections[0].Folder, sections[1].Folder);
    }

    [Fact]
    public void FilteredRunsStartAtTheFirstRemainingTrack()
    {
        var tracks = new[] { Track(@"C:\Album\1.wav", " Album "), Track(@"c:\album\2.wav", "album"), Track(@"C:\Other\3.wav") };
        var all = AlbumSections.Create(tracks);
        Assert.Equal(2, all[0].Count);
        var filtered = AlbumSections.Create(tracks.Skip(1));
        Assert.Equal([0, 1], filtered.Select(s => s.StartIndex));
        Assert.All(filtered, s => Assert.Equal(1, s.Count));
        Assert.Empty(AlbumSections.Create([]));
    }

    [Fact]
    public void ExistingSettingsEnableHeadingsAndPreferenceRoundTrips()
    {
        var old = JsonSerializer.Deserialize<PlayerSettings>("{\"SchemaVersion\":1,\"Language\":\"ru\"}")!.Validate();
        Assert.True(old.ShowAlbumSections);
        var reopened = JsonSerializer.Deserialize<PlayerSettings>(JsonSerializer.Serialize(old with { ShowAlbumSections = false }))!.Validate();
        Assert.False(reopened.ShowAlbumSections);
        Assert.Equal("ru", reopened.Language);
    }

    [Fact]
    public void AllocationFreeHeadingsMatchTheNormalizedStringDefinition()
    {
        // Reference: the original definition normalizes separators, then compares folder and trimmed album ignoring case.
        static (string Folder, string Album) Key(MediaTrack track)
        {
            var path = track.Path.Replace('\\', '/'); var slash = path.LastIndexOf('/');
            return (slash >= 0 ? path[..slash] : "", track.Album?.Trim() ?? "");
        }
        var random = new Random(11);
        string[] folders = [@"C:\Music\A", "C:/Music/A", @"c:\music\a", @"C:\Music\Ä", @"C:\Music/ä", @"C:\Music\AB", "", @"D:\x"];
        string?[] albums = ["One", " one ", "ONE", "Two", null, "", "ä", "Ä"];
        for (var attempt = 0; attempt < 300; attempt++)
        {
            var tracks = Enumerable.Range(0, random.Next(1, 40)).Select(i =>
            {
                var folder = folders[random.Next(folders.Length)];
                return new MediaTrack(Guid.NewGuid(), (folder.Length > 0 ? folder + "\\" : "") + i + ".flac", "T", Album: albums[random.Next(albums.Length)]);
            }).ToArray();
            var expected = new List<AlbumSection>();
            for (var i = 0; i < tracks.Length; i++)
            {
                var (folder, album) = Key(tracks[i]);
                var start = expected.Count == 0 ? null : tracks[expected[^1].StartIndex];
                if (start is null || !string.Equals(folder, Key(start).Folder, StringComparison.OrdinalIgnoreCase) || !string.Equals(album, Key(start).Album, StringComparison.OrdinalIgnoreCase))
                {
                    if (expected.Count > 0) expected[^1] = expected[^1] with { Count = i - expected[^1].StartIndex };
                    expected.Add(new(i, 0, album.Length > 0 ? album : folder[(folder.LastIndexOf('/') + 1)..], folder));
                }
            }
            expected[^1] = expected[^1] with { Count = tracks.Length - expected[^1].StartIndex };
            Assert.Equal(expected, AlbumSections.Create(tracks));
            for (var i = 1; i < tracks.Length; i++)
                Assert.Equal(string.Equals(Key(tracks[i]).Folder, Key(tracks[0]).Folder, StringComparison.OrdinalIgnoreCase) && string.Equals(Key(tracks[i]).Album, Key(tracks[0]).Album, StringComparison.OrdinalIgnoreCase),
                    AlbumSections.IsSameAlbum(tracks[0], tracks[i]));
        }
        Assert.True(PlaylistSearch.Matches(new(Guid.NewGuid(), @"C:\Music\Café.flac", "Title"), ""));
        Assert.True(PlaylistSearch.Matches(new(Guid.NewGuid(), "C:\\Music\\Cafe\u0301.flac", "Title"), "café"));
    }
}
