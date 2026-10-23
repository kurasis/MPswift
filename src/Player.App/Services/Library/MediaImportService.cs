using System.IO;
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
    private readonly TrackCache _tracks = new();
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
        { ".mp3", ".wav", ".aif", ".aiff", ".flac", ".ogg", ".opus", ".m4a", ".aac", ".alac", ".cue", ".m3u", ".m3u8", ".pls", ".m4b", ".wma", ".ape", ".wv", ".mpc", ".tta", ".dsf", ".dff" };
    private const int MaximumEntries = 10000;

    public void RememberTracks(IEnumerable<MediaTrack> tracks)
    {
        // Saved playlists carry no file stamp, so the next import of such a file re-reads its tags under the same ID.
        foreach (var track in tracks.Take(MaximumEntries)) _tracks.Remember(Identity(track), track, null);
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
            // Bound planning before associating images with selected sidecars. Repeated explicit
            // FLAC inputs still produce repeated album occurrences; folder FLAC+CUE pairs do not.
            var sources = ExpandDocuments(Enumerate(paths, Error, token), Error, fallbackEncoding, token).Take(MaximumEntries + 1).ToArray();
            if (sources.Length > MaximumEntries) limit = true;
            var discovery = new CueAlbumDiscovery(Error, token, fallbackEncoding);
            string? FindCue(string path)
            {
                try { LocalFileAccess.ValidateFileLocation(path); return File.Exists(path) ? discovery.Find(path) : null; }
                catch (Exception error) when (error is IOException or InvalidDataException or ArgumentException or UnauthorizedAccessException)
                { Error("Companion CUE: " + error.Message); return null; }
            }
            foreach (var source in CueImportSelection.Resolve(sources, FindCue))
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
                        try { sheet = CueAlbumDiscovery.ReadAssociated(path, fallbackEncoding); }
                        catch (System.Text.DecoderFallbackException) { Error("CUE is not valid UTF-8/Unicode. Re-import using the explicit legacy encoding action: " + path); continue; }
                        foreach (var diagnostic in sheet.Diagnostics) Error(diagnostic);
                        var imageDuration = CueAlbumDiscovery.ValidateImageDuration(sheet);
                        foreach (var song in sheet.Songs)
                        {
                            if (processed >= maximumItems) { limit = true; break; }
                            LocalFileAccess.ValidateFileLocation(song.Path);
                            var exists = File.Exists(song.Path);
                            if (exists) BassSmokeSession.ValidateSourcePath(song.Path); else Error("Missing CUE source: " + song.Path);
                            var segment = new TrackSegment(TrackSegment.FromCueFrames(song.StartFrame), song.EndFrame is { } end ? TrackSegment.FromCueFrames(end) : null);
                            var durationHint = segment.Duration ?? (imageDuration is { } full ? full - segment.Start : (TimeSpan?)null);
                            var cue = new MediaTrack(CueSheet.TrackId(path, song), song.Path, song.Title, song.Performer, song.Album, durationHint, "CUE", exists, segment, path, song.Number);
                            _tracks.Remember(Identity(cue), cue, null); batch.Add(new(Guid.NewGuid(), cue, AddedUtcTicks: DateTime.UtcNow.Ticks)); processed++; if (batch.Count == 32) Flush();
                        }
                        continue;
                    }
                    // Reject network drives and redirecting links before an untrusted path is probed.
                    LocalFileAccess.ValidateFileLocation(path);
                    var available = File.Exists(path);
                    FileStamp? stamp = null;
                    if (available) stamp = FileStamp.Read(BassSmokeSession.ValidateSourcePath(path));
                    else Error("File unavailable: " + path);
                    var known = _tracks.Find(path);
                    // Tags edited since the file was last read are re-read; logical identity (ratings, duplicates) is kept.
                    var track = known is { } cached && (!available || cached.Stamp == stamp) ? cached.Track :
                        _tracks.Refresh(path, MediaMetadataReader.Read(path, known?.Track.Id ?? Guid.NewGuid(), Error), stamp);
                    track = track with { Available = available };
                    // Duplicate tracks share logical identity, but every playlist occurrence has its own entry ID.
                    batch.Add(new PlaylistEntry(Guid.NewGuid(), track, AddedUtcTicks: DateTime.UtcNow.Ticks));
                    processed++;
                    if (batch.Count == 32) Flush();
                }
                catch (Exception error) when (error is AudioBackendException or ArgumentException or InvalidDataException or IOException or UnauthorizedAccessException)
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
    private readonly record struct FileStamp(long Length, long LastWriteUtcTicks)
    {
        public static FileStamp Read(string path) { var file = new FileInfo(path); return new(file.Length, file.LastWriteTimeUtc.Ticks); }
    }
    /// <summary>Bounded first-in-first-out track cache shared by concurrent imports.</summary>
    private sealed class TrackCache
    {
        private readonly object _gate = new();
        private readonly Dictionary<string, (MediaTrack Track, FileStamp? Stamp)> _tracks = new(StringComparer.OrdinalIgnoreCase);
        private readonly Queue<string> _order = new();
        public (MediaTrack Track, FileStamp? Stamp)? Find(string key) { lock (_gate) return _tracks.TryGetValue(key, out var value) ? value : null; }
        public void Remember(string key, MediaTrack track, FileStamp? stamp) { lock (_gate) Store(key, track, stamp); }
        /// <summary>Stores freshly read metadata unless a concurrent import already stored the same file version.</summary>
        public MediaTrack Refresh(string key, MediaTrack track, FileStamp? stamp)
        {
            lock (_gate)
            {
                if (_tracks.TryGetValue(key, out var existing))
                {
                    if (stamp is not null && existing.Stamp == stamp) return existing.Track;
                    track = track with { Id = existing.Track.Id };
                }
                Store(key, track, stamp); return track;
            }
        }
        private void Store(string key, MediaTrack track, FileStamp? stamp)
        {
            if (!_tracks.ContainsKey(key))
            {
                while (_tracks.Count >= MaximumEntries) _tracks.Remove(_order.Dequeue());
                _order.Enqueue(key);
            }
            _tracks[key] = (track, stamp);
        }
    }
    private static string Identity(MediaTrack track) => track.CueDocument is null ? track.Path : track.CueDocument + "|" + track.CueNumber;

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
