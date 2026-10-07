using System.IO;
using System.Text;

namespace Player.App.Services.Storage;

internal static class UiSmokeWorkspace
{
    public const string Prefix = "mpswift-ui-smoke-";
    public const string Marker = ".player-ui-validation";
    private const FileAttributes Unavailable = FileAttributes.ReparsePoint | FileAttributes.Offline | (FileAttributes)(0x00040000 | 0x00400000);

    public static void Validate(string directory)
    {
        var root = new DirectoryInfo(Path.GetFullPath(directory));
        if (!root.Name.StartsWith(Prefix, StringComparison.Ordinal) ||
            !Guid.TryParseExact(root.Name[Prefix.Length..], "N", out var token))
            throw new InvalidDataException("UI validation requires a fresh token-named workspace.");
        for (var parent = root; parent is not null; parent = parent.Parent)
            CheckAttributes(parent);

        var marker = new FileInfo(Path.Combine(root.FullName, Marker));
        CheckAttributes(marker);
        using (var stream = new FileStream(marker.FullName, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            if (stream.Length is < 1 or > 128) throw new InvalidDataException("Invalid UI validation marker size.");
            var bytes = new byte[(int)stream.Length];
            stream.ReadExactly(bytes);
            if (stream.ReadByte() != -1 || !Guid.TryParseExact(new UTF8Encoding(false, true).GetString(bytes).Trim(), "N", out var marked) || marked != token)
                throw new InvalidDataException("UI validation marker does not match its workspace.");
        }

        // Permit only the runner's optional language seed before any diagnostic writes.
        var pending = new Stack<DirectoryInfo>();
        pending.Push(root);
        var count = 0;
        while (pending.TryPop(out var current))
            foreach (var entry in current.EnumerateFileSystemInfos())
            {
                if (++count > 8) throw new InvalidDataException("UI validation workspace is not fresh.");
                CheckAttributes(entry);
                var relative = Path.GetRelativePath(root.FullName, entry.FullName).Replace('\\', '/');
                if (entry is DirectoryInfo child && relative is "artifacts" or "artifacts/smoke" or "artifacts/smoke/stage-c-data")
                    pending.Push(child);
                else if (entry is FileInfo file && (relative == Marker || relative == "artifacts/smoke/stage-c-data/settings.json" && file.Length <= 65536))
                    continue;
                else throw new InvalidDataException("UI validation workspace contains unexpected files or directories.");
            }
    }

    private static void CheckAttributes(FileSystemInfo entry)
    {
        if (!entry.Exists || (entry.Attributes & Unavailable) != 0)
            throw new InvalidDataException("UI validation workspace must exist locally without reparse points or hydration.");
    }
}
