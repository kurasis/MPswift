using System.IO;
using System.Runtime.InteropServices;
using ManagedBass;
using ManagedBass.Mix;
using ManagedBass.Wasapi;
using Player.Core.Playback;
using PlaybackStateNative = ManagedBass.PlaybackState;

namespace Player.App.Services.Audio;

/// <summary>Owned exclusively by SerializedAudioPlayer's thread. No UI or database access.</summary>
public sealed class BassAudioBackend : IAudioBackend
{
    private readonly WasapiProcedure _render;
    private readonly NativeDecoderPlugins _plugins;
    private int _source;
    private int _mixer;
    private bool _wasapi;
    private bool _running;
    private bool _disposed;
    private int _callbackError;
    private double _volume = 0.5;
    private bool _muted;
    private float _targetGain = 0.5f;
    private float _callbackGain = 0.5f;
    private int _outputChannels = 2;
    private TimeSpan _position;
    private TimeSpan? _duration;
    private AudioFormatInfo? _output;

    public BassAudioBackend()
    {
        _render = Render;
        try
        {
            NativeLibraryBootstrap.LoadAndVerify();
            Check(Bass.Init(0), "BASS_Init", AudioErrorCategory.Dependency);
            try { _plugins = new NativeDecoderPlugins(); }
            catch { Bass.Free(); throw; }
        }
        catch (Exception error) when (error is not AudioBackendException)
        { throw new AudioBackendException(AudioErrorCategory.Dependency, error.Message); }
    }

    public AudioSourceInfo Open(string path)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        CloseSource();
        try { path = BassSmokeSession.ValidateSourcePath(path); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        { throw new AudioBackendException(AudioErrorCategory.FileUnavailable, error.Message); }
        _source = Bass.CreateStream(path, 0, 0, BassFlags.Decode | BassFlags.Float | BassFlags.Prescan);
        if (_source == 0)
        {
            var error = Error("Open decoder", AudioErrorCategory.Decoder);
            throw new AudioBackendException(error.Category, error.Message +
                (_plugins.Errors.Count > 0 ? " Unavailable decoders: " + string.Join("; ", _plugins.Errors.Take(4).Select(p => p.Key + "=" + p.Value)) : ""), error.NativeCode);
        }
        try
        {
            Check(Bass.ChannelGetInfo(_source, out var info), "Read source info", AudioErrorCategory.Decoder);
            var bytes = Bass.ChannelGetLength(_source);
            var seconds = bytes >= 0 ? Bass.ChannelBytes2Seconds(_source, bytes) : -1;
            _duration = double.IsFinite(seconds) && seconds >= 0 ? TimeSpan.FromSeconds(seconds) : null;
            _position = TimeSpan.Zero;
            return new AudioSourceInfo(_duration,
                // BASS marks original float resolution with bit 16; only the low word is bit depth.
                new AudioFormatInfo(info.Frequency, info.Channels, info.ChannelType.ToString(), (info.OriginalResolution & 0xffff) > 0 ? info.OriginalResolution & 0xffff : null),
                _duration > TimeSpan.Zero);
        }
        catch { CloseSource(); throw; }
    }

    public void Play()
    {
        if (_source == 0) throw new AudioBackendException(AudioErrorCategory.Decoder, "No source is loaded.");
        if (_duration is { } duration && _position >= duration) Seek(TimeSpan.Zero);
        EnsureOutput();
        Check(BassWasapi.Start(), "Start shared output", AudioErrorCategory.OutputUnavailable);
        _running = true;
    }

    private void EnsureOutput()
    {
        if (_wasapi) return;
        Check(BassWasapi.Init(-1, 0, 0, WasapiInitFlags.Shared | WasapiInitFlags.Buffer, 0.1f, 0, _render),
            "Open Windows default shared WASAPI endpoint", AudioErrorCategory.OutputUnavailable);
        _wasapi = true;
        try
        {
            Check(BassWasapi.GetInfo(out var info), "Read endpoint mix format", AudioErrorCategory.OutputUnavailable);
            _mixer = BassMix.CreateMixerStream(info.Frequency, info.Channels,
                BassFlags.Decode | BassFlags.Float | BassFlags.MixerNonStop | BassFlags.MixerPositionEx);
            if (_mixer == 0) throw Error("Create output mixer", AudioErrorCategory.OutputUnavailable);
            // Float stereo/multichannel resampling/downmix is performed by BASSmix in the endpoint mix format.
            Check(BassMix.MixerAddChannel(_mixer, _source, BassFlags.MixerChanDownMix), "Attach source", AudioErrorCategory.Decoder);
            _output = new AudioFormatInfo(info.Frequency, info.Channels, "WASAPI shared / float processing");
            _outputChannels = info.Channels;
            ApplyVolume();
        }
        catch { CloseOutput(); throw; }
    }

    public void Pause()
    {
        if (!_running) return;
        try
        {
            var position = ReadPosition().Position;
            if (_running) Check(BassWasapi.Stop(false), "Pause output", AudioErrorCategory.OutputUnavailable);
            _running = false;
            _position = position;
        }
        catch { CloseOutput(); throw; }
    }

    public void Stop()
    {
        if (_running) Check(BassWasapi.Stop(true), "Stop output", AudioErrorCategory.OutputUnavailable);
        else if (_wasapi) CloseOutput(); // Discard retained paused endpoint samples before a new start.
        _running = false;
        if (_source == 0) return;
        Check(_mixer != 0 ? BassMix.ChannelSetPosition(_source, 0) : Bass.ChannelSetPosition(_source, 0),
            "Reset source", AudioErrorCategory.Decoder);
        _position = TimeSpan.Zero;
        Interlocked.Exchange(ref _callbackError, 0);
    }

    public void Seek(TimeSpan position)
    {
        if (_source == 0 || _duration is null) throw new AudioBackendException(AudioErrorCategory.Decoder, "Source is not seekable.");
        var resume = _running;
        // Flush the old endpoint before touching source/mixer state. Graph callbacks are quiescent here.
        CloseOutput();
        var bytes = Bass.ChannelSeconds2Bytes(_source, position.TotalSeconds);
        Check(Bass.ChannelSetPosition(_source, bytes), "Seek source", AudioErrorCategory.Decoder);
        _position = position;
        if (resume) Play();
    }

    public void SetVolume(double volume, bool muted)
    {
        _volume = volume;
        _muted = muted;
        ApplyVolume();
    }

    private void ApplyVolume()
    {
        // BASS channel volume does not affect a decode-only mixer's ChannelGetData.
        // Apply master gain to interleaved float PCM in Render, after mixing/resampling.
        Volatile.Write(ref _targetGain, _muted ? 0 : (float)_volume);
    }

    public BackendPosition ReadPosition()
    {
        if (!_running || !_wasapi || _source == 0) return new BackendPosition(_position, false, _output);
        var error = Interlocked.Exchange(ref _callbackError, 0);
        if (error != 0) throw new AudioBackendException(AudioErrorCategory.OutputUnavailable, "WASAPI render callback failed.", error);
        if (!BassWasapi.IsStarted) throw new AudioBackendException(AudioErrorCategory.OutputUnavailable, "The output endpoint stopped unexpectedly.");
        var delay = BassWasapi.GetData(nint.Zero, (int)DataFlags.Available);
        if (delay < 0) throw Error("Read endpoint latency", AudioErrorCategory.OutputUnavailable);
        var bytes = BassMix.ChannelGetPosition(_source, PositionFlags.Bytes, delay);
        // Initial endpoint fill can precede position-history availability; retain the last reliable position.
        if (bytes >= 0)
        {
            var seconds = Bass.ChannelBytes2Seconds(_source, bytes);
            if (double.IsFinite(seconds)) _position = TimeSpan.FromSeconds(Math.Max(0, seconds));
        }
        var end = _duration is { } duration && _position >= duration && Bass.ChannelIsActive(_source) == PlaybackStateNative.Stopped;
        if (end)
        {
            Check(BassWasapi.Stop(false), "Pause at natural end", AudioErrorCategory.OutputUnavailable);
            _running = false;
            _position = _duration!.Value;
        }
        return new BackendPosition(_position, end, _output);
    }

    private unsafe int Render(nint buffer, int length, nint user)
    {
        try
        {
            var count = Bass.ChannelGetData(_mixer, buffer, length);
            if (count >= 0)
            {
                var samples = new Span<float>(buffer.ToPointer(), count / sizeof(float));
                var frames = samples.Length / _outputChannels;
                var target = Volatile.Read(ref _targetGain);
                var step = frames > 0 ? (target - _callbackGain) / frames : 0;
                for (var frame = 0; frame < frames; frame++)
                {
                    _callbackGain += step;
                    for (var channel = 0; channel < _outputChannels; channel++) samples[frame * _outputChannels + channel] *= _callbackGain;
                }
                _callbackGain = target;
                return count;
            }
            Interlocked.Exchange(ref _callbackError, (int)Bass.LastError);
        }
        catch { Interlocked.Exchange(ref _callbackError, -1); }
        return 0;
    }

    private void CloseOutput()
    {
        if (_wasapi)
        {
            Check(BassWasapi.Free(), "Quiesce/free WASAPI", AudioErrorCategory.OutputUnavailable);
            _wasapi = false;
        }
        _running = false;
        if (_mixer != 0) { Check(Bass.StreamFree(_mixer), "Free mixer", AudioErrorCategory.Dependency); _mixer = 0; }
        _output = null;
    }

    public void CloseSource()
    {
        CloseOutput();
        if (_source != 0) { Check(Bass.StreamFree(_source), "Free decoder", AudioErrorCategory.Dependency); _source = 0; }
        _position = TimeSpan.Zero;
        _duration = null;
        Interlocked.Exchange(ref _callbackError, 0);
    }

    public void Dispose()
    {
        if (_disposed) return;
        CloseSource();
        _plugins.Dispose();
        Check(Bass.Free(), "Free BASS", AudioErrorCategory.Dependency);
        _disposed = true;
        GC.KeepAlive(_render);
    }

    private static void Check(bool success, string operation, AudioErrorCategory category) { if (!success) throw Error(operation, category); }
    private static AudioBackendException Error(string operation, AudioErrorCategory category)
    {
        var error = Bass.LastError;
        return new AudioBackendException(category, $"{operation}: {error} ({(int)error}).", (int)error);
    }
}
