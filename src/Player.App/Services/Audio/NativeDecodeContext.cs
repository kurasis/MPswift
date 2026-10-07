using ManagedBass;
using Player.Core.Playback;

namespace Player.App.Services.Audio;

/// <summary>Process-wide BASS device/plugins, thread-local context, independently owned streams.</summary>
public sealed class NativeDecodeContext : IDisposable
{
    private static readonly object Gate = new();
    private static int _references;
    private static NativeDecoderPlugins? _plugins;
    private bool _disposed;
    public IReadOnlyDictionary<string, string> DecoderErrors => _plugins!.Errors;
    public NativeDecodeContext()
    {
        lock (Gate)
        {
            NativeLibraryBootstrap.LoadAndVerify();
            if (_references == 0)
            {
                if (!Bass.Init(0)) throw new AudioBackendException(AudioErrorCategory.Dependency, "BASS_Init failed.", (int)Bass.LastError);
                try { _plugins = new NativeDecoderPlugins(); }
                catch { Bass.Free(); throw; }
            }
            else Bass.CurrentDevice = 0;
            _references++;
        }
    }
    public void Dispose()
    {
        lock (Gate)
        {
            if (_disposed) return;
            Bass.CurrentDevice = 0;
            if (_references == 1)
            {
                _plugins!.Dispose();
                if (!Bass.Free()) throw new AudioBackendException(AudioErrorCategory.Dependency, "BASS_Free failed.", (int)Bass.LastError);
                NativeStreamPins.ReleaseAfterEngineFree();
                _plugins = null;
            }
            _references--; _disposed = true;
        }
    }
}
