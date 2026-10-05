using System.IO;
using ManagedBass;
using ManagedBass.Mix;
using ManagedBass.Wasapi;
using Player.Core.Media;

namespace Player.App.Services.Audio;

/// <summary>Single-thread-owned M0 harness, not the future asynchronous production player.</summary>
public sealed class BassSmokeSession : IDisposable
{
    private readonly WasapiProcedure _render;
    private int _source;
    private int _mixer;
    private bool _bassInitialized;
    private bool _wasapiInitialized;
    private bool _disposed;
    private long _submittedBytes;
    private int _callbackError;

    public BassSmokeSession()
    {
        _render = Render;
        NativeLibraryBootstrap.LoadAndVerify();
        Check(Bass.Init(0), "BASS_Init(no-sound)");
        _bassInitialized = true;
    }

    public DecodeEvidence Decode(string filePath)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_source != 0) throw new InvalidOperationException("Use one source per smoke session.");
        var path = ValidateSourcePath(filePath);
        _source = Bass.CreateStream(path, 0, 0, BassFlags.Decode | BassFlags.Float | BassFlags.Prescan);
        if (_source == 0) throw Error("BASS_StreamCreateFile");
        Check(Bass.ChannelGetInfo(_source, out var info), "BASS_ChannelGetInfo");
        var length = Bass.ChannelGetLength(_source);
        if (length < 0) throw Error("BASS_ChannelGetLength");
        var duration = Bass.ChannelBytes2Seconds(_source, length);
        if (!double.IsFinite(duration) || duration <= 0) throw new InvalidDataException("Invalid decoded duration.");

        var buffer = new float[8192];
        long decodedBytes = 0;
        float peak = 0;
        while (true)
        {
            var read = Bass.ChannelGetData(_source, buffer, buffer.Length * sizeof(float));
            if (read < 0)
            {
                var error = Bass.LastError;
                if (error == Errors.Ended) break;
                throw new InvalidOperationException($"BASS_ChannelGetData failed: {error} ({(int)error}).");
            }
            if (read == 0) throw new InvalidDataException("Decoder stalled before end of file.");
            decodedBytes += read;
            if (decodedBytes > 256L * 1024 * 1024)
                throw new InvalidDataException("Smoke fixtures must be small (at most 256 MiB decoded). Use the app for long files.");
            for (var i = 0; i < read / sizeof(float); i++)
            {
                if (!float.IsFinite(buffer[i])) throw new InvalidDataException("Non-finite decoded sample.");
                peak = Math.Max(peak, Math.Abs(buffer[i]));
            }
        }
        if (decodedBytes == 0) throw new InvalidDataException("Decoder produced no samples.");
        Check(Bass.ChannelSetPosition(_source, Bass.ChannelSeconds2Bytes(_source, duration / 2)), "BASS_ChannelSetPosition(midpoint)");
        if (Bass.ChannelGetData(_source, buffer, buffer.Length * sizeof(float)) <= 0)
            throw Error("Decode after midpoint seek");
        Check(Bass.ChannelSetPosition(_source, 0), "BASS_ChannelSetPosition(start)");
        return new DecodeEvidence(info.Frequency, info.Channels, duration, decodedBytes, peak);
    }

    public OutputEvidence ExerciseSharedOutput()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_source == 0) throw new InvalidOperationException("Decode a fixture before testing output.");
        Check(BassWasapi.Init(-1, 0, 0, WasapiInitFlags.Shared, 0.1f, 0, _render), "BASS_WASAPI_Init(default shared)");
        _wasapiInitialized = true;
        Check(BassWasapi.GetInfo(out var output), "BASS_WASAPI_GetInfo");
        _mixer = BassMix.CreateMixerStream(output.Frequency, output.Channels,
            BassFlags.Decode | BassFlags.Float | BassFlags.MixerNonStop);
        if (_mixer == 0) throw Error("BASS_Mixer_StreamCreate");
        Check(BassMix.MixerAddChannel(_mixer, _source, BassFlags.MixerChanDownMix), "BASS_Mixer_StreamAddChannel");
        // Smoke output is deliberately low gain, applied by the mixer rather than system volume.
        Check(Bass.ChannelSetAttribute(_source, ChannelAttribute.Volume, 0.1), "Set smoke source gain");
        Check(BassWasapi.Start(), "BASS_WASAPI_Start");
        Thread.Sleep(350);
        Check(BassWasapi.Stop(false), "BASS_WASAPI_Stop(pause)");
        var paused = Bass.ChannelGetPosition(_source);
        Thread.Sleep(100);
        if (Bass.ChannelGetPosition(_source) != paused)
            throw new InvalidDataException("Decode position advanced while output was paused.");
        Check(BassWasapi.Start(), "BASS_WASAPI_Start(resume)");
        Thread.Sleep(350);
        Check(BassWasapi.Stop(true), "BASS_WASAPI_Stop(flush)");
        var submitted = Interlocked.Read(ref _submittedBytes);
        var callbackError = Volatile.Read(ref _callbackError);
        if (callbackError != 0) throw new InvalidOperationException($"Output callback failed: {callbackError}.");
        if (submitted == 0 || Bass.ChannelGetPosition(_source) <= paused || paused <= 0)
            throw new InvalidDataException("Output did not consume source samples across pause/resume.");
        Check(BassMix.ChannelSetPosition(_source, 0), "BASS_Mixer_ChannelSetPosition(stop reset)");
        if (Bass.ChannelGetPosition(_source) != 0) throw new InvalidDataException("Stop failed to reset source.");
        return new OutputEvidence(output.Frequency, output.Channels, output.IsExclusive, submitted);
    }

    private int Render(nint buffer, int length, nint user)
    {
        try
        {
            var read = Bass.ChannelGetData(_mixer, buffer, length);
            if (read >= 0)
            {
                Interlocked.Add(ref _submittedBytes, read);
                return read;
            }
            Interlocked.Exchange(ref _callbackError, (int)Bass.LastError);
        }
        catch
        {
            // Never allocate/log/throw through a native callback. Observe the signal on the command thread.
            Interlocked.Exchange(ref _callbackError, -1);
        }
        return 0;
    }

    public void Dispose()
    {
        if (_disposed) return;
        // Do not free a graph if output could not be quiesced. Failure aborts the smoke process.
        if (_wasapiInitialized)
        {
            Check(BassWasapi.Free(), "BASS_WASAPI_Free");
            _wasapiInitialized = false;
        }
        if (_mixer != 0) { Check(Bass.StreamFree(_mixer), "BASS_StreamFree(mixer)"); _mixer = 0; }
        if (_source != 0) { Check(Bass.StreamFree(_source), "BASS_StreamFree(source)"); _source = 0; }
        if (_bassInitialized) { Check(Bass.Free(), "BASS_Free"); _bassInitialized = false; }
        _disposed = true;
        GC.KeepAlive(_render);
    }

    private static void Check(bool success, string operation)
    {
        if (!success) throw Error(operation);
    }

    public static string ValidateSourcePath(string filePath)
    {
        var path = LocalMediaPath.Parse(filePath);
        var drive = new DriveInfo(Path.GetPathRoot(path.Value)!);
        if (drive.DriveType == DriveType.Network)
            throw new IOException("Mapped network drives are unsupported offline sources.");
        if ((File.GetAttributes(path.Value) & FileAttributes.Offline) != 0)
            throw new IOException("The source is an unavailable offline/cloud placeholder.");
        return path.Value;
    }

    private static Exception Error(string operation)
    {
        var error = Bass.LastError;
        return new InvalidOperationException($"{operation} failed: {error} ({(int)error}).");
    }
}

public sealed record DecodeEvidence(int SampleRate, int Channels, double DurationSeconds, long DecodedBytes, float Peak);
public sealed record OutputEvidence(int SampleRate, int Channels, bool Exclusive, long SubmittedBytes);
