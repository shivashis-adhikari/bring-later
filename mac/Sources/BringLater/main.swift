import AppKit

// A menu bar app: no Dock icon (LSUIElement in Info.plist), no main window.
let app = NSApplication.shared
let delegate = MainActor.assumeIsolated { AppDelegate() }
app.delegate = delegate
app.run()
