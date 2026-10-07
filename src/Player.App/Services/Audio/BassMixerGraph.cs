using System.IO;
using ManagedBass;
using ManagedBass.Mix;
using Player.Core.Playback;

namespace Player.App.Services.Audio;

/// <summary>Persistent decode mixer. Only Render reads PCM; owner mutations lock that mixer.</summary>
public sealed class BassMixerGraph : IDisposable
{
    private sealed record Source(int Handle, AudioRequest Request, AudioSourceInfo Info, double StartSeconds);
    private Source? _current, _next, _tail;
    private long _currentStart, _nextStart, _currentEnd, _tailEnd;
    private double _relativeStart;
    private readonly int _rate, _channels;
    private AudioProcessingSettings _settings;
    private PcmProcessor _processor;
    private readonly int _mixer;
    private float _gain = 0.5f, _targetGain = 0.5f;
    public int Handle => _mixer;
    public Guid? ActiveEntryId => _current?.Request.EntryId;
    public long ProtectedSamples => Volatile.Read(ref _processor).ProtectedSamples;
    public BassMixerGraph(int rate, int channels, AudioProcessingSettings settings)
    {
        _rate = rate; _channels = channels; _settings = settings.Validate(); _processor = new(rate, channels, settings);
        _mixer = BassMix.CreateMixerStream(rate, channels, BassFlags.Decode | BassFlags.Float | BassFlags.MixerNonStop | BassFlags.MixerPositionEx);
        if (_mixer == 0) throw Error("Create persistent mixer");
    }
    public static (int Handle, AudioSourceInfo Info, double Start) OpenSource(AudioRequest request)
    {
        SourceReadPins? read;
        try { read = SourceReadPins.Open(request.Path); }
        catch (Exception e) when (e is IOException or ArgumentException or UnauthorizedAccessException) { throw new AudioBackendException(AudioErrorCategory.FileUnavailable, e.Message); }
        var handle = 0;
        try
        {
            handle = read.CreateDecoder();
            if (handle == 0) throw Error("Open decoder");
            NativeStreamPins.Attach(handle, read); read = null;
            Check(Bass.ChannelGetInfo(handle, out var info), "Read source format");
            var length = Bass.ChannelGetLength(handle); var seconds = length >= 0 ? Bass.ChannelBytes2Seconds(handle, length) : -1;
            if (!double.IsFinite(seconds) || seconds <= 0) throw new AudioBackendException(AudioErrorCategory.Decoder, "A finite source duration is required by the mixer.");
            var start = request.Segment?.Start.TotalSeconds ?? 0; var end = request.Segment?.End?.TotalSeconds ?? seconds;
            if (start >= seconds || end > seconds + 1.0 / info.Frequency || end <= start) throw new AudioBackendException(AudioErrorCategory.Decoder, "CUE segment exceeds the decoded source duration.");
            end = Math.Min(end, seconds);
            var source = new AudioSourceInfo(TimeSpan.FromSeconds(end - start), new(info.Frequency, info.Channels, info.ChannelType.ToString(), (info.OriginalResolution & 0xffff) is > 0 and var bits ? bits : null), true);
            Check(Bass.ChannelSetPosition(handle, Bass.ChannelSeconds2Bytes(handle, start)), "Seek logical source start");
            return (handle, source, start);
        }
        catch { if (handle != 0) NativeStreamPins.Free(handle); throw; }
        finally { read?.Dispose(); }
    }
    public AudioSourceInfo Load(AudioRequest request, TimeSpan relativePosition = default)
    {
        var opened = OpenSource(request);
        Lock();
        try
        {
            ClearSources();
            _current = new(opened.Handle, request, opened.Info, opened.Start);
            _relativeStart = Math.Clamp(relativePosition.TotalSeconds, 0, opened.Info.Duration!.Value.TotalSeconds);
            Check(Bass.ChannelSetPosition(opened.Handle, Bass.ChannelSeconds2Bytes(opened.Handle, opened.Start + _relativeStart)), "Seek current decoder");
            _currentStart = MixPosition(); _currentEnd = _currentStart + MixBytes(opened.Info.Duration.Value.TotalSeconds - _relativeStart);
            Attach(_current, 0, Math.Max(0, _currentEnd - _currentStart));
            return opened.Info;
        }
        catch { if (_current?.Handle != opened.Handle) NativeStreamPins.Free(opened.Handle); throw; }
        finally { Unlock(); }
    }
    public void Seek(TimeSpan position)
    {
        Lock();
        try
        {
            var current = _current ?? throw new InvalidOperationException("No source loaded.");
            Free(ref _next); Free(ref _tail);
            BassMix.MixerRemoveChannel(current.Handle);
            _relativeStart = Math.Clamp(position.TotalSeconds, 0, current.Info.Duration!.Value.TotalSeconds);
            Check(Bass.ChannelSetPosition(current.Handle, Bass.ChannelSeconds2Bytes(current.Handle, current.StartSeconds + _relativeStart)), "Seek current source");
            _currentStart = MixPosition(); _currentEnd = _currentStart + MixBytes(current.Info.Duration.Value.TotalSeconds - _relativeStart);
            Attach(current, 0, Math.Max(0, _currentEnd - _currentStart));
            Volatile.Write(ref _processor, new(_rate, _channels, _settings));
        }
        finally { Unlock(); }
    }
    public bool PrepareNext(AudioRequest? request, bool repeatOne)
    {
        // Decode/open outside the mixer lock: rendering never waits on file metadata/open.
        Source? incoming = null;
        if (request is not null) { var opened = OpenSource(request); incoming = new(opened.Handle, request, opened.Info, opened.Start); }
        Lock();
        try
        {
            // Owner invalidation after read-ahead reaches a transition is handled by flushing/seeking in the backend.
            Free(ref _next);
            if (_current is null) { if (incoming is not null) NativeStreamPins.Free(incoming.Handle); return false; }
            if (BassMix.ChannelGetMixer(_current.Handle) != 0) Check(BassMix.ChannelSetEnvelope(_current.Handle, MixEnvelope.Volume, [], 0), "Clear outgoing envelope");
            if (incoming is null) return true;
            var contiguous = _current.Request.Segment is { End: { } end } && incoming.Request.Segment is { } segment && end == segment.Start && string.Equals(_current.Request.Path, incoming.Request.Path, StringComparison.OrdinalIgnoreCase);
            var overlap = AudioProcessingSettings.Overlap(_settings.CrossfadeSeconds, _current.Info.Duration!.Value, incoming.Info.Duration!.Value, contiguous, repeatOne);
            var now = MixPosition(); var overlapBytes = MixBytes(overlap);
            _nextStart = _currentEnd - overlapBytes;
            if (now > _nextStart) { NativeStreamPins.Free(incoming.Handle); return false; }
            _next = incoming; Attach(incoming, _nextStart - now, MixBytes(incoming.Info.Duration.Value.TotalSeconds));
            if (overlapBytes > 0)
            {
                // 65 equal-power nodes; envelope positions use mixer bytes from this call.
                var outgoing = new MixerNode[66]; outgoing[0] = new MixerNode { Position = 0, Value = 1 };
                var fade = new MixerNode[65];
                for (var i = 0; i <= 64; i++)
                {
                    var x = i / 64.0;
                    outgoing[i + 1] = new MixerNode { Position = _nextStart - now + overlapBytes * i / 64, Value = (float)Math.Cos(x * Math.PI / 2) };
                    fade[i] = new MixerNode { Position = overlapBytes * i / 64, Value = (float)Math.Sin(x * Math.PI / 2) };
                }
                Check(BassMix.ChannelSetEnvelope(_current.Handle, MixEnvelope.Volume, outgoing, outgoing.Length), "Set outgoing envelope");
                Check(BassMix.ChannelSetEnvelope(incoming.Handle, MixEnvelope.Volume, fade, fade.Length), "Set incoming envelope");
            }
            return true;
        }
        catch { if (_next?.Handle == incoming?.Handle) Free(ref _next); else if (incoming is not null) NativeStreamPins.Free(incoming.Handle); if (_current is not null && BassMix.ChannelGetMixer(_current.Handle) != 0) BassMix.ChannelSetEnvelope(_current.Handle, MixEnvelope.Volume, [], 0); throw; }
        finally { Unlock(); }
    }
    public bool TransitionDecoded => _next is not null && MixPosition() >= _nextStart;
    public BackendPosition ReadPosition(int latencyBytes)
    {
        Lock();
        try
        {
            var audible = Math.Max(0, MixPosition() - Math.Max(0, latencyBytes));
            AudioRequest? transition = null; AudioSourceInfo? info = null;
            if (_next is not null && audible >= _nextStart)
            {
                Free(ref _tail); _tail = _current; _tailEnd = _currentEnd;
                _current = _next; _next = null; _currentStart = _nextStart; _relativeStart = 0;
                _currentEnd = _currentStart + MixBytes(_current.Info.Duration!.Value.TotalSeconds);
                transition = _current.Request; info = _current.Info;
            }
            if (_tail is not null && audible >= _tailEnd) Free(ref _tail);
            var seconds = _current is null ? 0 : Math.Clamp(_relativeStart + (double)(audible - _currentStart) / (_rate * _channels * sizeof(float)), _relativeStart, _current.Info.Duration!.Value.TotalSeconds);
            return new(TimeSpan.FromSeconds(seconds), _current is not null && audible >= _currentEnd && _next is null, Transition: transition, TransitionInfo: info);
        }
        finally { Unlock(); }
    }
    public void SetProcessing(AudioProcessingSettings settings)
    {
        _settings = settings.Validate(); Volatile.Write(ref _processor, new(_rate, _channels, settings, Volatile.Read(ref _processor)));
        Lock(); try { foreach (var source in new[] { _current, _next, _tail }.OfType<Source>()) SetTrackGain(source); } finally { Unlock(); }
    }
    public void SetVolume(double volume, bool muted, bool immediate = false)
    { Volatile.Write(ref _targetGain, muted ? 0 : (float)volume); if (immediate) _gain = Volatile.Read(ref _targetGain); }
    public unsafe int Render(nint buffer, int length)
    {
        var count = Bass.ChannelGetData(_mixer, buffer, length);
        if (count < 0) return count;
        var target = Volatile.Read(ref _targetGain);
        Volatile.Read(ref _processor).Process(new Span<float>(buffer.ToPointer(), count / sizeof(float)), _gain, target);
        _gain = target; return count;
    }
    public void Clear() { Lock(); try { ClearSources(); } finally { Unlock(); } }
    private void ClearSources() { Free(ref _next); Free(ref _tail); Free(ref _current); }
    private static void Free(ref Source? source)
    { if (source is null) return; var handle = source.Handle; source = null; Check(NativeStreamPins.Free(handle), "Free source"); }
    private void Attach(Source source, long delay, long length)
    {
        if (length <= 0) return; // Seeking to exact EOF must never attach an unlimited stream.
        Check(BassMix.MixerAddChannel(_mixer, source.Handle, BassFlags.MixerChanDownMix, delay, length), "Attach bounded source"); SetTrackGain(source);
    }
    private void SetTrackGain(Source source) => Check(Bass.ChannelSetAttribute(source.Handle, ChannelAttribute.Volume, (float)(source.Request.ReplayGain?.Gain(_settings.ReplayGain) ?? 1)), "Set per-track gain");
    private long MixBytes(double seconds) => checked((long)Math.Round(seconds * _rate, MidpointRounding.AwayFromZero) * _channels * sizeof(float));
    private long MixPosition() { var value = Bass.ChannelGetPosition(_mixer); if (value < 0) throw Error("Read mixer position"); return value; }
    private void Lock() => Check(Bass.ChannelLock(_mixer, true), "Lock mixer");
    private void Unlock() => Check(Bass.ChannelLock(_mixer, false), "Unlock mixer");
    public void Dispose() { Clear(); Check(NativeStreamPins.Free(_mixer), "Free mixer"); }
    private static void Check(bool ok, string operation) { if (!ok) throw Error(operation); }
    private static AudioBackendException Error(string operation) { var error = Bass.LastError; return new(AudioErrorCategory.Decoder, $"{operation}: {error} ({(int)error}).", (int)error); }
}
