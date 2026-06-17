using System.Net.Http;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RadioPlayer.Services;
using RadioPlayer.ViewModels;
using RadioPlayer.Views;

namespace RadioPlayer;

/// <summary>
/// Application entry point: configures dependency injection, global exception
/// handling and window state persistence.
/// </summary>
public partial class App : Application
{
    /// <summary>User-Agent sent to the Radio Browser API and stream servers (API etiquette).</summary>
    public const string UserAgent = "RadioPlayer/1.0";

    private ServiceProvider? _services;
    private ILogger<App>? _logger;

    /// <inheritdoc />
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _services = ConfigureServices();
        _logger = _services.GetRequiredService<ILogger<App>>();
        RegisterGlobalExceptionHandlers();

        var settingsService = _services.GetRequiredService<ISettingsService>();
        await settingsService.LoadAsync();

        var mainViewModel = _services.GetRequiredService<MainViewModel>();
        var window = new MainWindow(settingsService)
        {
            DataContext = mainViewModel,
        };
        MainWindow = window;
        window.Show();

        // Load data after the UI is visible so startup stays fast (NFR-1.3).
        await mainViewModel.InitializeAsync();
    }

    /// <inheritdoc />
    protected override void OnExit(ExitEventArgs e)
    {
        _services?.Dispose();
        base.OnExit(e);
    }

    private static ServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        services.AddLogging(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Information);
            builder.AddDebug();
        });

        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IFavoritesService, FavoritesService>();
        services.AddSingleton<ICacheService, FileCacheService>();

        services.AddSingleton<IAudioPlayerService>(provider =>
        {
            // Radio streams never complete, so this client must not time out mid-stream.
            var streamingClient = new HttpClient(new SocketsHttpHandler
            {
                ConnectTimeout = TimeSpan.FromSeconds(10),
            })
            {
                Timeout = Timeout.InfiniteTimeSpan,
            };
            streamingClient.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
            return new AudioPlayerService(
                streamingClient,
                provider.GetRequiredService<ILogger<AudioPlayerService>>());
        });

        services.AddSingleton<IRadioBrowserService>(provider =>
        {
            var apiClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            apiClient.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
            return new RadioBrowserService(
                apiClient,
                provider.GetRequiredService<ICacheService>(),
                provider.GetRequiredService<ILogger<RadioBrowserService>>());
        });

        services.AddSingleton<PlayerViewModel>();
        services.AddSingleton<StationListViewModel>();
        services.AddSingleton<MainViewModel>();

        return services.BuildServiceProvider();
    }

    private void RegisterGlobalExceptionHandlers()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            _logger?.LogCritical(args.ExceptionObject as Exception, "Unhandled AppDomain exception");

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            _logger?.LogError(args.Exception, "Unobserved task exception");
            args.SetObserved();
        };
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _logger?.LogError(e.Exception, "Unhandled UI exception");
        e.Handled = true;
        MessageBox.Show(
            "Something went wrong, but the app will keep running.\n\n" + e.Exception.Message,
            "RadioPlayer",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }
}
