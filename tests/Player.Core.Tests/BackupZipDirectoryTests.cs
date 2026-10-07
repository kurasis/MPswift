using System.Buffers.Binary;
using System.IO.Compression;
using Player.App.Services.Storage;

namespace Player.Core.Tests;

public sealed class BackupZipDirectoryTests
{
    private static byte[] Create(int count)
    {
        using var file = new MemoryStream();
        using (var zip = new ZipArchive(file, ZipArchiveMode.Create, true))
            for (var i = 0; i < count; i++) zip.CreateEntry("owned-" + i);
        return file.ToArray();
    }
    [Fact]
    public void ExcessiveEntryCountIsRefusedAfterOnlyTheBoundedTail()
    {
        using var file = new CountedStream(Create(10000));
        Assert.Throws<InvalidDataException>(() => BackupZipDirectory.Validate(file));
        Assert.Equal(65557, file.ReadBytes);
    }
    [Fact]
    public void FalseEntryCountCannotHideAdditionalCentralDirectoryHeaders()
    {
        var bytes = Create(4);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(bytes.Length - 22 + 8), 3);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(bytes.Length - 22 + 10), 3);
        using var file = new MemoryStream(bytes);
        Assert.Throws<InvalidDataException>(() => BackupZipDirectory.Validate(file));
    }
    [Fact]
    public void ValidThreeEntryDirectoryResetsTheStreamForTheFrameworkReader()
    {
        using var file = new MemoryStream(Create(3)); BackupZipDirectory.Validate(file);
        Assert.Equal(0, file.Position);
        using var zip = new ZipArchive(file, ZipArchiveMode.Read); Assert.Equal(3, zip.Entries.Count);
    }
    [Fact]
    public void ValidZip64DirectoryRetainsCompatibility()
    {
        var bytes = Create(3); var end = bytes.Length - 22;
        var offset = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(end + 16));
        var size = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(end + 12));
        using var file = new MemoryStream(); file.Write(bytes.AsSpan(0, end));
        var extended = new byte[56]; BinaryPrimitives.WriteUInt32LittleEndian(extended, 0x06064b50);
        BinaryPrimitives.WriteUInt64LittleEndian(extended.AsSpan(4), 44);
        BinaryPrimitives.WriteUInt64LittleEndian(extended.AsSpan(24), 3); BinaryPrimitives.WriteUInt64LittleEndian(extended.AsSpan(32), 3);
        BinaryPrimitives.WriteUInt64LittleEndian(extended.AsSpan(40), size); BinaryPrimitives.WriteUInt64LittleEndian(extended.AsSpan(48), offset);
        file.Write(extended);
        var locator = new byte[20]; BinaryPrimitives.WriteUInt32LittleEndian(locator, 0x07064b50);
        BinaryPrimitives.WriteUInt64LittleEndian(locator.AsSpan(8), (ulong)end); BinaryPrimitives.WriteUInt32LittleEndian(locator.AsSpan(16), 1); file.Write(locator);
        var ordinary = bytes.AsSpan(end).ToArray(); ordinary.AsSpan(8, 4).Fill(255); ordinary.AsSpan(12, 8).Fill(255); file.Write(ordinary);
        BackupZipDirectory.Validate(file);
        using var zip = new ZipArchive(file, ZipArchiveMode.Read); Assert.Equal(3, zip.Entries.Count);
    }
    private sealed class CountedStream(byte[] bytes) : MemoryStream(bytes)
    {
        public long ReadBytes { get; private set; }
        public override int Read(Span<byte> buffer) { var count = base.Read(buffer); ReadBytes += count; return count; }
    }
}
