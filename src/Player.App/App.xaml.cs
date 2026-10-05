using System.Windows;
using Player.App.Services.Audio;
using Player.App.ViewModels;
using Player.App.Views;

namespace Player.App;

public partial class App : Application
{
    private void OnStartup(object sender, StartupEventArgs e)
    {
        var window = new MainWindow
        {
            DataContext = new DiagnosticsViewModel(new NativeDiagnostics())
        };
        MainWindow = window;
        window.Show();
    }
}
