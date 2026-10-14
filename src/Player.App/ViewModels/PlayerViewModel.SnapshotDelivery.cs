using System.Diagnostics;
using System.Windows.Threading;
using Player.App.Resources;
using Player.Core.Library;
using Player.Core.Playback;

namespace Player.App.ViewModels;

public partial class PlayerViewModel
{
    private readonly object _snapshotGate = new();
    private readonly PlaybackSnapshotQueue _snapshotQueue = new();
    private long _observedRevision = -1;
    private long _receivedSnapshots, _appliedSnapshots, _listeningObservations;
    internal long ReceivedSnapshots => Interlocked.Read(ref _receivedSnapshots);
    internal long AppliedSnapshots => Interlocked.Read(ref _appliedSnapshots);
    internal long ListeningObservations => Interlocked.Read(ref _listeningObservations);
    private void OnSnapshot(PlaybackSnapshot snapshot) => ReceiveSnapshot(snapshot);
    internal void ReceiveSnapshot(PlaybackSnapshot snapshot)
    {
        // Never acquire the coordinator lock while holding _snapshotGate: Load publishes under its own lock.
        var active = _coordinator.ActiveEntry;
        var track = active?.Id == snapshot.EntryId ? active?.Track.Id : null;
        bool schedule;
        lock (_snapshotGate)
        {
            if (_closing || _dispatcher.HasShutdownStarted || snapshot.Revision <= _observedRevision || snapshot.Revision < Volatile.Read(ref _revision)) return;
            _observedRevision = snapshot.Revision; Interlocked.Increment(ref _receivedSnapshots);
            // Statistics see every accepted engine snapshot at receipt time, independently of UI backlogs.
            ObserveListening(snapshot, track, Stopwatch.GetElapsedTime(_clockOrigin), DateTime.UtcNow);
            schedule = _snapshotQueue.Enqueue(snapshot);
        }
        if (schedule) _dispatcher.BeginInvoke(DrainSnapshots, DispatcherPriority.Background);
    }
    private void DrainSnapshots()
    {
        var batch = _snapshotQueue.Drain();
        foreach (var snapshot in batch)
        {
            if (_closing || _dispatcher.HasShutdownStarted) return;
            if (snapshot.Revision < _revision) continue;
            ApplySnapshot(snapshot, false); Interlocked.Increment(ref _appliedSnapshots);
        }
    }
    private void ObserveListening(PlaybackSnapshot snapshot, Guid? track, TimeSpan monotonic, DateTime utc)
    {
        if (_index is null) return;
        Interlocked.Increment(ref _listeningObservations);
        if (_listening.Update(snapshot, track, monotonic, utc) is { } occurrence)
            _statisticsTask = SaveListeningAfterAsync(_statisticsTask, occurrence);
    }
    private async Task SaveListeningAfterAsync(Task preceding, ListeningEvent occurrence)
    {
        await preceding.ConfigureAwait(false);
        try { await _index!.RecordListeningAsync(occurrence).ConfigureAwait(false); }
        catch (Exception error)
        {
            if (!_dispatcher.HasShutdownStarted)
                _ = _dispatcher.BeginInvoke(() => { if (!_closing) { Message = Strings.Get("ErrorUnexpected"); Details = error.Message; } }, DispatcherPriority.Background);
        }
    }
}
