using System.IO;
using System.Security.Cryptography;
using Player.App.Services.Audio;
using Player.App.Services.Storage;

namespace Player.App.Services.Library;

internal sealed record TrackFileOperationResult(string[] Completed, string[] Errors);

internal static class TrackFileOperations
{
    internal static async Task<TrackFileOperationResult> CopyAsync(IEnumerable<string> selected, string destination, CancellationToken cancellation)
    {
        LocalFileAccess.ValidateDirectory(destination);
        using var directory = DataDirectoryLease.Open(destination);
        var paths = selected.Distinct(StringComparer.OrdinalIgnoreCase).Take(10001).ToArray();
        if (paths.Length > 10000) throw new IOException("Too many selected files.");
        if (paths.Select(Path.GetFileName).Distinct(StringComparer.OrdinalIgnoreCase).Count() != paths.Length)
            throw new IOException("Selected files have duplicate filenames; choose separate destination folders.");
        var completed = new List<string>(); var errors = new List<string>();
        foreach (var path in paths)
        {
            cancellation.ThrowIfCancellationRequested();
            try
            {
                using var source = LocalReadLease.Open(BassSmokeSession.ValidateSourcePath(path));
                var target = Path.Combine(directory.DirectoryPath, Path.GetFileName(path));
                await using var copy = new FileStream(target, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 128 * 1024, useAsync: true);
                var done = false; var identity = DataFileLease.Identify(copy.SafeFileHandle);
                try
                {
                    DataFileLease.Check(copy.SafeFileHandle);
                    await source.Stream.CopyToAsync(copy, cancellation); await copy.FlushAsync(cancellation); copy.Flush(true);
                    source.Stream.Position = copy.Position = 0;
                    var original = await SHA256.HashDataAsync(source.Stream, cancellation); var written = await SHA256.HashDataAsync(copy, cancellation);
                    if (!original.AsSpan().SequenceEqual(written)) throw new IOException("Copied file checksum differs from its source.");
                    done = true; completed.Add(path);
                }
                finally
                {
                    if (!done)
                    {
                        await copy.DisposeAsync();
                        if (!DataFileLease.DeleteIfSame(target, identity)) throw new IOException("Incomplete copy changed identity; unknown replacement retained.");
                    }
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
            { errors.Add(Path.GetFileName(path) + ": " + error.Message); }
        }
        return new(completed.ToArray(), errors.ToArray());
    }

    // Called only after the app's preview confirmation. Native shell dialogs stay
    // enabled, including any additional prompt when a volume cannot recycle.
    internal static TrackFileOperationResult Recycle(IEnumerable<string> selected, string? activePath)
    {
        var completed = new List<string>(); var errors = new List<string>();
        foreach (var path in selected.Distinct(StringComparer.OrdinalIgnoreCase).Take(10000))
        {
            try
            {
                if (string.Equals(path, activePath, StringComparison.OrdinalIgnoreCase)) throw new IOException("The file is still loaded by the player. Select another source first.");
                var validated = BassSmokeSession.ValidateSourcePath(path);
                using var parent = DataDirectoryLease.Open(Path.GetDirectoryName(validated)!);
                if (!parent.DirectoryPath.Equals(Path.GetDirectoryName(validated), StringComparison.OrdinalIgnoreCase) || (File.GetAttributes(validated) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Linked files or directories are not recycled by this action.");
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(validated, Microsoft.VisualBasic.FileIO.UIOption.AllDialogs,
                    Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin, Microsoft.VisualBasic.FileIO.UICancelOption.ThrowException);
                completed.Add(path);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
            { errors.Add(Path.GetFileName(path) + ": " + error.Message); }
        }
        return new(completed.ToArray(), errors.ToArray());
    }
}
