using Player.App.Resources;
using System.IO;
using System.Windows;
using Player.App.Services.Audio;
using Player.App.Services.Storage;

namespace Player.App.Services.Windows;

public static class StorageLocation
{
    public static string Resolve()
    {
        var user = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MPswift", "LocalAudioPlayer");
        return Resolve(AppContext.BaseDirectory, user, error => MessageBox.Show(Strings.Get("PortableUnwritable") + "\n\n" + error.Message,
            Strings.Get("StorageUnavailable"), MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes);
    }
    internal static string Resolve(string applicationDirectory, string userDirectory, Func<Exception, bool> chooseFallback)
    {
        if (!File.Exists(Path.Combine(applicationDirectory, "portable.marker"))) return Prepare(userDirectory);
        try { return Prepare(Path.Combine(applicationDirectory, "Data")); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            if (!chooseFallback(error)) throw;
            return Prepare(userDirectory);
        }
    }
    public static string Prepare(string path)
    {
        var full = Path.GetFullPath(path);
        var parent = new DirectoryInfo(full);
        while (!parent.Exists) parent = parent.Parent ?? throw new IOException("No local storage parent exists.");
        LocalFileAccess.ValidateDirectory(parent.FullName);
        using var lease = DataDirectoryLease.Create(full);
        full = lease.DirectoryPath; LocalFileAccess.ValidateDirectory(full);
        var probe = Path.Combine(full, ".write-probe-" + Guid.NewGuid().ToString("N"));
        using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose)) { }
        return full;
    }
}
