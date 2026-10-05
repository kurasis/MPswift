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
}
