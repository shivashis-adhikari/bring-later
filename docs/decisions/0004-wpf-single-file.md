# 0004 WPF and a single-file exe on Windows

The first design used WinUI 3. The release has to be one `.exe` a user downloads and runs. WinUI 3 does not reliably support single-file publishing, and its XAML compiler only runs on Windows.

WPF on .NET 10 publishes as one self-contained `.exe` and compiles on macOS with `EnableWindowsTargeting`, so every change can be built locally before CI. Controls are styled by hand to match Windows 11 (Segoe UI Variable, Segoe Fluent Icons, rounded corners, light and dark themes) with the brand blue as the accent.
