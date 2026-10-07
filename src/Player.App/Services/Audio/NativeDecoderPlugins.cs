using ManagedBass;
using Player.Core.Playback;

namespace Player.App.Services.Audio;

public sealed class NativeDecoderPlugins : IDisposable
{
    private readonly List<int> _handles = [];
    public Dictionary<string, string> Errors { get; } = [];
    public NativeDecoderPlugins()
    {
        try
        {
            if (!Bass.Configure(Configuration.MFDisable, true))
                throw new AudioBackendException(AudioErrorCategory.Dependency, "Could not disable optional system-codec fallback.", (int)Bass.LastError);
            foreach (var pair in NativeLibraryBootstrap.DecoderValidationErrors) Errors[pair.Key] = pair.Value;
            foreach (var path in NativeLibraryBootstrap.DecoderPaths)
            {
                try { NativeLibraryBootstrap.PrepareDecoder(path); }
                catch (Exception error) when (error is DllNotFoundException or BadImageFormatException)
                { Errors[System.IO.Path.GetFileName(path)] = error.Message; continue; }
                var handle = Bass.PluginLoad(path);
                if (handle == 0)
                {
                    var error = Bass.LastError;
                    Errors[System.IO.Path.GetFileName(path)] = $"{error} ({(int)error})";
                    continue;
                }
                _handles.Add(handle);
            }
        }
        catch { Dispose(); throw; }
    }
    public void Dispose()
    {
        for (var i = _handles.Count - 1; i >= 0; i--)
        {
            if (!Bass.PluginFree(_handles[i]))
                throw new AudioBackendException(AudioErrorCategory.Dependency, "Decoder unload failed.", (int)Bass.LastError);
            _handles.RemoveAt(i);
        }
    }
}
