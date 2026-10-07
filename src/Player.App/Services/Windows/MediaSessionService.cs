using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using Player.App.ViewModels;
using Windows.Media;
using Windows.Storage.Streams;

namespace Player.App.Services.Windows;

/// <summary>One SMTC handler is the sole global media-key route; no duplicate WM_APPCOMMAND registration.</summary>
public sealed class MediaSessionService : IDisposable
{
    private readonly PlayerViewModel _model;
    private readonly SystemMediaTransportControls _controls;
    private InMemoryRandomAccessStream? _artwork;
    private long _artGeneration;
    private bool _disposed;
    public MediaSessionService(System.Windows.Window window, PlayerViewModel model)
    {
        _model = model;
        var className = "Windows.Media.SystemMediaTransportControls";
        Marshal.ThrowExceptionForHR(WindowsCreateString(className, className.Length, out var name));
        try
        {
            var iid = typeof(ISystemMediaTransportControlsInterop).GUID;
            Marshal.ThrowExceptionForHR(RoGetActivationFactory(name, ref iid, out var factory));
            try
            {
                var controlId = new Guid("99FA3FF4-1742-42A6-902E-087D41F965EC");
                Marshal.ThrowExceptionForHR(factory.GetForWindow(new WindowInteropHelper(window).EnsureHandle(), ref controlId, out var pointer));
                try { _controls = SystemMediaTransportControls.FromAbi(pointer); }
                finally { Marshal.Release(pointer); }
            }
            finally { Marshal.ReleaseComObject(factory); }
        }
        finally { WindowsDeleteString(name); }
        _controls.IsEnabled = true; _controls.IsPlayEnabled = true; _controls.IsPauseEnabled = true;
        _controls.IsNextEnabled = true; _controls.IsPreviousEnabled = true; _controls.IsStopEnabled = true;
        _controls.ButtonPressed += OnButton;
        model.PropertyChanged += OnChanged;
        Update();
    }
    private async void OnButton(SystemMediaTransportControls sender, SystemMediaTransportControlsButtonPressedEventArgs args)
    {
        if (_disposed || System.Windows.Application.Current.Dispatcher.HasShutdownStarted) return;
        try { await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => _model.HandleMediaAsync(args.Button.ToString())).Task.Unwrap(); }
        catch (Exception error) { if (!_disposed) _ = System.Windows.Application.Current.Dispatcher.BeginInvoke(() => _model.Details = error.Message); }
    }
    private void OnChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(PlayerViewModel.Title) or nameof(PlayerViewModel.Artist) or nameof(PlayerViewModel.Album) or nameof(PlayerViewModel.IsPlaying) or nameof(PlayerViewModel.State)) Update();
        if (args.PropertyName == nameof(PlayerViewModel.CoverArt)) UpdateArtwork();
    }
    private void Update()
    {
        if (_disposed) return;
        _controls.PlaybackStatus = _model.Snapshot.State switch
        { Player.Core.Playback.PlaybackState.Playing => MediaPlaybackStatus.Playing, Player.Core.Playback.PlaybackState.Paused => MediaPlaybackStatus.Paused, _ => MediaPlaybackStatus.Stopped };
        var display = _controls.DisplayUpdater; display.Type = MediaPlaybackType.Music;
        display.MusicProperties.Title = _model.Title; display.MusicProperties.Artist = _model.Artist; display.MusicProperties.AlbumTitle = _model.Album;
        display.Update();
    }
    private async void UpdateArtwork()
    {
        var generation = ++_artGeneration;
        InMemoryRandomAccessStream? next = null;
        Exception? failure = null;
        try
        {
            next = new InMemoryRandomAccessStream();
            if (_model.CoverArt is BitmapSource image)
            {
                await WriteArtworkAsync(next, image);
                if (_disposed || generation != _artGeneration) { return; }
                _controls.DisplayUpdater.Thumbnail = RandomAccessStreamReference.CreateFromStream(next);
            }
            else _controls.DisplayUpdater.Thumbnail = null;
            if (_disposed || generation != _artGeneration) { return; }
            _controls.DisplayUpdater.Update(); var old = _artwork; _artwork = next; next = null; old?.Dispose();
        }
        catch (Exception error) { failure = error; }
        finally
        {
            try { next?.Dispose(); }
            catch (Exception error) { failure = failure is null ? error : new AggregateException(failure, error); }
            if (failure is not null && !_disposed) _model.Details = failure.Message;
        }
    }
    internal static async Task WriteArtworkAsync(InMemoryRandomAccessStream destination, BitmapSource image)
    {
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(image));
        using var bytes = new System.IO.MemoryStream(); png.Save(bytes);
        using (var output = destination.GetOutputStreamAt(0))
        using (var writer = new DataWriter(output))
        { writer.WriteBytes(bytes.ToArray()); await writer.StoreAsync(); writer.DetachStream(); }
        destination.Seek(0);
    }
    public bool MetadataMatches(PlayerViewModel model) => _controls.DisplayUpdater.MusicProperties.Title == model.Title && _controls.DisplayUpdater.MusicProperties.Artist == model.Artist;
    public void Dispose()
    {
        if (_disposed) return; _disposed = true; ++_artGeneration;
        _model.PropertyChanged -= OnChanged; _controls.ButtonPressed -= OnButton;
        _controls.IsEnabled = false; _controls.DisplayUpdater.ClearAll(); _artwork?.Dispose();
    }
    // Documented desktop ISystemMediaTransportControlsInterop, needed because WPF has no CoreWindow.
    [ComImport, Guid("DDB0472D-C911-4A1F-86D9-DC3D71A95F5A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ISystemMediaTransportControlsInterop
    {
        // IInspectable extends IUnknown with these three slots. GetForWindow is slot 6,
        // not slot 3; omitting them calls GetIids with an HWND as an output pointer.
        [PreserveSig] int GetIids(out uint count, out nint ids);
        [PreserveSig] int GetRuntimeClassName(out nint className);
        [PreserveSig] int GetTrustLevel(out int trustLevel);
        [PreserveSig] int GetForWindow(nint window, ref Guid iid, out nint controls);
    }
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("combase.dll", CharSet = CharSet.Unicode)] private static extern int WindowsCreateString(string source, int length, out nint value);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("combase.dll")] private static extern int WindowsDeleteString(nint value);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("combase.dll")] private static extern int RoGetActivationFactory(nint name, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out ISystemMediaTransportControlsInterop factory);
}
