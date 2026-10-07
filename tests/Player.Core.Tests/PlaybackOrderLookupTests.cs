using Player.Core.Playback;

namespace Player.Core.Tests;

public sealed class PlaybackOrderLookupTests
{
    private static PlaylistEntry Entry(string title) => new(Guid.NewGuid(), new(Guid.NewGuid(), @"C:\Music\owned.wav", title));

    [Fact]
    public void SourceUpdatesKeepExistingBagOrderAndAddOnlyNewEligibleOccurrences()
    {
        var entries = Enumerable.Range(0, 8).Select(i => Entry(i.ToString())).ToArray();
        var order = new PlaybackOrder(new Random(7)); order.SetSource(entries); order.Started(entries[0], false); order.Shuffle = true;
        var before = order.Capture().Remaining; var added = Entry("added");
        var updated = entries.Where(e => e.Id != entries[3].Id).Select(e => e.Id == entries[1].Id ? e with { Enabled = false } :
            e.Id == entries[2].Id ? e with { Track = e.Track with { Available = false } } : e).Append(added).ToArray();
        order.SetSource(updated);
        var eligible = updated.Where(e => e.Enabled && e.Track.Available && e.Id != entries[0].Id).Select(e => e.Id).ToHashSet();
        var after = order.Capture().Remaining;
        Assert.Equal(before.Where(eligible.Contains), after.Where(id => id != added.Id));
        Assert.Equal(eligible.Order(), after.Order());
        Assert.Equal(after, order.Candidates(entries[0], true).Select(e => e.Id));
        Assert.Equal(entries[0].Id, order.Capture().PlaylistCursorId);
    }

    [Fact]
    public void RestoreFiltersIneligibleAndRemovedEntriesWithoutReshufflingTheBag()
    {
        var entries = Enumerable.Range(0, 8).Select(i => Entry(i.ToString())).ToArray();
        var original = new PlaybackOrder(new Random(7)); original.SetSource(entries); original.Shuffle = true; var state = original.Capture();
        var updated = entries.Where(e => e.Id != entries[2].Id).Reverse().Select(e => e.Id == entries[0].Id ? e with { Enabled = false } :
            e.Id == entries[1].Id ? e with { Track = e.Track with { Available = false } } : e with { Track = e.Track with { Title = "updated" } }).ToArray();
        var reopened = new PlaybackOrder(new Random(99)); reopened.SetSource(updated); reopened.Restore(state);
        var eligible = updated.Where(e => e.Enabled && e.Track.Available).Select(e => e.Id).ToHashSet();
        Assert.Equal(state.Remaining.Where(eligible.Contains), reopened.Capture().Remaining);
        Assert.Equal(reopened.Capture().Remaining, reopened.Candidates(null, true).Select(e => e.Id));
        Assert.All(reopened.Candidates(null, true), e => Assert.Equal("updated", e.Track.Title));
    }

    [Fact]
    public void LookupUsesOccurrenceIdentityAndRetainsFirstMatchForRepeatedIds()
    {
        var first = Entry("first"); var duplicateTrack = first with { Id = Guid.NewGuid() }; var repeatedId = first with { Track = first.Track with { Title = "later" } };
        var order = new PlaybackOrder(new Random(7)); order.SetSource([first, duplicateTrack, repeatedId]); order.Shuffle = true;
        var candidates = order.Candidates(null, true);
        Assert.Equal(3, candidates.Length); Assert.Contains(candidates, e => e.Id == duplicateTrack.Id);
        Assert.All(candidates.Where(e => e.Id == first.Id), e => Assert.Same(first, e));
    }

    [Fact]
    public void LargeSourceReorderingAndRestoreKeepExactBagAndUpdatedMetadata()
    {
        var entries = Enumerable.Range(0, 10000).Select(i => Entry(i.ToString())).ToArray();
        var order = new PlaybackOrder(new Random(7)); order.SetSource(entries); order.Shuffle = true; var state = order.Capture();
        var updated = entries.Reverse().Select(e => e with { Track = e.Track with { Title = "updated " + e.Track.Title } }).ToArray();
        order.SetSource(updated); order.Restore(state);
        Assert.Equal(state.Remaining, order.Capture().Remaining);
        var byId = updated.ToDictionary(e => e.Id);
        Assert.Equal(state.Remaining.Select(id => byId[id]), order.Candidates(null, true));
    }
}
