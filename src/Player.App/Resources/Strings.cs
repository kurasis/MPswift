using System.Globalization;
using System.Resources;

namespace Player.App.Resources;

public static class Strings
{
    private static readonly ResourceManager Manager = new("Player.App.Resources.Strings", typeof(Strings).Assembly);
    public static string Get(string key) => Manager.GetString(key, CultureInfo.CurrentUICulture)
        ?? throw new MissingManifestResourceException($"Missing resource: {key}");
    public static string StageTitle => Get(nameof(StageTitle));
    public static string StageDescription => Get(nameof(StageDescription));
    public static string VerifyNative => Get(nameof(VerifyNative));
    public static string AddFiles => Get(nameof(AddFiles));
    public static string AddFolder => Get(nameof(AddFolder));
    public static string Play => Get(nameof(Play));
    public static string Pause => Get(nameof(Pause));
    public static string Stop => Get(nameof(Stop));
    public static string Next => Get(nameof(Next));
    public static string Previous => Get(nameof(Previous));
    public static string Mute => Get(nameof(Mute));
    public static string Volume => Get(nameof(Volume));
    public static string Seek => Get(nameof(Seek));
    public static string Search => Get(nameof(Search));
    public static string ClearSearch => Get(nameof(ClearSearch));
    public static string DefaultPlaylist => Get(nameof(DefaultPlaylist));
    public static string Enabled => Get(nameof(Enabled));
    public static string Remove => Get(nameof(Remove));
    public static string CancelImport => Get(nameof(CancelImport));
    public static string Details => Get(nameof(Details));
    public static string EmptyPlaylist => Get(nameof(EmptyPlaylist));
    public static string NoWaveform => Get(nameof(NoWaveform));
    public static string Help => Get(nameof(Help));
    public static string PlaylistName => Get("PlaylistName");
    public static string SaveName => Get("SaveName");
    public static string NewPlaylist => Get("NewPlaylist");
    public static string PlaylistActions => Get("PlaylistActions");
    public static string Playlists => Get("Playlists");
    public static string RenamePlaylist => Get("RenamePlaylist");
    public static string DuplicatePlaylist => Get("DuplicatePlaylist");
    public static string DeletePlaylist => Get("DeletePlaylist");
    public static string TabLeft => Get("TabLeft");
    public static string TabRight => Get("TabRight");
    public static string MoveUp => Get("MoveUp");
    public static string MoveDown => Get("MoveDown");
    public static string RefreshWaveform => Get("RefreshWaveform");
    public static string Backup => Get("Backup");
    public static string BackupSaved => Get("BackupSaved");
    public static string Saved => Get("Saved");
    public static string Saving => Get("Saving");
    public static string SaveFailed => Get("SaveFailed");
    public static string WaveformLoading => Get("WaveformLoading");
    public static string WaveformProgress => Get("WaveformProgress");
    public static string WaveformUnavailable => Get("WaveformUnavailable");
    public static string TabLimit => Get("TabLimit");
    public static string ReorderFiltered => Get("ReorderFiltered");
}
