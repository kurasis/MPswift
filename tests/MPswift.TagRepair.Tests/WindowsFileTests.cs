using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using MPswift.TagRepair;

namespace MPswift.TagRepair.Tests;

public sealed class WindowsFactAttribute : FactAttribute
{
    public WindowsFactAttribute() { if (!OperatingSystem.IsWindows()) Skip = "Requires genuine Windows handle/link/write semantics."; }
}

public sealed class WindowsFileTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "mpswift-repair-owned-" + Guid.NewGuid().ToString("N"));
    public WindowsFileTests() { Directory.CreateDirectory(root); Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); }
    public void Dispose() => Directory.Delete(root, true);
    private string Cue()
    { var path = Path.Combine(root, "Беларускае.cue"); File.WriteAllBytes(path, Encoding.GetEncoding(1251).GetBytes("TITLE \"Людзі\"\r\nFILE \"owned.flac\" WAVE\r\nTRACK 01 AUDIO\r\nINDEX 01 02:40:03\r\n")); return path; }

    [WindowsFact] public void CueApplyCreatesExactBackupAndIsIdempotent()
    {
        var path = Cue(); var original = File.ReadAllBytes(path);
        var result = FileRepair.Process(root, path, true, 1251);
        Assert.Equal("repaired", result.Status); Assert.Equal(original, File.ReadAllBytes(result.Backup!));
        Assert.Contains("Людзі", new UTF8Encoding(false, true).GetString(File.ReadAllBytes(path)));
        Assert.Equal("unchanged", FileRepair.Process(root, path, true, 1251).Status);
        Assert.Single(Directory.GetFiles(root, "*.bak")); Assert.Empty(Directory.GetFiles(root, "*.tmp"));
    }

    [WindowsFact] public void Mp3ApplyRepairsTagsAndPreservesActualEncodedAudio()
    {
        var path = Path.Combine(root, "Новыя.mp3"); File.Copy(RepairTests.Fixture("mp3-cbr.mp3"), path);
        using (var file = TagLib.File.Create(path))
        { file.Tag.Title = Encoding.Latin1.GetString(Encoding.GetEncoding(1251).GetBytes("Людзі і сонца")); file.Tag.Year = 2004; file.Tag.Track = 8; file.Save(); }
        var original = File.ReadAllBytes(path); AudioFingerprint fingerprint;
        using (var source = File.OpenRead(path)) fingerprint = AudioFingerprint.Read(source, ".mp3");
        var result = FileRepair.Process(root, path, true, 1251);
        Assert.Equal("repaired", result.Status); Assert.Equal(original, File.ReadAllBytes(result.Backup!));
        using var verified = File.OpenRead(path); Assert.Equal(fingerprint, AudioFingerprint.Read(verified, ".mp3"));
        using var read = TagLib.File.Create(path); Assert.Equal("Людзі і сонца", read.Tag.Title); Assert.Equal(2004u, read.Tag.Year); Assert.Equal(8u, read.Tag.Track);
    }

    [WindowsFact] public void FilenameConfirmedAsciiITitlesApplyWithExactBackupsAndIdempotence()
    {
        foreach (var title in new[] { "Чарнобыльскi фон", "Гомельскi вальс", "Чарнобыльскi шлях" })
            foreach (var fixture in new[] { "mp3-cbr.mp3", "flac16.flac" })
            {
                var extension = Path.GetExtension(fixture); var path = Path.Combine(root, title + extension);
                File.Copy(RepairTests.Fixture(fixture), path);
                using (var file = TagLib.File.Create(path)) { file.Tag.Title = Encoding.Latin1.GetString(Encoding.GetEncoding(1251).GetBytes(title)); file.Save(); }
                var original = File.ReadAllBytes(path); AudioFingerprint fingerprint;
                using (var stream = File.OpenRead(path)) fingerprint = AudioFingerprint.Read(stream, extension);
                var result = FileRepair.Process(root, path, true, 1251);
                Assert.Equal("repaired", result.Status); Assert.Equal(original, File.ReadAllBytes(result.Backup!));
                using (var stream = File.OpenRead(path)) Assert.Equal(fingerprint, AudioFingerprint.Read(stream, extension));
                using (var file = TagLib.File.Create(path)) Assert.Equal(title, file.Tag.Title);
                Assert.Equal("unchanged", FileRepair.Process(root, path, true, 1251).Status);
            }
        Assert.Equal(6, Directory.GetFiles(root, "*.bak").Length); Assert.Empty(Directory.GetFiles(root, "*.tmp"));
    }

    [WindowsFact] public void PartialCommitFailureRestoresOriginalAndKeepsVerifiedBackup()
    {
        var path = Cue(); var original = File.ReadAllBytes(path);
        var error = Assert.Throws<IOException>(() => FileRepair.Process(root, path, true, 1251, commit: (_, target) =>
        { target.Write("partial bad bytes"u8); throw new IOException("Injected mid-write failure."); }));
        Assert.Contains("original restored", error.Message); Assert.Equal(original, File.ReadAllBytes(path));
        Assert.Equal(original, File.ReadAllBytes(Assert.Single(Directory.GetFiles(root, "*.bak")))); Assert.Empty(Directory.GetFiles(root, "*.tmp"));
    }

    [WindowsFact] public void BusySourceIsRejectedBeforeWritesOrBackups()
    {
        var path = Cue(); var original = File.ReadAllBytes(path);
        using var locked = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        Assert.Throws<IOException>(() => FileRepair.Process(root, path, true, 1251));
        Assert.Equal(original, File.ReadAllBytes(path)); Assert.Empty(Directory.GetFiles(root, "*.bak")); Assert.Empty(Directory.GetFiles(root, "*.tmp"));
    }

    [WindowsFact] public void HardlinkIsRejectedAndOtherNameRemainsUnchanged()
    {
        var path = Cue(); var original = File.ReadAllBytes(path); var linked = Path.Combine(root, "alias.cue");
        Assert.True(CreateHardLink(linked, path, 0));
        Assert.Throws<IOException>(() => FileRepair.Process(root, linked, true, 1251));
        Assert.Equal(original, File.ReadAllBytes(path)); Assert.Empty(Directory.GetFiles(root, "*.bak"));
    }

    [WindowsFact] public void ResolvedOutsideDirectoryCannotBeProcessed()
    {
        var path = Cue(); var original = File.ReadAllBytes(path); var selected = Directory.CreateDirectory(Path.Combine(root, "selected")).FullName;
        Assert.Throws<IOException>(() => FileRepair.Process(selected, path, true, 1251)); Assert.Equal(original, File.ReadAllBytes(path));
    }

    [WindowsFact] public void CancellationBeforeCommitPreservesSource()
    {
        var path = Cue(); var original = File.ReadAllBytes(path);
        Assert.Throws<OperationCanceledException>(() => FileRepair.Process(root, path, true, 1251, new CancellationToken(true)));
        Assert.Equal(original, File.ReadAllBytes(path)); Assert.Empty(Directory.GetFiles(root, "*.bak"));
    }

    [WindowsFact] public void RealCliRecursiveApplyRetainsUnrelatedFilesAndSiblingDirectories()
    {
        var cue = Cue(); var original = File.ReadAllBytes(cue); File.WriteAllText(Path.Combine(root, "private.txt"), "untouched");
        using var output = new StringWriter(); using var errors = new StringWriter();
        Assert.Equal(0, Program.Run([root, "--apply"], output, errors)); Assert.Contains("repaired=1", output.ToString()); Assert.Empty(errors.ToString());
        Assert.Equal("untouched", File.ReadAllText(Path.Combine(root, "private.txt"))); Assert.Equal(original, File.ReadAllBytes(Assert.Single(Directory.GetFiles(root, "*.bak"))));
    }

    [WindowsFact] public void PaddedCbrAndVbrMp3ApplyPreservesPrefixAndAudioWithExactBackups()
    {
        foreach (var name in new[] { "mp3-cbr.mp3", "mp3-vbr.mp3" })
        {
            var path = Path.Combine(root, name); File.WriteAllBytes(path, RepairTests.LegacyGapMp3(name, 257));
            using (var file = TagLib.File.Create(path))
            { file.Tag.Title = Encoding.Latin1.GetString(Encoding.GetEncoding(1251).GetBytes("Людзі і сонца")); file.Save(); }
            var original = File.ReadAllBytes(path); string hash; AudioFingerprint fingerprint;
            using (var file = TagLib.File.Create(path))
            {
                var range = original.AsSpan(checked((int)file.InvariantStartPosition), checked((int)(file.InvariantEndPosition - file.InvariantStartPosition)));
                hash = Convert.ToHexStringLower(SHA256.HashData(range));
                Assert.True(range.StartsWith("Legacy padding"u8));
            }
            using (var source = File.OpenRead(path)) fingerprint = AudioFingerprint.Read(source, ".mp3");
            Assert.Equal(hash, fingerprint.Sha256);
            var result = FileRepair.Process(root, path, true, 1251);
            Assert.Equal("repaired", result.Status); Assert.Equal(original, File.ReadAllBytes(result.Backup!));
            using (var source = File.OpenRead(path)) Assert.Equal(fingerprint, AudioFingerprint.Read(source, ".mp3"));
            using (var file = TagLib.File.Create(path)) Assert.Equal("Людзі і сонца", file.Tag.Title);
            Assert.Equal("unchanged", FileRepair.Process(root, path, true, 1251).Status);
        }
        Assert.Equal(2, Directory.GetFiles(root, "*.bak").Length); Assert.Empty(Directory.GetFiles(root, "*.tmp"));
    }

    [WindowsFact] public void ApplyRejectsInvalidDocumentAndContinuesWithoutChangingIt()
    {
        var path = Path.Combine(root, "broken.cue"); var original = Encoding.UTF8.GetBytes("TITLE \"no tracks\"\n"); File.WriteAllBytes(path, original);
        var valid = Cue(); var validOriginal = File.ReadAllBytes(valid);
        var next = Directory.CreateDirectory(Path.Combine(root, "next-album")).FullName;
        File.Move(valid, Path.Combine(next, Path.GetFileName(valid)));
        using var output = new StringWriter(); using var errors = new StringWriter();
        Assert.Equal(1, Program.Run([root, "--apply"], output, errors));
        Assert.Contains("broken.cue", errors.ToString()); Assert.Contains("repaired=1; unchanged=0; errors=1", output.ToString());
        Assert.Equal(original, File.ReadAllBytes(path));
        Assert.Equal(validOriginal, File.ReadAllBytes(Assert.Single(Directory.GetFiles(root, "*.bak", SearchOption.AllDirectories))));
        Assert.Empty(Directory.GetFiles(root, "*.tmp"));
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("kernel32.dll", EntryPoint = "CreateHardLinkW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CreateHardLink(string link, string target, nint security);
}
