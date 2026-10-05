using System.IO;
using ManagedBass;
using ManagedBass.Wasapi;
using Player.Core.Playback;

namespace Player.App.Services.Audio;

/// <summary>One engine owner; persistent mixer/output, separately prepared sources and explicit output policy.</summary>
public sealed class BassAudioBackend : IAudioBackend, IAdvancedAudioBackend
{
    private readonly WasapiProcedure _render;
    private readonly NativeDecodeContext _context;
    private BassMixerGraph? _graph;
    private AudioRequest? _request, _prepared;
    private AudioSourceInfo? _info;
    private int _preparedHandle;
    private bool _wasapi, _running, _disposed, _repeatOne;
    private int _callbackError;
    private double _volume = 0.5;
    private bool _muted;
    private AudioProcessingSettings _processing = new();
    private AudioOutputSettings _settings = new();
    private string? _actualDevice;
    private TimeSpan _position;
    private AudioFormatInfo? _output;
    public BassAudioBackend()
    {
        _render = Render;
        try { _context = new NativeDecodeContext(); }
        catch (Exception e) when (e is not AudioBackendException) { throw new AudioBackendException(AudioErrorCategory.Dependency, e.Message); }
    }
    public AudioSourceInfo Open(string path) => Open(new AudioRequest(Guid.NewGuid(), path));
    public AudioSourceInfo Open(AudioRequest request)
    {
        ObjectDisposedException.ThrowIf(_disposed, this); CloseSource();
        var opened = BassMixerGraph.OpenSource(request);
        _preparedHandle = opened.Handle; _info = opened.Info; _request = request; _position = TimeSpan.Zero;
        if (_graph is not null) { Bass.StreamFree(_preparedHandle); _preparedHandle = 0; _info = _graph.Load(request); }
        return _info;
    }
    public void Play()
    {
        if (_request is null) throw new AudioBackendException(AudioErrorCategory.Decoder, "No source is loaded.");
        if (_position >= _info?.Duration) Seek(TimeSpan.Zero);
        EnsureOutput();
        if (_graph?.ActiveEntryId != _request.EntryId) _graph!.Load(_request, _position);
        if (_prepared is not null) _graph!.PrepareNext(_prepared, _repeatOne);
        _graph!.SetVolume(_volume, _muted, true);
        Check(BassWasapi.Start(), "Start output"); _running = true;
    }
    private int FindDevice()
    {
        for (var i = 0; BassWasapi.GetDeviceInfo(i, out var info); i++)
            if (!info.IsInput && !info.IsLoopback && info.IsEnabled && (_settings.DeviceId is null ? info.IsDefault : info.ID == _settings.DeviceId)) return i;
        throw new AudioBackendException(AudioErrorCategory.OutputUnavailable, "Output device unavailable. Select a device or Windows default; no automatic fallback occurred.");
    }
    private void EnsureOutput()
    {
        var device = FindDevice(); var deviceInfo = BassWasapi.GetDeviceInfo(device);
        if (_wasapi && deviceInfo.ID == _actualDevice) return;
        if (_wasapi) CloseOutput();
        var rate = _settings.Exclusive ? _info!.Format.SampleRate : deviceInfo.MixFrequency;
        var channels = _settings.Exclusive ? Math.Min(2, _info!.Format.Channels) : deviceInfo.MixChannels;
        var flags = (_settings.Exclusive ? WasapiInitFlags.Exclusive : WasapiInitFlags.Shared) | WasapiInitFlags.Buffer;
        if (BassWasapi.CheckFormat(device, rate, channels, flags) < 0) throw new AudioBackendException(AudioErrorCategory.OutputUnavailable, "Requested output format/mode is unavailable. Exclusive mode was not silently replaced with shared mode.");
        Check(BassWasapi.Init(device, rate, channels, flags, 0.1f, 0, _render), "Open selected WASAPI endpoint"); _wasapi = true; _actualDevice = deviceInfo.ID;
        try
        {
            Check(BassWasapi.GetInfo(out var info), "Read negotiated output format");
            _graph = new(info.Frequency, info.Channels, _processing);
            if (_preparedHandle != 0) { Check(Bass.StreamFree(_preparedHandle), "Free initial decoder"); _preparedHandle = 0; }
            if (_request is not null) _graph.Load(_request, _position);
            _output = new(info.Frequency, info.Channels, (_settings.Exclusive ? "WASAPI exclusive" : "WASAPI shared") + " / float mixer / final saturation protection");
        }
        catch { CloseOutput(); throw; }
    }
    public AudioDevice[] GetDevices()
    {
        var devices = new List<AudioDevice> { new(null, "Windows default output", 0, 0) };
        for (var i = 0; i < 1024 && BassWasapi.GetDeviceInfo(i, out var info); i++)
            if (info.IsEnabled && !info.IsInput && !info.IsLoopback) devices.Add(new(info.ID, info.Name, info.MixFrequency, info.MixChannels));
        return devices.ToArray();
    }
    public void SetOutput(AudioOutputSettings settings)
    {
        if (_settings == settings) return;
        Pause(); _settings = settings; CloseOutput(); // An explicit Play is needed after changing device/mode.
    }
    public void SetProcessing(AudioProcessingSettings settings)
    { if (_processing == settings) return; _processing = settings.Validate(); _graph?.SetProcessing(_processing); }
    public void PrepareNext(AudioRequest? request, bool repeatOne)
    {
        if (_prepared == request && _repeatOne == repeatOne) return;
        if (_graph?.TransitionDecoded == true) FlushAtCurrentPosition();
        _prepared = request; _repeatOne = repeatOne;
        // A bad prepared decoder must not interrupt the still-playing current item.
        try { _graph?.PrepareNext(request, repeatOne); }
        catch (AudioBackendException e) when (e.Category is AudioErrorCategory.FileUnavailable or AudioErrorCategory.Decoder) { _prepared = null; _graph?.PrepareNext(null, false); }
    }
    public void Pause()
    {
        if (!_running) return;
        try { _position = ReadPosition().Position; Check(BassWasapi.Stop(false), "Pause output"); _running = false; }
        catch { CloseOutput(); throw; }
    }
    public void Stop()
    {
        if (_wasapi) Check(BassWasapi.Stop(true), "Stop and discard output");
        _running = false; _position = TimeSpan.Zero; _prepared = null;
        _graph?.Seek(TimeSpan.Zero);
        if (_preparedHandle != 0 && _request is not null) Check(Bass.ChannelSetPosition(_preparedHandle, Bass.ChannelSeconds2Bytes(_preparedHandle, _request.Segment?.Start.TotalSeconds ?? 0)), "Reset source");
        Interlocked.Exchange(ref _callbackError, 0);
    }
    public void Seek(TimeSpan position)
    {
        if (_request is null || _info is null) throw new AudioBackendException(AudioErrorCategory.Decoder, "Source is not seekable.");
        var resume = _running;
        if (_wasapi) Check(BassWasapi.Stop(true), "Flush old endpoint samples"); _running = false; _position = position; _prepared = null;
        _graph?.Seek(position);
        if (_preparedHandle != 0) Check(Bass.ChannelSetPosition(_preparedHandle, Bass.ChannelSeconds2Bytes(_preparedHandle, (_request.Segment?.Start.TotalSeconds ?? 0) + position.TotalSeconds)), "Seek prepared source");
        if (resume) { Check(BassWasapi.Start(), "Resume after seek"); _running = true; }
    }
    private void FlushAtCurrentPosition() => Seek(_position);
    public void SetVolume(double volume, bool muted) { _volume = volume; _muted = muted; _graph?.SetVolume(volume, muted); }
    public BackendPosition ReadPosition()
    {
        if (!_running || !_wasapi || _graph is null) return new(_position, false, _output);
        var error = Interlocked.Exchange(ref _callbackError, 0);
        if (error != 0) throw new AudioBackendException(AudioErrorCategory.OutputUnavailable, "WASAPI render callback failed.", error);
        if (!BassWasapi.IsStarted) throw new AudioBackendException(AudioErrorCategory.OutputUnavailable, "Output endpoint stopped. Select/retry output; context is retained.");
        var device = FindDevice();
        if (BassWasapi.GetDeviceInfo(device).ID != _actualDevice)
        { PauseForDeviceChange(); throw new AudioBackendException(AudioErrorCategory.OutputUnavailable, "Windows default output changed. Playback is paused; Play reconnects to the new default."); }
        var latency = BassWasapi.GetData(nint.Zero, (int)DataFlags.Available); if (latency < 0) throw Error("Read output latency");
        var position = _graph.ReadPosition(latency); _position = position.Position;
        if (position.Transition is { } transition) { _request = transition; _info = position.TransitionInfo; _prepared = null; }
        if (position.Ended) { Check(BassWasapi.Stop(false), "Pause at natural end"); _running = false; }
        return position with { OutputFormat = _output };
    }
    private void PauseForDeviceChange() { Check(BassWasapi.Stop(true), "Pause for device change"); _running = false; CloseOutput(); }
    private int Render(nint buffer, int length, nint user)
    {
        try { var count = _graph?.Render(buffer, length) ?? 0; if (count >= 0) return count; Interlocked.Exchange(ref _callbackError, (int)Bass.LastError); }
        catch { Interlocked.Exchange(ref _callbackError, -1); }
        return 0;
    }
    private void CloseOutput()
    {
        if (_wasapi) { Check(BassWasapi.Free(), "Quiesce/free WASAPI"); _wasapi = false; }
        _running = false; _graph?.Dispose(); _graph = null; _output = null; _actualDevice = null;
    }
    public void CloseSource()
    {
        if (_wasapi) Check(BassWasapi.Stop(true), "Quiesce source replacement"); _running = false;
        _graph?.Clear();
        if (_preparedHandle != 0) { Check(Bass.StreamFree(_preparedHandle), "Free prepared source"); _preparedHandle = 0; }
        _request = null; _prepared = null; _info = null; _position = TimeSpan.Zero; Interlocked.Exchange(ref _callbackError, 0);
    }
    public void Dispose()
    { if (_disposed) return; CloseSource(); CloseOutput(); _context.Dispose(); _disposed = true; GC.KeepAlive(_render); }
    private static void Check(bool success, string operation) { if (!success) throw Error(operation); }
    private static AudioBackendException Error(string operation) { var error = Bass.LastError; return new(AudioErrorCategory.OutputUnavailable, $"{operation}: {error} ({(int)error}).", (int)error); }
}
