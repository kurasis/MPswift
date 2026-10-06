using System.Buffers.Binary;
using System.IO;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Threading.Channels;
using Player.Core.Integration;

namespace Player.App.Services.Windows;

/// <summary>Per-user ownership and bounded, current-user-only IPC. Own/dispose the mutex on the UI thread.</summary>
public sealed class SingleInstanceService : IDisposable
{
    private readonly Mutex _mutex;
    private readonly CancellationTokenSource _stop = new();
    private readonly Channel<OpenRequest> _requests = Channel.CreateBounded<OpenRequest>(new BoundedChannelOptions(32) { SingleReader = true, SingleWriter = true });
    private Task _server = Task.CompletedTask, _consumer = Task.CompletedTask;
    public string PipeName { get; }
    public bool IsPrimary { get; }
    public SingleInstanceService()
    {
        using var user = WindowsIdentity.GetCurrent();
        var sid = user.User?.Value ?? throw new InvalidOperationException("Windows user identity unavailable.");
        var identity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sid)))[..32];
        PipeName = "LocalAudioPlayer-" + identity;
        _mutex = new Mutex(false, "Global\\" + PipeName);
        try { IsPrimary = _mutex.WaitOne(0); } catch (AbandonedMutexException) { IsPrimary = true; }
        if (IsPrimary) _server = Task.Run(ServeAsync);
    }
    public void StartReceiving(Func<OpenRequest, Task> receive)
    {
        if (!IsPrimary) throw new InvalidOperationException("Only the primary instance receives requests.");
        _consumer = ConsumeAsync(receive);
    }
    private async Task ConsumeAsync(Func<OpenRequest, Task> receive)
    {
        try { await foreach (var request in _requests.Reader.ReadAllAsync(_stop.Token)) await receive(request); }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
    }
    private async Task ServeAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                using var pipe = new NamedPipeServerStream(PipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly, 4096, 4096);
                await pipe.WaitForConnectionAsync(_stop.Token);
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token); deadline.CancelAfter(TimeSpan.FromSeconds(2));
                var header = new byte[4]; await pipe.ReadExactlyAsync(header, deadline.Token);
                var length = BinaryPrimitives.ReadInt32LittleEndian(header);
                if (length is < 2 or > OpenRequest.MaximumBytes) { await pipe.WriteAsync(new byte[] { 0 }, deadline.Token); continue; }
                var bytes = new byte[length]; await pipe.ReadExactlyAsync(bytes, deadline.Token);
                OpenRequest request;
                try { request = OpenRequest.Decode(bytes); }
                catch (Exception error) when (error is ArgumentException or InvalidDataException or System.Text.Json.JsonException)
                { await pipe.WriteAsync(new byte[] { 0 }, deadline.Token); continue; }
                var accepted = _requests.Writer.TryWrite(request);
                await pipe.WriteAsync(new byte[] { accepted ? (byte)1 : (byte)0 }, deadline.Token);
            }
            catch (OperationCanceledException) { }
            catch (IOException) { if (!_stop.IsCancellationRequested) await Task.Delay(50, _stop.Token).ConfigureAwait(false); }
        }
    }
    public async Task ForwardAsync(OpenRequest request)
    {
        var bytes = request.Encode(); using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(7));
        using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await pipe.ConnectAsync(deadline.Token); // Waits through the primary's short startup race.
        var header = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(header, bytes.Length);
        await pipe.WriteAsync(header, deadline.Token); await pipe.WriteAsync(bytes, deadline.Token);
        var reply = new byte[1]; await pipe.ReadExactlyAsync(reply, deadline.Token);
        if (reply[0] != 1) throw new InvalidDataException("The primary instance rejected the request or its queue is full.");
    }
    public void Dispose()
    {
        _stop.Cancel(); _requests.Writer.TryComplete();
        if (IsPrimary) _mutex.ReleaseMutex();
        _mutex.Dispose();
        // Async I/O finishes under cancellation; no blocking UI-thread wait for a dispatcher callback.
        _ = Task.WhenAll(_server, _consumer).ContinueWith(task => { _ = task.Exception; _stop.Dispose(); }, TaskScheduler.Default);
    }
}
