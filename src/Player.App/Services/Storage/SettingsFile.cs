using System.IO;
using System.Text.Json;
using Player.Core.Library;

namespace Player.App.Services.Storage;

public sealed class SettingsFile(string directory)
{
    private readonly string _path = Path.Combine(directory, "settings.json");
    public PlayerSettings Load()
    {
        if (!File.Exists(_path))
        {
            if (File.Exists(_path + ".bak")) throw new InvalidDataException("Settings are missing but a previous backup exists; choose recovery explicitly.");
            return new();
        }
        return Read(_path);
    }
    private static PlayerSettings Read(string path)
    {
        if (new FileInfo(path).Length > 65536) throw new InvalidDataException("Settings file is too large; original preserved.");
        var value = JsonSerializer.Deserialize<PlayerSettings>(File.ReadAllText(path)) ?? throw new InvalidDataException("Invalid settings; original preserved.");
        if (value.SchemaVersion != 1) throw new SettingsCompatibilityException(value.SchemaVersion);
        return value.Validate();
    }
    public PlayerSettings LoadBackup() => Read(_path + ".bak");
    /// <summary>No implicit fallback: only a valid supported backup and explicit user choice permit recovery.</summary>
    public PlayerSettings LoadWithRecovery(Func<Exception, bool> chooseRecovery)
    {
        try { return Load(); }
        catch (Exception error) when (error is JsonException or InvalidDataException)
        {
            try { LoadBackup(); }
            catch (Exception backupError) when (backupError is IOException or UnauthorizedAccessException or JsonException)
            { throw new IOException("Settings and their previous backup are unavailable; originals preserved.", new AggregateException(error, backupError)); }
            if (!chooseRecovery(error)) throw;
            RestoreBackup();
            return Load();
        }
    }
    public string? RestoreBackup()
    {
        var settings = LoadBackup();
        var temporary = _path + ".restore-" + Guid.NewGuid().ToString("N");
        var preserved = _path + ".preserved-" + Guid.NewGuid().ToString("N");
        try
        {
            Export(temporary, settings);
            if (File.Exists(_path)) { File.Replace(temporary, _path, preserved); return preserved; }
            File.Move(temporary, _path);
            return null; // No original existed to preserve.
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public static void Export(string path, PlayerSettings settings)
    {
        settings = settings.Validate();
        using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        JsonSerializer.Serialize(file, settings); file.Flush(true);
    }
    public void Save(PlayerSettings settings)
    {
        settings = settings.Validate();
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { JsonSerializer.Serialize(file, settings); file.Flush(true); }
            if (File.Exists(_path)) File.Replace(temporary, _path, _path + ".bak");
            else File.Move(temporary, _path);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}

public sealed class SettingsCompatibilityException(int version) : IOException($"Settings schema {version} is unsupported. Use a compatible application; original settings preserved.");
