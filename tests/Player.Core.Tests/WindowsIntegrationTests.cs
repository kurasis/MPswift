using System.Text;
using System.Text.Json;
using Player.Core.Integration;
using Player.Core.Library;

namespace Player.Core.Tests;

public sealed class WindowsIntegrationTests
{
    [Fact]
    public async Task InitialPlaylistNameCanBeLocalizedWithoutRenamingSavedUserData()
    {
        var directory = Path.Combine(Path.GetTempPath(), "mp-language-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "library.db");
        try
        {
            Guid id;
            await using (var store = new Player.App.Services.Storage.SqlitePlayerStore(path, "Основной %_ '"))
            { var state = await store.LoadAsync(); Assert.Equal("Основной %_ '", state.Playlists[0].Name); id = state.Playlists[0].Id; }
            await using (var reopened = new Player.App.Services.Storage.SqlitePlayerStore(path, "Default"))
            { var state = await reopened.LoadAsync(); Assert.Equal(id, state.Playlists[0].Id); Assert.Equal("Основной %_ '", state.Playlists[0].Name); }
        }
        finally { Directory.Delete(directory, true); }
    }
    [Fact]
    public void CommandLineResolvesUnicodePathsAndRequiresExplicitPlay()
    {
        var request = OpenRequest.ParseArguments(["music\\Музыка 🎵.wav", "C:\\Audio\\track.flac"], "C:\\Player");
        Assert.False(request.Play); Assert.Equal("C:\\Player\\music\\Музыка 🎵.wav", request.Paths[0]);
        var played = OpenRequest.ParseArguments(["--play", "--", "-song.wav"], "C:\\Player");
        Assert.True(played.Play); Assert.Equal("C:\\Player\\-song.wav", played.Paths[0]);
        var restored = OpenRequest.Decode(played.Encode()); Assert.Equal(played.Paths, restored.Paths); Assert.True(restored.Play);
        Assert.Empty(OpenRequest.ParseArguments([], "C:\\Player").Paths);
    }
    [Theory]
    [InlineData("--eval")]
    [InlineData("https://example.com/song.mp3")]
    [InlineData("\\\\server\\music\\track.mp3")]
    [InlineData("C:\\Music\\NUL.wav")]
    [InlineData("C:\\Music\\track.mp3:stream")]
    public void UnsupportedOptionsAndNonLocalSourcesAreRejected(string argument)
    { Assert.Throws<ArgumentException>(() => OpenRequest.ParseArguments([argument], "C:\\Player")); }
    [Theory]
    [InlineData("{\"Version\":1,\"Paths\":[],\"Execute\":\"cmd.exe\"}")]
    [InlineData("{\"Version\":2,\"Paths\":[]}")]
    [InlineData("{\"Version\":1,\"Paths\":[\"https://example.com/song.mp3\"]}")]
    [InlineData("{\"Version\":1,\"Paths\":null}")]
    public void MalformedIpcCannotInvokeCommandsOrResolveRemotePaths(string json)
    { Assert.ThrowsAny<Exception>(() => OpenRequest.Decode(Encoding.UTF8.GetBytes(json))); }
    [Fact]
    public void IpcBoundsBothBytesAndNumberOfPaths()
    {
        Assert.Throws<InvalidDataException>(() => OpenRequest.Decode(new byte[OpenRequest.MaximumBytes + 1]));
        Assert.Throws<InvalidDataException>(() => new OpenRequest(1, Enumerable.Repeat("C:\\Music\\track.mp3", 1001).ToArray()).Encode());
        Assert.Throws<InvalidDataException>(() => new OpenRequest(1, ["C:\\Music\\" + new string('a', 32000), "C:\\Music\\" + new string('b', 32000), "C:\\Music\\" + new string('c', 32000)]).Encode());
    }
    [Fact]
    public void OldSettingsRemainEnglishWithExplicitExitAndLanguageValidation()
    {
        var old = JsonSerializer.Deserialize<PlayerSettings>("{\"SchemaVersion\":1,\"Volume\":23}")!.Validate();
        Assert.Equal("en", old.Language); Assert.False(old.CloseToTray); Assert.Equal(23, old.Volume);
        var russian = (old with { Language = "ru", CloseToTray = true }).Validate();
        var reopened = JsonSerializer.Deserialize<PlayerSettings>(JsonSerializer.Serialize(russian))!.Validate();
        Assert.Equal("ru", reopened.Language); Assert.True(reopened.CloseToTray);
        Assert.Equal("en", (old with { Language = "invalid" }).Validate().Language);
    }
}
