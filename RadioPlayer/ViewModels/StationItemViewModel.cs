using CommunityToolkit.Mvvm.ComponentModel;
using RadioPlayer.Models;

namespace RadioPlayer.ViewModels;

/// <summary>
/// Row item for the station list: wraps a <see cref="RadioStation"/> and adds a
/// live favorite flag.
/// </summary>
public sealed partial class StationItemViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isFavorite;

    /// <summary>Creates a row item.</summary>
    public StationItemViewModel(RadioStation station, bool isFavorite)
    {
        Station = station;
        IsFavorite = isFavorite;
    }

    /// <summary>The wrapped station.</summary>
    public RadioStation Station { get; }
}
