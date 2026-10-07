using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media.Imaging;
using Player.App.Services.Audio;

namespace Player.App.Services.Library;

/// <summary>One thumbnail worker, local embedded/sibling sources only; bounded encoded bytes/pixels and LRU.</summary>
public sealed class ArtworkService
{
    private readonly SemaphoreSlim _worker = new(1);
    private readonly Dictionary<string, BitmapSource?> _cache = [];
    private readonly LinkedList<string> _lru = [];
    private const int MaximumBytes = 20 * 1024 * 1024;
    public async Task<BitmapSource?> LoadAsync(string source, CancellationToken token)
    {
        await _worker.WaitAsync(token).ConfigureAwait(false);
        try
        {
            return await Task.Run(() =>
            {
                token.ThrowIfCancellationRequested(); source = BassSmokeSession.ValidateSourcePath(source); var facts = new FileInfo(source);
                var directory = Path.GetDirectoryName(source)!; var sibling = new[] { "cover.jpg", "folder.jpg", "cover.png", "folder.png" }.Select(n => Path.Combine(directory, n)).FirstOrDefault(File.Exists);
                var siblingFacts = sibling is null ? null : new FileInfo(sibling);
                var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source.ToUpperInvariant() + "|" + facts.Length + "|" + facts.LastWriteTimeUtc.Ticks + "|" + sibling?.ToUpperInvariant() + "|" + siblingFacts?.Length + "|" + siblingFacts?.LastWriteTimeUtc.Ticks)));
                if (_cache.TryGetValue(key, out var cached)) { _lru.Remove(key); _lru.AddLast(key); return cached; }
                byte[]? encoded = null;
                try
                {
                    using var lease = LocalReadLease.Open(source);
                    Player.Core.Media.MetadataReadGuard.Validate(lease.Path);
                    using var file = TagLib.File.Create(lease.Path, TagLib.ReadStyle.Average);
                    var picture = file.Tag.Pictures.FirstOrDefault(p => p.Type == TagLib.PictureType.FrontCover) ?? file.Tag.Pictures.FirstOrDefault();
                    if (picture?.Data.Count is > 0 and <= MaximumBytes) encoded = picture.Data.Data;
                }
                catch (Exception e) when (e is TagLib.CorruptFileException or TagLib.UnsupportedFormatException or IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or NotImplementedException or KeyNotFoundException) { }
                if (encoded is null && sibling is not null)
                {
                    using var lease = LocalReadLease.Open(sibling);
                    var file = lease.Stream;
                    if (file.Length <= MaximumBytes) { encoded = new byte[checked((int)file.Length)]; file.ReadExactly(encoded); }
                }
                BitmapSource? image = null;
                if (encoded is not null)
                {
                    try
                    {
                        using var header = new MemoryStream(encoded, false); var decoder = BitmapDecoder.Create(header, BitmapCreateOptions.DelayCreation, BitmapCacheOption.OnDemand);
                        var frame = decoder.Frames.FirstOrDefault();
                        if (frame is not null && frame.PixelWidth > 0 && frame.PixelHeight > 0 && (long)frame.PixelWidth * frame.PixelHeight <= 40000000)
                        {
                            token.ThrowIfCancellationRequested(); using var input = new MemoryStream(encoded, false);
                            var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad;
                            if (frame.PixelWidth >= frame.PixelHeight) bitmap.DecodePixelWidth = Math.Min(192, frame.PixelWidth);
                            else bitmap.DecodePixelHeight = Math.Min(192, frame.PixelHeight);
                            bitmap.StreamSource = input; bitmap.EndInit(); bitmap.Freeze(); image = bitmap;
                        }
                    }
                    catch (Exception e) when (e is FileFormatException or NotSupportedException or ArgumentException or IOException) { }
                }
                _cache[key] = image; _lru.AddLast(key);
                while (_cache.Count > 64) { var oldest = _lru.First!.Value; _lru.RemoveFirst(); _cache.Remove(oldest); }
                return image;
            }, token).ConfigureAwait(false);
        }
        finally { _worker.Release(); }
    }
}
