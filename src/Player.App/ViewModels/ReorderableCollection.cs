using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace Player.App.ViewModels;

/// <summary>Bulk reorder/removal for playlist rows: a few incremental moves, or one Reset instead of thousands of notifications.</summary>
public sealed class ReorderableCollection<T> : ObservableCollection<T> where T : class
{
    // Beyond this, one Reset (regenerating only the visible containers) is cheaper than per-row moves.
    private const int MaximumIncrementalChanges = 128;

    /// <summary>Applies a permutation of the current rows.</summary>
    public void Reorder(IReadOnlyList<T> order)
    {
        if (order.Count != Count) throw new ArgumentException("Reorder requires the same items.", nameof(order));
        var forward = PlanMoves(order, false);
        var backward = forward is { Count: <= 1 } ? null : PlanMoves(order, true);
        var moves = forward is null ? backward : backward is null || forward.Count <= backward.Count ? forward : backward;
        if (moves is null) { ReplaceAll(order); return; }
        foreach (var (from, to) in moves) Move(from, to);
    }
    public void RemoveRows(IReadOnlySet<T> rows)
    {
        if (rows.Count <= MaximumIncrementalChanges) { foreach (var row in rows) Remove(row); return; }
        ReplaceAll(Items.Where(row => !rows.Contains(row)).ToArray());
    }
    private void ReplaceAll(IReadOnlyList<T> rows)
    {
        CheckReentrancy();
        Items.Clear();
        foreach (var row in rows) Items.Add(row);
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
    /// <summary>Simulates moves that fix positions front-to-back (or back-to-front); null when too many are needed.</summary>
    private List<(int From, int To)>? PlanMoves(IReadOnlyList<T> order, bool backward)
    {
        var current = new List<T>(Items);
        var moves = new List<(int From, int To)>();
        for (var step = 0; step < current.Count; step++)
        {
            var i = backward ? current.Count - 1 - step : step;
            if (ReferenceEquals(current[i], order[i])) continue;
            if (moves.Count == MaximumIncrementalChanges) return null;
            // Positions already fixed never hold order[i], so it is found on the unfixed side.
            var from = i;
            do from += backward ? -1 : 1; while (!ReferenceEquals(current[from], order[i]));
            var row = current[from]; current.RemoveAt(from); current.Insert(i, row);
            moves.Add((from, i));
        }
        return moves;
    }
}
