using Player.Core.Media;

namespace Player.Core.Tests;

public sealed class CueImportSelectionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FolderImageAndCueAreImportedOnceRegardlessOfEnumerationOrder(bool cueFirst)
    {
        const string image = @"C:\Music\Album.flac", cue = @"C:\Music\Album.cue";
        var sources = cueFirst ? new[] { cue, image } : new[] { image, cue };
        Assert.Equal([cue], CueImportSelection.Resolve(sources, _ => cue));
    }

    [Fact]
    public void RepeatedImagesAndExplicitCueOccurrencesArePreserved()
    {
        Assert.Equal(["album.cue", "album.cue"], CueImportSelection.Resolve(["album.flac", "album.flac"], _ => "album.cue"));
        Assert.Equal(["album.cue", "album.cue"], CueImportSelection.Resolve(["album.cue", "album.cue"], _ => throw new Exception()));
    }

    [Fact]
    public void AmbiguousMissingAndUnrelatedSidecarsKeepTheWholeImage()
    {
        Assert.Equal(["album.flac", "other.cue"], CueImportSelection.Resolve(["album.flac", "other.cue"], _ => null));
        Assert.Equal(["song.mp3", "other.cue"], CueImportSelection.Resolve(["song.mp3", "other.cue"], _ => throw new Exception()));
    }

    [Fact]
    public void MultipleAlbumsKeepCueOrderAndCaseInsensitivePairing()
    {
        var sources = new[] { "one.FLAC", "ONE.CUE", "two.flac", "two.cue" };
        Assert.Equal(["ONE.CUE", "two.cue"], CueImportSelection.Resolve(sources, p => p.StartsWith("one") ? "one.cue" : "two.cue"));
    }
}
