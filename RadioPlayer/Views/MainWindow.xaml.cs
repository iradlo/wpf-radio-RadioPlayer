using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using RadioPlayer.Services;
using RadioPlayer.ViewModels;

namespace RadioPlayer.Views;

/// <summary>
/// Main application window. Code-behind contains only pure UI concerns:
/// window placement persistence, focus handling and keyboard shortcuts
/// that forward to view-model commands.
/// </summary>
public partial class MainWindow : Window
{
    private readonly ISettingsService _settingsService;

    /// <summary>Creates the window and restores the saved placement.</summary>
    public MainWindow(ISettingsService settingsService)
    {
        _settingsService = settingsService;
        InitializeComponent();
        RestoreWindowPlacement();
        Closing += (_, _) => SaveWindowPlacement();
    }

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    /// <summary>
    /// Global shortcuts: Ctrl+F focuses search, Space toggles play/stop
    /// (unless the user is typing in a text box).
    /// </summary>
    private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Space && e.OriginalSource is not TextBox && ViewModel is { } vm)
        {
            if (vm.Player.TogglePlayStopCommand.CanExecute(null))
            {
                vm.Player.TogglePlayStopCommand.Execute(null);
            }

            e.Handled = true;
        }
    }

    private void OnStationListDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel is { } vm && vm.StationList.PlaySelectedCommand.CanExecute(null))
        {
            vm.StationList.PlaySelectedCommand.Execute(null);
        }
    }

    private void RestoreWindowPlacement()
    {
        var settings = _settingsService.Settings;
        if (settings.WindowWidth is double width && settings.WindowHeight is double height)
        {
            Width = width;
            Height = height;
        }

        if (settings.WindowLeft is double left && settings.WindowTop is double top)
        {
            // Only restore a position that is still on a visible screen.
            if (left < SystemParameters.VirtualScreenWidth - 100 &&
                top < SystemParameters.VirtualScreenHeight - 100)
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                Left = left;
                Top = top;
            }
        }
    }

    private void SaveWindowPlacement()
    {
        var settings = _settingsService.Settings;
        if (WindowState == WindowState.Normal)
        {
            settings.WindowLeft = Left;
            settings.WindowTop = Top;
            settings.WindowWidth = Width;
            settings.WindowHeight = Height;
        }

        // Fire-and-forget is acceptable on shutdown; errors are logged inside the service.
        _ = _settingsService.SaveAsync();
    }
}
