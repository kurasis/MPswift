using System.Text;
using System.Text.Json;
using MPswift.TagRepair;

namespace MPswift.TagRepair.Tests;

public sealed class CollectionRepairTests
{
    public static TheoryData<string, string, string, string> OwnerExamples => new()
    {
        { "Title", "Выпрауляла мацi сына", "Выпрауляла ìàöi сына", "Albums/Запаветы/02.mp3" },
        { "Album", "Пашпарт грамадзянiна N.R.M.", "Пашпарт ãðàìàäçÿíiíà N.R.M.", "Albums/PASSPART грамадзянiна N.R.M/25.mp3" },
        { "Album", "Дзецi леса", "Äçåöi леса", "Albums/Дзецi леса/01-Шлях.mp3" },
        { "Album", "Дзецi леса", "Äçåöi леса", "Albums/Collection/10-Сонца у змроку.mp3" },
        { "Title", "Вясна iдзе", "Вясна iäçå", "Albums/Дзецi леса/04-Вясна iдзе.mp3" },
        { "Title", "derKillem - Сэкс з прэзiдэнтам", "derKillem - Сэкс ç прэзiдэнтам", "Albums/Tribute/11 - derKillem - Сэкс з прэзiдэнтам.mp3" },
        { "Title", "Денис Черноморский - Забi мяне мент", "Денис Черноморский - Çàái мяне мент", "Albums/Tribute/13 - Денис Черноморский - Забi мяне мент .mp3" }
    };

    [Theory]
    [InlineData("ìàöi", "Albums/Запаветы/02.mp3", false)]
    [InlineData("Вецеры ìàöi", "Albums/Запаветы/02.mp3", false)]
    [InlineData("Вецеры вецеры ìàöi", "Albums/Запаветы/02.mp3", false)]
    [InlineData("Вецеры сонца éèi", "Albums/Запаветы/02.mp3", false)]
    [InlineData("derKillem ç", "Albums/Artist з/derKillem з.mp3", false)]
    [InlineData("derKillem - Сэкс ç", "Albums/з/other.mp3", false)]
    [InlineData("Äçåöi", "Albums/Дзецi леса/01.mp3", false)]
    [InlineData("Äçåöi", "Albums/Дзецi леса/Other/01.mp3", true)]
    [InlineData("Сонца вецеры Björki", "Albums/Запаветы/02.mp3", false)]
    [InlineData("Сонца вецеры Caféi", "Albums/Запаветы/02.mp3", false)]
    [InlineData("Сонца вецеры Broken �", "Albums/Запаветы/02.mp3", false)]
    [InlineData("Вецеры сонца ìàöj i", "Albums/Запаветы/02.mp3", false)]
    public void ContextualRecoveryPreservesAmbiguousLatinAndUncorroboratedWords(string text, string relative, bool album)
    { Assert.Equal(text, TextRepair.Recover(text, relative, album)); }

    [Fact] public void PartialCueTitleRecoveryPreservesFileReferenceAndIndexes()
    {
        const string text = "FILE \"ìàöi.flac\" WAVE\r\nTRACK 01 AUDIO\r\nTITLE \"Выпрауляла ìàöi сына\"\r\nINDEX 01 02:40:03\r\n";
        var result = TextRepair.Cue(Encoding.UTF8.GetBytes(text), "owned.cue", 1251);
        Assert.Equal(text.Replace("TITLE \"Выпрауляла ìàöi сына\"", "TITLE \"Выпрауляла мацi сына\"", StringComparison.Ordinal), Encoding.UTF8.GetString(result));
        Assert.Equal(result, TextRepair.Cue(result, "owned.cue", 1251));
    }

    [Theory]
    [MemberData(nameof(OwnerExamples))]
    public void AllOwnerCasesRewriteRealMp3AndFlacWithUnchangedAudio(string field, string expected, string partial, string relative)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var root = Path.Combine(Path.GetTempPath(), "cli-collection-owned-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            foreach (var fixture in new[] { "mp3-cbr.mp3", "flac16.flac" })
                foreach (var alreadyPartiallyRepaired in new[] { false, true })
                {
                    var extension = Path.GetExtension(fixture);
                    var path = Path.Combine(root, alreadyPartiallyRepaired ? "partial" : "original", Path.ChangeExtension(relative, extension));
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.Copy(RepairTests.Fixture(fixture), path);
                    using (var file = TagLib.File.Create(path))
                    {
                        var broken = alreadyPartiallyRepaired ? partial : Encoding.Latin1.GetString(Encoding.GetEncoding(1251).GetBytes(expected));
                        if (field == "Album") file.Tag.Album = broken; else file.Tag.Title = broken;
                        file.Save();
                    }
                    var original = File.ReadAllBytes(path);
                    using var stream = new MemoryStream(); stream.Write(original);
                    var fingerprint = AudioFingerprint.Read(stream, extension); var edit = TagEditor.Read(stream, path);
                    Assert.Equal(expected, JsonSerializer.Deserialize<string>(edit.After[field]));
                    TagEditor.Rewrite(stream, path, edit);
                    Assert.Equal(fingerprint, AudioFingerprint.Read(stream, extension));
                    var saved = TagEditor.Read(stream, path);
                    Assert.Equal(expected, JsonSerializer.Deserialize<string>(saved.Before[field])); Assert.Empty(saved.Changes);
                    Assert.Equal(original, File.ReadAllBytes(path));
                }
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact] public void EscapedWindowsContextRetainsImmediateAlbumFolderEvidence()
    {
        const string context = @"C:\\Music\\Albums\\Дзецi леса\\01.mp3";
        Assert.Equal("Дзецi леса", TextRepair.Recover("Äçåöi леса", context, album: true));
        Assert.Equal("Äçåöi", TextRepair.Recover("Äçåöi", context));
    }
}
