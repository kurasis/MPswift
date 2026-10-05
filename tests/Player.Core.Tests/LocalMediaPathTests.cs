using Player.Core.Media;

namespace Player.Core.Tests;

public sealed class LocalMediaPathTests
{
    [Theory]
    [InlineData("https://example.com/a.mp3")]
    [InlineData("file:///C:/a.wav")]
    [InlineData(@"\\server\music\a.flac")]
    [InlineData(@"\\?\C:\music\a.wav")]
    [InlineData(@"\\.\PhysicalDrive0")]
    [InlineData(@"C:relative.wav")]
    [InlineData(@"C:\music\song.wav:stream")]
    [InlineData(@"C:\..\song.wav")]
    [InlineData(@"C:\music\CON.wav")]
    [InlineData(@"C:\music\LPT¹.wav")]
    [InlineData(@"C:\music\bad?.wav")]
    [InlineData("C:\\music\\bad\0.wav")]
    [InlineData(@"C:\music\trailing.\song.wav")]
    [InlineData(@"C:\")]
    [InlineData("")]
    public void RejectsNonLocalOrAmbiguousInput(string input) =>
        Assert.Throws<ArgumentException>(() => LocalMediaPath.Parse(input));

    [Fact]
    public void PreservesUnicodeAndNormalizesDirectoryTraversal()
    {
        var path = LocalMediaPath.Parse("c:/Музыка/ignored/../Björk's e\u0301 🎵.flac");
        Assert.Equal("C:\\Музыка\\Björk's e\u0301 🎵.flac", path.Value);
    }

    [Fact]
    public void DoesNotInterpretShellTextInFilenames() =>
        Assert.Equal(@"C:\music\$(echo secret).wav", LocalMediaPath.Parse(@"C:\music\$(echo secret).wav").Value);
}
