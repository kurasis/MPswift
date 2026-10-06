using Player.App.Resources;
using System.IO;
using System.Windows;
using Player.App.Services.Audio;

namespace Player.App.Services.Windows;

public static class StorageLocation
{
    public static string Resolve()
    {
        var user = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MPswift", "LocalAudioPlayer");
        if (!File.Exists(Path.Combine(AppContext.BaseDirectory, "portable.marker"))) return Prepare(user);
        try { return Prepare(Path.Combine(AppContext.BaseDirectory, "Data")); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            var choice = MessageBox.Show(Strings.Get("PortableUnwritable") + "\n\n" + error.Message,
                Strings.Get("StorageUnavailable"), MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (choice != MessageBoxResult.Yes) throw;
            return Prepare(user);
        }
    }
    public static string Prepare(string path)
    {
        var full = Path.GetFullPath(path);
        var parent = new DirectoryInfo(full);
        while (!parent.Exists) parent = parent.Parent ?? throw new IOException("No local storage parent exists.");
        LocalFileAccess.ValidateDirectory(parent.FullName);
        Directory.CreateDirectory(full); LocalFileAccess.ValidateDirectory(full);
        var probe = Path.Combine(full, ".write-probe-" + Guid.NewGuid().ToString("N"));
        using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose)) { }
        return full;
    }
}
