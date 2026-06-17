using System.IO;

namespace RadioPlayer.Services;

/// <summary>Well-known file system locations and names used by the app.</summary>
public static class AppPaths
{
    /// <summary>Application folder name under %AppData%.</summary>
    public const string AppFolderName = "RadioPlayer";

    /// <summary>Settings file name.</summary>
    public const string SettingsFileName = "settings.json";

    /// <summary>Favorites file name.</summary>
    public const string FavoritesFileName = "favorites.json";

    /// <summary>Subfolder for cached API responses.</summary>
    public const string CacheFolderName = "cache";

    /// <summary>Full path of the per-user application data folder (%AppData%\RadioPlayer).</summary>
    public static string AppDataFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppFolderName);
}
