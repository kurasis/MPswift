using System.Text;
using System.Text.Json;
using Player.Core.Library;
using Player.Core.Media;

namespace Player.Core.Tests;

public sealed class CueImageAssociationTests
{
    private const string Document = @"C:\Music\Album.cue";
    private static CueSheet Sheet(string file = "Album.wav") => CueSheet.Parse("FILE \"" + file + "\" WAVE\nTRACK 01 AUDIO\nTITLE \"Первая\"\nINDEX 01 00:00:00\nTRACK 02 AUDIO\nTITLE \"Вторая\"\nINDEX 01 00:01:00", Document);

    [Theory]
    [InlineData("Album.wav", "Album.flac")]
    [InlineData("Old name.wav", "Album.flac")]
    [InlineData("Old name.ape", "Renamed album.flac")]
    public void MissingSingleImageReferenceResolvesWithoutChangingTimingOrIdentity(string reference, string image)
    {
        var original = Sheet(reference); var path = @"C:\Music\" + image;
        var resolved = CueImageAssociation.Resolve(original, Document, [path], _ => false);
        Assert.All(resolved.Songs, song => Assert.Equal(path, song.Path));
        Assert.Equal(original.Songs.Select(song => song.StartFrame), resolved.Songs.Select(song => song.StartFrame));
        Assert.Equal(original.Songs.Select(song => song.Title), resolved.Songs.Select(song => song.Title));
        Assert.Equal(original.Songs.Select(song => CueSheet.TrackId(Document, song)), resolved.Songs.Select(song => CueSheet.TrackId(Document, song)));
        Assert.All(original.Songs, song => Assert.EndsWith(reference, song.Path));
    }

    [Fact]
    public void ExistingReferenceIsNeverRedirected()
    {
        var original = Sheet();
        Assert.Same(original, CueImageAssociation.Resolve(original, Document, [@"C:\Music\Album.flac"], _ => true));
    }

    [Fact]
    public void MultipleImagesRequireAnUnambiguousFilenameMatch()
    {
        var original = Sheet("Unknown.wav");
        Assert.Same(original, CueImageAssociation.Resolve(original, Document, [@"C:\Music\One.flac", @"C:\Music\Two.flac"], _ => false));
        var resolved = CueImageAssociation.Resolve(original, Document, [@"C:\Music\One.flac", @"C:\Music\Album.flac"], _ => false);
        Assert.All(resolved.Songs, song => Assert.Equal(@"C:\Music\Album.flac", song.Path));
    }

    [Fact]
    public void MultiFileMalformedAndOutsideFolderReferencesArePreserved()
    {
        var images = new[] { @"C:\Music\Album.flac" };
        var multi = CueSheet.Parse("FILE \"one.wav\" WAVE\nTRACK 01 AUDIO\nINDEX 01 00:00:00\nFILE \"two.wav\" WAVE\nTRACK 02 AUDIO\nINDEX 01 00:00:00", Document);
        var invalid = CueSheet.Parse("FILE \"Album.wav\" WAVE\nTRACK 01 AUDIO\nINDEX 01 00:00:00\nTRACK 02 AUDIO\nINDEX 01 00:00:00", Document);
        var outside = Sheet(@"Other\Album.wav");
        foreach (var sheet in new[] { multi, invalid, outside }) Assert.Same(sheet, CueImageAssociation.Resolve(sheet, Document, images, _ => false));
    }

    [Fact]
    public void ConfiguredLegacyEncodingPreservesRussianTitles()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var encoding = Encoding.GetEncoding(1251, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        const string text = "FILE \"Альбом.wav\" WAVE\nTRACK 01 AUDIO\nTITLE \"Первая\"\nINDEX 01 00:00:00\nTRACK 02 AUDIO\nTITLE \"Вторая\"\nINDEX 01 00:01:00";
        var bytes = encoding.GetBytes(text);
        Assert.Throws<DecoderFallbackException>(() => CueSheet.Decode(bytes));
        var sheet = CueSheet.Parse(CueSheet.Decode(bytes, encoding), Document);
        Assert.Equal(new[] { "Первая", "Вторая" }, sheet.Songs.Select(song => song.Title));
        Assert.Equal(text, CueSheet.Decode(Encoding.UTF8.GetBytes(text), encoding));
    }

    [Fact]
    public void LegacyPreferenceIsBackwardCompatibleAndBounded()
    {
        Assert.Equal(1251, JsonSerializer.Deserialize<PlayerSettings>("{\"SchemaVersion\":1,\"Language\":\"ru\"}")!.Validate().CueCodePage);
        foreach (var codePage in new[] { 0, 1251, 1252, 866 }) Assert.Equal(codePage, new PlayerSettings(CueCodePage: codePage).Validate().CueCodePage);
        Assert.Equal(1251, new PlayerSettings(CueCodePage: 9999).Validate().CueCodePage);
    }
}
