using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using Player.App.Services.Storage;

namespace MPswift.TagRepair;

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new() { Encoder = JavaScriptEncoder.Create(UnicodeRanges.All) };
    public static int Main(string[] args)
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler handler = (_, e) => { e.Cancel = true; cancellation.Cancel(); };
        Console.CancelKeyPress += handler;
        try { return Run(args, Console.Out, Console.Error, cancellation.Token); }
        finally { Console.CancelKeyPress -= handler; }
    }

    internal static int Run(string[] args, TextWriter output, TextWriter errors, CancellationToken cancellation = default)
    {
        if (args.Length == 0 || args is ["--help"] or ["-h"])
        {
            output.WriteLine("MPswift Tag Repair " + Player.Core.ProductInfo.Version);
            output.WriteLine("Usage: MPswift.TagRepair.exe \"C:\\Music\" [--apply] [--top-only] [--cue-codepage 1251|1252|866]");
            output.WriteLine("Default: recursive PREVIEW only. Supported files: MP3, FLAC, CUE.");
            output.WriteLine("--apply: Windows-only writes; verified originals retained beside each changed file as .mpswift-<id>.bak.");
            output.WriteLine("MP3: ID3v2.4/UTF-8 without ID3v1. FLAC: UTF-8 tags. CUE: UTF-8 without BOM; file references/timings retained.");
            output.WriteLine("No audio transcoding, file renaming, shell commands, network access or administrator requirement.");
            output.WriteLine("Exit codes: 0 success; 1 per-file errors; 2 invalid arguments/folder; 3 unsupported write platform; 130 cancelled.");
            return 0;
        }
        if (args is ["--version"]) { output.WriteLine(Player.Core.ProductInfo.Version); return 0; }
        var apply = false; var recursive = true; var codePage = 1251;
        try
        {
            for (var i = 1; i < args.Length; i++)
                switch (args[i])
                {
                    case "--apply": apply = true; break;
                    case "--top-only": recursive = false; break;
                    case "--cue-codepage" when i + 1 < args.Length && int.TryParse(args[++i], out var value) && value is 1251 or 1252 or 866: codePage = value; break;
                    default: throw new ArgumentException("Unknown/incomplete argument: " + args[i]);
                }
            if (args[0].StartsWith("--", StringComparison.Ordinal)) throw new ArgumentException("A folder is required.");
            if (apply && !OperatingSystem.IsWindows()) { errors.WriteLine("--apply is supported only on Windows. No files were changed."); return 3; }
            var requested = Path.GetFullPath(args[0]);
            if ((File.GetAttributes(requested) & FileAttributes.ReparsePoint) != 0) throw new IOException("The selected folder must not be a directory link.");
            using var root = DataDirectoryLease.Open(requested);
            var failures = 0; var planned = 0; var unchanged = 0;
            output.WriteLine(apply ? "APPLY: changes will be written; verified .bak originals will be retained." : "PREVIEW: no files will be changed. Add --apply after reviewing the proposed changes.");
            foreach (var path in Enumerate(root.DirectoryPath, recursive, output, cancellation))
            {
                cancellation.ThrowIfCancellationRequested();
                try
                {
                    var result = FileRepair.Process(root.DirectoryPath, path, apply, codePage, cancellation);
                    if (result.Status == "unchanged") { unchanged++; continue; }
                    planned++; output.WriteLine(result.Status.ToUpperInvariant() + " " + Quote(path));
                    foreach (var change in result.Changes) output.WriteLine("  " + change);
                    if (result.Backup is not null) output.WriteLine("  BACKUP " + Quote(result.Backup));
                    if (result.AudioSha256 is not null) output.WriteLine("  AUDIO SHA256 " + result.AudioSha256);
                }
                catch (Exception error) when (IsFileError(error))
                { failures++; errors.WriteLine("ERROR " + Quote(path) + ": " + Quote(error.Message)); }
            }
            output.WriteLine($"Summary: {(apply ? "repaired" : "planned")}={planned}; unchanged={unchanged}; errors={failures}.");
            return failures == 0 ? 0 : 1;
        }
        catch (OperationCanceledException) { errors.WriteLine("Cancelled. Completed writes and verified backups were retained."); return 130; }
        catch (Exception error) when (IsFileError(error)) { errors.WriteLine(Quote(error.Message)); return 2; }
    }

    private static IEnumerable<string> Enumerate(string root, bool recursive, TextWriter output, CancellationToken cancellation)
    {
        var pending = new Stack<(string Path, int Depth)>(); pending.Push((root, 0)); var total = 0;
        while (pending.TryPop(out var directory))
        {
            cancellation.ThrowIfCancellationRequested();
            using var pin = DataDirectoryLease.Open(directory.Path);
            if (!Path.GetRelativePath(root, pin.DirectoryPath).Equals(Path.GetRelativePath(root, directory.Path), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                throw new IOException("Directory changed during enumeration.");
            // Snapshot this bounded directory before writing backups into it.
            var entries = Directory.EnumerateFileSystemEntries(pin.DirectoryPath).Take(100001 - total).ToArray();
            foreach (var entry in entries)
            {
                if (++total > 100000) throw new IOException("Folder exceeds the 100000-entry safety limit. Process individual album folders.");
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0) { output.WriteLine("SKIP LINK " + Quote(entry)); continue; }
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    if (recursive) { if (directory.Depth >= 64) throw new IOException("Folder exceeds the 64-level depth limit."); pending.Push((entry, directory.Depth + 1)); }
                }
                else if (Path.GetExtension(entry).ToLowerInvariant() is ".mp3" or ".flac" or ".cue") yield return entry;
            }
        }
    }
    private static bool IsFileError(Exception error) => error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or
        TagLib.CorruptFileException or TagLib.UnsupportedFormatException or NotImplementedException or KeyNotFoundException or System.Reflection.TargetInvocationException;
    private static string Quote(string value) => JsonSerializer.Serialize(value, JsonOptions);
}
