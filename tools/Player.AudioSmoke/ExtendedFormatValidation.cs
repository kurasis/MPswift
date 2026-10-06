using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using ManagedBass;
using Player.App.Services.Audio;

namespace Player.AudioSmoke;

/// <summary>Owned format checks. No endpoint/device and no whole-file allocation for RF64.</summary>
public static class ExtendedFormatValidation
{
    public static float ComparePcm(string source, string reference)
    {
        using var context = new NativeDecodeContext();
        var first = Bass.CreateStream(source, 0, 0, BassFlags.Decode | BassFlags.Float);
        var second = Bass.CreateStream(reference, 0, 0, BassFlags.Decode | BassFlags.Float);
        try
        {
            Check(first != 0 && second != 0, "PCM comparison streams: " + Bass.LastError);
            Check(Bass.ChannelGetLength(first) == Bass.ChannelGetLength(second), "Lossless decoded lengths differ.");
            float maximum = 0; var a = new float[8192]; var b = new float[8192];
            while (true)
            {
                var read = Bass.ChannelGetData(first, a, a.Length * 4);
                var other = Bass.ChannelGetData(second, b, b.Length * 4);
                Check(read == other, "Lossless decoded block lengths differ.");
                if (read < 0) { Check(Bass.LastError == Errors.Ended, "PCM comparison did not end normally."); break; }
                Check(read > 0, "PCM comparison stalled.");
                for (var i = 0; i < read / 4; i++) { Check(float.IsFinite(a[i]) && float.IsFinite(b[i]), "Invalid PCM."); maximum = Math.Max(maximum, Math.Abs(a[i] - b[i])); }
            }
            return maximum;
        }
        finally { if (first != 0) Bass.StreamFree(first); if (second != 0) Bass.StreamFree(second); }
    }

    public static object Run(string directory)
    {
        directory = Path.GetFullPath(directory);
        if (Directory.Exists(directory)) throw new IOException("Extended checks require a new owned directory.");
        Directory.CreateDirectory(directory);
        using var context = new NativeDecodeContext();
        var lossless = EncodeWma(directory, false);
        var pro = EncodeWma(directory, true);
        var rf64 = LargeRf64(directory);
        return new { Status = "extended-formats-passed", WmaLossless = lossless, WmaPro = pro, LargeRf64 = rf64,
            Windows = RuntimeInformation.OSDescription, WindowsN = "not-run: hosted Server is not Windows N", DeviceOutput = "not-run" };
    }

    private static unsafe object EncodeWma(string directory, bool pro)
    {
        var rate = pro ? 96000 : 48000; var channels = pro ? 6 : 2;
        var samples = new short[rate * channels * 3];
        for (var frame = 0; frame < rate * 3; frame++)
            for (var channel = 0; channel < channels; channel++)
                samples[frame * channels + channel] = (short)Math.Round(1638 * Math.Sin(2 * Math.PI * 440 * frame / rate) * (channel % 2 == 0 ? 1 : -1));
        var path = Path.Combine(directory, pro ? "owned-pro.wma" : "owned-lossless.wma");
        uint flags = pro ? 0x4000u : 0;
        var pointer = BASS_WMA_EncodeGetRates((uint)rate, (uint)channels, flags | (pro ? 0u : 0x10000u));
        Check(pointer != 0, "WMA encoder rates unavailable: " + Bass.LastError);
        var rates = new List<uint>();
        for (var i = 0; i < 256; i++) { var value = unchecked((uint)Marshal.ReadInt32(pointer, i * 4)); if (value == 0) break; rates.Add(value); }
        var bitrate = pro ? rates.Where(r => r >= 192000).DefaultIfEmpty(0u).Min() : 100u;
        Check(pro ? bitrate > 0 : rates.Contains(100), "Required WMA encoder profile unavailable.");
        var encoder = BASS_WMA_EncodeOpenFile((uint)rate, (uint)channels, flags | 0x80000000u, bitrate, path);
        Check(encoder != 0, "WMA encode open failed: " + Bass.LastError);
        try { fixed (short* data = samples) Check(BASS_WMA_EncodeWrite(encoder, (nint)data, (uint)(samples.Length * 2)), "WMA encode write failed."); }
        finally { Check(BASS_WMA_EncodeClose(encoder), "WMA encode close failed."); }
        // Read the ASF Stream Properties WAVEFORMATEX tag independently of the requested encoder flags.
        var bytes = File.ReadAllBytes(path); var guid = new Guid("b7dc0791-a9b7-11cf-8ee6-00c00c205365").ToByteArray();
        var index = bytes.AsSpan().IndexOf(guid);
        Check(index >= 0 && index + 80 <= bytes.Length, "Missing ASF stream properties.");
        var codecTag = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(index + 78, 2));
        Check(codecTag == (pro ? 0x162 : 0x163), "WMA ASF codec tag differs: " + codecTag);
        DecodeEvidence decode; using (var session = new BassSmokeSession()) decode = session.Decode(path);
        Check(decode.SampleRate == rate && decode.Channels == channels && Math.Abs(decode.DurationSeconds - 3) <= 0.2 && decode.Peak is > 0.01f and < 0.2f, "WMA decode profile differs.");
        float error = 0;
        if (!pro)
        {
            var stream = Bass.CreateStream(path, 0, 0, BassFlags.Decode | BassFlags.Float);
            Check(stream != 0, "WMA lossless comparison open failed.");
            try
            {
                var buffer = new float[8192]; var offset = 0;
                while (true)
                {
                    var read = Bass.ChannelGetData(stream, buffer, buffer.Length * 4);
                    if (read < 0) { Check(Bass.LastError == Errors.Ended, "WMA lossless comparison read failed."); break; }
                    Check(read > 0 && offset + read / 4 <= samples.Length, "WMA lossless sample count differs.");
                    for (var i = 0; i < read / 4; i++) error = Math.Max(error, Math.Abs(buffer[i] - samples[offset++] / 32768f));
                }
                Check(offset == samples.Length && error == 0, "WMA lossless PCM is not exact.");
            }
            finally { Bass.StreamFree(stream); }
        }
        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None)) { }
        Check(bytes.AsSpan().SequenceEqual(File.ReadAllBytes(path)), "WMA fixture changed.");
        return new { CodecTag = $"0x{codecTag:x4}", EncoderRates = rates, BitrateOrQuality = bitrate, Decode = decode, ExactLosslessMaximumError = pro ? (float?)null : error, SourceUnchanged = true, SourceHandleReleased = true };
    }

    private static object LargeRf64(string directory)
    {
        const long dataLength = (1L << 32) + 192000;
        var path = Path.Combine(directory, "owned-over-4gib.wav");
        var offsets = new[] { 0L, 1L << 31, (1L << 32) + 1024, dataLength - 16384 };
        var pcm = new byte[16384];
        for (var i = 0; i < pcm.Length / 2; i++) BinaryPrimitives.WriteInt16LittleEndian(pcm.AsSpan(i * 2, 2), (short)(i % 2 == 0 ? 1638 : -1638));
        using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
        {
            Check(DeviceIoControl(output.SafeFileHandle, 0x900c4, nint.Zero, 0, nint.Zero, 0, out _, nint.Zero), "NTFS sparse flag failed.");
            using var writer = new BinaryWriter(output, System.Text.Encoding.ASCII, true);
            writer.Write("RF64"u8); writer.Write(uint.MaxValue); writer.Write("WAVEds64"u8); writer.Write(28u);
            writer.Write((ulong)(dataLength + 72)); writer.Write((ulong)dataLength); writer.Write((ulong)(dataLength / 4)); writer.Write(0u);
            writer.Write("fmt "u8); writer.Write(16u); writer.Write((ushort)1); writer.Write((ushort)2); writer.Write(48000u); writer.Write(192000u); writer.Write((ushort)4); writer.Write((ushort)16);
            writer.Write("data"u8); writer.Write(uint.MaxValue); writer.Flush();
            output.SetLength(dataLength + 80);
            foreach (var offset in offsets) { output.Position = 80 + offset; output.Write(pcm); }
            output.Flush(true);
        }
        var stream = Bass.CreateStream(path, 0, 0, BassFlags.Decode | BassFlags.Float);
        Check(stream != 0, "Large RF64 open failed: " + Bass.LastError);
        long readBytes = 0;
        try
        {
            Check(Bass.ChannelGetLength(stream) == dataLength * 2, "RF64 decoded length truncated at 32 bits.");
            var buffer = new float[8192];
            foreach (var offset in offsets)
            {
                Check(Bass.ChannelSetPosition(stream, offset * 2), "RF64 64-bit seek failed.");
                var read = Bass.ChannelGetData(stream, buffer, buffer.Length * 4); readBytes += Math.Max(0, read);
                Check(read == buffer.Length * 4, "RF64 range decode length differs.");
                for (var i = 0; i < buffer.Length; i++) Check(buffer[i] == (i % 2 == 0 ? 1638 : -1638) / 32768f, "RF64 seek read the wrong range.");
            }
            Check(Bass.ChannelGetData(stream, buffer, buffer.Length * 4) < 0 && Bass.LastError == Errors.Ended, "RF64 final range did not reach end.");
        }
        finally { Bass.StreamFree(stream); }
        using (var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Check(source.Length == dataLength + 80, "RF64 source length changed."); var buffer = new byte[pcm.Length];
            foreach (var offset in offsets) { source.Position = 80 + offset; source.ReadExactly(buffer); Check(buffer.AsSpan().SequenceEqual(pcm), "RF64 source range changed."); }
        }
        File.Delete(path);
        return new { FileBytes = dataLength + 80, DataBytes = dataLength, SparseOwnedNtfsFile = true, CheckedDataOffsets = offsets, DecodedBytesRead = readBytes,
            BufferBytes = 32768, SourcePreservation = "Exclusive reopen, length and all written signal ranges; not a whole-file hash", BoundaryAndEndSeek = true };
    }

    private static void Check(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
    [DllImport("basswma.dll")] private static extern nint BASS_WMA_EncodeGetRates(uint frequency, uint channels, uint flags);
    [DllImport("basswma.dll", CharSet = CharSet.Unicode)] private static extern uint BASS_WMA_EncodeOpenFile(uint frequency, uint channels, uint flags, uint bitrate, string file);
    [DllImport("basswma.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool BASS_WMA_EncodeWrite(uint handle, nint buffer, uint length);
    [DllImport("basswma.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool BASS_WMA_EncodeClose(uint handle);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeviceIoControl(SafeFileHandle file, uint code, nint input, uint inputBytes, nint output, uint outputBytes, out uint returned, nint overlapped);
}
