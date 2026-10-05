using System.IO;
using Player.Core.Media;

namespace Player.App.Services.Audio;

public static class LocalFileAccess
{
    private const FileAttributes RecallAttributes = (FileAttributes)(0x00040000 | 0x00400000);
    public static string ValidateFile(string input)
    {
        var path = LocalMediaPath.Parse(input).Value;
        ValidateDrive(path);
        ValidateParents(new FileInfo(path).Directory, new HashSet<string>(StringComparer.OrdinalIgnoreCase), 0);
        var attributes = File.GetAttributes(path);
        if ((attributes & (FileAttributes.Offline | RecallAttributes)) != 0) throw new IOException("The file requires offline/cloud hydration.");
        if ((attributes & FileAttributes.Directory) != 0) throw new IOException("An audio file is required.");
        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            var target = new FileInfo(path).ResolveLinkTarget(true) ?? throw new IOException("Unresolved file reparse point.");
            path = LocalMediaPath.Parse(target.FullName).Value;
            ValidateDrive(path);
            ValidateParents(new FileInfo(path).Directory, new HashSet<string>(StringComparer.OrdinalIgnoreCase), 0);
            if ((File.GetAttributes(path) & (FileAttributes.Offline | RecallAttributes)) != 0) throw new IOException("The link target is unavailable offline.");
        }
        return path;
    }
    public static void ValidateDirectory(string input)
    {
        ValidateDrive(input.TrimEnd('\\') + "\\local-validation");
        ValidateParents(new DirectoryInfo(input), new HashSet<string>(StringComparer.OrdinalIgnoreCase), 0);
    }
    private static void ValidateDrive(string path)
    {
        LocalMediaPath.Parse(path);
        if (new DriveInfo(Path.GetPathRoot(path)!).DriveType == DriveType.Network) throw new IOException("Mapped network drives are unsupported.");
    }
    private static void ValidateParents(DirectoryInfo? directory, HashSet<string> links, int depth)
    {
        while (directory is not null)
        {
            if (++depth > 256) throw new IOException("Directory/reparse traversal limit reached.");
            var attributes = directory.Attributes;
            if ((attributes & (FileAttributes.Offline | RecallAttributes)) != 0) throw new IOException("The directory is unavailable offline.");
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                if (!links.Add(directory.FullName)) throw new IOException("Reparse loop detected.");
                var target = directory.ResolveLinkTarget(true) ?? throw new IOException("Unresolved directory reparse point.");
                ValidateDrive(target.FullName.TrimEnd('\\') + "\\local-validation");
                ValidateParents(new DirectoryInfo(target.FullName), links, depth);
            }
            directory = directory.Parent;
        }
    }
}
