using System.Text.Json;
using Player.App.Services.Storage;
using Player.Core.Library;

namespace Player.Core.Tests;

public sealed class SettingsRecoveryTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "settings-recovery-" + Guid.NewGuid().ToString("N"));
    private string Main => Path.Combine(_directory, "settings.json");
    private string Backup => Main + ".bak";
    private SettingsFile Seed()
    {
        var file = new SettingsFile(_directory);
        file.Save(new PlayerSettings(Volume: 23, Muted: true, Language: "ru"));
        file.Save(new PlayerSettings(Volume: 45));
        return file;
    }
    [Fact]
    public void ExplicitRecoveryRestoresValidatedBackupAndPreservesDamagedOriginal()
    {
        var file = Seed(); var backup = File.ReadAllBytes(Backup); File.WriteAllText(Main, "{interrupted");
        var chosen = false;
        var value = file.LoadWithRecovery(error => { Assert.IsAssignableFrom<JsonException>(error); chosen = true; return true; });
        Assert.True(chosen); Assert.Equal(23, value.Volume); Assert.True(value.Muted); Assert.Equal("ru", value.Language);
        Assert.Equal("{interrupted", File.ReadAllText(Directory.GetFiles(_directory, "settings.json.preserved-*").Single()));
        Assert.Equal(backup, File.ReadAllBytes(Backup)); Assert.Empty(Directory.GetFiles(_directory, "*.restore-*"));
    }
    [Fact]
    public void DecliningRecoveryChangesNeitherCopy()
    {
        var file = Seed(); File.WriteAllText(Main, "broken"); var backup = File.ReadAllBytes(Backup);
        Assert.ThrowsAny<JsonException>(() => file.LoadWithRecovery(_ => false));
        Assert.Equal("broken", File.ReadAllText(Main)); Assert.Equal(backup, File.ReadAllBytes(Backup));
        Assert.Empty(Directory.GetFiles(_directory, "*.preserved-*"));
    }
    [Fact]
    public void InvalidBackupIsNeverOfferedOrReplacesTheOriginal()
    {
        var file = Seed(); File.WriteAllText(Main, "broken-main"); File.WriteAllText(Backup, "broken-backup");
        Assert.Throws<IOException>(() => file.LoadWithRecovery(_ => throw new Exception("Invalid backup offered")));
        Assert.Equal("broken-main", File.ReadAllText(Main)); Assert.Equal("broken-backup", File.ReadAllText(Backup));
    }
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void UnsupportedSchemaCannotSilentlyDowngradeToPreviousSettings(int version)
    {
        var file = Seed(); var original = "{\"SchemaVersion\":" + version + ",\"Volume\":71}"; File.WriteAllText(Main, original);
        Assert.Throws<SettingsCompatibilityException>(() => file.LoadWithRecovery(_ => throw new Exception("Schema downgrade offered")));
        Assert.Equal(original, File.ReadAllText(Main));
    }
    [Fact]
    public void MissingMainWithExistingBackupRequiresExplicitRecovery()
    {
        var file = Seed(); File.Delete(Main); Assert.Throws<InvalidDataException>(file.Load);
        Assert.Equal(23, file.LoadWithRecovery(_ => true).Volume);
        Assert.Empty(Directory.GetFiles(_directory, "*.preserved-*"));
    }
    [Fact]
    public void ExportUsesValidatedValuesWithoutOverwritingExistingFiles()
    {
        Directory.CreateDirectory(_directory); var export = Path.Combine(_directory, "export.json");
        SettingsFile.Export(export, new PlayerSettings(Volume: 200));
        Assert.Equal(100, JsonSerializer.Deserialize<PlayerSettings>(File.ReadAllText(export))!.Volume);
        Assert.Throws<IOException>(() => SettingsFile.Export(export, new PlayerSettings(Volume: 10)));
    }
    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
}
