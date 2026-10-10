using System.ComponentModel;
using System.IO;
using System.Windows;
using Player.App.Resources;
using Player.App.ViewModels;
using Player.App.Views;

namespace Player.App.Services.Windows;

internal sealed class DesktopPanelController : IDisposable
{
    private readonly MainWindow _main;
    private PlayerViewModel? _model;
    private bool _enabled, _disposed, _shown;
    internal DesktopPanelWindow? Panel { get; private set; }
    internal DesktopPanelController(MainWindow main)
    {
        _main = main;
        main.StateChanged += Changed; main.IsVisibleChanged += VisibilityChanged; main.IsEnabledChanged += VisibilityChanged;
        main.DataContextChanged += ContextChanged; main.Closed += Closed;
        main.Loaded += Loaded;
    }
    private void ContextChanged(object sender, DependencyPropertyChangedEventArgs args)
    {
        if (_model is not null) _model.PropertyChanged -= ModelChanged;
        _model = args.NewValue as PlayerViewModel;
        if (_model is not null) _model.PropertyChanged += ModelChanged;
        if (Panel is not null) Panel.DataContext = _model;
        Sync();
    }
    private void ModelChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(PlayerViewModel.Initialized) || (args.PropertyName == nameof(PlayerViewModel.WindowSettings) && _enabled != _model?.WindowSettings.DesktopPanelEnabled)) Sync();
    }
    private void Changed(object? sender, EventArgs args) => Sync();
    private void VisibilityChanged(object sender, DependencyPropertyChangedEventArgs args) => Sync();
    private void Closed(object? sender, EventArgs args) => Dispose();
    private void Loaded(object sender, RoutedEventArgs args) { _shown = true; Sync(); }
    private void Sync()
    {
        if (_disposed) return;
        _enabled = _model?.WindowSettings.DesktopPanelEnabled == true;
        try
        {
            if (_shown && _enabled && _model?.Initialized == true && _main.IsEnabled && (_main.WindowState == WindowState.Minimized || !_main.IsVisible))
            {
                Panel ??= new DesktopPanelWindow(_main) { DataContext = _model };
                if (!Panel.IsVisible) Panel.Reveal();
            }
            else Panel?.Conceal();
        }
        catch (Exception error) when (error is IOException or Win32Exception or InvalidOperationException)
        { Panel?.Conceal(); if (_model is not null) { _model.Message = Strings.Get("DesktopPanelUnavailable"); _model.Details = error.Message; } }
    }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        _main.StateChanged -= Changed; _main.IsVisibleChanged -= VisibilityChanged; _main.IsEnabledChanged -= VisibilityChanged;
        _main.DataContextChanged -= ContextChanged; _main.Closed -= Closed;
        _main.Loaded -= Loaded;
        if (_model is not null) _model.PropertyChanged -= ModelChanged;
        Panel?.Close(); Panel = null; _model = null;
    }
}
