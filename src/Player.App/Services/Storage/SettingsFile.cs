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
        using var lease = DataDirectoryLease.Open(Path.GetDirectoryName(_path)!);
        return Read(Path.Combine(lease.DirectoryPath, "settings.json"));
    }
    private static PlayerSettings Read(string path)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var length = file.Length;
        if (length > 65536) throw new InvalidDataException("Settings file is too large; original preserved.");
        var bytes = new byte[checked((int)length)]; file.ReadExactly(bytes);
        if (file.ReadByte() >= 0) throw new InvalidDataException("Settings changed while reading; original preserved.");
        // Keep File.ReadAllText's BOM detection, including existing UTF-16/UTF-32 files.
        using var reader = new StreamReader(new MemoryStream(bytes, false));
        var value = JsonSerializer.Deserialize<PlayerSettings>(reader.ReadToEnd()) ?? throw new InvalidDataException("Invalid settings; original preserved.");
        if (value.SchemaVersion != 1) throw new SettingsCompatibilityException(value.SchemaVersion);
        return value.Validate();
    }
    public PlayerSettings LoadBackup()
    {
        using var lease = DataDirectoryLease.Open(Path.GetDirectoryName(_path)!);
        return Read(Path.Combine(lease.DirectoryPath, "settings.json.bak"));
    }
    /// <summary>No implicit fallback: only a valid supported backup and explicit user choice permit recovery.</summary>
    public PlayerSettings LoadWithRecovery(Func<Exception, bool> chooseRecovery)
    {
        try { return Load(); }
        catch (Exception error) when (error is JsonException or InvalidDataException)
        {
            try { LoadBackup(); }
            catch (Exception backupError) when (backupError is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
            { throw new IOException("Settings and their previous backup are unavailable; originals preserved.", new AggregateException(error, backupError)); }
            if (!chooseRecovery(error)) throw;
            RestoreBackup();
            return Load();
        }
    }
    public string? RestoreBackup()
    {
        using var lease = DataDirectoryLease.Open(Path.GetDirectoryName(_path)!);
        var path = Path.Combine(lease.DirectoryPath, "settings.json");
        var settings = Read(path + ".bak");
        var temporary = path + ".restore-" + Guid.NewGuid().ToString("N");
        var preserved = path + ".preserved-" + Guid.NewGuid().ToString("N");
        var ownsTemporary = false;
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { ownsTemporary = true; JsonSerializer.Serialize(file, settings); file.Flush(true); }
            if (File.Exists(path)) { File.Replace(temporary, path, preserved); return preserved; }
            File.Move(temporary, path);
            return null; // No original existed to preserve.
        }
        finally { if (ownsTemporary && File.Exists(temporary)) File.Delete(temporary); }
    }
    public static void Export(string path, PlayerSettings settings)
    {
        settings = settings.Validate();
        using var lease = DataDirectoryLease.Open(Path.GetDirectoryName(Path.GetFullPath(path))!);
        path = Path.Combine(lease.DirectoryPath, Path.GetFileName(path));
        using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        JsonSerializer.Serialize(file, settings); file.Flush(true);
    }
    public void Save(PlayerSettings settings)
    {
        settings = settings.Validate();
        using var lease = DataDirectoryLease.Create(Path.GetDirectoryName(_path)!);
        var path = Path.Combine(lease.DirectoryPath, "settings.json");
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        var ownsTemporary = false;
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { ownsTemporary = true; JsonSerializer.Serialize(file, settings); file.Flush(true); }
            if (File.Exists(path)) File.Replace(temporary, path, path + ".bak");
            else File.Move(temporary, path);
        }
        finally { if (ownsTemporary && File.Exists(temporary)) File.Delete(temporary); }
    }
}

public sealed class SettingsCompatibilityException(int version) : IOException($"Settings schema {version} is unsupported. Use a compatible application; original settings preserved.");
