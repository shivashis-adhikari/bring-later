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
            Text(afterUpdate ? "Turn Accessibility Access Back On" : "Allow Accessibility Access")
                .font(.title2.weight(.semibold))
            Text(afterUpdate
                 ? "macOS turns off Accessibility access when Bring Later is updated. Turn it on again to keep snoozing windows."
                 : "Bring Later uses Accessibility access to hide and restore other apps' windows. It reads window titles so you can tell them apart, and nothing else.")
                .foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
            Text("In System Settings, turn on Bring Later under Privacy & Security › Accessibility. If it's already on, remove it with the minus button and add it again.")
                .font(.callout)
                .foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
            HStack {
                ProgressView().controlSize(.small)
                Text("Waiting for access…").font(.callout).foregroundStyle(.secondary)
                Spacer()
                Button("Open System Settings") { Access.openSettings() }
                    .keyboardShortcut(.defaultAction)
            }
            .padding(.top, 4)
        }
        .padding(24)
        .frame(width: 440)
    }
}
