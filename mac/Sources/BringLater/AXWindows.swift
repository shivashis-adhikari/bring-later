import AppKit
import ApplicationServices
import BringLaterCore

enum CaptureRefusal: Error {
    case noWindow
    case fullScreen
    case notStandard
}

struct CapturedWindow {
    let element: AXUIElement
    let target: WindowTarget
    /// Where it is, in Cocoa coordinates, for placing the panel over it.
    let cocoaFrame: CGRect
}

/// Finds, parks, restores and watches other apps' windows through the Accessibility API.
///
/// There is no public way to hide one window of another app. Parking moves the window past the
/// corner of a display so only a sliver stays on screen, the same approach AeroSpace uses. Windows
/// that refuse to move are minimized instead.
@MainActor
final class AXWindows: WindowSystem {
    /// Parked windows keep less than this much of their area on screen.
    private static let parkedFraction: CGFloat = 0.02

    private var elements: [String: AXUIElement] = [:]
    private var observers: [pid_t: AXObserver] = [:]
    private var watched: [pid_t: Set<String>] = [:]

    /// A snoozed window was focused, destroyed or otherwise touched by someone other than us.
    var onWatchedWindowEvent: ((_ ref: String?, _ focused: Bool) -> Void)?

    static func ref(for id: CGWindowID) -> String { "cg:\(id)" }

    // MARK: Capture

    func captureFocused(in app: NSRunningApplication?) -> Result<CapturedWindow, CaptureRefusal> {
        guard let app, app.processIdentifier != ProcessInfo.processInfo.processIdentifier else { return .failure(.noWindow) }
        let appElement = AXUIElementCreateApplication(app.processIdentifier)
        AXUIElementSetMessagingTimeout(appElement, 1)
        guard let window: AXUIElement = appElement.value(kAXFocusedWindowAttribute) else { return .failure(.noWindow) }
        return capture(window, app: app)
    }

    private func capture(_ window: AXUIElement, app: NSRunningApplication) -> Result<CapturedWindow, CaptureRefusal> {
        AXUIElementSetMessagingTimeout(window, 1)
        if let fullScreen: Bool = window.value("AXFullScreen"), fullScreen {
            return .failure(.fullScreen)
        }
        let subrole: String? = window.value(kAXSubroleAttribute)
        guard subrole == nil || subrole == (kAXStandardWindowSubrole as String) else { return .failure(.notStandard) }
        guard let id = window.windowID, let frame = window.frame else { return .failure(.noWindow) }

        let appName = app.localizedName ?? "App"
        let title = (window.value(kAXTitleAttribute) as String?)?.trimmingCharacters(in: .whitespaces) ?? ""
        let ref = Self.ref(for: id)
        elements[ref] = window
        let target = WindowTarget(
            ref: ref,
            title: title.isEmpty ? appName : title,
            app: .init(id: app.bundleIdentifier ?? app.bundleURL?.path ?? appName, name: appName, pid: app.processIdentifier, started: app.launchDate),
            frame: .init(x: frame.minX, y: frame.minY, width: frame.width, height: frame.height)
        )
        return .success(CapturedWindow(element: window, target: target, cocoaFrame: Screens.cocoaRect(fromAX: frame)))
    }

    // MARK: WindowSystem

    func hide(_ target: WindowTarget) -> Result<Snooze.Method, HideFailure> {
        guard let window = elements[target.ref], window.isAlive, let frame = window.frame else { return .failure(.gone) }

        window.setPosition(parkingSpot(for: frame))
        if let parked = window.frame, Screens.visibleFraction(of: parked) <= Self.parkedFraction {
            watch(target)
            return .success(.park)
        }

        // It wouldn't move far enough. Put it back and minimize it instead.
        window.setPosition(frame.origin)
        if window.set(kAXMinimizedAttribute, true), (window.value(kAXMinimizedAttribute) as Bool?) == true {
            watch(target)
            return .success(.minimize)
        }
        return .failure(.refused)
    }

    func restore(_ snooze: Snooze) -> Bool {
        guard let window = element(for: snooze), window.isAlive else { return false }
        switch snooze.method {
        case .minimize:
            window.set(kAXMinimizedAttribute, false)
        case .park, .hide:
            let saved = snooze.window.frame.map { CGRect(x: $0.x, y: $0.y, width: $0.width, height: $0.height) }
            if let saved, Screens.visibleFraction(of: saved) > 0.2 {
                window.setPosition(saved.origin)
                window.setSize(saved.size)
            } else {
                // Its display is gone. Center it on the main one.
                let screen = NSScreen.main?.visibleFrame ?? .zero
                let size = saved?.size ?? window.frame?.size ?? .zero
                let area = Screens.axRect(fromCocoa: screen)
                window.setPosition(CGPoint(x: area.midX - size.width / 2, y: max(area.minY, area.midY - size.height / 2)))
            }
        }
        unwatch(snooze)
        return true
    }

    func probe(_ snooze: Snooze) -> LiveState {
        guard let app = NSRunningApplication(processIdentifier: snooze.app.pid), !app.isTerminated,
              sameLaunch(app, snooze.app.started) else { return .gone }
        guard let window = element(for: snooze) else {
            // The app is running but doesn't list the window, which happens when it's on another
            // Space. Keep waiting rather than calling it closed.
            return .hidden
        }
        guard window.isAlive else { return .gone }
        switch snooze.method {
        case .minimize:
            return (window.value(kAXMinimizedAttribute) as Bool?) == true ? .hidden : .visible
        case .park, .hide:
            guard let frame = window.frame else { return .hidden }
            return Screens.visibleFraction(of: frame) <= Self.parkedFraction * 2 ? .hidden : .visible
        }
    }

    // MARK: Activation

    func activate(_ snooze: Snooze) {
        guard let window = element(for: snooze) else { return }
        NSRunningApplication(processIdentifier: snooze.app.pid)?.activate()
        window.set(kAXMainAttribute, true)
        AXUIElementPerformAction(window, kAXRaiseAction as CFString)
    }

    // MARK: Private

    /// The corner to park at: the bottom corner of the window's display whose neighborhood
    /// overlaps other displays the least, so the window doesn't reappear on a second monitor.
    private func parkingSpot(for frame: CGRect) -> CGPoint {
        let screens = Screens.frames
        let center = CGPoint(x: frame.midX, y: frame.midY)
        let home = screens.first { $0.contains(center) } ?? screens.first ?? .zero
        let candidates = [
            CGPoint(x: home.maxX - 1, y: home.maxY - 1),
            CGPoint(x: home.minX - frame.width + 1, y: home.maxY - 1),
        ]
        return candidates.min { a, b in
            overlap(CGRect(origin: a, size: frame.size), excluding: home, in: screens)
                < overlap(CGRect(origin: b, size: frame.size), excluding: home, in: screens)
        } ?? candidates[0]
    }

    private func overlap(_ rect: CGRect, excluding home: CGRect, in screens: [CGRect]) -> CGFloat {
        screens.filter { $0 != home }.reduce(0) { total, screen in
            let intersection = screen.intersection(rect)
            return total + (intersection.isNull ? 0 : intersection.width * intersection.height)
        }
    }

    private func sameLaunch(_ app: NSRunningApplication, _ started: Date?) -> Bool {
        guard let started, let launched = app.launchDate else { return true }
        return abs(launched.timeIntervalSince(started)) < 1
    }

    private func element(for snooze: Snooze) -> AXUIElement? {
        if let cached = elements[snooze.window.ref] { return cached }
        // After a relaunch the cache is empty: find the window again by its window server id.
        let appElement = AXUIElementCreateApplication(snooze.app.pid)
        AXUIElementSetMessagingTimeout(appElement, 1)
        guard let windows: [AXUIElement] = appElement.value(kAXWindowsAttribute) else { return nil }
        let match = windows.first { $0.windowID.map(Self.ref(for:)) == snooze.window.ref }
        if let match { elements[snooze.window.ref] = match }
        return match
    }

    // MARK: Watching

    /// Watches the snoozed window's app so that focusing the window (from the Window menu or
    /// Mission Control) or closing it is noticed right away, not at the next tick.
    func watch(_ snoozes: [Snooze]) {
        let wanted = Dictionary(grouping: snoozes.filter { $0.state == .hidden }, by: \.app.pid).mapValues { Set($0.map(\.window.ref)) }
        for pid in observers.keys where wanted[pid] == nil {
            if let observer = observers.removeValue(forKey: pid) {
                CFRunLoopRemoveSource(CFRunLoopGetMain(), AXObserverGetRunLoopSource(observer), .defaultMode)
            }
        }
        watched = wanted
        for snooze in snoozes where snooze.state == .hidden {
            if let window = element(for: snooze) { observe(window, pid: snooze.app.pid) }
        }
    }

    private func watch(_ target: WindowTarget) {
        watched[target.app.pid, default: []].insert(target.ref)
        if let window = elements[target.ref] { observe(window, pid: target.app.pid) }
    }

    private func unwatch(_ snooze: Snooze) {
        watched[snooze.app.pid]?.remove(snooze.window.ref)
    }

    private func observe(_ window: AXUIElement, pid: pid_t) {
        let observer: AXObserver
        if let existing = observers[pid] {
            observer = existing
        } else {
            var created: AXObserver?
            guard AXObserverCreate(pid, axCallback, &created) == .success, let created else { return }
            observer = created
            observers[pid] = observer
            CFRunLoopAddSource(CFRunLoopGetMain(), AXObserverGetRunLoopSource(observer), .defaultMode)
            let appElement = AXUIElementCreateApplication(pid)
            AXObserverAddNotification(observer, appElement, kAXFocusedWindowChangedNotification as CFString, Unmanaged.passUnretained(self).toOpaque())
        }
        AXObserverAddNotification(observer, window, kAXUIElementDestroyedNotification as CFString, Unmanaged.passUnretained(self).toOpaque())
    }

    fileprivate func handle(windowID: CGWindowID?, notification: String) {
        if notification == kAXFocusedWindowChangedNotification as String {
            guard let windowID else { return }
            let ref = Self.ref(for: windowID)
            if watched.values.contains(where: { $0.contains(ref) }) {
                onWatchedWindowEvent?(ref, true)
            }
        } else {
            onWatchedWindowEvent?(nil, false)
        }
    }
}

private func axCallback(_ observer: AXObserver, _ element: AXUIElement, _ notification: CFString, _ refcon: UnsafeMutableRawPointer?) {
    guard let refcon else { return }
    let windows = Unmanaged<AXWindows>.fromOpaque(refcon).takeUnretainedValue()
    let name = notification as String
    let windowID = element.windowID
    // AX observers deliver on the run loop they were added to, which is the main one.
    MainActor.assumeIsolated {
        windows.handle(windowID: windowID, notification: name)
    }
}
