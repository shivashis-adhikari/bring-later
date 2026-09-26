# 0001 Native apps per platform, one shared behavior contract

Most of Bring Later is OS code: finding the focused window, hiding it, watching it, hotkeys, tray and menu bar, notifications. The logic that can be shared (presets, typed times, crash recovery rules) is small.

We write each app natively (C# on Windows, Swift on macOS) and share behavior through JSON fixtures that both test suites load. A shared Rust core over FFI, Tauri, and Electron were considered and rejected: they add a toolchain or a runtime to save a few hundred lines, and a webview makes the UI look less native.
