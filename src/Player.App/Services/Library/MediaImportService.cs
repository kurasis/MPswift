using System.IO;
using System.Collections.Concurrent;
using System.Text;
using Player.App.Services.Audio;
using Player.Core.Media;
using Player.Core.Playback;

namespace Player.App.Services.Library;

public sealed record ImportProgress(PlaylistEntry[] Entries, int Processed, int Errors);
public sealed record ImportSummary(int Added, int Errors, IReadOnlyList<string> Details, bool LimitReached);
public interface IMediaImportService
{
    void RememberTracks(IEnumerable<MediaTrack> tracks);
    Task<ImportSummary> ImportAsync(IEnumerable<string> paths, IProgress<ImportProgress> progress, CancellationToken token, int maximumItems = 10000, Encoding? fallbackEncoding = null);
}

/// <summary>One cancellable background worker, bounded entries/errors, read-only metadata.</summary>
public sealed class MediaImportService : IMediaImportService
{
    private readonly ConcurrentDictionary<string, MediaTrack> _tracks = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
        { ".mp3", ".wav", ".aif", ".aiff", ".flac", ".ogg", ".opus", ".m4a", ".aac", ".alac", ".cue", ".m3u", ".m3u8", ".pls", ".m4b", ".wma", ".ape", ".wv", ".mpc", ".tta", ".dsf", ".dff" };
    private const int MaximumEntries = 10000;

    public void RememberTracks(IEnumerable<MediaTrack> tracks)
    {
        foreach (var track in tracks.Take(MaximumEntries)) _tracks[Identity(track)] = track;
    }

    public Task<ImportSummary> ImportAsync(IEnumerable<string> paths, IProgress<ImportProgress> progress, CancellationToken token, int maximumItems = MaximumEntries, Encoding? fallbackEncoding = null) =>
        Task.Run(() => Import(paths.Take(MaximumEntries + 1).ToArray(), progress, token, Math.Clamp(maximumItems, 0, MaximumEntries), fallbackEncoding), token);

    private ImportSummary Import(string[] paths, IProgress<ImportProgress> progress, CancellationToken token, int maximumItems, Encoding? fallbackEncoding)
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
            foreach (var source in ExpandDocuments(Enumerate(paths, Error, token), Error, fallbackEncoding, token))
            {
                token.ThrowIfCancellationRequested();
                if (processed >= maximumItems) { limit = true; break; }
                try
                {
                    var path = LocalMediaPath.Parse(source).Value;
                    if (Path.GetExtension(path).Equals(".cue", StringComparison.OrdinalIgnoreCase))
                    {
                        BassSmokeSession.ValidateSourcePath(path);
                        if (new FileInfo(path).Length > 4 * 1024 * 1024) throw new IOException("CUE exceeds 4 MiB.");
                        CueSheet sheet;
                        try { sheet = CueSheet.Parse(CueSheet.Decode(ReadDocument(path), fallbackEncoding), path); }
                        catch (System.Text.DecoderFallbackException) { Error("CUE is not valid UTF-8/Unicode. Re-import using the explicit legacy encoding action: " + path); continue; }
                        foreach (var diagnostic in sheet.Diagnostics) Error(diagnostic);
                        foreach (var song in sheet.Songs)
                        {
                            if (processed >= maximumItems) { limit = true; break; }
                            var exists = File.Exists(song.Path);
                            if (exists) BassSmokeSession.ValidateSourcePath(song.Path); else Error("Missing CUE source: " + song.Path);
                            var segment = new TrackSegment(TrackSegment.FromCueFrames(song.StartFrame), song.EndFrame is { } end ? TrackSegment.FromCueFrames(end) : null);
                            var cue = new MediaTrack(CueSheet.TrackId(path, song), song.Path, song.Title, song.Performer, song.Album, segment.Duration, "CUE", exists, segment, path, song.Number);
                            _tracks[Identity(cue)] = cue; batch.Add(new(Guid.NewGuid(), cue, AddedUtcTicks: DateTime.UtcNow.Ticks)); processed++; if (batch.Count == 32) Flush();
                        }
                        continue;
                    }
                    var available = File.Exists(path);
                    if (available) BassSmokeSession.ValidateSourcePath(path);
                    else Error("File unavailable: " + path);
                    if (!_tracks.TryGetValue(path, out var track))
                    {
                        track = MediaMetadataReader.Read(path, Guid.NewGuid(), Error);
                        if (_tracks.Count >= MaximumEntries) _tracks.TryRemove(_tracks.Keys.First(), out _);
                        track = _tracks.GetOrAdd(path, track);
                    }
                    track = track with { Available = available };
                    // Duplicate tracks share logical identity, but every playlist occurrence has its own entry ID.
                    batch.Add(new PlaylistEntry(Guid.NewGuid(), track, AddedUtcTicks: DateTime.UtcNow.Ticks));
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

    private static byte[] ReadDocument(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > 4 * 1024 * 1024) throw new InvalidDataException("Document exceeds 4 MiB.");
        var bytes = new byte[checked((int)stream.Length)]; stream.ReadExactly(bytes);
        if (stream.ReadByte() >= 0) throw new InvalidDataException("Document changed while reading; retry after it is saved.");
        return bytes;
    }
    private static IEnumerable<string> ExpandDocuments(IEnumerable<string> sources, Action<string> error, Encoding? fallback, CancellationToken token)
    {
        foreach (var source in sources)
        {
            token.ThrowIfCancellationRequested();
            if (!new[] { ".m3u", ".m3u8", ".pls" }.Contains(Path.GetExtension(source).ToLowerInvariant())) { yield return source; continue; }
            PlaylistDocument? document = null;
            try
            {
                var path = BassSmokeSession.ValidateSourcePath(source); if (new FileInfo(path).Length > 4 * 1024 * 1024) throw new IOException("Playlist exceeds 4 MiB.");
                document = PlaylistDocument.Parse(CueSheet.Decode(ReadDocument(path), fallback), path, Path.GetExtension(path).Equals(".pls", StringComparison.OrdinalIgnoreCase));
                foreach (var diagnostic in document.Diagnostics) error(diagnostic);
            }
            catch (Exception e) when (e is IOException or ArgumentException or DecoderFallbackException) { error("Playlist import: " + e.Message + "; choose an explicit legacy encoding for non-Unicode files."); }
            if (document is not null) foreach (var path in document.Paths) yield return path;
        }
    }
    private static string Identity(MediaTrack track) => track.CueDocument is null ? track.Path : track.CueDocument + "|" + track.CueNumber;
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
