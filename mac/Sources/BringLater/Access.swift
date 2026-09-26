import AppKit
import ApplicationServices
import SwiftUI

/// The Accessibility permission, which Bring Later needs to move other apps' windows.
@MainActor
enum Access {
    static var isGranted: Bool { AXIsProcessTrusted() }

    static func openSettings() {
        NSWorkspace.shared.open(URL(string: "x-apple.systempreferences:com.apple.preference.security?Privacy_Accessibility")!)
    }

    /// Unsigned builds are identified by a hash of the binary, so an update looks like a new app
    /// and the old grant silently stops working. Clearing the stale entry lets macOS ask again.
    static func resetStaleGrant() {
        guard let bundleID = Bundle.main.bundleIdentifier else { return }
        let reset = Process()
        reset.executableURL = URL(fileURLWithPath: "/usr/bin/tccutil")
        reset.arguments = ["reset", "Accessibility", bundleID]
        try? reset.run()
        reset.waitUntilExit()
    }
}

/// Asks for Accessibility access and closes itself once it's granted.
@MainActor
final class AccessWindow {
    private let window: NSWindow
    private var timer: Timer?

    init(afterUpdate: Bool, onGranted: @escaping @MainActor () -> Void) {
        window = NSWindow(contentRect: .zero, styleMask: [.titled, .closable], backing: .buffered, defer: false)
        window.title = "Bring Later"
        window.isReleasedWhenClosed = false
        window.contentView = NSHostingView(rootView: AccessView(afterUpdate: afterUpdate))
        window.center()
        timer = Timer.scheduledTimer(withTimeInterval: 1, repeats: true) { [weak self] _ in
            MainActor.assumeIsolated {
                guard Access.isGranted else { return }
                self?.close()
                onGranted()
            }
        }
    }

    func show() {
        NSApp.activate()
        window.makeKeyAndOrderFront(nil)
    }

    func close() {
        timer?.invalidate()
        timer = nil
        window.close()
    }
}

private struct AccessView: View {
    let afterUpdate: Bool

    var body: some View {
        VStack(alignment: .leading, spacing: 14) {
            Image(nsImage: NSApp.applicationIconImage)
                .resizable()
                .frame(width: 64, height: 64)
            Text(afterUpdate ? L("access.titleAgain") : L("access.title"))
                .font(.title2.weight(.semibold))
            Text(afterUpdate ? L("access.bodyAgain") : L("access.body"))
                .foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
            Text(L("access.steps"))
                .font(.callout)
                .foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
            HStack {
                ProgressView().controlSize(.small)
                Text(L("access.waiting")).font(.callout).foregroundStyle(.secondary)
                Spacer()
                Button(L("settings.openSystemSettings")) { Access.openSettings() }
                    .keyboardShortcut(.defaultAction)
            }
            .padding(.top, 4)
        }
        .padding(24)
        .frame(width: 440)
    }
}
