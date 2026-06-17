# RadioPlayer

A WPF internet radio player for Windows built with .NET 8 and NAudio.

## Features

- Browse stations from [radio-browser.info](https://www.radio-browser.info/) — popular, by country, by genre
- Search stations by name
- Favorites list persisted locally
- ICY metadata support (now-playing track info from stream headers)
- AAC/MP3 streaming via NAudio with Media Foundation fallback
- Auto-reconnect on stream drop (exponential backoff, up to 3 retries)
- Volume control and mute toggle
- Keyboard shortcuts: `Space` play/stop, `Ctrl+F` focus search, `Enter` play selected

## Requirements

- Windows 10/11
- [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)

## Build & Run

```
dotnet build
dotnet run --project RadioPlayer
```

```
dotnet test
```

## Stack

| Concern | Library |
|---|---|
| UI | WPF (.NET 8) |
| MVVM | CommunityToolkit.Mvvm 8.4 |
| DI | Microsoft.Extensions.DependencyInjection |
| Audio | NAudio 2.3 |
| Tests | xUnit + Moq |

## Planned

- Stream recorder
- Equalizer
- System tray / minimize to tray
- Dark / light theme toggle
- Sleep timer
- Installer (MSI/MSIX)
