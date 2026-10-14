using Player.App.Services.Storage;
using Player.Core.Library;
using Player.Core.Playback;

namespace Player.Core.Tests;

public sealed class UnchangedSettingsTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "mpswift-unchanged-" + Guid.NewGuid().ToString("N"));
    public UnchangedSettingsTests() => Directory.CreateDirectory(_directory);
    [Fact]
    public void IdenticalContentPreservesNamesBytesTimestampIdentityAndPreviousBackup()
    {
        var path = Path.Combine(_directory, "settings.json"); var file = new SettingsFile(_directory);
        file.Save(new(Volume: 20)); var settings = new PlayerSettings(Volume: 40, Processing: new(Bands: new double[10])); file.Save(settings);
        var names = Directory.GetFiles(_directory).Order().ToArray(); var current = File.ReadAllBytes(path); var backup = File.ReadAllBytes(path + ".bak");
        var time = File.GetLastWriteTimeUtc(path); using var lease = DataFileLease.OpenExisting(path); var identity = lease!.Identity;
        for (var i = 0; i < 100; i++) file.Save(settings with { Processing = new(Bands: new double[10]) });
        Assert.Equal(2, file.Writes); Assert.Equal(100, file.UnchangedSaves);
        Assert.Equal(names, Directory.GetFiles(_directory).Order()); Assert.Equal(current, File.ReadAllBytes(path)); Assert.Equal(backup, File.ReadAllBytes(path + ".bak"));
        Assert.Equal(time, File.GetLastWriteTimeUtc(path)); using var after = DataFileLease.OpenExisting(path); Assert.Equal(identity, after!.Identity);
    }
    [Fact]
    public void InPlaceArrayChangesAndExternalFileReplacementAreDetected()
    {
        var settings = new PlayerSettings(Processing: new(Bands: new double[10])); var file = new SettingsFile(_directory); var path = Path.Combine(_directory, "settings.json");
        file.Save(settings); var original = File.ReadAllBytes(path);
        settings.Processing!.Bands![0] = 6; file.Save(settings); Assert.Equal(6, file.Load().Processing!.Bands![0]); Assert.Equal(original, File.ReadAllBytes(path + ".bak"));
        File.WriteAllText(path, "external-owned-change"); file.Save(settings);
        Assert.Equal(3, file.Writes); Assert.Equal("external-owned-change", File.ReadAllText(path + ".bak"));
        Assert.Equal(6, file.Load().Processing!.Bands![0]);
        File.Delete(path); file.Save(settings); Assert.Equal(4, file.Writes); Assert.Equal(6, file.Load().Processing!.Bands![0]);
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void SameContentDoesNotBypassLinkedCurrentOrBackupChecks(bool backup)
    {
        var file = new SettingsFile(_directory); var path = Path.Combine(_directory, "settings.json"); var settings = new PlayerSettings(); file.Save(settings);
        var outside = Path.Combine(_directory, "untouched.json"); File.WriteAllBytes(outside, File.ReadAllBytes(path));
        var target = backup ? path + ".bak" : path; File.Delete(target); File.CreateSymbolicLink(target, outside);
        var bytes = File.ReadAllBytes(outside); Assert.Throws<IOException>(() => file.Save(settings)); Assert.Equal(bytes, File.ReadAllBytes(outside));
    }
    public void Dispose() => Directory.Delete(_directory, true);
}
