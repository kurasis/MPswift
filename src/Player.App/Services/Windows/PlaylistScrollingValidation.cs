using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Player.App.Controls;
using Player.App.ViewModels;
using Player.App.Views;
using Player.Core.Playback;

namespace Player.App.Services.Windows;

internal static class PlaylistScrollingValidation
{
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    internal static async Task<object> RunAsync(MainWindow window, PlayerViewModel model, string fixture, string output)
    {
        var selected = model.SelectedPlaylist; var search = model.Search; var initial = model.Playlists.ToArray();
        var source = model.SourcePlaylistId; var snapshot = model.Snapshot; var queue = model.Queue.Select(item => item.Id).ToArray();
        var list = (SmoothPlaylistListBox)window.FindName("PlaylistList");
        async Task Idle() { await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle); window.UpdateLayout(); }
        try
        {
            model.Search = ""; model.CreatePlaylist("Owned smooth scrolling");
            model.AddLibraryTracks(Enumerable.Range(0, 1000).Select(i => new MediaTrack(Guid.NewGuid(), fixture, "Owned scroll " + i, Album: "Album " + i / 10)));
            await Idle();
            var viewer = CustomizationValidation.Descendants(list).OfType<ScrollViewer>().First();
            var bar = CustomizationValidation.Descendants(list).OfType<ScrollBar>().Single(bar => bar.Orientation == Orientation.Vertical && bar.IsVisible);
            var track = (Track)bar.Template.FindName("PART_Track", bar);
            var body = (Border)track.Thumb.Template.FindName("ThumbBody", track.Thumb);
            Check(VirtualizingPanel.GetScrollUnit(list) == ScrollUnit.Pixel && ScrollViewer.GetCanContentScroll(list) && VirtualizingPanel.GetIsVirtualizing(list), "Smooth scroll disabled recycling or pixel units.");
            Check(body.ActualWidth is >= 3 and <= 5 && track.Thumb.ActualWidth >= 8 && track.Thumb.ActualHeight >= 36,
                "Thin painted scrollbar lost its larger drag target or minimum thumb length.");
            viewer.ScrollToTop(); await Idle(); list.ScrollAnimationOverride = true;
            list.ScrollWheelBy(240); Check(list.IsScrollAnimating, "Wheel input did not subscribe an animation.");
            list.ApplyScrollFrame(45); list.UpdateLayout(); var first = viewer.VerticalOffset;
            list.ApplyScrollFrame(90); list.UpdateLayout(); var second = viewer.VerticalOffset;
            Check(first > 0 && second > first && second < 240, "Scroll did not traverse intermediate pixel offsets.");
            list.ScrollWheelBy(-480); list.ApplyScrollFrame(180); list.UpdateLayout();
            Check(!list.IsScrollAnimating && viewer.VerticalOffset == 0, "Reverse input retained a stale destination or idle render subscription.");
            list.ScrollAnimationOverride = false; list.ScrollWheelBy(240); list.UpdateLayout();
            Check(!list.IsScrollAnimating && Math.Abs(viewer.VerticalOffset - 240) < 1, "Reduced-motion scrolling did not apply immediately.");
            list.ScrollAnimationOverride = true;
            var wheel = new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, -30) { RoutedEvent = Mouse.PreviewMouseWheelEvent };
            list.RaiseEvent(wheel);
            Check(SystemParameters.WheelScrollLines == 0 || wheel.Handled && list.IsScrollAnimating, "High-resolution wheel route did not reach smooth scrolling.");
            list.CancelScrollAnimation();
            viewer.ScrollToBottom(); list.UpdateLayout();
            Check(Math.Abs(viewer.VerticalOffset - viewer.ScrollableHeight) < 1 && Math.Abs(track.Value - bar.Maximum) < 1, "Thin thumb lost access to the last song.");
            list.ScrollAnimationOverride = false; list.ScrollWheelBy(10000); list.UpdateLayout();
            Check(Math.Abs(viewer.VerticalOffset - viewer.ScrollableHeight) < 1, "Wheel escaped the lower scroll boundary.");
            list.ScrollAnimationOverride = true; list.ScrollWheelBy(-240);
            list.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(list), 0, Key.Home) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
            Check(!list.IsScrollAnimating, "Keyboard navigation did not interrupt the wheel animation.");
            list.ScrollIntoView(list.Items[^1]); await Idle();
            Check(list.ItemContainerGenerator.ContainerFromIndex(list.Items.Count - 1) is ListBoxItem && PerformanceValidation.Containers(list) < 150,
                "Smooth scrolling lost ScrollIntoView or realized the entire playlist.");
            list.ScrollWheelBy(-240); model.Search = "no-owned-scroll-match"; await Idle();
            Check(!list.IsScrollAnimating && list.Items.Count == 0, "Filtering retained a stale scroll animation.");
            model.Search = ""; await Idle();
            list.ScrollWheelBy(240); list.Visibility = Visibility.Collapsed;
            Check(!list.IsScrollAnimating, "Hidden playlist retained a rendering subscription."); list.Visibility = Visibility.Visible; await Idle();
            CustomizationValidation.Render(window, output, "smooth-scroll-" + model.WindowSettings.Language + ".png");
            Check(model.SourcePlaylistId == source && model.Snapshot.EntryId == snapshot.EntryId && model.Snapshot.Position == snapshot.Position &&
                model.Queue.Select(item => item.Id).SequenceEqual(queue), "Scrolling changed playback source, position or queue.");
            return new { Status = "playlist-scrolling-passed", Rows = 1000, Dataset = "Owned metadata occurrences over an existing native fixture; not 1000 physical files",
                PaintedThumbWidth = body.ActualWidth, DragTargetWidth = track.Thumb.ActualWidth, MinimumThumbLength = true,
                PixelRecycling = true, IntermediateOffsets = new[] { first, second }, ControlledElapsedFramesMilliseconds = new[] { 45, 90, 180 },
                InterruptibleReverseAndKeyboard = true, ReducedMotionImmediate = true, HighResolutionWheelRoute = true,
                FullRangeAndBoundaries = true, ScrollIntoViewAndBoundedContainers = true, FilterAndHiddenCancel = true,
                NoIdleRenderSubscription = true, PlaybackAndQueuePreserved = true, PhysicalWheelTouchAndNarrator = "not-run" };
        }
        finally
        {
            list.CancelScrollAnimation(); list.ScrollAnimationOverride = null; list.Visibility = Visibility.Visible; model.Search = "";
            foreach (var tab in model.Playlists.Except(initial).ToArray()) { model.SelectedPlaylist = tab; model.DeletePlaylist(); }
            model.SelectedPlaylist = selected; model.Search = search; await model.SaveNowAsync();
        }
    }
}
