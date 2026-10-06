using System.IO;
using System.Threading.Channels;
using Player.App.Services.Audio;
using Player.Core.Library;

namespace Player.App.Services.Library;

public sealed record ScanProgress(int Seen, int Changed, int Errors, string[] Details, bool Completed);

/// <summary>One bounded path producer, one metadata reader, 64-record database transactions.</summary>
public sealed class LibraryScanner(ILibraryIndexStore store)
{
    private readonly SemaphoreSlim _scanGate = new(1);
    public async Task ScanAsync(LibraryRoot root, IReadOnlyDictionary<string, Guid> knownIds, IProgress<ScanProgress> progress, CancellationToken token)
    {
        await _scanGate.WaitAsync(token).ConfigureAwait(false);
        try { await ScanCoreAsync(root, knownIds, progress, token).ConfigureAwait(false); }
        finally { _scanGate.Release(); }
    }
    private async Task ScanCoreAsync(LibraryRoot root, IReadOnlyDictionary<string, Guid> knownIds, IProgress<ScanProgress> progress, CancellationToken token)
    {
        if (!root.Enabled) return;
        LocalFileAccess.ValidateDirectory(root.Path);
        var generation = Guid.NewGuid().ToString("N"); var paths = Channel.CreateBounded<string>(new BoundedChannelOptions(256) { SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.Wait });
        var seen = 0; var changed = 0; var errors = 0; var details = new List<string>(); var complete = true;
        void Error(string text) { errors++; complete = false; if (details.Count < 20) details.Add(text[..Math.Min(1024, text.Length)]); }
        var producer = Task.Run(async () =>
        {
            try
            {
                var directories = new Stack<string>(); directories.Push(root.Path); var traversed = 0;
                while (directories.TryPop(out var directory))
                {
                    token.ThrowIfCancellationRequested(); if (++traversed > 100000) throw new IOException("Scan directory limit exceeded.");
                    try
                    {
                        LocalFileAccess.ValidateDirectory(directory);
                        foreach (var path in Directory.EnumerateFileSystemEntries(directory))
                        {
                            token.ThrowIfCancellationRequested(); var attributes = File.GetAttributes(path);
                            if ((attributes & (FileAttributes.ReparsePoint | FileAttributes.Offline)) != 0) continue;
                            if ((attributes & FileAttributes.Directory) != 0) { if (directories.Count >= 100000) throw new IOException("Pending directory limit exceeded."); directories.Push(path); }
                            else if (MediaMetadataReader.Extensions.Contains(Path.GetExtension(path))) await paths.Writer.WriteAsync(path, token).ConfigureAwait(false);
                        }
                    }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException) { lock (details) Error(e.Message); }
                }
                paths.Writer.TryComplete();
            }
            catch (Exception e) { paths.Writer.TryComplete(e); throw; }
        }, token);
        try
        {
            var batch = new List<string>(64);
            async Task Flush()
            {
                if (batch.Count == 0) return;
                var old = (await store.FindFilesAsync(batch.ToArray()).ConfigureAwait(false)).ToDictionary(f => f.Path, StringComparer.OrdinalIgnoreCase); var updates = new List<IndexedFile>();
                foreach (var path in batch)
                {
                    token.ThrowIfCancellationRequested(); if (++seen > 200000) throw new IOException("Library limit is 200,000 files; add a smaller root.");
                    try
                    {
                        BassSmokeSession.ValidateSourcePath(path); var facts = new FileInfo(path); old.TryGetValue(path, out var existing);
                        var track = existing is not null && existing.Size == facts.Length && existing.ModifiedUtcTicks == facts.LastWriteTimeUtc.Ticks ? existing.Track with { Available = true } :
                            MediaMetadataReader.Read(path, existing?.Track.Id ?? (knownIds.TryGetValue(path, out var id) ? id : Guid.NewGuid()), text => { lock (details) { errors++; if (details.Count < 20) details.Add(text[..Math.Min(text.Length, 1024)]); } });
                        if (existing is null || existing.Size != facts.Length || existing.ModifiedUtcTicks != facts.LastWriteTimeUtc.Ticks) changed++;
                        updates.Add(new(existing?.Id ?? Guid.NewGuid(), root.Id, path, facts.Length, facts.LastWriteTimeUtc.Ticks, true, generation, track));
                    }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { lock (details) Error(e.Message); }
                }
                await store.UpsertFilesAsync(updates.ToArray()).ConfigureAwait(false);
                lock (details) progress.Report(new(seen, changed, errors, details.ToArray(), false)); batch.Clear();
            }
            await foreach (var path in paths.Reader.ReadAllAsync(token).ConfigureAwait(false)) { batch.Add(path); if (batch.Count == 64) await Flush().ConfigureAwait(false); }
            await Flush().ConfigureAwait(false); await producer.ConfigureAwait(false);
            if (complete) await store.CompleteScanAsync(root.Id, generation).ConfigureAwait(false);
            lock (details) progress.Report(new(seen, changed, errors, details.ToArray(), complete));
        }
        catch { paths.Writer.TryComplete(); try { await producer.ConfigureAwait(false); } catch { } throw; }
    }
}

/// <summary>Events are dirty hints; overflow triggers the same debounced reconciliation, never file deletion.</summary>
public sealed class LibraryWatcher : IDisposable
{
    private readonly List<FileSystemWatcher> _watchers = [];
    private readonly HashSet<Guid> _dirty = [];
    private readonly Timer _timer;
    private readonly Action<Guid[]> _rescan;
    private bool _disposed;
    private int _overflowCount;
    public int OverflowCount => Volatile.Read(ref _overflowCount);
    internal int BufferSize { get; init; } = 8192;
    internal Action? NotificationCheckpoint { get; init; }
    public LibraryWatcher(Action<Guid[]> rescan) { _rescan = rescan; _timer = new Timer(_ => Flush(), null, Timeout.Infinite, Timeout.Infinite); }
    public void Watch(IEnumerable<LibraryRoot> roots)
    {
        foreach (var watcher in _watchers) watcher.Dispose(); _watchers.Clear();
        foreach (var root in roots.Where(r => r.Enabled).Take(100))
        {
            try
            {
                LocalFileAccess.ValidateDirectory(root.Path);
                var watcher = new FileSystemWatcher(root.Path) { IncludeSubdirectories = true, InternalBufferSize = BufferSize, NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size };
                void Changed(object sender, FileSystemEventArgs e) { NotificationCheckpoint?.Invoke(); var extension = Path.GetExtension(e.FullPath); if (extension.Length == 0 || MediaMetadataReader.Extensions.Contains(extension) || new[] { "cover.jpg", "cover.png", "folder.jpg", "folder.png" }.Contains(Path.GetFileName(e.FullPath).ToLowerInvariant())) Dirty(root.Id); }
                watcher.Changed += Changed; watcher.Created += Changed; watcher.Deleted += Changed; watcher.Renamed += (sender, e) => { Changed(sender, e); var oldExtension = Path.GetExtension(e.OldFullPath); if (oldExtension.Length == 0 || MediaMetadataReader.Extensions.Contains(oldExtension)) Dirty(root.Id); };
                watcher.Error += (_, e) => { if (e.GetException() is InternalBufferOverflowException) Interlocked.Increment(ref _overflowCount); Dirty(root.Id); };
                _watchers.Add(watcher); watcher.EnableRaisingEvents = true;
            }
            catch (IOException) { Dirty(root.Id); }
            catch (UnauthorizedAccessException) { Dirty(root.Id); }
        }
    }
    private void Dirty(Guid root) { lock (_dirty) { if (_disposed) return; _dirty.Add(root); _timer.Change(1500, Timeout.Infinite); } }
    private void Flush() { Guid[] roots; lock (_dirty) { if (_disposed) return; roots = _dirty.ToArray(); _dirty.Clear(); } if (roots.Length > 0) _rescan(roots); }
    public void Dispose() { lock (_dirty) { _disposed = true; _timer.Dispose(); foreach (var watcher in _watchers) watcher.Dispose(); _watchers.Clear(); } }
}
