import Foundation

/// A window the user picked to snooze, as captured when they pressed the shortcut.
public struct WindowTarget: Sendable, Hashable {
    public var ref: String
    public var title: String
    public var app: Snooze.App
    public var frame: Snooze.Frame?

    public init(ref: String, title: String, app: Snooze.App, frame: Snooze.Frame?) {
        self.ref = ref
        self.title = title
        self.app = app
        self.frame = frame
    }
}

public enum HideFailure: Error, Sendable {
    /// The window closed before we could hide it.
    case gone
    /// The app put the window straight back, or ignored the request.
    case refused
}

/// The only OS boundary `Snoozer` depends on. The app implements it with the Accessibility API;
/// tests implement it in memory.
@MainActor
public protocol WindowSystem: AnyObject {
    /// Hides the window and confirms it is gone. On failure the window is left visible.
    func hide(_ target: WindowTarget) -> Result<Snooze.Method, HideFailure>
    /// Shows the window where it was, without activating it. True if it is back on screen.
    func restore(_ snooze: Snooze) -> Bool
    func probe(_ snooze: Snooze) -> LiveState
}

public enum SnoozeFailure: Error, Sendable {
    /// The snooze couldn't be saved, so the window was left alone.
    case storage
    case gone
    case refused
}

/// Windows that came back on their own schedule, and reminders for windows that closed.
public struct ReturnReport: Sendable {
    public var returned: [Snooze]
    public var closedReminders: [Snooze]
}

/// Owns the list of snoozed windows. Every change goes through here, in an order that never leaves
/// a window hidden without a record: save first, then hide; restore first, then forget.
@MainActor
public final class Snoozer {
    private let windows: WindowSystem
    private let store: SnoozeStore
    private let now: () -> Date

    public private(set) var snoozes: [Snooze] = []
    public var onChange: (() -> Void)?
    public var onReturn: ((ReturnReport) -> Void)?
    public var onStorageError: ((Error) -> Void)?

    public init(windows: WindowSystem, store: SnoozeStore, now: @escaping () -> Date = Date.init) {
        self.windows = windows
        self.store = store
        self.now = now
    }

    /// Loads what was saved last time. Call `tick()` afterwards to reconcile it with the windows that exist now.
    @discardableResult
    public func load() throws -> SnoozeStore.LoadResult {
        let result = try store.load()
        snoozes = result.snoozes
        onChange?()
        return result
    }

    public func snooze(_ target: WindowTarget, until dueAt: Date) -> Result<Snooze, SnoozeFailure> {
        var snooze = Snooze(
            id: UUID(),
            createdAt: now(),
            dueAt: dueAt,
            state: .hidden,
            method: .park,
            app: target.app,
            window: .init(ref: target.ref, title: target.title, frame: target.frame)
        )

        // The record goes to disk before the window disappears. If we crash in between, launch-time
        // reconcile finds the window still visible and drops the record. Nothing is lost.
        snoozes.append(snooze)
        guard save() else {
            snoozes.removeAll { $0.id == snooze.id }
            return .failure(.storage)
        }

        switch windows.hide(target) {
        case .failure(let failure):
            snoozes.removeAll { $0.id == snooze.id }
            save()
            return .failure(failure == .gone ? .gone : .refused)
        case .success(let method):
            if method != snooze.method, let index = snoozes.firstIndex(where: { $0.id == snooze.id }) {
                snooze.method = method
                snoozes[index] = snooze
                save()
            }
            onChange?()
            return .success(snooze)
        }
    }

    public func reschedule(_ id: UUID, to dueAt: Date) {
        guard let index = snoozes.firstIndex(where: { $0.id == id }) else { return }
        snoozes[index].dueAt = dueAt
        save()
        onChange?()
    }

    /// Brings one window back now, at the user's request. Returns the snooze if its window is on screen.
    @discardableResult
    public func bringBack(_ id: UUID) -> Snooze? {
        guard let snooze = snoozes.first(where: { $0.id == id }) else { return nil }
        let restored = snooze.state == .hidden && windows.restore(snooze)
        snoozes.removeAll { $0.id == id }
        save()
        onChange?()
        return restored ? snooze : nil
    }

    /// Brings every hidden window back and forgets all snoozes. Used for "Bring Back All", quitting,
    /// logging out and errors.
    @discardableResult
    public func bringBackAll() -> Int {
        let restored = snoozes.filter { $0.state == .hidden && windows.restore($0) }.count
        snoozes.removeAll()
        save()
        onChange?()
        return restored
    }

    /// Applies `Reconcile` to every snooze: restores what's due, forgets what the user already
    /// brought back, and keeps reminders for closed windows.
    public func tick() {
        let now = now()
        var returned: [Snooze] = []
        var closed: [Snooze] = []
        var next: [Snooze] = []
        var changed = false

        for snooze in snoozes {
            let live = snooze.state == .closed ? LiveState.gone : windows.probe(snooze)
            switch Reconcile.action(for: snooze, live: live, now: now) {
            case .keep:
                next.append(snooze)
            case .restore:
                if windows.restore(snooze) { returned.append(snooze) } else { closed.append(snooze) }
                changed = true
            case .notifyClosed:
                closed.append(snooze)
                changed = true
            case .drop:
                changed = true
            case .markClosed:
                var marked = snooze
                marked.state = .closed
                next.append(marked)
                changed = true
            }
        }

        guard changed else { return }
        snoozes = next
        save()
        onChange?()
        if !returned.isEmpty || !closed.isEmpty {
            onReturn?(ReturnReport(returned: returned, closedReminders: closed))
        }
    }

    public func nextCheck() -> Date? {
        Reconcile.nextCheck(dues: snoozes.map(\.dueAt), now: now())
    }

    @discardableResult
    private func save() -> Bool {
        do {
            try store.save(snoozes)
            return true
        } catch {
            onStorageError?(error)
            return false
        }
    }
}
