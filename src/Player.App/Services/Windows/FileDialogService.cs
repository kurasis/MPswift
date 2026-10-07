using Microsoft.Win32;
using Player.App.Resources;

namespace Player.App.Services.Windows;

public interface IFileDialogService
{
    string[] PickFiles();
    string? PickFolder();
}

public sealed class FileDialogService : IFileDialogService
{
    public string? LastFileDirectory { get; set; }
    public string? LastFolderDirectory { get; set; }
    public string[] PickFiles()
    {
        var dialog = new OpenFileDialog { Multiselect = true, Title = Strings.Get("AddFiles"), Filter = Strings.Get("AudioFilter"), InitialDirectory = LastFileDirectory ?? "" };
        return dialog.ShowDialog() == true ? dialog.FileNames : [];
    }
    public string? PickFolder()
    {
        var dialog = new OpenFolderDialog { Title = Strings.Get("AddFolder"), Multiselect = false, InitialDirectory = LastFolderDirectory ?? "" };
        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }
}
