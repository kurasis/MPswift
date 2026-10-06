using System.IO;
using System.Text;
using ManagedBass;
using Player.Core.Playback;
using Player.App.Services.Audio;
using Player.Core.Media;

namespace Player.App.Services.Library;

/// <summary>Bounded, operation-local sidecar discovery. No guessed timing or encoding.</summary>
public sealed class CueAlbumDiscovery(Action<string> diagnostic, CancellationToken cancellation, Encoding? fallback = null)
{
    private readonly Dictionary<string, (string Path, CueSheet Sheet)[]> _directories = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string?> _images = new(StringComparer.OrdinalIgnoreCase);
    private long _readBytes;
    public string? Find(string image)
    {
        cancellation.ThrowIfCancellationRequested();
        image = BassSmokeSession.ValidateSourcePath(image);
        if (_images.TryGetValue(image, out var cached)) return cached;
        var directory = Path.GetDirectoryName(image)!;
        if (!_directories.TryGetValue(directory, out var sheets))
        {
            LocalFileAccess.ValidateDirectory(directory);
            var paths = Directory.EnumerateFiles(directory, "*.cue").Take(65).ToArray();
            if (paths.Length > 64) { diagnostic("Too many companion CUE files; import the desired CUE explicitly: " + directory); sheets = []; }
            else
            {
                var loaded = new List<(string, CueSheet)>();
                foreach (var path in paths)
                {
                    cancellation.ThrowIfCancellationRequested();
                    try
                    {
                        BassSmokeSession.ValidateSourcePath(path);
                        var length = new FileInfo(path).Length;
                        if (length > 4 * 1024 * 1024 || length > 16 * 1024 * 1024 - _readBytes)
                            throw new InvalidDataException("Companion CUE discovery budget exceeded; import the desired CUE explicitly.");
                        _readBytes += length;
                        var sheet = Read(path, fallback);
                        if (sheet.Diagnostics.Length != 0) throw new InvalidDataException(string.Join("; ", sheet.Diagnostics.Take(3)));
                        if (sheet.Songs.Length >= 2) loaded.Add((path, sheet));
                    }
                    catch (Exception error) when (error is IOException or InvalidDataException or ArgumentException or UnauthorizedAccessException)
                    { diagnostic("Companion CUE: " + path + ": " + error.Message + " (use explicit legacy-encoding import for non-Unicode CUE)."); }
                }
                sheets = loaded.ToArray();
            }
            _directories[directory] = sheets;
        }
        var candidates = sheets.Where(s => s.Sheet.Songs.All(song => string.Equals(song.Path, image, StringComparison.OrdinalIgnoreCase))).ToArray();
        if (candidates.Length > 1) diagnostic("Multiple CUE sheets describe this FLAC; import the desired CUE explicitly: " + image);
        string? result = null;
        if (candidates.Length == 1)
        {
            try
            {
                // Validate all cue times against one independently owned native decoder.
                // This never opens an output device or touches the active playback graph.
                using var native = new NativeDecodeContext();
                var source = BassMixerGraph.OpenSource(new(Guid.NewGuid(), image));
                try
                {
                    var duration = source.Info.Duration!.Value;
                    foreach (var song in candidates[0].Sheet.Songs)
                    {
                        var start = TrackSegment.FromCueFrames(song.StartFrame);
                        var end = song.EndFrame is { } frame ? TrackSegment.FromCueFrames(frame) : duration;
                        if (start >= duration || end <= start || end.TotalSeconds > duration.TotalSeconds + 1.0 / source.Info.Format.SampleRate)
                            throw new InvalidDataException("CUE indices exceed the decoded FLAC duration.");
                    }
                    result = candidates[0].Path;
                }
                finally { if (!Bass.StreamFree(source.Handle)) throw new IOException("Could not release the CUE validation decoder."); }
            }
            catch (Exception error) when (error is AudioBackendException or IOException or InvalidDataException or ArgumentException or UnauthorizedAccessException)
            { result = null; diagnostic("Companion CUE validation: " + error.Message); }
        }
        _images[image] = result;
        return result;
    }

    public static CueSheet Read(string path, Encoding? fallback = null)
    {
        BassSmokeSession.ValidateSourcePath(path);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > 4 * 1024 * 1024) throw new InvalidDataException("CUE exceeds 4 MiB.");
        var bytes = new byte[checked((int)stream.Length)]; stream.ReadExactly(bytes);
        if (stream.ReadByte() >= 0) throw new InvalidDataException("CUE changed during import.");
        return CueSheet.Parse(CueSheet.Decode(bytes, fallback), path);
    }
}
