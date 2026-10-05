using System.IO;
using System.Text.Json;
using Player.Core.Library;

namespace Player.App.Services.Storage;

public sealed class SettingsFile(string directory)
{
    private readonly string _path = Path.Combine(directory, "settings.json");
    public PlayerSettings Load()
    {
        if (!File.Exists(_path)) return new();
        if (new FileInfo(_path).Length > 65536) throw new InvalidDataException("Settings file is too large; original preserved.");
        return (JsonSerializer.Deserialize<PlayerSettings>(File.ReadAllText(_path)) ?? throw new InvalidDataException("Invalid settings; original preserved.")).Validate();
    }
    public static void Export(string path, PlayerSettings settings)
    {
        settings.Validate();
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
