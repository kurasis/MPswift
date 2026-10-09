using System.Text.Json;
using System.Text;
using Player.App.Services.Storage;
using Player.Core.Library;

namespace Player.Core.Tests;

public sealed class SettingsRecoveryTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "settings-recovery-" + Guid.NewGuid().ToString("N"));
    private string Main => Path.Combine(_directory, "settings.json");
    private string Backup => Main + ".bak";
    [Fact] public void FavoritesAndPreviousPlaylistIdentifiersRoundTripWithoutChangingSchema()
    {
        var file = Seed(); var favorites = Guid.NewGuid(); var previous = Guid.NewGuid();
        file.Save(new PlayerSettings { FavoritesPlaylistId = favorites, PreviousPlaylistId = previous });
        var loaded = file.Load();
        Assert.Equal(favorites, loaded.FavoritesPlaylistId); Assert.Equal(previous, loaded.PreviousPlaylistId); Assert.Equal(1, loaded.SchemaVersion);
    }
    [Fact] public void LegacySettingsAndEmptyPlaylistIdentifiersRetainCompatibleDefaults()
    {
        var file = Seed(); File.WriteAllText(Main, "{\"SchemaVersion\":1,\"Volume\":23}");
        var loaded = file.Load(); Assert.Null(loaded.FavoritesPlaylistId); Assert.Null(loaded.PreviousPlaylistId); Assert.Equal(23, loaded.Volume);
        var normalized = new PlayerSettings { FavoritesPlaylistId = Guid.Empty, PreviousPlaylistId = Guid.Empty }.Validate();
        Assert.Null(normalized.FavoritesPlaylistId); Assert.Null(normalized.PreviousPlaylistId);
    }
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
    [Fact]
    public void SemanticallyInvalidBackupRetainsBothFailureCausesAndOriginals()
    {
        var file = Seed(); File.WriteAllText(Main, "broken-main");
        File.WriteAllText(Backup, "{\"Processing\":{\"Bands\":[0]}}");
        var main = File.ReadAllBytes(Main); var backup = File.ReadAllBytes(Backup);
        var error = Assert.Throws<IOException>(() => file.LoadWithRecovery(_ => throw new Exception("Invalid backup offered")));
        var causes = Assert.IsType<AggregateException>(error.InnerException).InnerExceptions;
        Assert.IsAssignableFrom<JsonException>(causes[0]); Assert.IsType<InvalidDataException>(causes[1]);
        Assert.Equal(main, File.ReadAllBytes(Main)); Assert.Equal(backup, File.ReadAllBytes(Backup));
        Assert.Empty(Directory.GetFiles(_directory, "*.preserved-*"));
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
    [Theory]
    [InlineData("utf8")]
    [InlineData("utf16le")]
    [InlineData("utf16be")]
    [InlineData("utf32le")]
    [InlineData("utf32be")]
    public void BoundedReaderRetainsBomEncodingAndBackupCompatibility(string name)
    {
        Encoding encoding = name switch
        {
            "utf8" => new UTF8Encoding(true), "utf16le" => new UnicodeEncoding(false, true),
            "utf16be" => new UnicodeEncoding(true, true), "utf32le" => new UTF32Encoding(false, true),
            _ => new UTF32Encoding(true, true)
        };
        var file = Seed();
        File.WriteAllText(Main, "{\"SchemaVersion\":1,\"Volume\":23,\"Language\":\"ru\",\"Note\":\"Музыка 🎵\"}", encoding);
        var original = File.ReadAllBytes(Main); Assert.Equal(23, file.Load().Volume); Assert.Equal("ru", file.Load().Language);
        file.Save(new PlayerSettings(Volume: 61));
        Assert.Equal(original, File.ReadAllBytes(Backup)); Assert.Equal(23, file.LoadBackup().Volume); Assert.Equal(61, file.Load().Volume);
    }
    [Theory]
    [InlineData(65536)]
    [InlineData(65537)]
    public void SettingsByteLimitIsInclusiveAndRejectedFilesArePreserved(int size)
    {
        var file = Seed(); var bytes = Enumerable.Repeat((byte)' ', size).ToArray();
        Encoding.UTF8.GetBytes("{\"Volume\":23}").CopyTo(bytes, 0); File.WriteAllBytes(Main, bytes); var backup = File.ReadAllBytes(Backup);
        if (size == 65536) Assert.Equal(23, file.Load().Volume);
        else Assert.Throws<InvalidDataException>(file.Load);
        Assert.Equal(bytes, File.ReadAllBytes(Main)); Assert.Equal(backup, File.ReadAllBytes(Backup));
    }
    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
}
