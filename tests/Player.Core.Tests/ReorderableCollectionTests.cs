using System.Collections.Specialized;
using Player.App.ViewModels;

namespace Player.Core.Tests;

public sealed class ReorderableCollectionTests
{
    private sealed record Row(int Value);
    private static (ReorderableCollection<Row> Rows, List<NotifyCollectionChangedAction> Events) Create(int count)
    {
        var rows = new ReorderableCollection<Row>();
        foreach (var i in Enumerable.Range(0, count)) rows.Add(new(i));
        var events = new List<NotifyCollectionChangedAction>();
        rows.CollectionChanged += (_, change) => events.Add(change.Action);
        return (rows, events);
    }

    [Fact]
    public void MovingABlockEitherWayUsesOneMovePerBlockRow()
    {
        var (rows, events) = Create(10000);
        // A 12-row album from the top to the end: the backward plan moves the block, not the 9,988 rows between.
        var order = rows.Skip(12).Concat(rows.Take(12)).ToArray();
        rows.Reorder(order);
        Assert.Equal(order, rows); Assert.Equal(12, events.Count); Assert.All(events, action => Assert.Equal(NotifyCollectionChangedAction.Move, action));
        events.Clear();
        order = rows.Skip(9988).Concat(rows.Take(9988)).ToArray();
        rows.Reorder(order);
        Assert.Equal(order, rows); Assert.Equal(12, events.Count);
    }

    [Fact]
    public void LargePermutationIsOneResetAndUnchangedOrderIsSilent()
    {
        var (rows, events) = Create(5000);
        rows.Reorder(rows.ToArray());
        Assert.Empty(events);
        var order = rows.OrderBy(_ => Random.Shared.Next()).ToArray();
        rows.Reorder(order);
        Assert.Equal(order, rows); Assert.Equal([NotifyCollectionChangedAction.Reset], events);
    }

    [Fact]
    public void RandomSmallPermutationsMatchTheRequestedOrder()
    {
        var random = new Random(7);
        for (var attempt = 0; attempt < 200; attempt++)
        {
            var (rows, events) = Create(random.Next(1, 300));
            var order = rows.ToList();
            for (var swaps = random.Next(0, 6); swaps > 0; swaps--)
            {
                var from = random.Next(order.Count); var row = order[from]; order.RemoveAt(from); order.Insert(random.Next(order.Count + 1), row);
            }
            rows.Reorder(order);
            Assert.Equal(order, rows);
            Assert.True(events.Count <= 128 || events.SequenceEqual([NotifyCollectionChangedAction.Reset]));
        }
    }

    [Fact]
    public void RemovingManyRowsIsOneResetAndFewRowsStayIncremental()
    {
        var (rows, events) = Create(10000);
        var removed = rows.Where(row => row.Value % 3 == 0).ToHashSet();
        rows.RemoveRows(removed);
        Assert.Equal(Enumerable.Range(0, 10000).Where(i => i % 3 != 0), rows.Select(row => row.Value));
        Assert.Equal([NotifyCollectionChangedAction.Reset], events);
        events.Clear();
        rows.RemoveRows(rows.Take(3).Append(new Row(-1)).ToHashSet());
        Assert.Equal(6663, rows.Count); Assert.Equal(3, events.Count);
        Assert.Throws<ArgumentException>(() => rows.Reorder(rows.Skip(1).ToArray()));
    }
}
