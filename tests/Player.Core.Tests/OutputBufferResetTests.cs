using Player.App.Services.Audio;

namespace Player.Core.Tests;

public sealed class OutputBufferResetTests
{
    [Theory]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(true, false, true)]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    public void ReplacementRequiresStoppedCallbacksAndDiscardedOldSamples(bool started, bool stopFails, bool resetFails)
    {
        var endpoint = new Endpoint(started) { StopFails = stopFails, ResetFails = resetFails };
        OutputBufferReset.Flush(started, endpoint.Stop, endpoint.Release);
        endpoint.ReplaceSource();
        Assert.False(endpoint.Started);
        Assert.False(endpoint.HasOldSamples);
        Assert.Equal(stopFails || resetFails, endpoint.Released);
        Assert.DoesNotContain("reset-while-running", endpoint.Calls);
        if (started) Assert.Equal("stop", endpoint.Calls[0]);
        else Assert.Equal("reset", endpoint.Calls[0]);
    }

    [Fact]
    public void FailedReleasePreventsSourceMutation()
    {
        var endpoint = new Endpoint(true) { StopFails = true, ReleaseFails = true };
        Assert.Throws<IOException>(() =>
        {
            OutputBufferReset.Flush(endpoint.Started, endpoint.Stop, endpoint.Release);
            endpoint.ReplaceSource();
        });
        Assert.True(endpoint.Started);
        Assert.True(endpoint.HasOldSamples);
        Assert.False(endpoint.Replaced);
        Assert.Equal(new[] { "stop", "free" }, endpoint.Calls);
    }

    private sealed class Endpoint(bool started)
    {
        public bool Started { get; private set; } = started;
        public bool HasOldSamples { get; private set; } = true;
        public bool Released { get; private set; }
        public bool Replaced { get; private set; }
        public bool StopFails { get; init; }
        public bool ResetFails { get; init; }
        public bool ReleaseFails { get; init; }
        public List<string> Calls { get; } = [];
        public bool Stop(bool reset)
        {
            Calls.Add(reset ? Started ? "reset-while-running" : "reset" : "stop");
            if (reset && (Started || ResetFails) || !reset && StopFails) return false;
            Started = false;
            if (reset) HasOldSamples = false;
            return true;
        }
        public void Release()
        {
            Calls.Add("free");
            if (ReleaseFails) throw new IOException("Endpoint could not be released.");
            Started = false; HasOldSamples = false; Released = true;
        }
        public void ReplaceSource()
        {
            if (Started || HasOldSamples) throw new InvalidOperationException("Old output is still active/buffered.");
            Replaced = true;
        }
    }
}
