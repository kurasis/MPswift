using System.Text;
using Player.App.Services.Storage;
using Player.Core.Library;
using Player.Core.Media;
using Player.Core.Playback;

namespace Player.Core.Tests;

public sealed class LegacyTagTextTests
{
    internal static string Broken(string text, int western = 28591)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(western).GetString(Encoding.GetEncoding(1251).GetBytes(text));
    }

    [Theory]
    [InlineData("Людзі", 28591)]
    [InlineData("Кастусь Герашчанка", 28591)]
    [InlineData("Новыя словы", 28591)]
    [InlineData("Гомельскі вальс", 28591)]
    [InlineData("19 пішучых ідэяў!", 1252)]
    [InlineData("Тры сонцы", 1252)]
    [InlineData("Вер мне", 1252)]
    [InlineData("Людзі — www", 1252)]
    [InlineData("Indiga - Дні (2004)", 1252)]
    public void RecoversLosslessBelarusianLegacyWords(string text, int western)
    {
        var repaired = LegacyTagText.Recover(Broken(text, western), @"C:\Music\Беларускае\album.mp3");
        Assert.Equal(text, repaired);
        Assert.Equal(repaired, LegacyTagText.Recover(repaired));
    }

    [Theory]
    [InlineData("Людзі — Цэпэліны / Тры сонцы")]
    [InlineData("Björk — Jóga")]
    [InlineData("Café / Déjà vu / München")]
    [InlineData("Sigur Rós / Straße / Beyoncé")]
    [InlineData("Été / À bientôt / Håkan")]
    [InlineData("Amaroka / :B:N: / www / 2004")]
    [InlineData("日本語 / Ελληνικά / 🎵")]
    [InlineData("Broken � text")]
    [InlineData("Ёþäçi")]
    [InlineData("")]
    [InlineData(null)]
    public void PreservesUnicodeLatinAndIrreversibleText(string? text)
        => Assert.Equal(text, LegacyTagText.Recover(text, @"C:\Беларускае\song.mp3"));

    [Fact]
    public void TrackRecoveryPreservesIdentityPathsAndPlaybackMetadata()
    {
        var original = new MediaTrack(Guid.NewGuid(), @"C:\Беларускае\song.flac", Broken("Людзі"),
            Broken("Кастусь"), Broken("Цэпэліны"), TimeSpan.FromMinutes(3), "FLAC", Segment: new(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5)),
            AlbumArtist: Broken("Індыга"), Genre: "Rock", TrackNumber: 3);
        var result = LegacyTagText.Recover(original);
        Assert.Equal(original with { Title = "Людзі", Artist = "Кастусь", Album = "Цэпэліны", AlbumArtist = "Індыга" }, result);
    }

    [Fact]
    public async Task ExistingPlaylistRecoversOnLoadWithoutReimportOrLosingIds()
    {
        var directory = Path.Combine(Path.GetTempPath(), "legacy-tags-" + Guid.NewGuid()); Directory.CreateDirectory(directory);
        try
        {
            var track = new MediaTrack(Guid.NewGuid(), @"C:\Беларускае\song.mp3", Broken("Людзі"), Album: Broken("Цэпэліны"));
            var entry = new PlaylistEntry(Guid.NewGuid(), track, false);
            var state = LibraryState.CreateDefault(); state = state with { Playlists = [state.Playlists[0] with { Entries = [entry] }] };
            await using (var store = new SqlitePlayerStore(Path.Combine(directory, "player.db"))) await store.SaveAsync(state, true);
            await using var reopened = new SqlitePlayerStore(Path.Combine(directory, "player.db"));
            var result = (await reopened.LoadAsync()).Playlists[0].Entries[0];
            Assert.Equal(entry with { Track = track with { Title = "Людзі", Album = "Цэпэліны" } }, result);
        }
        finally { Directory.Delete(directory, true); }
    }
}
