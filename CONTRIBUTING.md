# Contributing

Bug reports and pull requests are welcome. For anything bigger than a fix, open an issue first so we can agree on the approach before you spend time on it.

## Layout

| Path | What it is |
|---|---|
| `fixtures/` | The behavior both apps must match: presets, typed times, crash recovery. Loaded by both test suites. |
| `windows/src/BringLater.Core` | Platform-neutral C#: the grammar, presets, store and `Snoozer`. |
| `windows/src/BringLater` | The Windows app (WPF, .NET 10). All Win32 code is in `Win32/`. |
| `windows/tools/Screenshots` | Renders the real Windows UI to PNG in CI. |
| `mac/Sources/BringLaterCore` | The same Core in Swift. |
| `mac/Sources/BringLater` | The macOS menu bar app (AppKit and SwiftUI). All Accessibility code is in `AX*.swift`. |
| `site/` | The website, deployed to GitHub Pages. |
| `docs/decisions/` | Why things are the way they are. |

## Build and test

Windows (builds on macOS and Linux too, through `EnableWindowsTargeting`):

```bash
dotnet test --project windows/tests/BringLater.Core.Tests
dotnet build windows/BringLater.slnx
dotnet publish windows/src/BringLater -c Release -r win-x64
```

macOS:

```bash
swift test --package-path mac
mac/scripts/bundle.sh
```

## Rules of the road

- **A change to presets or typed times starts with a fixture.** Add the case to `fixtures/`, watch it fail on both platforms, then fix both.
- **Never hide a window before its snooze is saved.** The `Snoozer` enforces this; keep all changes to the list inside it.
- **Follow the platform.** System fonts, controls and wording. See [docs/copy.md](docs/copy.md).
- **Keep it small.** No new dependency without a note in `docs/decisions/` explaining why a few lines of code wouldn't do.
- **Commits** are imperative and describe one change: "Restore parked windows on wake".
