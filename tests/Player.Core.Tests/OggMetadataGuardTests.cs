using System.Buffers.Binary;
using Player.Core.Media;

namespace Player.Core.Tests;

public sealed class OggMetadataGuardTests : IDisposable
{
    private readonly string _directory = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "player-ogg-guard-" + Guid.NewGuid().ToString("N"))).FullName;
    [Theory]
    [InlineData("unknown-stream")]
    [InlineData("duplicate-beginning")]
    [InlineData("version")]
    [InlineData("flags")]
    public void InvalidOggHeaderStructureIsRejectedBeforeTagLib(string damage)
    {
        var path = Path.Combine(_directory, "owned.ogg");
        var first = Page(2, 7); var second = Page(0, 7);
        switch (damage)
        {
            case "unknown-stream": BinaryPrimitives.WriteUInt32LittleEndian(second.AsSpan(14), 8); break;
            case "duplicate-beginning": second[5] = 2; break;
            case "version": first[4] = 255; break;
            case "flags": first[5] = 128; break;
        }
        var bytes = first.Concat(second).Concat(Page(4, 7)).ToArray(); File.WriteAllBytes(path, bytes);
        Assert.Throws<InvalidDataException>(() => MetadataReadGuard.Validate(path));
        Assert.Equal(bytes, File.ReadAllBytes(path));
    }
    [Fact]
    public void MultipleLogicalStreamsWithTheirOwnBeginningsRemainEligible()
    {
        var path = Path.Combine(_directory, "owned.ogg");
        File.WriteAllBytes(path, [.. Page(2, 7), .. Page(2, 8), .. Page(0, 7)]);
        MetadataReadGuard.Validate(path); // Eligibility only: this header fixture is not real decodable audio.
    }
    private static byte[] Page(byte flags, uint serial)
    {
        var page = new byte[29]; "OggS"u8.CopyTo(page); page[5] = flags;
        BinaryPrimitives.WriteUInt32LittleEndian(page.AsSpan(14), serial);
        page[26] = 1; page[27] = 1; page[28] = 0;
        return page;
    }
    public void Dispose() => Directory.Delete(_directory, true);
}
