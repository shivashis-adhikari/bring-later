import BringLaterCore
import Foundation
import os
import UserNotifications

enum NotificationAction: String {
    case show
    case snoozeAgain
}

/// Notifications for windows coming back. One per window, grouped when several return at once.
@MainActor
final class Notifier: NSObject, UNUserNotificationCenterDelegate {
    private let center = UNUserNotificationCenter.current()
    private static let returnedCategory = "returned"
    var onAction: ((NotificationAction, UUID) -> Void)?

    override init() {
        super.init()
        center.delegate = self
        center.setNotificationCategories([
            UNNotificationCategory(
                identifier: Self.returnedCategory,
                actions: [
                    UNNotificationAction(identifier: NotificationAction.show.rawValue, title: L("notify.show"), options: [.foreground]),
                    UNNotificationAction(identifier: NotificationAction.snoozeAgain.rawValue, title: L("notify.snoozeHour")),
                ],
                intentIdentifiers: []
            ),
        ])
    }

    func requestPermission() {
        // macOS calls back on a background queue. @Sendable keeps the closure off the main actor,
        // which Swift 6 would otherwise enforce with a crash; it only logs.
        center.requestAuthorization(options: [.alert, .sound]) { @Sendable _, error in
            if let error { Logger.app.error("Notification permission: \(error.localizedDescription, privacy: .public)") }
        }
    }

    func report(_ report: ReturnReport, now: Date) {
        if report.returned.count == 1, let snooze = report.returned.first {
            post(title: snooze.window.title, body: L("notify.snoozedAt", snooze.app.name, snoozedAt(snooze, now: now)), category: Self.returnedCategory, id: snooze.id)
        } else if report.returned.count > 1 {
            let titles = report.returned.map { Copy.trim($0.window.title, 40) }
            let body = titles.count == 2 ? L("notify.two", titles[0], titles[1]) : L("notify.more", titles[0], titles[1], String(titles.count - 2))
            post(title: L("notify.manyBack", String(report.returned.count)), body: body, category: nil, id: nil)
        }
        for snooze in report.closedReminders {
            post(title: snooze.window.title, body: L("notify.closed", snooze.app.name), category: nil, id: nil)
        }
    }

    func message(_ title: String, _ body: String) {
        post(title: title, body: body, category: nil, id: nil)
    }

    private func snoozedAt(_ snooze: Snooze, now: Date) -> String {
        Calendar.current.isDate(snooze.createdAt, inSameDayAs: now)
            ? Format.time(snooze.createdAt)
            : Format.dateAndTime(snooze.createdAt)
    }

    private func post(title: String, body: String, category: String?, id: UUID?) {
        let content = UNMutableNotificationContent()
        content.title = title
        content.body = body
        content.sound = .default
        if let category { content.categoryIdentifier = category }
        if let id { content.userInfo = ["id": id.uuidString] }
        center.add(UNNotificationRequest(identifier: UUID().uuidString, content: content, trigger: nil))
    }

    nonisolated func userNotificationCenter(_ center: UNUserNotificationCenter, willPresent notification: UNNotification) async -> UNNotificationPresentationOptions {
        [.banner, .sound]
    }

    nonisolated func userNotificationCenter(_ center: UNUserNotificationCenter, didReceive response: UNNotificationResponse) async {
        guard let raw = response.notification.request.content.userInfo["id"] as? String, let id = UUID(uuidString: raw) else { return }
        let action: NotificationAction = response.actionIdentifier == NotificationAction.snoozeAgain.rawValue ? .snoozeAgain : .show
        await MainActor.run { onAction?(action, id) }
    }
}

extension Logger {
    static let app = Logger(subsystem: "io.github.shivashis-adhikari.BringLater", category: "app")
}
