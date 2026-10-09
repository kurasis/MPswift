using System.Buffers.Binary;

namespace Player.Core.Media;

/// <summary>Reject oversized/truncated tag containers before TagLib allocates their declared payloads.
/// Audio payloads are skipped with 64-bit seeks, never read into a tag-sized buffer.</summary>
public static class MetadataReadGuard
{
    public const long MaximumMetadataBytes = 32L * 1024 * 1024;
    public static void Validate(string path)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        Validate(file, path);
    }
    public static void Validate(Stream file, string path)
    {
        var originalPosition = file.Position;
        try { ValidateCore(file, path); }
        catch (OverflowException error) { throw new InvalidDataException("Metadata size cannot be represented safely.", error); }
        finally { file.Position = originalPosition; }
    }
    private static void ValidateCore(Stream file, string path)
    {
        Span<byte> header = stackalloc byte[32];
        header.Clear();
        if (file.Length < 4) return;
        Read(file, 0, header[..(int)Math.Min(32, file.Length)]);
        long start = 0;
        if (header[..3].SequenceEqual("ID3"u8))
        {
            Require(file.Length >= 10 && header[3] is >= 2 and <= 4 && header[6..10].IndexOfAnyInRange((byte)128, byte.MaxValue) < 0, "Invalid ID3 header.");
            var size = 10L + ((long)header[6] << 21 | (long)header[7] << 14 | (long)header[8] << 7 | header[9]) + ((header[3] == 4 && (header[5] & 16) != 0) ? 10 : 0);
            Limit(size, file.Length); start = size;
            if (file.Length - start >= 4) Read(file, start, header[..4]);
        }
        if (file.Length >= 32)
        {
            Span<byte> footer = stackalloc byte[32]; Read(file, file.Length - 32, footer);
            if (footer[..8].SequenceEqual("APETAGEX"u8)) Limit(BinaryPrimitives.ReadUInt32LittleEndian(footer[12..16]), file.Length);
        }
        if (header[..4].SequenceEqual("fLaC"u8))
        {
            long position = start + 4, total = 0;
            for (var blocks = 0; blocks < 4096; blocks++)
            {
                Read(file, position, header[..4]); var size = (header[1] << 16) | (header[2] << 8) | header[3];
                total += size + 4; Limit(total, file.Length); Limit(size, file.Length - position - 4);
                position += size + 4; if ((header[0] & 128) != 0) return;
            }
            throw new InvalidDataException("Too many FLAC metadata blocks.");
        }
        if (header[..4].SequenceEqual("RIFF"u8) || header[..4].SequenceEqual("RF64"u8) || header[..4].SequenceEqual("FORM"u8))
        {
            var bigEndian = header[..4].SequenceEqual("FORM"u8); var rf64 = header[..4].SequenceEqual("RF64"u8);
            long position = start + 12, total = 0, largeData = -1;
            for (var chunks = 0; position < file.Length && chunks < 4096; chunks++)
            {
                Read(file, position, header[..8]); long size = bigEndian ? BinaryPrimitives.ReadUInt32BigEndian(header[4..8]) : BinaryPrimitives.ReadUInt32LittleEndian(header[4..8]);
                if (rf64 && header[..4].SequenceEqual("ds64"u8)) { Read(file, position + 8, header[8..24]); largeData = checked((long)BinaryPrimitives.ReadUInt64LittleEndian(header[16..24])); }
                var audio = header[..4].SequenceEqual("data"u8) || bigEndian && header[..4].SequenceEqual("SSND"u8);
                if (rf64 && audio && size == uint.MaxValue) { Require(largeData >= 0, "Missing RF64 ds64 size."); size = largeData; }
                Require(size >= 0 && size <= file.Length - position - 8, "Truncated audio container chunk.");
                if (!audio) { total += size + 8; Limit(total, file.Length); }
                position += 8 + size + (size & 1);
            }
            Require(position >= file.Length, "Too many container chunks."); return;
        }
        if (header[..4].SequenceEqual("DSD "u8))
        {
            Read(file, start, header); var tagOffset = BinaryPrimitives.ReadUInt64LittleEndian(header[20..28]);
            if (tagOffset != 0) { Require(tagOffset <= (ulong)file.Length, "Invalid DSF metadata offset."); Limit(file.Length - (long)tagOffset, file.Length); }
            return;
        }
        if (header[..4].SequenceEqual("FRM8"u8))
        {
            long position = start + 16, total = 0;
            for (var chunks = 0; position < file.Length && chunks < 4096; chunks++)
            {
                Read(file, position, header[..12]); var size = checked((long)BinaryPrimitives.ReadUInt64BigEndian(header[4..12]));
                Require(size <= file.Length - position - 12, "Truncated DFF chunk.");
                if (!header[..4].SequenceEqual("DSD "u8) && !header[..4].SequenceEqual("DST "u8)) { total += size + 12; Limit(total, file.Length); }
                position += 12 + size + (size & 1);
            }
            Require(position >= file.Length, "Too many DFF chunks."); return;
        }
        if (header[..4].SequenceEqual("OggS"u8))
        {
            long position = start, total = 0; var packets = 0; Span<byte> laces = stackalloc byte[255];
            var streams = new HashSet<uint>();
            for (var pages = 0; packets < 3 && pages < 65536; pages++)
            {
                Read(file, position, header[..27]); Require(header[..4].SequenceEqual("OggS"u8), "Invalid Ogg page.");
                Require(header[4] == 0 && (header[5] & ~7) == 0, "Invalid Ogg version/flags.");
                var serial = BinaryPrimitives.ReadUInt32LittleEndian(header[14..18]);
                if ((header[5] & 2) != 0) Require(streams.Add(serial), "Duplicate Ogg stream beginning.");
                else Require(streams.Contains(serial), "Ogg page references a stream without a beginning.");
                var count = header[26]; Read(file, position + 27, laces[..count]); var body = 0;
                foreach (var lace in laces[..count]) { body += lace; if (lace < 255) packets++; }
                total += 27 + count + body; Limit(total, file.Length); position += 27 + count + body;
                Require(position <= file.Length, "Truncated Ogg packet.");
            }
            Require(packets >= 3, "Incomplete Ogg headers."); return;
        }
        // ASF header contains codec/extended metadata. Its declared size excludes audio data.
        if (header[..16].SequenceEqual(new Guid("75b22630-668e-11cf-a6d9-00aa0062ce6c").ToByteArray()))
        { Read(file, start + 16, header[..8]); Limit(checked((long)BinaryPrimitives.ReadUInt64LittleEndian(header[..8])), file.Length); return; }
        if (Path.GetExtension(path).ToLowerInvariant() is ".m4a" or ".m4b" or ".alac")
        {
            long position = 0, total = 0;
            for (var atoms = 0; position < file.Length && atoms < 4096; atoms++)
            {
                Read(file, position, header[..8]); long size = BinaryPrimitives.ReadUInt32BigEndian(header[..4]); var headerSize = 8;
                if (size == 1) { Read(file, position + 8, header[8..16]); size = checked((long)BinaryPrimitives.ReadUInt64BigEndian(header[8..16])); headerSize = 16; }
                if (size == 0) size = file.Length - position;
                Require(size >= headerSize && size <= file.Length - position, "Invalid MP4 atom.");
                if (!header[4..8].SequenceEqual("mdat"u8) && !header[4..8].SequenceEqual("free"u8) && !header[4..8].SequenceEqual("skip"u8)) { total += size; Limit(total, file.Length); }
                position += size;
            }
            Require(position == file.Length, "Too many MP4 atoms.");
        }
    }
    private static void Limit(long size, long available) => Require(size >= 0 && size <= MaximumMetadataBytes && size <= available, "Metadata exceeds its safe byte limit or file bounds.");
    private static void Read(Stream file, long position, Span<byte> buffer)
    { Require(position >= 0 && position <= file.Length - buffer.Length, "Truncated metadata header."); file.Position = position; file.ReadExactly(buffer); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
}
