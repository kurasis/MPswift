namespace Player.App.Services.Audio;

/// <summary>Quiesce callbacks and discard queued samples before mutating a source.</summary>
internal static class OutputBufferReset
{
    public static void Flush(bool started, Func<bool, bool> stop, Action release)
    {
        // An endpoint can stop independently of the player's cached running state.
        // Failed Stop/Reset does not establish quiescence. Free must succeed before
        // the caller can touch the old graph; release failures must propagate.
        if (started && !stop(false)) { release(); return; }
        if (!stop(true)) release();
    }
}
