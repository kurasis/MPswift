using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace Player.Core.Integration;

/// <summary>Local bounded diagnostics. File I/O never runs on a caller or audio callback.</summary>
public sealed class RotatingLog : IAsyncDisposable
{
    private readonly string _directory;
    private readonly int _maximumBytes, _files;
    private readonly string[] _privatePrefixes;
    private readonly Channel<byte[]> _queue = Channel.CreateBounded<byte[]>(128);
    private readonly Task _worker;
    private long _dropped;
    public long Dropped => Interlocked.Read(ref _dropped);
    public string? LastError { get; private set; }
    public RotatingLog(string directory, IEnumerable<string>? privatePrefixes = null, int maximumBytes = 2 * 1024 * 1024, int files = 5)
    {
        if (maximumBytes < 4096 || files is < 1 or > 10) throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        _directory = directory; _maximumBytes = maximumBytes; _files = files;
        _privatePrefixes = DiagnosticReport.PreparePrefixes(privatePrefixes ?? []);
        _worker = Task.Run(WriteAsync);
    }
    public bool Record(string operation, string detail)
    {
        foreach (var prefix in _privatePrefixes)
        {
            detail = detail.Replace(prefix, "<local>", StringComparison.OrdinalIgnoreCase);
            operation = operation.Replace(prefix, "<local>", StringComparison.OrdinalIgnoreCase);
        }
        static string Bound(string value, int limit)
        { var length = Math.Min(value.Length, limit); if (length < value.Length && length > 0 && char.IsHighSurrogate(value[length - 1])) length--; return value[..length]; }
        var record = JsonSerializer.SerializeToUtf8Bytes(new { Utc = DateTime.UtcNow, Operation = Bound(operation, 64), Detail = Bound(detail, 2048) });
        if (_queue.Writer.TryWrite(record)) return true;
        Interlocked.Increment(ref _dropped); return false;
    }
    private async Task WriteAsync()
    {
        await foreach (var entry in _queue.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            try
            {
                Directory.CreateDirectory(_directory);
                var active = Path.Combine(_directory, "log-0.jsonl");
                if (File.Exists(active) && new FileInfo(active).Length + entry.Length + 1 > _maximumBytes)
                {
                    File.Delete(Path.Combine(_directory, $"log-{_files - 1}.jsonl"));
                    for (var i = _files - 2; i >= 0; i--)
                    { var source = Path.Combine(_directory, $"log-{i}.jsonl"); if (File.Exists(source)) File.Move(source, Path.Combine(_directory, $"log-{i + 1}.jsonl"), true); }
                }
                // Escape-heavy JSON can exceed the configured file budget; truncate safely as a complete JSON record.
                var bytes = entry.Length + 1 <= _maximumBytes ? entry : Encoding.UTF8.GetBytes("{\"Operation\":\"diagnostic-truncated\"}");
                await using var stream = new FileStream(active, FileMode.Append, FileAccess.Write, FileShare.Read, 4096, true);
                await stream.WriteAsync(bytes).ConfigureAwait(false); await stream.WriteAsync(new byte[] { 10 }).ConfigureAwait(false);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { LastError = error.Message; }
        }
    }
    public async ValueTask DisposeAsync() { _queue.Writer.TryComplete(); await _worker.ConfigureAwait(false); }
}
