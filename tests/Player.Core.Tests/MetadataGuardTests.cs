using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Player.App.Services.Storage;
using Player.Core.Library;
using Player.Core.Media;
using Player.Core.Playback;

namespace Player.Core.Tests;

public sealed class MetadataGuardTests : IDisposable
{
    private readonly string _directory = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "player-metadata-" + Guid.NewGuid().ToString("N"))).FullName;
    [Theory]
    [InlineData("id3")]
    [InlineData("ape")]
    [InlineData("flac")]
    [InlineData("mp4")]
    [InlineData("asf")]
    public void OversizedDeclaredTagsAreRejectedBeforePayloadAllocationAndSourcesRemainUnchanged(string kind)
    {
        var path = Path.Combine(_directory, kind == "mp4" ? "owned.m4a" : "owned.bin");
        using (var file = File.Create(path)) using (var writer = new BinaryWriter(file))
        {
            switch (kind)
            {
                case "id3": writer.Write("ID3"u8); writer.Write(new byte[] { 4, 0, 0, 0x20, 0, 0, 0 }); break;
                case "ape": writer.Write("APETAGEX"u8); writer.Write(2000u); writer.Write(uint.MaxValue); writer.Write(new byte[16]); break;
                case "flac": writer.Write("fLaC"u8); writer.Write(new byte[] { 0x84, 255, 255, 255 }); break;
                case "mp4": writer.Write(new byte[] { 0, 0, 0, 1 }); writer.Write("moov"u8); writer.Write(ulong.MaxValue); break;
                case "asf": writer.Write(new Guid("75b22630-668e-11cf-a6d9-00aa0062ce6c").ToByteArray()); writer.Write(ulong.MaxValue); writer.Write(new byte[8]); break;
            }
        }
        var before = SHA256.HashData(File.ReadAllBytes(path));
        Assert.Throws<InvalidDataException>(() => MetadataReadGuard.Validate(path));
        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None)) { }
        Assert.Equal(before, SHA256.HashData(File.ReadAllBytes(path)));
    }
    [Fact]
    public void FlacLimitAppliesToCombinedBlocksNotOnlyOneDeclaredBlock()
    {
        var path = Path.Combine(_directory, "owned.flac");
        using (var file = File.Create(path))
        {
            file.Write("fLaC"u8);
            for (var i = 0; i < 3; i++) { file.Write([i == 2 ? (byte)0x81 : (byte)1, 255, 255, 255]); file.Position += 0xffffff; }
            file.SetLength(file.Position);
        }
        Assert.Throws<InvalidDataException>(() => MetadataReadGuard.Validate(path));
    }
    [Fact]
    public void LargeRf64AudioAndMp4MdatAreSkippedUsing64BitOffsets()
    {
        const long dataBytes = (1L << 32) + 4096;
        var rf64 = Path.Combine(_directory, "owned.wav");
        using (var file = File.Create(rf64)) using (var writer = new BinaryWriter(file))
        {
            MakeSparse(file);
            writer.Write("RF64"u8); writer.Write(uint.MaxValue); writer.Write("WAVEds64"u8); writer.Write(28u);
            writer.Write((ulong)(dataBytes + 72)); writer.Write((ulong)dataBytes); writer.Write((ulong)(dataBytes / 4)); writer.Write(0u);
            writer.Write("fmt "u8); writer.Write(16u); writer.Write(new byte[16]); writer.Write("data"u8); writer.Write(uint.MaxValue); file.SetLength(dataBytes + 80);
        }
        MetadataReadGuard.Validate(rf64);
        var mp4 = Path.Combine(_directory, "owned.m4a");
        using (var file = File.Create(mp4))
        {
            MakeSparse(file);
            file.Write([0,0,0,1]); file.Write("mdat"u8); Span<byte> size = stackalloc byte[8]; System.Buffers.Binary.BinaryPrimitives.WriteUInt64BigEndian(size, (ulong)(dataBytes + 16)); file.Write(size); file.SetLength(dataBytes + 16);
        }
        MetadataReadGuard.Validate(mp4);
    }
    [Fact]
    public void OwnedCommittedFormatCorpusRemainsEligibleForMetadataParsing()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "global.json"))) root = root.Parent;
        Assert.NotNull(root);
        foreach (var name in new[] { "audio", "audio-extended" })
        {
            var directory = Path.Combine(root.FullName, "tests", "fixtures", name);
            using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "manifest.json")));
            foreach (var fixture in manifest.RootElement.GetProperty("fixtures").EnumerateArray()) MetadataReadGuard.Validate(Path.Combine(directory, fixture.GetProperty("path").GetString()!));
        }
    }
    [Fact]
    public async Task InvalidSavedTrackIsRejectedBeforeAccumulatingOrDeserializingLaterRows()
    {
        var database = Path.Combine(_directory, "library.db"); var track = new MediaTrack(Guid.NewGuid(), @"C:\Music\owned.wav", "Owned");
        var other = track with { Id = Guid.NewGuid() }; var tab = Guid.NewGuid();
        var state = new LibraryState([new(tab, "Owned", [new(Guid.NewGuid(), track), new(Guid.NewGuid(), other)])], new(tab, null, null, 0));
        await using (var store = new SqlitePlayerStore(database)) { await store.LoadAsync(); await store.SaveAsync(state, true); }
        var invalid = JsonSerializer.Serialize(track with { Title = new string('x', 5000) });
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = database, Pooling = false }.ToString()); connection.Open();
        using var command = connection.CreateCommand(); command.CommandText = "UPDATE Tracks SET Json=$json WHERE Id=$id; UPDATE Tracks SET Json='{' WHERE Id=$other";
        command.Parameters.AddWithValue("$json", invalid); command.Parameters.AddWithValue("$id", track.Id.ToString()); command.Parameters.AddWithValue("$other", other.Id.ToString()); command.ExecuteNonQuery();
        await using (var store = new SqlitePlayerStore(database)) await Assert.ThrowsAsync<InvalidDataException>(() => store.LoadAsync());
        command.CommandText = "SELECT Json FROM Tracks WHERE Id=$id"; Assert.Equal(invalid, command.ExecuteScalar());
        command.CommandText = "SELECT COUNT(*) FROM PlaylistEntries"; Assert.Equal(2L, command.ExecuteScalar());
    }
    public void Dispose() => Directory.Delete(_directory, true);
    private static void MakeSparse(FileStream file)
    {
        if (OperatingSystem.IsWindows()) Assert.True(DeviceIoControl(file.SafeFileHandle, 0x900c4, 0, 0, 0, 0, out _, 0));
    }
    [System.Runtime.InteropServices.DefaultDllImportSearchPaths(System.Runtime.InteropServices.DllImportSearchPath.System32)]
    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(Microsoft.Win32.SafeHandles.SafeFileHandle file, uint code, nint input, uint inputBytes, nint output, uint outputBytes, out uint returned, nint overlapped);
}
