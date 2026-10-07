using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using ManagedBass;
using Player.App.Services.Audio;
using Player.App.Services.Storage;
using Player.Core.Waveforms;

namespace Player.App.Services.Waveforms;

public sealed class BassWaveformService : IWaveformService, IWaveformCacheControl
{
    private readonly BlockingCollection<Job> _jobs = new(1);
    private readonly Dictionary<string, Job> _inflight = [];
    private readonly object _gate = new();
    private readonly WaveformCache _cache;
    private readonly TaskCompletionSource _exit = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Job? _active;
    private bool _closing;
    public BassWaveformService(WaveformCache cache)
    {
        _cache = cache;
        new Thread(Run) { IsBackground = true, Priority = ThreadPriority.BelowNormal, Name = "Player waveform analysis" }.Start();
    }
    public Task<WaveformCacheUsage> GetCacheUsageAsync() => Task.Run(_cache.GetUsage);
    public Task SetCacheBudgetAsync(long bytes) => Task.Run(() => _cache.SetBudget(bytes));
    public Task ClearCacheAsync() => Task.Run(_cache.Clear);
    public Task<WaveformData> AnalyzeAsync(string path, IProgress<double>? progress, CancellationToken cancellationToken, bool refresh = false)
    {
        // Validation/stat/cache work also stays off the dispatcher.
        return Task.Run(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            path = LocalFileAccess.ValidateFile(path);
            var file = new FileInfo(path);
            var decoder = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "native", "manifest.json"))));
            var key = WaveformCache.Fingerprint(path, file.Length, file.LastWriteTimeUtc.Ticks, decoder);
            Task<WaveformData> task;
            Job shared;
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_closing, this);
                if (!_inflight.TryGetValue(key, out var job) || job.Cancellation.IsCancellationRequested || refresh)
                {
                    _active?.Cancellation.Cancel();
                    if (_jobs.TryTake(out var pending))
                    { pending.Cancellation.Cancel(); pending.Completion.TrySetCanceled(); _inflight.Remove(pending.Key); pending.Cancellation.Dispose(); }
                    job = new Job(path, key, file.Length, file.LastWriteTimeUtc.Ticks, refresh, _cache.Epoch);
                    _inflight[key] = job;
                    if (!_jobs.TryAdd(job)) throw new IOException("Waveform worker queue is unavailable.");
                }
                if (progress is not null && job.Progress.Count < 8) job.Progress.Add(progress);
                task = job.Completion.Task; shared = job; job.Waiters++;
            }
            try { return await task.WaitAsync(cancellationToken).ConfigureAwait(false); }
            finally { lock (_gate) { shared.Waiters--; if (shared.Waiters == 0 && !shared.Completion.Task.IsCompleted) shared.Cancellation.Cancel(); } }
        }, cancellationToken);
    }
    private void Run()
    {
        try
        {
            foreach (var job in _jobs.GetConsumingEnumerable())
            {
                lock (_gate) { _active = job; if (_jobs.Count > 0) job.Cancellation.Cancel(); }
                try
                {
                    job.Cancellation.Token.ThrowIfCancellationRequested();
                    var data = job.Refresh ? null : _cache.Read(job.Key);
                    if (data is null)
                    {
                        data = Decode(job);
                        job.Cancellation.Token.ThrowIfCancellationRequested();
                        var file = new FileInfo(job.Path);
                        if (file.Length != job.Size || file.LastWriteTimeUtc.Ticks != job.Modified) throw new IOException("Source changed during waveform analysis.");
                        // A cache write failure must not discard a valid in-memory waveform.
                        try { _cache.WriteIfCurrent(job.Key, data, job.CacheEpoch); } catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
                    }
                    job.Completion.TrySetResult(data);
                }
                catch (OperationCanceledException) { job.Completion.TrySetCanceled(); }
                catch (Exception error) { job.Completion.TrySetException(error); }
                finally { lock (_gate) { if (_inflight.TryGetValue(job.Key, out var mapped) && ReferenceEquals(mapped, job)) _inflight.Remove(job.Key); _active = null; job.Cancellation.Dispose(); } }
            }
            _exit.TrySetResult();
        }
        catch (Exception error) { _exit.TrySetException(error); }
        finally { _jobs.Dispose(); }
    }
    private WaveformData Decode(Job job)
    {
        using var context = new NativeDecodeContext();
        using var read = LocalReadLease.Open(job.Path);
        var stream = Bass.CreateStream(read.Path, 0, 0, BassFlags.Decode | BassFlags.Float | BassFlags.Prescan);
        if (stream == 0) throw new IOException("Waveform decoder could not open source: " + Bass.LastError);
        try
        {
            if (!Bass.ChannelGetInfo(stream, out var info)) throw new IOException("Waveform format unavailable: " + Bass.LastError);
            var length = Bass.ChannelGetLength(stream);
            if (length <= 0 || info.Channels is < 1 or > 64) throw new InvalidDataException("A validated finite duration is required for waveform analysis.");
            var frames = length / (sizeof(float) * info.Channels);
            var accumulator = new WaveformAccumulator(info.Frequency, info.Channels, frames);
            var buffer = new float[info.Channels * 4096];
            var clock = Stopwatch.StartNew(); var lastProgress = TimeSpan.Zero;
            while (true)
            {
                job.Cancellation.Token.ThrowIfCancellationRequested();
                var bytes = Bass.ChannelGetData(stream, buffer, buffer.Length * sizeof(float));
                if (bytes < 0)
                { if (Bass.LastError == Errors.Ended) break; throw new IOException("Waveform decode failed: " + Bass.LastError); }
                if (bytes == 0 || bytes % (info.Channels * sizeof(float)) != 0) throw new InvalidDataException("Waveform decoder stalled or returned an incomplete frame.");
                accumulator.Add(buffer.AsSpan(0, bytes / sizeof(float)));
                if (clock.Elapsed - lastProgress >= TimeSpan.FromMilliseconds(100))
                {
                    lastProgress = clock.Elapsed;
                    lock (_gate) foreach (var progress in job.Progress) progress.Report(Math.Min(1, (double)accumulator.Frames / frames));
                }
            }
            return accumulator.Complete();
        }
        finally { if (!Bass.StreamFree(stream)) throw new IOException("Waveform decoder release failed: " + Bass.LastError); }
    }
    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (!_closing)
            {
                _closing = true; _active?.Cancellation.Cancel();
                foreach (var job in _inflight.Values) job.Cancellation.Cancel();
                _jobs.CompleteAdding();
            }
        }
        return new(_exit.Task);
    }
    private sealed class Job(string path, string key, long size, long modified, bool refresh, long cacheEpoch)
    {
        public string Path { get; } = path;
        public string Key { get; } = key;
        public long Size { get; } = size;
        public long Modified { get; } = modified;
        public bool Refresh { get; } = refresh;
        public long CacheEpoch { get; } = cacheEpoch;
        public CancellationTokenSource Cancellation { get; } = new();
        public TaskCompletionSource<WaveformData> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<IProgress<double>> Progress { get; } = [];
        public int Waiters { get; set; }
    }
}
