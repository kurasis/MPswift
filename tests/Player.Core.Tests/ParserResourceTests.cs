using System.Text;
using Player.Core.Media;

namespace Player.Core.Tests;

public sealed class ParserResourceTests
{
    [Fact]
    public void CueGlobalRemarksAreSharedWithoutMultiplyingTheirStorageAndTrackOverridesAreIndependent()
    {
        var text = new StringBuilder();
        for (var i = 0; i < 10000; i++) text.Append("REM KEY").Append(i).Append(" value\n");
        text.Append("FILE \"album.flac\" WAVE\n");
        for (var i = 1; i <= 999; i++)
        {
            text.Append("TRACK ").Append(i).Append(" AUDIO\nINDEX 01 ").Append(i).Append(":00:00\n");
            if (i == 1) text.Append("REM KEY0 overridden\nREM PRIVATE first-only\n");
        }
        var document = text.ToString();
        var before = GC.GetAllocatedBytesForCurrentThread();
        var cue = CueSheet.Parse(document, @"C:\Owned\album.cue");
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Empty(cue.Diagnostics); Assert.Equal(999, cue.Songs.Length);
        Assert.Equal("overridden", cue.Songs[0].Remarks["key0"]);
        Assert.Equal("value", cue.Songs[1].Remarks["KEY0"]);
        Assert.False(cue.Songs[1].Remarks.ContainsKey("PRIVATE"));
        Assert.All(cue.Songs.Skip(1), song => Assert.Equal(10000, song.Remarks.Count));
        // A dictionary per track previously required roughly 280 MiB of backing arrays alone.
        Assert.True(allocated < 96L * 1024 * 1024, $"CUE parse allocated {allocated} bytes.");
    }

    [Fact]
    public void CueFileTransitionsSnapshotGlobalRemarksAndPreserveCaseInsensitiveUpdates()
    {
        var cue = CueSheet.Parse("REM GENRE Rock\nFILE a.flac WAVE\nTRACK 01 AUDIO\nINDEX 01 00:00:00\nREM genre Metal\nFILE b.flac WAVE\nREM Genre Jazz\nTRACK 02 AUDIO\nINDEX 01 00:00:00", @"C:\Owned\album.cue");
        Assert.Empty(cue.Diagnostics); Assert.Equal("Metal", cue.Songs[0].Remarks["GENRE"]);
        Assert.Equal("Jazz", cue.Songs[1].Remarks["genre"]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FullPlaylistBoundaryRemainsUsableAndOneMoreEntryIsRejected(bool pls)
    {
        var text = string.Join('\n', Enumerable.Range(1, 10000).Select(i => pls ? $"File{i}=track{i}.flac" : $"track{i}.flac"));
        var parsed = PlaylistDocument.Parse(text, @"C:\Owned\list.m3u8", pls);
        Assert.Equal(10000, parsed.Paths.Length); Assert.Empty(parsed.Diagnostics);
        Assert.Throws<InvalidDataException>(() => PlaylistDocument.Parse(text + (pls ? "\nFile10001=extra.flac" : "\nextra.flac"), @"C:\Owned\list.m3u8", pls));
    }

    [Fact]
    public void MalformedTextMutationsCannotIntroduceRemoteOrRecursiveSources()
    {
        var random = new Random(0x4d5053);
        var seed = "FILE \"album.flac\" WAVE\nTRACK 01 AUDIO\nTITLE \"Song\"\nINDEX 01 00:00:00\n";
        var malicious = "https://example.invalid/a.flac\n\\\\server\\share\\a.flac\nrecursive.cue\nrecursive.m3u8\nvalid.flac";
        for (var i = 0; i < 128; i++)
        {
            var characters = seed.ToCharArray(); characters[random.Next(characters.Length)] = (char)random.Next(128);
            var cue = CueSheet.Parse(new string(characters), @"C:\Owned\album.cue");
            Assert.All(cue.Songs, song => Assert.StartsWith(@"C:\", LocalMediaPath.Parse(song.Path).Value));
            Assert.True(cue.Diagnostics.Length <= 100);
        }
        var playlist = PlaylistDocument.Parse(malicious, @"C:\Owned\list.m3u8");
        Assert.Equal(new[] { @"C:\Owned\valid.flac" }, playlist.Paths); Assert.Equal(4, playlist.Diagnostics.Length);
    }
}
