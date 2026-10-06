using System.IO;
using System.Text.Json;
using Player.App.Services.Audio;

namespace Player.App.Services.Windows;

internal static class AcceptanceWorkspace
{
    public const string Marker = ".player-acceptance-validation";
    public static string Validate()
    {
        var root = Path.GetFullPath(Environment.CurrentDirectory);
        LocalFileAccess.ValidateDirectory(root);
        var marker = Path.Combine(root, Marker);
        LocalFileAccess.ValidateFile(marker);
        if (new FileInfo(marker).Length > 128 || !Guid.TryParseExact(File.ReadAllText(marker).Trim(), "N", out var token) ||
            !new DirectoryInfo(root).Name.Contains(token.ToString("N"), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Acceptance requires a fresh token-named workspace and matching ownership marker.");
        return root;
    }
    public static string ReadFile(string path)
    {
        var root = Validate(); path = LocalFileAccess.ValidateFile(path);
        if (Path.GetRelativePath(root, path).Split(Path.DirectorySeparatorChar).Contains(".."))
            throw new InvalidDataException("Acceptance input must be inside its owned workspace.");
        return path;
    }
    public static void WriteReport(string name, object report)
    {
        var root = Validate();
        if (Path.GetFileName(name) != name) throw new ArgumentException("Report name must be a filename.", nameof(name));
        using var file = new FileStream(Path.Combine(root, name), FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        JsonSerializer.Serialize(file, report, new JsonSerializerOptions { WriteIndented = true });
    }
}
