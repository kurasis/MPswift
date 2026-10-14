using Player.Core.Playback;

namespace Player.Core.Tests;

public sealed class AlbumMoveTests
{
    private static PlaylistEntry Song(string folder, string name) => new(Guid.NewGuid(), new(Guid.NewGuid(), "/music/" + folder + "/" + name, name, Album: "Same album"));

    [Fact]
    public void MovingForwardAndBackwardRetainsSongsWithinAlbumAndOriginalInput()
    {
        var a1 = Song("A", "one"); var a2 = Song("A", "two"); var b = Song("B", "one"); var c = Song("C", "one");
        var entries = new[] { a1, a2, b, c }; var ids = entries.Select(entry => entry.Id).ToArray();
        var forward = AlbumSections.MoveRun(entries, a1.Id, entries.Length);
        Assert.Equal([b, c, a1, a2], forward);
        Assert.Equal(entries, AlbumSections.MoveRun(forward, a1.Id, 0));
        Assert.Equal(ids, entries.Select(entry => entry.Id));
    }

    [Fact]
    public void SameNamesInDifferentFoldersAndRepeatedRunsAreNotCombined()
    {
        var a = Song("A", "one"); var b = Song("B", "one"); var repeat = a with { Id = Guid.NewGuid() };
        var c = Song("C", "one");
        Assert.Equal([a, repeat, b, c], AlbumSections.MoveRun([a, b, repeat, c], repeat.Id, 1));
        Assert.Equal([b, repeat, c, a], AlbumSections.MoveRun([a, b, repeat, c], a.Id, 4));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    public void InteriorSelfAndInvalidBoundariesDoNotChangeAnything(int insertion)
    {
        var entries = new[] { Song("A", "one"), Song("A", "two"), Song("B", "one") };
        Assert.Equal(entries, AlbumSections.MoveRun(entries, entries[0].Id, insertion));
        Assert.Equal(entries, AlbumSections.MoveRun(entries, entries[1].Id, 0));
        Assert.Equal(entries, AlbumSections.MoveRun(entries, Guid.NewGuid(), 0));
        Assert.Empty(AlbumSections.MoveRun([], Guid.NewGuid(), 0));
    }

    [Fact]
    public void CueEntriesKeepTheirSharedImageTimingAndIndependentOccurrenceData()
    {
        var image = Song("A", "image.flac");
        var first = image with { Track = image.Track with { Segment = new(TimeSpan.Zero, TimeSpan.FromSeconds(1)) }, Enabled = false };
        var second = new PlaylistEntry(Guid.NewGuid(), image.Track with { Id = Guid.NewGuid(), Segment = new(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)) });
        var other = Song("B", "one");
        Assert.Equal([other, first, second], AlbumSections.MoveRun([first, second, other], first.Id, 3));
    }
}
