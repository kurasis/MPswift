using System.Security.Cryptography;
using System.Text;
using MPswift.TagRepair;
using Player.Core.Media;

[assembly: CollectionBehavior(DisableTestParallelization = true)]
namespace MPswift.TagRepair.Tests;

public sealed class RepairTests
{
    public RepairTests() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    private static string Broken(string text, int from = 1251, int to = 28591)
    { Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); return Encoding.GetEncoding(to).GetString(Encoding.GetEncoding(from).GetBytes(text)); }
    internal static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "fixtures", name);
    internal static byte[] LegacyGapMp3(string name, int gap)
    {
        var source = File.ReadAllBytes(Fixture(name));
        using var file = TagLib.File.Create(Fixture(name));
        var start = checked((int)file.InvariantStartPosition);
        Assert.InRange(start, 0, source.Length - 4);
        var result = new byte[source.Length + gap];
        source.AsSpan(0, start).CopyTo(result);
        "Legacy padding"u8.CopyTo(result.AsSpan(start));
        source.AsSpan(start).CopyTo(result.AsSpan(start + gap));
        return result;
    }
    [Theory]
    [InlineData("Гук ў вецеры", 1251, 28591)]
    [InlineData("Людзі і сонца", 1251, 1252)]
    [InlineData("Кастусь Герашчанка", 1251, 28591)]
    [InlineData("Людзі — Цэпэліны", 65001, 28591)]
    [InlineData("Людзі — Цэпэліны", 65001, 1252)]
    [InlineData("Людзі — Цэпэліны", 65001, 1251)]
    public void RepairsKnownLegacyAndUtf8Mojibake(string text, int from, int to)
    { Assert.Equal(text, TextRepair.Recover(Broken(text, from, to), @"C:\Беларускае\album.mp3")); }

    [Theory]
    [InlineData("×àðíîáûëüñêi ôîí", "Чарнобыльскi фон")]
    [InlineData("Ãîìåëüñêi âàëüñ", "Гомельскi вальс")]
    [InlineData("×àðíîáûëüñêi øëÿõ", "Чарнобыльскi шлях")]
    public void RepairsOwnerTitlesWithFilenameConfirmedAsciiI(string broken, string expected)
    {
        var context = @"C:\Беларускае\" + expected + ".mp3";
        Assert.Equal(expected, TextRepair.Recover(broken, context));
        Assert.Equal(expected, TextRepair.Recover(expected, context));
    }

    [Theory]
    [InlineData("×àðíîáûëüñêi", @"C:\Чарнобыльскi\album.mp3")]
    [InlineData("×àðíîáûëüñêi", @"C:\Беларускае\іншы.mp3")]
    [InlineData("Björki", @"C:\Беларускае\Björki.mp3")]
    [InlineData("Caféi", @"C:\Беларускае\Caféi.mp3")]
    [InlineData("Чарнобыльскi фон", @"C:\Беларускае\Чарнобыльскi фон.mp3")]
    public void PreservesAmbiguousOrIntactWordsWithAsciiI(string text, string context)
    { Assert.Equal(text, TextRepair.Recover(text, context)); }

    [Fact] public void OwnerTitleRewritePreservesRealMp3AndFlacAudio()
    {
        var root = Path.Combine(Path.GetTempPath(), "cli-owner-titles-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            foreach (var title in new[] { "Чарнобыльскi фон", "Гомельскi вальс", "Чарнобыльскi шлях" })
                foreach (var fixture in new[] { "mp3-cbr.mp3", "flac16.flac" })
                {
                    var extension = Path.GetExtension(fixture); var path = Path.Combine(root, title + extension);
                    File.Copy(Fixture(fixture), path);
                    using (var file = TagLib.File.Create(path)) { file.Tag.Title = Broken(title); file.Save(); }
                    var original = File.ReadAllBytes(path);
                    using var stream = new MemoryStream(); stream.Write(original);
                    var fingerprint = AudioFingerprint.Read(stream, extension); var edit = TagEditor.Read(stream, path);
                    Assert.Equal(title, System.Text.Json.JsonSerializer.Deserialize<string>(edit.After["Title"]));
                    TagEditor.Rewrite(stream, path, edit);
                    Assert.Equal(fingerprint, AudioFingerprint.Read(stream, extension));
                    var saved = TagEditor.Read(stream, path);
                    Assert.Equal(title, System.Text.Json.JsonSerializer.Deserialize<string>(saved.Before["Title"]));
                    Assert.Empty(saved.Changes); Assert.Equal(original, File.ReadAllBytes(path));
                }
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData("Людзі / Цэпэліны / 🎵")]
    [InlineData("Björk / Café / München / Straße / Déjà vu")]
    [InlineData("À bientôt / www / :B:N:")]
    [InlineData("Broken � metadata")]
    [InlineData(null)]
    public void PreservesValidOrIrreversibleText(string? text) => Assert.Equal(text, TextRepair.Recover(text, @"C:\Беларускае\song.mp3"));

    [Theory]
    [InlineData("mp3-cbr.mp3")]
    [InlineData("mp3-vbr.mp3")]
    [InlineData("flac16.flac")]
    [InlineData("flac24.flac")]
    public void RealEncodedAudioIsIdenticalAfterTagRewrite(string name)
    {
        var original = File.ReadAllBytes(Fixture(name));
        using var stream = new MemoryStream(); stream.Write(original);
        var before = AudioFingerprint.Read(stream, Path.GetExtension(name));
        var preview = TagEditor.Read(stream, name); TagEditor.Rewrite(stream, name, preview);
        Assert.Equal(before, AudioFingerprint.Read(stream, Path.GetExtension(name)));
        Assert.Equal(preview.After, TagEditor.Read(stream, name).Before);
        if (name.EndsWith(".mp3")) Assert.False(TagEditor.NeedsUnicodeRewrite(stream, name));
        Assert.Equal(original, File.ReadAllBytes(Fixture(name)));
    }

    [Theory]
    [InlineData("mp3-cbr.mp3", 32)]
    [InlineData("mp3-cbr.mp3", 257)]
    [InlineData("mp3-vbr.mp3", 32)]
    [InlineData("mp3-vbr.mp3", 257)]
    [InlineData("mp3-cbr.mp3", 8191)]
    public void LegacyGapBeforeRealMpegFramesIsPreservedDuringTagRewrite(string name, int gap)
    {
        var bytes = LegacyGapMp3(name, gap);
        using var stream = new MemoryStream(); stream.Write(bytes);
        var original = AudioFingerprint.Read(stream, ".mp3");
        var tags = TagEditor.Read(stream, name); TagEditor.Rewrite(stream, name, tags);
        Assert.Equal(original, AudioFingerprint.Read(stream, ".mp3"));
        using var source = TagLib.File.Create(Fixture(name));
        bytes[checked((int)source.InvariantStartPosition)] ^= 1;
        using var mutated = new MemoryStream(bytes);
        Assert.NotEqual(original, AudioFingerprint.Read(mutated, ".mp3"));
    }

    [Fact] public void UnrecognizedMpegSearchIsBoundedAndDoesNotAcceptIsolatedSyncBytes()
    {
        using var far = new MemoryStream(LegacyGapMp3("mp3-cbr.mp3", 8193));
        Assert.Throws<InvalidDataException>(() => AudioFingerprint.Read(far, ".mp3"));
        var bytes = new byte[8192]; "invalid prefix"u8.CopyTo(bytes);
        using var fixture = TagLib.File.Create(Fixture("mp3-cbr.mp3"));
        File.ReadAllBytes(Fixture("mp3-cbr.mp3")).AsSpan(checked((int)fixture.InvariantStartPosition), 4).CopyTo(bytes.AsSpan(32));
        using var isolated = new MemoryStream(bytes);
        Assert.Throws<InvalidDataException>(() => AudioFingerprint.Read(isolated, ".mp3"));
    }

    [Fact] public void ApeHeaderClaimCannotCreateNegativeMpegSearchBounds()
    {
        var bytes = new byte[90]; "ID3"u8.CopyTo(bytes); bytes[3] = 4; bytes[9] = 32;
        "APETAGEX"u8.CopyTo(bytes.AsSpan(10)); "APETAGEX"u8.CopyTo(bytes.AsSpan(58));
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(70), 48);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(78), 0x80000000);
        using var stream = new MemoryStream(bytes);
        Assert.Throws<InvalidDataException>(() => AudioFingerprint.Read(stream, ".mp3"));
    }

    [Fact] public void UnsupportedMpegPrefixReportsFileAndContinuesPreview()
    {
        var root = Path.Combine(Path.GetTempPath(), "cli-mpeg-prefix-owned-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "unsupported.mp3"); var bytes = LegacyGapMp3("mp3-cbr.mp3", 8193); File.WriteAllBytes(path, bytes);
            using (var file = TagLib.File.Create(path)) { file.Tag.Title = Broken("Людзі і сонца"); file.Save(); }
            bytes = File.ReadAllBytes(path);
            var album = Directory.CreateDirectory(Path.Combine(root, "next-album")).FullName;
            File.WriteAllBytes(Path.Combine(album, "valid.cue"), Encoding.GetEncoding(1251).GetBytes("TITLE \"Людзі\"\nFILE \"owned.flac\" WAVE\nTRACK 01 AUDIO\nINDEX 01 00:00:00\n"));
            using var output = new StringWriter(); using var errors = new StringWriter();
            Assert.Equal(1, Program.Run([root], output, errors));
            Assert.Contains("unsupported.mp3", errors.ToString()); Assert.Contains("Unrecognized MPEG audio start", errors.ToString());
            Assert.Contains("first bytes", errors.ToString()); Assert.Contains("planned=1; unchanged=0; errors=1", output.ToString());
            Assert.Equal(bytes, File.ReadAllBytes(path)); Assert.Empty(Directory.GetFiles(root, "*.bak", SearchOption.AllDirectories));
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData(1251)]
    [InlineData(65001)]
    [InlineData(1200)]
    [InlineData(1201)]
    [InlineData(12000)]
    [InlineData(12001)]
    public void CueConvertsEncodingWithoutChangingReferencesOrIndexes(int codePage)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var text = "REM DATE 2004\r\nPERFORMER \"Індыга\"\r\nTITLE \"Цэпэліны\"\r\nFILE \"Індыга - Дні.flac\" WAVE\r\n  TRACK 01 AUDIO\r\n    TITLE \"Людзі\"\r\n    INDEX 01 02:40:03\r\n";
        var encoding = Encoding.GetEncoding(codePage); var bytes = encoding.GetPreamble().Concat(encoding.GetBytes(text)).ToArray();
        var result = TextRepair.Cue(bytes, @"C:\Беларускае\album.cue", 1251);
        Assert.Equal(text, new UTF8Encoding(false, true).GetString(result));
        Assert.False(result.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf }));
        Assert.Equal(result, TextRepair.Cue(result, "album.cue", 1251));
    }

    [Fact] public void CueRepairsMetadataButLeavesFileReferencesVerbatim()
    {
        var broken = Broken("Людзі і сонца"); var reference = Broken("Індыга");
        var text = $"FILE \"{reference}.flac\" WAVE\nTRACK 01 AUDIO\nTITLE \"{broken}\"\nINDEX 01 00:00:00\n";
        var result = Encoding.UTF8.GetString(TextRepair.Cue(Encoding.UTF8.GetBytes(text), "album.cue", 1251));
        Assert.Contains($"FILE \"{reference}.flac\" WAVE", result); Assert.Contains("TITLE \"Людзі і сонца\"", result);
        Assert.EndsWith("INDEX 01 00:00:00\n", result);
    }

    [Fact] public void OversizedMalformedAndIrreversibleDocumentsAreRejected()
    {
        Assert.Throws<InvalidDataException>(() => TextRepair.Cue(new byte[4 * 1024 * 1024 + 1], "album.cue", 1251));
        Assert.Throws<InvalidDataException>(() => TextRepair.Cue(Encoding.UTF8.GetBytes("TITLE \"�\""), "album.cue", 1251));
        using var malformed = new MemoryStream(new byte[] { 73, 68, 51, 4, 0, 0, 127, 127, 127, 127 });
        malformed.Position = 3; Assert.Throws<InvalidDataException>(() => TagEditor.Read(malformed, "bad.mp3")); Assert.Equal(3, malformed.Position);
    }

    [Fact] public void GuardStreamOverloadPreservesPositionAndOwnership()
    {
        using var stream = File.OpenRead(Fixture("flac16.flac")); stream.Position = 7;
        MetadataReadGuard.Validate(stream, "fixture.flac"); Assert.Equal(7, stream.Position); Assert.True(stream.CanRead);
    }

    [Fact] public void PreviewIsReadOnlyRecursiveAndSkipsUnknownFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "cli-preview-" + Guid.NewGuid()); Directory.CreateDirectory(root);
        try
        {
            var album = Directory.CreateDirectory(Path.Combine(root, "album")).FullName;
            var cue = Path.Combine(album, "legacy.cue"); File.WriteAllBytes(cue, Encoding.GetEncoding(1251).GetBytes("TITLE \"Людзі\"\nFILE \"owned.flac\" WAVE\nTRACK 01 AUDIO\nINDEX 01 00:00:00\n"));
            var original = File.ReadAllBytes(cue); File.WriteAllText(Path.Combine(root, "private.txt"), "private data");
            using var output = new StringWriter(); using var errors = new StringWriter();
            Assert.Equal(0, Program.Run([root], output, errors)); Assert.Contains("planned=1", output.ToString());
            Assert.Equal(original, File.ReadAllBytes(cue)); Assert.Equal(2, Directory.GetFiles(root, "*", SearchOption.AllDirectories).Length);
            Assert.Empty(errors.ToString());
        }
        finally { Directory.Delete(root, true); }
    }
    [Fact] public void InvalidArgumentsCannotStartProcessing()
    {
        using var output = new StringWriter(); using var errors = new StringWriter();
        Assert.Equal(2, Program.Run(["missing", "--cue-codepage", "9999"], output, errors)); Assert.Empty(output.ToString());
    }

    [Fact] public async Task FreshCliProcessDecodesCueBeforeAnyAudioTagsInitializeCodePages()
    {
        var root = Path.Combine(Path.GetTempPath(), "cli-cue-first-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "Беларускае.cue");
            var original = Encoding.GetEncoding(1251).GetBytes("TITLE \"Людзі\"\nFILE \"owned.flac\" WAVE\nTRACK 01 AUDIO\nINDEX 01 00:00:00\n");
            File.WriteAllBytes(path, original);
            var start = new System.Diagnostics.ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
            { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "MPswift.TagRepair.dll"));
            start.ArgumentList.Add(root);
            using var child = System.Diagnostics.Process.Start(start)!;
            try
            {
                var output = child.StandardOutput.ReadToEndAsync(); var errors = child.StandardError.ReadToEndAsync();
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                await child.WaitForExitAsync(timeout.Token);
                Assert.Equal(0, child.ExitCode); Assert.Empty(await errors);
                Assert.Contains("planned=1; unchanged=0; errors=0", await output);
                Assert.Equal(original, File.ReadAllBytes(path)); Assert.Single(Directory.GetFiles(root));
            }
            finally { if (!child.HasExited) { child.Kill(true); child.WaitForExit(5000); } }
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData(".cue")]
    [InlineData(".mp3")]
    [InlineData(".flac")]
    public async Task FreshCliReportsInvalidDataAndContinuesToOtherFiles(string extension)
    {
        var root = Path.Combine(Path.GetTempPath(), "cli-invalid-owned-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var broken = Path.Combine(root, "broken" + extension);
            var bytes = extension switch
            {
                ".cue" => Encoding.UTF8.GetBytes("TITLE \"missing structure\"\n"),
                ".mp3" => new byte[] { 73, 68, 51, 4, 0, 0, 127, 127, 127, 127 },
                _ => new byte[] { 102, 76, 97, 67, 128, 0, 1, 0 }
            };
            File.WriteAllBytes(broken, bytes);
            var album = Directory.CreateDirectory(Path.Combine(root, "next-album")).FullName;
            var cue = Path.Combine(album, "valid.cue");
            var original = Encoding.GetEncoding(1251).GetBytes("TITLE \"Людзі\"\nFILE \"owned.flac\" WAVE\nTRACK 01 AUDIO\nINDEX 01 00:00:00\n");
            File.WriteAllBytes(cue, original);
            var start = new System.Diagnostics.ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
            { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "MPswift.TagRepair.dll")); start.ArgumentList.Add(root);
            using var child = System.Diagnostics.Process.Start(start)!;
            try
            {
                var output = child.StandardOutput.ReadToEndAsync(); var errors = child.StandardError.ReadToEndAsync();
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30)); await child.WaitForExitAsync(timeout.Token);
                Assert.Equal(1, child.ExitCode); var message = await errors;
                Assert.Contains("broken" + extension, message); Assert.DoesNotContain("Unhandled exception", message);
                Assert.Contains("planned=1; unchanged=0; errors=1", await output);
                Assert.Equal(bytes, File.ReadAllBytes(broken)); Assert.Equal(original, File.ReadAllBytes(cue));
                Assert.Empty(Directory.GetFiles(root, "*.bak", SearchOption.AllDirectories));
            }
            finally { if (!child.HasExited) { child.Kill(true); child.WaitForExit(5000); } }
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact] public void AudioOrCodecHeaderMutationIsDetected()
    {
        var bytes = File.ReadAllBytes(Fixture("flac16.flac")); using var stream = new MemoryStream(bytes);
        var original = AudioFingerprint.Read(stream, ".flac"); bytes[^20] ^= 1; Assert.NotEqual(original, AudioFingerprint.Read(stream, ".flac")); bytes[^20] ^= 1;
        var offset = Array.IndexOf(bytes, (byte)'f'); Assert.True(offset >= 0); bytes[offset + 10] ^= 1;
        Assert.NotEqual(original, AudioFingerprint.Read(stream, ".flac"));
    }

    [Fact] public void Id3v1OnlyFileMigratesLegacyTextAndNumericTagsToUtf8()
    {
        using var source = TagLib.File.Create(Fixture("mp3-cbr.mp3"));
        Assert.True(source.InvariantStartPosition >= 0 && source.InvariantEndPosition > source.InvariantStartPosition);
        var bytes = File.ReadAllBytes(Fixture("mp3-cbr.mp3"));
        using var stream = new MemoryStream(); stream.Write(bytes.AsSpan((int)source.InvariantStartPosition, (int)(source.InvariantEndPosition - source.InvariantStartPosition)));
        var legacy = new TagLib.Id3v1.Tag { Title = Broken("Людзі"), Performers = [Broken("Індыга")], Album = Broken("Цэпэліны"), Year = 2004, Track = 8 };
        stream.Write(legacy.Render().Data);
        var audio = AudioFingerprint.Read(stream, ".mp3"); var preview = TagEditor.Read(stream, "legacy.mp3");
        TagEditor.Rewrite(stream, "legacy.mp3", preview);
        Assert.Equal(audio, AudioFingerprint.Read(stream, ".mp3")); Assert.Contains("Людзі", TagEditor.Read(stream, "legacy.mp3").Before["Title"]);
        Assert.False(TagEditor.NeedsUnicodeRewrite(stream, "legacy.mp3"));
    }
}
