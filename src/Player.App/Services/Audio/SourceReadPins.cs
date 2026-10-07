using System.Buffers;
using System.IO;
using System.Runtime.InteropServices;
using ManagedBass;

namespace Player.App.Services.Audio;

/// <summary>Own the exact main/correction inputs. WavPack callbacks never reopen a sidecar pathname.</summary>
internal sealed class SourceReadPins : IDisposable
{
    public LocalReadLease Main { get; }
    private LocalReadLease? Correction { get; }
    private readonly FileProcedures _procedures;
    private readonly object _mainGate = new(), _correctionGate = new();
    private bool _disposed;
    private int _primaryClosed, _correctionClosed, _correctionUsed;
    internal Action? FileClosed { get; set; }
    public bool UsesCallbacks { get; }
    public bool InputsClosed => Volatile.Read(ref _primaryClosed) != 0 && (Volatile.Read(ref _correctionUsed) == 0 || Volatile.Read(ref _correctionClosed) != 0);
    public string CorrectionSignature => !UsesCallbacks ? "" : Correction is null ? "wvc:absent" :
        "wvc:" + Correction.Path.ToUpperInvariant() + "|" + Correction.Stream.Length + "|" + File.GetLastWriteTimeUtc(Correction.Stream.SafeFileHandle).Ticks;

    private SourceReadPins(LocalReadLease main, LocalReadLease? correction, bool wavPack)
    {
        Main = main; Correction = correction; UsesCallbacks = wavPack;
        _procedures = new FileProcedures { Close = CloseInput, Length = Length, Read = Read, Seek = Seek };
    }
    public static SourceReadPins Open(string path)
    {
        var main = LocalReadLease.Open(path);
        try
        {
            var sidecar = Path.ChangeExtension(main.Path, ".wvc");
            Span<byte> header = stackalloc byte[4];
            var count = main.Stream.Read(header); main.Stream.Position = 0;
            // Plugins recognize contents, so a renamed WavPack input needs the same correction policy.
            var wavPack = Path.GetExtension(main.Path).Equals(".wv", StringComparison.OrdinalIgnoreCase) ||
                count == 4 && header.SequenceEqual("wvpk"u8);
            return new(main, wavPack && (new FileInfo(sidecar).LinkTarget is not null || File.Exists(sidecar)) ? LocalReadLease.Open(sidecar) : null, wavPack);
        }
        catch { main.Dispose(); throw; }
    }
    public int CreateDecoder()
    {
        var flags = BassFlags.Decode | BassFlags.Float | BassFlags.Prescan;
        if (!UsesCallbacks) return Bass.CreateStream(Main.Path, 0, 0, flags);
        var create = Marshal.GetDelegateForFunctionPointer<WavPackCreate>(NativeLibraryBootstrap.DecoderExport("basswv", "BASS_WV_StreamCreateFileUserEx"));
        // The pinned vendor header uses one callback table with separate primary/correction user values.
        return create((uint)StreamSystem.NoBuffer, (uint)flags, _procedures, 0, Correction is null ? 0 : 1);
    }
    private FileStream? Input(nint user)
    {
        if (user == 0) return Main.Stream;
        Interlocked.Exchange(ref _correctionUsed, 1); return Correction?.Stream;
    }
    private long Length(nint user)
    { try { return Input(user)?.Length ?? 0; } catch (Exception) { return 0; } }
    private bool Seek(long offset, nint user)
    {
        try
        {
            var file = Input(user); if (file is null || offset < 0 || offset > file.Length) return false;
            lock (user == 0 ? _mainGate : _correctionGate) file.Position = offset;
            return true;
        }
        catch (Exception) { return false; }
    }
    private int Read(nint buffer, int bytes, nint user)
    {
        if (bytes <= 0) return 0;
        byte[]? rented = null;
        try
        {
            rented = ArrayPool<byte>.Shared.Rent(Math.Min(bytes, 65536));
            var file = Input(user); if (file is null) return 0;
            lock (user == 0 ? _mainGate : _correctionGate)
            {
                var copied = 0;
                while (copied < bytes)
                {
                    var count = file.Read(rented, 0, Math.Min(rented.Length, bytes - copied));
                    if (count == 0) break;
                    Marshal.Copy(rented, 0, buffer + copied, count); copied += count;
                }
                return copied;
            }
        }
        catch (Exception) { return 0; } // Never unwind a managed I/O exception through a native callback.
        finally { if (rented is not null) ArrayPool<byte>.Shared.Return(rented); }
    }
    private void CloseInput(nint user)
    {
        if (user == 0) Interlocked.Exchange(ref _primaryClosed, 1); else Interlocked.Exchange(ref _correctionClosed, 1);
        FileClosed?.Invoke();
    }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true; FileClosed = null;
        Correction?.Dispose(); Main.Dispose();
    }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int WavPackCreate(uint system, uint flags, [In] FileProcedures procedures, nint user, nint correctionUser);
}
