using System.Buffers.Binary;
using System.IO;

namespace Player.App.Services.Storage;

internal static class BackupZipDirectory
{
    // Inspect only a bounded tail and three fixed headers before the framework allocates entry objects.
    public static void Validate(Stream file)
    {
        if (file.Length < 22) throw new InvalidDataException("Backup ZIP directory is missing.");
        var tail = new byte[(int)Math.Min(file.Length, 65557)];
        file.Position = file.Length - tail.Length; file.ReadExactly(tail);
        var end = -1;
        for (var i = tail.Length - 22; i >= 0; i--)
            if (BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(i)) == 0x06054b50 &&
                i + 22 + BinaryPrimitives.ReadUInt16LittleEndian(tail.AsSpan(i + 20)) == tail.Length)
            { end = i; break; }
        if (end < 0) throw new InvalidDataException("Backup ZIP directory is invalid.");
        var record = tail.AsSpan(end);
        if (BinaryPrimitives.ReadUInt16LittleEndian(record[4..]) != 0 || BinaryPrimitives.ReadUInt16LittleEndian(record[6..]) != 0)
            throw new InvalidDataException("Backup ZIP must use one disk.");
        ulong count = BinaryPrimitives.ReadUInt16LittleEndian(record[10..]);
        ulong diskCount = BinaryPrimitives.ReadUInt16LittleEndian(record[8..]);
        ulong size = BinaryPrimitives.ReadUInt32LittleEndian(record[12..]);
        ulong offset = BinaryPrimitives.ReadUInt32LittleEndian(record[16..]);
        var endPosition = file.Length - tail.Length + end;
        if (end >= 20 && BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(end - 20)) == 0x07064b50)
        {
            var locator = tail.AsSpan(end - 20, 20);
            var position = BinaryPrimitives.ReadUInt64LittleEndian(locator[8..]);
            if (endPosition < 76 || BinaryPrimitives.ReadUInt32LittleEndian(locator[4..]) != 0 || BinaryPrimitives.ReadUInt32LittleEndian(locator[16..]) != 1 || position > (ulong)endPosition - 76)
                throw new InvalidDataException("Invalid ZIP64 backup locator.");
            file.Position = (long)position;
            Span<byte> extended = stackalloc byte[56]; file.ReadExactly(extended);
            var recordSize = BinaryPrimitives.ReadUInt64LittleEndian(extended[4..]);
            if (BinaryPrimitives.ReadUInt32LittleEndian(extended) != 0x06064b50 || recordSize is < 44 or > 65536 ||
                position + 12 + recordSize != (ulong)endPosition - 20 ||
                BinaryPrimitives.ReadUInt32LittleEndian(extended[16..]) != 0 || BinaryPrimitives.ReadUInt32LittleEndian(extended[20..]) != 0)
                throw new InvalidDataException("Invalid ZIP64 backup directory.");
            diskCount = BinaryPrimitives.ReadUInt64LittleEndian(extended[24..]); count = BinaryPrimitives.ReadUInt64LittleEndian(extended[32..]);
            size = BinaryPrimitives.ReadUInt64LittleEndian(extended[40..]); offset = BinaryPrimitives.ReadUInt64LittleEndian(extended[48..]);
            endPosition = (long)position;
        }
        if (count != 3 || diskCount != 3) throw new InvalidDataException("Backup ZIP must contain exactly three entries.");
        if (size is < 138 or > 524288 || offset > (ulong)endPosition || size != (ulong)endPosition - offset)
            throw new InvalidDataException("Backup ZIP directory exceeds its bound or has inconsistent offsets.");
        file.Position = (long)offset;
        Span<byte> header = stackalloc byte[46];
        for (var i = 0; i < 3; i++)
        {
            if (file.Position + header.Length > endPosition) throw new InvalidDataException("Backup ZIP directory is truncated.");
            file.ReadExactly(header);
            if (BinaryPrimitives.ReadUInt32LittleEndian(header) != 0x02014b50)
                throw new InvalidDataException("Backup ZIP entry header is invalid.");
            var variableBytes = (long)BinaryPrimitives.ReadUInt16LittleEndian(header[28..]) +
                BinaryPrimitives.ReadUInt16LittleEndian(header[30..]) + BinaryPrimitives.ReadUInt16LittleEndian(header[32..]);
            if (file.Position + variableBytes > endPosition) throw new InvalidDataException("Backup ZIP entry exceeds the directory.");
            file.Position += variableBytes;
        }
        if (file.Position != endPosition) throw new InvalidDataException("Backup ZIP directory contains undeclared entries.");
        file.Position = 0;
    }
}
