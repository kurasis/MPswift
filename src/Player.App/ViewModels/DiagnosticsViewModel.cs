using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Player.App.Resources;
using Player.App.Services.Audio;
using Player.Core;

namespace Player.App.ViewModels;

public partial class DiagnosticsViewModel(INativeDiagnostics diagnostics) : ObservableObject
{
    public string ProductName => ProductInfo.Name;

    [ObservableProperty]
    private string _status = Strings.Get("InitialStatus");

    [RelayCommand]
    private async Task VerifyNativeAsync()
    {
        Status = Strings.Get("Checking");
        try
        {
            var versions = await diagnostics.VerifyAsync();
            Status = Strings.Get("Verified") + Environment.NewLine +
                string.Join(Environment.NewLine, versions.Select(pair => $"{pair.Key}: {pair.Value}"));
        }
        catch (Exception error) when (error is IOException or InvalidDataException or BadImageFormatException or
            DllNotFoundException or EntryPointNotFoundException or PlatformNotSupportedException or System.Text.Json.JsonException)
        {
            Status = Strings.Get("Failed") + Environment.NewLine + error.Message;
        }
    }
}
