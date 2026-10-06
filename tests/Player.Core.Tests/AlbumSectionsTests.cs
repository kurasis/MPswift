using System.Text.Json;
using Player.Core.Library;
using Player.Core.Playback;

namespace Player.Core.Tests;

public sealed class AlbumSectionsTests
{
    private static MediaTrack Track(string path, string? album = null) => new(Guid.NewGuid(), path, "Song", Album: album);

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
}
