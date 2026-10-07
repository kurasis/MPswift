using System.Text.Json;
using Player.App.Services.Storage;
using Player.Core.Library;
using Player.Core.Playback;

namespace Player.Core.Tests;

public sealed class SessionJsonTests
{
    [Fact]
    public void StreamingSessionMatchesExistingJsonIncludingUnicodeEscapesAndExactCharacterBoundary()
    {
        var track = new MediaTrack(Guid.NewGuid(), @"C:\Owned\Музыка.flac", "Русский 😀 \"quoted\" " + new string('я', 4096));
        var session = new SessionState(null, null, new(Guid.NewGuid(), track), 17);
        var expected = JsonSerializer.Serialize(session);
        Assert.Equal(expected, SessionJson.Serialize(session, expected.Length));
        Assert.Throws<InvalidDataException>(() => SessionJson.Serialize(session, expected.Length - 1));
    }
    [Fact]
    public void LargeSharedMetadataQueueIsRejectedBeforeSerializingTheEntireInput()
    {
        var track = new MediaTrack(Guid.NewGuid(), @"C:\Owned\song.flac", new string('x', 4096), Artist: new string('y', 4096));
        var queue = Enumerable.Range(0, 10000).Select(_ => new QueueItem(Guid.NewGuid(), new(Guid.NewGuid(), track), null)).ToArray();
        var session = new SessionState(null, null, null, 0, new(RepeatMode.Off, false, queue, [], [], -1, null));
        var before = GC.GetAllocatedBytesForCurrentThread();
        Assert.Throws<InvalidDataException>(() => SessionJson.Serialize(session));
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        // Default JSON is over 80 MiB here. Streaming must stop at the existing 16 Mi-character bound.
        Assert.True(allocated < 96L * 1024 * 1024, $"Oversized session serialization allocated {allocated} bytes.");
    }
}
