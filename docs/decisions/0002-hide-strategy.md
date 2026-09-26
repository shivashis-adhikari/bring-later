# 0002 How a window is hidden

**Windows:** `ShowWindowAsync(SW_HIDE)`. It removes the window and its taskbar button. Asynchronous so a hung app can't hang us. Windows hosted by `ApplicationFrameHost` (UWP) only hide their content, so they are minimized instead. Windows of elevated apps can't be touched from a normal process (UIPI) and are declined.

**macOS:** there is no public API to hide one window of another app. The window is parked: moved with `AXPosition` so only a sliver stays on screen past the corner of a display that has no neighbor there. Windows that refuse to move are minimized instead. Full-screen windows are declined in v0.1.

Every hide is verified. If the window is still visible afterwards, the snooze is rolled back and the user is told why.
