using Player.Core.Diagnostics;

namespace Player.Core.Tests;

public sealed class AcceptanceSignalTests
{
    [Fact] public void OwnedWaveHasCorrectRiffDimensionsAndDeterministicNonSilentPcm()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "owned.wav"); AcceptanceSignal.WriteWave(path, 48000, 2, 1);
            using var reader = new BinaryReader(File.OpenRead(path));
            Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(reader.ReadBytes(4))); Assert.Equal(192036, reader.ReadInt32());
            Assert.Equal("WAVEfmt ", System.Text.Encoding.ASCII.GetString(reader.ReadBytes(8))); Assert.Equal(16, reader.ReadInt32());
            Assert.Equal(1, reader.ReadInt16()); Assert.Equal(2, reader.ReadInt16()); Assert.Equal(48000, reader.ReadInt32()); Assert.Equal(192000, reader.ReadInt32());
            Assert.Equal(4, reader.ReadInt16()); Assert.Equal(16, reader.ReadInt16()); Assert.Equal("data", System.Text.Encoding.ASCII.GetString(reader.ReadBytes(4))); Assert.Equal(192000, reader.ReadInt32());
            var peak = 0;
            for (var frame = 0; frame < 48000; frame++) for (var channel = 0; channel < 2; channel++)
            { var sample = reader.ReadInt16(); Assert.Equal(AcceptanceSignal.Sample(frame, channel, 48000, 997), sample); peak = Math.Max(peak, Math.Abs((int)sample)); }
            Assert.Equal(1000, peak); Assert.Equal(reader.BaseStream.Length, reader.BaseStream.Position);
        }
        finally { Directory.Delete(directory, true); }
    }
    [Fact] public void ExistingSourceIsNeverOverwritten()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".wav"); File.WriteAllBytes(path, [1, 2, 3]);
        try { Assert.Throws<IOException>(() => AcceptanceSignal.WriteWave(path)); Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(path)); }
        finally { File.Delete(path); }
    }
    [Fact] public void InvalidOrExcessiveSignalIsRejectedBeforeFileCreation()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".wav");
        Assert.Throws<ArgumentOutOfRangeException>(() => AcceptanceSignal.WriteWave(path, seconds: 31));
        Assert.Throws<ArgumentOutOfRangeException>(() => AcceptanceSignal.WriteWave(path, frequency: double.NaN)); Assert.False(File.Exists(path));
    }
}
