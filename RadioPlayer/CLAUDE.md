# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Stack

- C# 12, .NET 8.0-windows, WPF (single-window app)
- MVVM via **CommunityToolkit.Mvvm** (source generators: `[ObservableProperty]`, `[RelayCommand]`)
- DI via **Microsoft.Extensions.DependencyInjection** — all services registered as singletons in `App.xaml.cs`
- Audio via **NAudio** — custom ICY-aware MP3 pipeline + Media Foundation fallback for AAC/others
- Tests via **xUnit + Moq** — all services must be mocked in unit tests (no integration tests)

## Architecture

**Startup order matters:** `App.OnStartup` builds the DI container before creating `MainWindow`. Never set `StartupUri` in App.xaml — window is instantiated in code after DI is ready.

**ViewModel hierarchy:** `MainViewModel` composes `PlayerViewModel` + `StationListViewModel`. Cross-VM communication uses events (e.g., `StationListViewModel.PlayRequested` → `MainViewModel` → `PlayerViewModel.PlayStationAsync`). Avoid direct VM-to-VM references.

**Async initialization:** ViewModels use `InitializeAsync()` called after the window is visible — never do heavy work in constructors.

**Volume scaling:** `PlayerViewModel.Volume` is 0–100 for UI bindings; divide by 100 before passing to `IAudioPlayerService`.

## Code Style (.editorconfig enforced)

- Private fields: `_camelCase` (underscore prefix, lowercase — enforced as warning)
- File-scoped namespaces: `namespace Foo;` not `namespace Foo { }` (warning)
- `var` when the type is apparent from the right-hand side
- Explicit braces on all control-flow blocks (no braceless `if`)
- XAML/JSON/XML: 2-space indent; C#: 4-space indent
- `TreatWarningsAsErrors` is on — zero-warning policy

## Audio Streaming Gotchas

- The streaming `HttpClient` uses `Timeout.InfiniteTimeSpan` — never reuse the API client for streaming.
- ICY streams use a custom `IcyReadFullyStream` pipeline; AAC/other formats fall back to Media Foundation.
- Auto-reconnect retries up to 3 times with exponential backoff — don't add extra retry layers above `AudioPlayerService`.
- Playback events arrive on background threads; use the captured `SynchronizationContext` in `PlayerViewModel` to marshal to UI thread.

## Testing

```
dotnet test
```

- Mock all service interfaces with Moq — no real file I/O or HTTP in unit tests
- Test project is `RadioPlayer.Tests/` — add tests alongside the service/model being tested
- Coverage is collected automatically via `coverlet.collector` when running `dotnet test`
