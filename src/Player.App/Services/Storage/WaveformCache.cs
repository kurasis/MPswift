using System.IO;
using System.Security.Cryptography;
using Player.Core.Waveforms;

namespace Player.App.Services.Storage;

/// <summary>Versioned completed extrema/RMS cache. All sizes validated before allocation.</summary>
public sealed class WaveformCache(string directory, long budgetBytes = 512L * 1024 * 1024)
{
    private const int HeaderBytes = 104;
    private readonly object _gate = new();
    private long _budgetBytes = budgetBytes;
    private long _epoch;
    public long Epoch => Interlocked.Read(ref _epoch);
    public WaveformCacheUsage GetUsage()
    {
        lock (_gate)
        {
            long bytes = 0; var count = 0;
            if (Directory.Exists(directory))
                foreach (var file in new DirectoryInfo(directory).EnumerateFiles("*.peaks")) { bytes += file.Length; count++; }
            return new(bytes, count, _budgetBytes);
        }
    }
    public void SetBudget(long bytes)
    {
        if (bytes is < 16L * 1024 * 1024 or > 2048L * 1024 * 1024) throw new ArgumentOutOfRangeException(nameof(bytes));
        lock (_gate) { _budgetBytes = bytes; if (Directory.Exists(directory)) Evict(); }
    }
    public static string Fingerprint(string canonicalPath, long size, long modifiedTicks, string decoderVersion) =>
        Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"v2|all-channel-extrema-rms|{canonicalPath.ToUpperInvariant()}|{size}|{modifiedTicks}|{decoderVersion}")));
    private string PathFor(string key)
    {
        if (key.Length != 64 || key.Any(c => !char.IsAsciiHexDigit(c))) throw new ArgumentException("Invalid cache key.");
        return Path.Combine(directory, key + ".peaks");
    }
    public WaveformData? Read(string key)
    { lock (_gate) return ReadCore(key); }
    private WaveformData? ReadCore(string key)
    {
        var path = PathFor(key);
        if (!File.Exists(path)) return null;
        try
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (file.Length is < HeaderBytes || file.Length > HeaderBytes + WaveformData.MaximumBuckets * 12L) return null;
            using var reader = new BinaryReader(file);
            if (reader.ReadUInt32() != 0x4b505657 || reader.ReadInt32() != 2) return null;
            var algorithm = reader.ReadInt32(); if (algorithm is not (1 or 2)) return null;
            var stride = algorithm == 2 ? 12 : 8;
            if (!reader.ReadBytes(32).AsSpan().SequenceEqual(Convert.FromHexString(key))) return null;
            var rate = reader.ReadInt32(); var channels = reader.ReadInt32();
            var framesPerBucket = reader.ReadInt64(); var totalFrames = reader.ReadInt64(); var count = reader.ReadInt32();
            if (count is < 1 or > WaveformData.MaximumBuckets || file.Length != HeaderBytes + count * (long)stride) return null;
            file.Position = 0; var header = reader.ReadBytes(72);
            var expectedHash = reader.ReadBytes(32);
            var payload = reader.ReadBytes(count * stride);
            using var checksum = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); checksum.AppendData(header); checksum.AppendData(payload);
            if (payload.Length != count * stride || !checksum.GetHashAndReset().AsSpan().SequenceEqual(expectedHash)) return null;
            var minimum = new float[count]; var maximum = new float[count];
            var rms = algorithm == 2 ? new float[count] : null;
            using var peaks = new BinaryReader(new MemoryStream(payload, false));
            for (var i = 0; i < count; i++) { minimum[i] = peaks.ReadSingle(); maximum[i] = peaks.ReadSingle(); if (rms is not null) rms[i] = peaks.ReadSingle(); }
            var data = new WaveformData(rate, channels, framesPerBucket, totalFrames, minimum, maximum, rms); data.Validate();
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow); return data;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or InvalidDataException) { return null; }
    }
    public void Write(string key, WaveformData data)
    { lock (_gate) WriteCore(key, data); }
    public void WriteIfCurrent(string key, WaveformData data, long epoch)
    { lock (_gate) { if (epoch == _epoch) WriteCore(key, data); } }
    private void WriteCore(string key, WaveformData data)
    {
        data.Validate(); Directory.CreateDirectory(directory);
        var path = PathFor(key); var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using var payload = new MemoryStream(data.Minimum.Length * (data.Rms is null ? 8 : 12));
            using (var writer = new BinaryWriter(payload, System.Text.Encoding.UTF8, true))
                for (var i = 0; i < data.Minimum.Length; i++) { writer.Write(data.Minimum[i]); writer.Write(data.Maximum[i]); if (data.Rms is not null) writer.Write(data.Rms[i]); }
            var bytes = payload.ToArray();
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                using var writer = new BinaryWriter(file, System.Text.Encoding.UTF8, true);
                using var header = new MemoryStream(72);
                using (var fields = new BinaryWriter(header, System.Text.Encoding.UTF8, true))
                {
                    fields.Write(0x4b505657u); fields.Write(2); fields.Write(data.Rms is null ? 1 : 2); fields.Write(Convert.FromHexString(key));
                    fields.Write(data.SampleRate); fields.Write(data.Channels); fields.Write(data.FramesPerBucket); fields.Write(data.TotalFrames); fields.Write(data.Minimum.Length);
                }
                var headerBytes = header.ToArray();
                using var checksum = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); checksum.AppendData(headerBytes); checksum.AppendData(bytes);
                writer.Write(headerBytes); writer.Write(checksum.GetHashAndReset()); writer.Write(bytes); writer.Flush(); file.Flush(true);
            }
            File.Move(temporary, path, true); Evict();
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public void Clear()
    {
        lock (_gate) { Interlocked.Increment(ref _epoch); ClearCore(); }
    }
    private void ClearCore()
    {
        if (!Directory.Exists(directory)) return;
        foreach (var file in Directory.EnumerateFiles(directory, "*.peaks")) File.Delete(file);
    }
    private void Evict()
    {
        var files = new DirectoryInfo(directory).EnumerateFiles("*.peaks").OrderBy(f => f.LastWriteTimeUtc).ToArray();
        var size = files.Sum(f => f.Length);
        foreach (var file in files) { if (size <= _budgetBytes) break; size -= file.Length; file.Delete(); }
    }
}
