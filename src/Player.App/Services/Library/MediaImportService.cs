using System.IO;
using Player.App.Services.Audio;
using Player.Core.Media;
using Player.Core.Playback;

namespace Player.App.Services.Library;

public sealed record ImportProgress(PlaylistEntry[] Entries, int Processed, int Errors);
public sealed record ImportSummary(int Added, int Errors, IReadOnlyList<string> Details, bool LimitReached);
public interface IMediaImportService
{
    void RememberTracks(IEnumerable<MediaTrack> tracks);
    Task<ImportSummary> ImportAsync(IEnumerable<string> paths, IProgress<ImportProgress> progress, CancellationToken token, int maximumItems = 10000);
}

/// <summary>One cancellable background worker, bounded entries/errors, read-only metadata.</summary>
public sealed class MediaImportService : IMediaImportService
{
    private readonly Dictionary<string, MediaTrack> _tracks = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
        { ".mp3", ".wav", ".aif", ".aiff", ".flac", ".ogg", ".opus", ".m4a", ".aac", ".alac" };
    private const int MaximumEntries = 10000;

    public void RememberTracks(IEnumerable<MediaTrack> tracks)
    {
        foreach (var track in tracks.Take(MaximumEntries)) _tracks[track.Path] = track;
    }

    public Task<ImportSummary> ImportAsync(IEnumerable<string> paths, IProgress<ImportProgress> progress, CancellationToken token, int maximumItems = MaximumEntries) =>
        Task.Run(() => Import(paths.Take(MaximumEntries + 1).ToArray(), progress, token, Math.Clamp(maximumItems, 0, MaximumEntries)), token);

    private ImportSummary Import(string[] paths, IProgress<ImportProgress> progress, CancellationToken token, int maximumItems)
    {
        var batch = new List<PlaylistEntry>(32);
        var details = new List<string>();
        var processed = 0;
        var errors = 0;
        var limit = false;
        void Error(string message) { errors++; if (details.Count < 20) details.Add(message.Length > 1024 ? message[..1024] : message); }
        void Flush() { if (batch.Count == 0) return; progress.Report(new ImportProgress(batch.ToArray(), processed, errors)); batch.Clear(); }
        try
        {
            foreach (var source in Enumerate(paths, Error, token))
            {
                token.ThrowIfCancellationRequested();
                if (processed >= maximumItems) { limit = true; break; }
                try
                {
                    var path = LocalMediaPath.Parse(source).Value;
                    var available = File.Exists(path);
                    if (available) BassSmokeSession.ValidateSourcePath(path);
                    else Error("File unavailable: " + path);
                    if (!_tracks.TryGetValue(path, out var track))
                    {
                        track = new MediaTrack(Guid.NewGuid(), path, Path.GetFileNameWithoutExtension(path),
                            FormatHint: Path.GetExtension(path).TrimStart('.').ToUpperInvariant(), Available: available);
                        if (available)
                        {
                            try
                            {
                                using var tags = TagLib.File.Create(path, TagLib.ReadStyle.Average);
                                track = track with
                                {
                                    Title = string.IsNullOrWhiteSpace(tags.Tag.Title) ? track.Title : Bounded(tags.Tag.Title) ?? track.Title,
                                    Artist = Bounded(string.Join(", ", tags.Tag.Performers)), Album = Bounded(tags.Tag.Album),
                                    DurationHint = tags.Properties.Duration > TimeSpan.Zero ? tags.Properties.Duration : null
                                };
                            }
                            catch (Exception error) when (error is TagLib.CorruptFileException or TagLib.UnsupportedFormatException or IOException or ArgumentException)
                            { Error("Metadata fallback: " + Path.GetFileName(path) + "; " + error.Message); }
                        }
                        if (_tracks.Count >= MaximumEntries) _tracks.Remove(_tracks.Keys.First());
                        _tracks.Add(path, track);
                    }
                    // Duplicate tracks share logical identity, but every playlist occurrence has its own entry ID.
                    batch.Add(new PlaylistEntry(Guid.NewGuid(), track));
                    processed++;
                    if (batch.Count == 32) Flush();
                }
                catch (Exception error) when (error is ArgumentException or IOException or UnauthorizedAccessException)
                { Error(source + ": " + error.Message); }
            }
        }
        finally { Flush(); }
        return new ImportSummary(processed, errors, details, limit);
    }

    private static string? Bounded(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Length <= 4096 ? value : value[..4096];

    private static IEnumerable<string> Enumerate(string[] paths, Action<string> error, CancellationToken token)
    {
        var directories = new Stack<string>();
        foreach (var source in paths)
        {
            token.ThrowIfCancellationRequested();
            string path;
            try
            {
                path = Path.GetFullPath(source);
                // Validate source syntax before filesystem enumeration; network/device paths are rejected.
                LocalMediaPath.Parse(path.EndsWith('\\') ? path + "local-folder" : path);
                if (new DriveInfo(Path.GetPathRoot(path)!).DriveType == DriveType.Network) throw new IOException("Network drives are unsupported.");
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
            { error(ex.Message); continue; }
            if (Directory.Exists(path))
            {
                try { LocalFileAccess.ValidateDirectory(path); }
                catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException) { error(ex.Message); continue; }
                directories.Push(path);
            }
            else yield return path;
        }
        var visited = 0;
        while (directories.TryPop(out var directory))
        {
            token.ThrowIfCancellationRequested();
            if (++visited > MaximumEntries) { error("Directory traversal limit reached."); yield break; }
            string[] children;
            try
            {
                if ((File.GetAttributes(directory) & (FileAttributes.ReparsePoint | FileAttributes.Offline)) != 0)
                    throw new IOException("Reparse/offline directories are not traversed: " + directory);
                // Enumeration itself is bounded; avoid a complete array for folders with very many files.
                children = Directory.EnumerateFileSystemEntries(directory).Take(MaximumEntries + 1).ToArray();
                if (children.Length > MaximumEntries) error("Folder item limit reached: " + directory);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { error(ex.Message); continue; }
            foreach (var child in children.Take(MaximumEntries))
            {
                token.ThrowIfCancellationRequested();
                FileAttributes attributes;
                try { attributes = File.GetAttributes(child); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { error(ex.Message); continue; }
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    if ((attributes & FileAttributes.ReparsePoint) == 0 && directories.Count < MaximumEntries) directories.Push(child);
                }
                else if (Extensions.Contains(Path.GetExtension(child))) yield return child;
            }
        }
    }
}
