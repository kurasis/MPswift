using System.Buffers.Binary;
using System.Security.Cryptography;
using Player.Core.Media;

namespace MPswift.TagRepair;

internal sealed record AudioFingerprint(long Bytes, string Sha256)
{
    internal static AudioFingerprint Read(Stream stream, string extension)
    {
        long start = 0, end = stream.Length;
        byte[]? codecHeader = null;
        Span<byte> header = stackalloc byte[32];
        void Bounds(long position, int count)
        { if (position < 0 || position > stream.Length - count) throw new InvalidDataException("Truncated media container."); }
        for (var tags = 0; tags < 4; tags++)
        {
            Bounds(start, 10); stream.Position = start; stream.ReadExactly(header[..10]);
            if (!header[..3].SequenceEqual("ID3"u8)) break;
            if (header[3] is < 2 or > 4 || header[6..10].IndexOfAnyInRange((byte)128, byte.MaxValue) >= 0) throw new InvalidDataException("Invalid ID3 size/version.");
            var size = 10L + ((long)header[6] << 21 | (long)header[7] << 14 | (long)header[8] << 7 | header[9]) + (header[3] == 4 && (header[5] & 16) != 0 ? 10 : 0);
            if (size > MetadataReadGuard.MaximumMetadataBytes || size > stream.Length - start) throw new InvalidDataException("Invalid ID3 bounds.");
            start += size;
            if (start > MetadataReadGuard.MaximumMetadataBytes) throw new InvalidDataException("Combined ID3 metadata exceeds its safe limit.");
        }
        if (extension.Equals(".flac", StringComparison.OrdinalIgnoreCase))
        {
            Bounds(start, 4); stream.Position = start; stream.ReadExactly(header[..4]);
            if (!header[..4].SequenceEqual("fLaC"u8)) throw new InvalidDataException("Missing FLAC signature.");
            start += 4; var total = 0L; var last = false;
            for (var blocks = 0; blocks < 4096 && !last; blocks++)
            {
                Bounds(start, 4); stream.Position = start; stream.ReadExactly(header[..4]);
                var size = header[1] << 16 | header[2] << 8 | header[3];
                if (blocks == 0 && ((header[0] & 127) != 0 || size != 34)) throw new InvalidDataException("Missing FLAC STREAMINFO.");
                if (blocks == 0) { codecHeader = new byte[34]; stream.ReadExactly(codecHeader); }
                total += size + 4; if (total > MetadataReadGuard.MaximumMetadataBytes || size > stream.Length - start - 4) throw new InvalidDataException("Invalid FLAC metadata bounds.");
                last = (header[0] & 128) != 0; start += size + 4;
            }
            if (!last) throw new InvalidDataException("Too many FLAC metadata blocks.");
        }
        else if (extension.Equals(".mp3", StringComparison.OrdinalIgnoreCase))
        {
            if (end - start >= 128)
            {
                stream.Position = end - 128; stream.ReadExactly(header[..3]);
                if (header[..3].SequenceEqual("TAG"u8)) end -= 128;
            }
            if (end - start >= 32)
            {
                stream.Position = end - 32; stream.ReadExactly(header);
                if (header[..8].SequenceEqual("APETAGEX"u8))
                {
                    var size = BinaryPrimitives.ReadUInt32LittleEndian(header[12..16]);
                    if (size < 32 || size > MetadataReadGuard.MaximumMetadataBytes || size > end - start) throw new InvalidDataException("Invalid APE bounds.");
                    end -= size;
                    if ((BinaryPrimitives.ReadUInt32LittleEndian(header[20..24]) & 0x80000000) != 0)
                    { Bounds(end - 32, 32); stream.Position = end - 32; stream.ReadExactly(header); if (!header[..8].SequenceEqual("APETAGEX"u8)) throw new InvalidDataException("Missing APE header."); end -= 32; }
                }
            }
            Bounds(start, 4); stream.Position = start; stream.ReadExactly(header[..4]);
            if (header[0] != 255 || (header[1] & 224) != 224 || (header[1] & 24) == 8 || (header[1] & 6) == 0 || (header[2] & 12) == 12)
                throw new InvalidDataException("Unrecognized MPEG audio start; original retained.");
        }
        else throw new NotSupportedException("Only MP3 and FLAC audio containers are supported.");
        if (end <= start) throw new InvalidDataException("No audio payload.");
        stream.Position = start; using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        if (codecHeader is not null) hash.AppendData(codecHeader);
        var buffer = new byte[81920]; var remaining = end - start;
        while (remaining > 0)
        { var count = stream.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining)); if (count == 0) throw new EndOfStreamException(); hash.AppendData(buffer.AsSpan(0, count)); remaining -= count; }
        return new(end - start, Convert.ToHexStringLower(hash.GetHashAndReset()));
    }
}
