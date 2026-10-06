using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Threading;
using ManagedBass;
using Player.App.Services.Audio;
using Player.App.Services.Library;
using Player.App.ViewModels;
using Player.App.Views;
using Player.Core.Playback;

namespace Player.App.Services.Windows;

internal static class CueImageValidation
{
    public static async Task<object> RunAsync(MainWindow window, PlayerViewModel model, string flacFixture, string output)
    {
        static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        var directory = Path.Combine(output, "cue-image-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        var image = Path.Combine(directory, "Альбом.flac"); File.Copy(flacFixture, image);
        var cue = Path.Combine(directory, "Album track list.cue");
        const string document = "PERFORMER \"Artist\"\nTITLE \"Альбом\"\nFILE \"Альбом.flac\" WAVE\nTRACK 01 AUDIO\nTITLE \"Первая\"\nINDEX 01 00:00:00\nTRACK 02 AUDIO\nTITLE \"Вторая\"\nINDEX 01 00:01:00\nTRACK 03 AUDIO\nTITLE \"Третья\"\nINDEX 01 00:02:00\n";
        var hash = Hash(image);
        var original = model.SelectedPlaylist; var snapshot = model.Snapshot;
        var initialTabs = model.Playlists.Select(p => p.Id).ToHashSet();
        try
        {
            model.CreatePlaylist("Owned existing FLAC image");
            await model.AddPathsAsync([image]); await Idle();
            Check(model.Entries.Count == 1 && model.Entries[0].Entry.Track.Segment is null, "FLAC without CUE did not remain whole.");
            var whole = model.Entries[0]; whole.Enabled = false;
            await model.ExpandCueImagesAsync([whole]);
            Check(model.Entries.Count == 1 && model.Entries[0].Id == whole.Id, "Missing CUE removed the original occurrence.");
            File.WriteAllText(cue, document.Replace("00:01:00", "00:00:00"), new UTF8Encoding(false));
            await model.ExpandCueImagesAsync([whole]);
            Check(model.Entries.Count == 1 && model.Entries[0].Id == whole.Id, "Malformed CUE replaced the whole image.");
            File.WriteAllText(cue, document.Replace("00:02:00", "00:04:00"), new UTF8Encoding(false));
            await model.ExpandCueImagesAsync([whole]);
            Check(model.Entries.Count == 1 && model.Entries[0].Id == whole.Id, "Out-of-range CUE replaced the whole image.");
            File.WriteAllText(cue, document, new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(directory, "ambiguous.cue"), document, new UTF8Encoding(false));
            await model.ExpandCueImagesAsync([whole]);
            Check(model.Entries.Count == 1 && model.Entries[0].Id == whole.Id, "Ambiguous CUE was guessed.");
            File.Delete(Path.Combine(directory, "ambiguous.cue"));
            var cueHash = Hash(cue);
            await model.ExpandCueImagesAsync([whole]); await Idle();
            var songs = model.Entries.Select(r => r.Entry).ToArray();
            Check(songs.Length == 3 && songs.All(e => !e.Enabled && e.Track.Segment is not null) && songs.Select(e => e.Id).Distinct().Count() == 3,
                "In-place CUE expansion lost song occurrences/flags.");
            Check(songs.Select(e => e.Track.Title).SequenceEqual(new[] { "Первая", "Вторая", "Третья" }), "CUE titles/order were lost.");
            Check(model.Snapshot.EntryId == snapshot.EntryId && model.Snapshot.State == snapshot.State && model.Snapshot.Position == snapshot.Position,
                "Expanding another playlist interrupted playback state.");
            CustomizationValidation.Render((System.Windows.FrameworkElement)window.Content, output, "cue-image-" + model.WindowSettings.Language + ".png");
            var sourceDurations = await Task.Run(() =>
            {
                using var native = new NativeDecodeContext();
                var durations = new List<double>();
                foreach (var song in songs)
                {
                    var source = BassMixerGraph.OpenSource(new(song.Id, song.Track.Path, song.Track.Segment));
                    try
                    {
                        durations.Add(source.Info.Duration!.Value.TotalSeconds);
                        Check(Bass.ChannelGetData(source.Handle, new byte[4096], 4096) > 0, "Native CUE song did not decode PCM.");
                    }
                    finally { Check(Bass.StreamFree(source.Handle), "CUE validation leaked a decoder."); }
                }
                return durations;
            });
            Check(sourceDurations.All(d => Math.Abs(d - 1) < .001), "Native FLAC song bounds were not one second each.");
            await model.SaveNowAsync();
            var persisted = await ((Storage.SqlitePlayerStore)model.LibraryIndex!).LoadAsync();
            Check(persisted.Playlists.Single(p => p.Id == model.SelectedPlaylist.Id).Entries.Select(e => e.Id).SequenceEqual(songs.Select(e => e.Id)),
                "Expanded CUE occurrences did not persist.");
            model.CreatePlaylist("Owned folder pair");
            await model.AddPathsAsync([directory]); await Idle();
            Check(model.Entries.Count == 3 && model.Entries.All(r => r.Entry.Track.Segment is not null), "Folder FLAC+CUE added a duplicate whole album.");
            Check(model.Entries.Select(r => r.Entry.Track.Id).SequenceEqual(songs.Select(e => e.Track.Id)), "Automatic and explicit CUE imports lost shared logical identity.");
            model.CreatePlaylist("Owned repeated images");
            await model.AddPathsAsync([image, image]); await Idle();
            Check(model.Entries.Count == 6 && model.Entries.Select(e => e.Id).Distinct().Count() == 6, "Deliberately repeated FLAC images were collapsed.");
            Check(Hash(image) == hash && Hash(cue) == cueHash, "CUE expansion changed source bytes.");
            using (File.Open(image, FileMode.Open, FileAccess.Read, FileShare.None)) { }
            return new { Status = "cue-image-passed", InPlaceExpansion = true, WholeImageFallback = true, AmbiguityRefused = true, OutOfRangeRefused = true,
                FolderPairWithoutDuplicate = true, DeliberateDuplicateImages = true, StableLogicalIdentity = true, PersistedOccurrences = true,
                SourceUnchanged = true, SourceHandleReleased = true, NativeSongDurations = sourceDurations, NoAutoplay = true };
        }
        finally
        {
            foreach (var tab in model.Playlists.Where(p => !initialTabs.Contains(p.Id)).ToArray()) { model.SelectedPlaylist = tab; model.DeletePlaylist(); }
            model.SelectedPlaylist = original; await model.SaveNowAsync();
            Directory.Delete(directory, true);
        }
        async Task Idle() { await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle); }
    }
}
