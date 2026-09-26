import Foundation

/// What the OS says about a snoozed window right now.
public enum LiveState: String, Sendable {
    /// Still hidden where we left it.
    case hidden
    /// Back on screen, but not because of us: the user or the app brought it back.
    case visible
    /// The window no longer exists.
    case gone
}

public enum ReconcileAction: String, Sendable {
    case keep
    case restore
    /// Forget the snooze without a notification. The window is already back.
    case drop
    case markClosed
    /// Tell the user the reminder is due even though the window is gone, then forget it.
    case notifyClosed
}

/// Decides what to do with each stored snooze. The same rules run at launch (crash recovery)
/// and on every tick, so there is one code path for "time's up" and "we were away".
public enum Reconcile {
    public static func action(for snooze: Snooze, live: LiveState, now: Date) -> ReconcileAction {
        let due = snooze.dueAt <= now
        switch snooze.state {
        case .closed:
            return due ? .notifyClosed : .keep
        case .hidden:
            switch live {
            case .hidden: return due ? .restore : .keep
            case .visible: return .drop
            case .gone: return due ? .notifyClosed : .markClosed
            }
        }
    }

    /// When to look again. Capped so a missed wake or clock change costs at most `maxInterval`.
    public static func nextCheck(dues: [Date], now: Date, maxInterval: TimeInterval = 60) -> Date? {
        guard let soonest = dues.min() else { return nil }
        return max(now, min(soonest, now.addingTimeInterval(maxInterval)))
    }
}
