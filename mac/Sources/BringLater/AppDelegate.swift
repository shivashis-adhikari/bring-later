import AppKit
import BringLaterCore
import os
import SwiftUI

/// Wires the pieces together: shortcut, menu bar item, panel, the snoozer and its timer,
/// notifications, and bringing every window back when the app quits or the user logs out.
@MainActor
final class AppDelegate: NSObject, NSApplicationDelegate, NSMenuDelegate {
    private let windows = AXWindows()
    private let hotkey = Hotkey()
    private let notifier = Notifier()
    private lazy var snoozer = Snoozer(windows: windows, store: SnoozeStore(directory: Self.dataDirectory))
    private var statusItem: NSStatusItem?
    private var panel: SnoozePanel?
    private var settingsWindow: NSWindow?
    private var accessWindow: AccessWindow?
    private var timer: DispatchSourceTimer?
    private var recentlyReturned: [UUID: Snooze] = [:]
    /// The app that was frontmost when the menu opened; the menu itself doesn't take focus.
    private var menuTarget: NSRunningApplication?

    private static let dataDirectory = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
        .appendingPathComponent("BringLater", isDirectory: true)

    // MARK: Lifecycle

    func applicationDidFinishLaunching(_ notification: Notification) {
        setUpStatusItem()

        snoozer.onChange = { [weak self] in self?.snoozesChanged() }
        snoozer.onReturn = { [weak self] report in self?.returned(report) }
        snoozer.onStorageError = { error in Logger.app.error("Couldn't save snoozes: \(error.localizedDescription, privacy: .public)") }
        windows.onWatchedWindowEvent = { [weak self] ref, focused in self?.watchedWindowEvent(ref: ref, focused: focused) }
        hotkey.onPress = { [weak self] in self?.openPanel(for: NSWorkspace.shared.frontmostApplication) }
        notifier.onAction = { [weak self] action, id in self?.notificationAction(action, id: id) }

        let center = NSWorkspace.shared.notificationCenter
        center.addObserver(forName: NSWorkspace.didWakeNotification, object: nil, queue: .main) { [weak self] _ in
            MainActor.assumeIsolated { self?.tick() }
        }
        center.addObserver(forName: NSWorkspace.activeSpaceDidChangeNotification, object: nil, queue: .main) { [weak self] _ in
            MainActor.assumeIsolated { self?.tick() }
        }
        center.addObserver(forName: NSWorkspace.didTerminateApplicationNotification, object: nil, queue: .main) { [weak self] _ in
            MainActor.assumeIsolated { self?.tick() }
        }
        center.addObserver(forName: NSWorkspace.willPowerOffNotification, object: nil, queue: .main) { [weak self] _ in
            MainActor.assumeIsolated { self?.bringEverythingBack(reason: "log out") }
        }
        NotificationCenter.default.addObserver(forName: .NSSystemClockDidChange, object: nil, queue: .main) { [weak self] _ in
            MainActor.assumeIsolated { self?.tick() }
        }
        NotificationCenter.default.addObserver(forName: .NSSystemTimeZoneDidChange, object: nil, queue: .main) { [weak self] _ in
            MainActor.assumeIsolated { self?.tick() }
        }

        let updated = Preferences.lastVersion.map { $0 != Updates.current } ?? false
        Preferences.lastVersion = Updates.current
        if Access.isGranted {
            start()
        } else {
            if updated { Access.resetStaleGrant() }
            let window = AccessWindow(afterUpdate: updated) { [weak self] in
                self?.accessWindow = nil
                self?.start()
            }
            accessWindow = window
            window.show()
        }
    }

    /// Everything that needs Accessibility access.
    private func start() {
        do {
            let loaded = try snoozer.load()
            if let aside = loaded.quarantined {
                Logger.app.error("Snooze file was unreadable and was moved to \(aside.path, privacy: .public)")
            }
        } catch {
            Logger.app.error("Couldn't read snoozes: \(error.localizedDescription, privacy: .public)")
        }
        // Anything still parked from last time is reconciled now: overdue windows come back,
        // windows the user already brought back are forgotten.
        tick()
        if !hotkey.register(Preferences.shortcut) {
            notifier.message(L("notify.shortcutTakenTitle"), L("notify.shortcutTakenBody", Preferences.shortcut.display))
        }
        notifier.requestPermission()
    }

    func applicationShouldTerminate(_ sender: NSApplication) -> NSApplication.TerminateReply {
        bringEverythingBack(reason: "quit")
        return .terminateNow
    }

    // MARK: Snoozing

    private func openPanel(for app: NSRunningApplication?) {
        if let panel {
            panel.dismiss()
            return
        }
        let now = Date()
        switch windows.captureFocused(in: app) {
        case .failure(let refusal):
            let copy = Copy.refusal(refusal, shortcut: Preferences.shortcut)
            let model = PanelModel(now: now, prefs: Preferences.timePrefs, title: "", subtitle: "", icon: nil, refusal: copy)
            show(SnoozePanel(model: model, onChoose: nil), over: NSScreen.main?.visibleFrame ?? .zero)
        case .success(let window):
            let target = window.target
            let model = PanelModel(now: now, prefs: Preferences.timePrefs, title: target.title, subtitle: target.app.name, icon: app?.icon)
            var panel: SnoozePanel!
            panel = SnoozePanel(model: model) { [weak self] due in
                guard let self else { return }
                switch snoozer.snooze(target, until: due) {
                case .success(let snooze):
                    Logger.app.info("Snoozed \(target.ref, privacy: .public) with \(snooze.method.rawValue, privacy: .public)")
                    panel.dismiss()
                case .failure(let failure):
                    panel.showError(Copy.failure(failure))
                }
            }
            show(panel, over: window.cocoaFrame)
        }
    }

    private func changeTime(_ id: UUID) {
        guard let snooze = snoozer.snoozes.first(where: { $0.id == id }) else { return }
        let now = Date()
        let subtitle = L("panel.changeSubtitle", Format.when(snooze.dueAt, now: now).lowercasedFirst)
        let icon = NSRunningApplication(processIdentifier: snooze.app.pid)?.icon
        let model = PanelModel(now: now, prefs: Preferences.timePrefs, title: snooze.window.title, subtitle: subtitle, icon: icon)
        var panel: SnoozePanel!
        panel = SnoozePanel(model: model) { [weak self] due in
            self?.snoozer.reschedule(id, to: due)
            panel.dismiss()
        }
        show(panel, over: NSScreen.main?.visibleFrame ?? .zero)
    }

    private func show(_ panel: SnoozePanel, over frame: CGRect) {
        self.panel?.dismiss()
        self.panel = panel
        panel.onClose = { [weak self, weak panel] in
            if self?.panel === panel { self?.panel = nil }
        }
        panel.present(over: frame)
    }

    // MARK: Coming back

    private func tick() {
        guard Access.isGranted else { return }
        snoozer.tick()
        schedule()
    }

    /// A wall-clock timer that isn't coalesced by App Nap, capped at a minute so a missed wake or
    /// clock change costs at most that.
    private func schedule() {
        timer?.cancel()
        timer = nil
        guard let next = snoozer.nextCheck() else { return }
        let timer = DispatchSource.makeTimerSource(flags: .strict, queue: .main)
        timer.schedule(wallDeadline: .now() + max(0.25, next.timeIntervalSinceNow), leeway: .milliseconds(100))
        timer.setEventHandler { [weak self] in
            MainActor.assumeIsolated { self?.tick() }
        }
        timer.resume()
        self.timer = timer
    }

    private func snoozesChanged() {
        windows.watch(snoozer.snoozes)
        updateStatusItem()
        schedule()
    }

    private func returned(_ report: ReturnReport) {
        for snooze in report.returned {
            recentlyReturned[snooze.id] = snooze
        }
        if recentlyReturned.count > 50 {
            recentlyReturned = recentlyReturned.filter { entry in report.returned.contains { $0.id == entry.key } }
        }
        notifier.report(report, now: Date())
        if Preferences.bringToFront, let last = report.returned.last {
            windows.activate(last)
        }
    }

    private func watchedWindowEvent(ref: String?, focused: Bool) {
        // Focusing a parked window (from the Window menu or Mission Control) means the user went
        // looking for it: bring it back now instead of leaving it off screen.
        if focused, let ref, let snooze = snoozer.snoozes.first(where: { $0.window.ref == ref && $0.state == .hidden }) {
            snoozer.bringBack(snooze.id)
            return
        }
        tick()
    }

    private func notificationAction(_ action: NotificationAction, id: UUID) {
        guard let snooze = recentlyReturned[id] else { return }
        switch action {
        case .show:
            windows.activate(snooze)
        case .snoozeAgain:
            resnooze(snooze)
        }
    }

    private func resnooze(_ snooze: Snooze) {
        let target = WindowTarget(ref: snooze.window.ref, title: snooze.window.title, app: snooze.app, frame: snooze.window.frame)
        let due = LocalTime.floorToMinute(Date()).addingTimeInterval(3600)
        if case .failure(let failure) = snoozer.snooze(target, until: due) {
            notifier.message(L("notify.againFailed"), Copy.failure(failure))
        } else {
            recentlyReturned[snooze.id] = nil
        }
    }

    private func bringEverythingBack(reason: String) {
        let count = snoozer.bringBackAll()
        Logger.app.info("Brought back \(count) windows (\(reason, privacy: .public))")
    }

    // MARK: Menu bar

    private func setUpStatusItem() {
        let item = NSStatusBar.system.statusItem(withLength: NSStatusItem.squareLength)
        let image = NSImage(named: "MenuBarTemplate")
        image?.isTemplate = true
        image?.accessibilityDescription = Copy.appName
        item.button?.image = image
        let menu = NSMenu()
        menu.delegate = self
        item.menu = menu
        statusItem = item
        updateStatusItem()
    }

    private func updateStatusItem() {
        let count = snoozer.snoozes.count
        statusItem?.button?.toolTip = count == 0 ? Copy.appName : L("menu.tooltip", String(count))
    }

    func menuNeedsUpdate(_ menu: NSMenu) {
        menu.removeAllItems()
        let front = NSWorkspace.shared.frontmostApplication
        menuTarget = front?.processIdentifier == ProcessInfo.processInfo.processIdentifier ? nil : front
        let targetTitle: String? = {
            guard Access.isGranted, case .success(let window) = windows.captureFocused(in: menuTarget) else { return nil }
            return window.target.title
        }()

        let snooze = NSMenuItem(title: Copy.snoozeTarget(targetTitle), action: targetTitle == nil ? nil : #selector(snoozeFromMenu), keyEquivalent: "")
        snooze.target = self
        let shortcut = Preferences.shortcut
        if let key = shortcut.keyLabel.lowercased().first, shortcut.keyLabel.count == 1 {
            snooze.keyEquivalent = String(key)
            snooze.keyEquivalentModifierMask = shortcut.flags
        }
        menu.addItem(snooze)
        menu.addItem(.separator())

        let now = Date()
        let snoozes = snoozer.snoozes.sorted { $0.dueAt < $1.dueAt }
        if snoozes.isEmpty {
            let empty = NSMenuItem(title: L("menu.empty"), action: nil, keyEquivalent: "")
            empty.isEnabled = false
            menu.addItem(empty)
        } else {
            menu.addItem(NSMenuItem.sectionHeader(title: L("menu.snoozed")))
            for snooze in snoozes {
                let row = NSMenuItem(title: Copy.trim(snooze.window.title, 48), action: nil, keyEquivalent: "")
                let when = Format.when(snooze.dueAt, now: now)
                if #available(macOS 14.4, *) {
                    row.subtitle = snooze.state == .closed ? L("menu.closed", when.lowercasedFirst) : "\(snooze.app.name) · \(when)"
                }
                row.image = icon(for: snooze)
                let submenu = NSMenu()
                if snooze.state == .hidden {
                    submenu.addItem(item(L("menu.bringBack"), #selector(bringBackFromMenu(_:)), snooze.id))
                }
                submenu.addItem(item(L("menu.changeTime"), #selector(changeTimeFromMenu(_:)), snooze.id))
                row.submenu = submenu
                menu.addItem(row)
            }
            menu.addItem(.separator())
            menu.addItem(item(L("menu.bringBackAll"), #selector(bringBackAllFromMenu), nil))
        }

        menu.addItem(.separator())
        let settings = item(L("menu.settings"), #selector(openSettings), nil)
        settings.keyEquivalent = ","
        menu.addItem(settings)
        menu.addItem(item(L("menu.checkUpdates"), #selector(checkForUpdatesFromMenu), nil))
        menu.addItem(.separator())
        let quit = NSMenuItem(title: L("menu.quit"), action: #selector(NSApplication.terminate(_:)), keyEquivalent: "q")
        menu.addItem(quit)
    }

    private func item(_ title: String, _ action: Selector, _ id: UUID?) -> NSMenuItem {
        let item = NSMenuItem(title: title, action: action, keyEquivalent: "")
        item.target = self
        item.representedObject = id
        return item
    }

    private func icon(for snooze: Snooze) -> NSImage? {
        guard let icon = NSRunningApplication(processIdentifier: snooze.app.pid)?.icon?.copy() as? NSImage else { return nil }
        icon.size = NSSize(width: 16, height: 16)
        return icon
    }

    @objc private func snoozeFromMenu() {
        // Wait for the menu to close so the panel can take the keyboard.
        let target = menuTarget
        DispatchQueue.main.async { [weak self] in self?.openPanel(for: target) }
    }

    @objc private func bringBackFromMenu(_ sender: NSMenuItem) {
        guard let id = sender.representedObject as? UUID, let snooze = snoozer.bringBack(id) else { return }
        windows.activate(snooze)
    }

    @objc private func changeTimeFromMenu(_ sender: NSMenuItem) {
        guard let id = sender.representedObject as? UUID else { return }
        DispatchQueue.main.async { [weak self] in self?.changeTime(id) }
    }

    @objc private func bringBackAllFromMenu() {
        snoozer.bringBackAll()
    }

    @objc private func checkForUpdatesFromMenu() {
        openSettings()
        Task { await settingsModel?.checkForUpdates() }
    }

    // MARK: Settings

    private var settingsModel: SettingsModel?

    @objc func openSettings() {
        if let settingsWindow {
            NSApp.activate()
            settingsWindow.makeKeyAndOrderFront(nil)
            return
        }
        let model = SettingsModel()
        model.applyShortcut = { [weak self] shortcut in self?.hotkey.register(shortcut) ?? false }
        model.pauseShortcut = { [weak self] paused in
            if paused { self?.hotkey.unregister() } else { self?.hotkey.register(Preferences.shortcut) }
        }
        settingsModel = model

        let window = NSWindow(contentRect: .zero, styleMask: [.titled, .closable], backing: .buffered, defer: false)
        window.title = L("settings.windowTitle")
        window.isReleasedWhenClosed = false
        window.contentViewController = NSHostingController(rootView: SettingsView(model: model))
        window.center()
        NotificationCenter.default.addObserver(forName: NSWindow.willCloseNotification, object: window, queue: .main) { [weak self] _ in
            MainActor.assumeIsolated {
                self?.settingsWindow = nil
                self?.settingsModel = nil
            }
        }
        settingsWindow = window
        NSApp.activate()
        window.makeKeyAndOrderFront(nil)
    }
}

extension String {
    /// "Today at 7:00 PM" → "today at 7:00 PM", for use mid-sentence.
    var lowercasedFirst: String { prefix(1).lowercased() + dropFirst() }
}
