import Foundation
import Testing
@testable import BringLaterCore

@MainActor
struct SnoozerTests {
    private let start = Date(timeIntervalSince1970: 1_790_150_400)
    private let directory = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
    private let windows = FakeWindows()
    private let clock: Clock
    private let store: SnoozeStore
    private let snoozer: Snoozer

    init() {
        clock = Clock(now: start)
        store = SnoozeStore(directory: directory)
        let clock = clock
        snoozer = Snoozer(windows: windows, store: store, now: { clock.now })
    }

    private func target(_ ref: String = "cg:1") -> WindowTarget {
        WindowTarget(ref: ref, title: "Flights", app: .init(id: "com.google.Chrome", name: "Google Chrome", pid: 7, started: nil), frame: nil)
    }

    @Test func theRecordIsOnDiskBeforeTheWindowIsHidden() throws {
        windows.onHide = { [store] target in
            #expect((try? store.load().snoozes.contains { $0.window.ref == target.ref }) == true)
        }
        let result = snoozer.snooze(target(), until: start.addingTimeInterval(3600))
        #expect((try? result.get()) != nil)
        #expect(windows.state["cg:1"] == .hidden)
    }

    @Test func aFailedHideLeavesNothingBehind() throws {
        windows.failure = .refused
        let result = snoozer.snooze(target(), until: start.addingTimeInterval(3600))
        #expect(throws: SnoozeFailure.refused) { try result.get() }
        #expect(snoozer.snoozes.isEmpty)
        #expect(try store.load().snoozes.isEmpty)
    }

    @Test func aWindowIsNeverHiddenIfTheRecordCannotBeSaved() throws {
        try Data("a file where the directory should be".utf8).write(to: directory)
        defer { try? FileManager.default.removeItem(at: directory) }
        let result = snoozer.snooze(target(), until: start.addingTimeInterval(3600))
        #expect(throws: SnoozeFailure.storage) { try result.get() }
        #expect(windows.state["cg:1"] == nil)
    }

    @Test func theMethodThatWorkedIsRecorded() throws {
        windows.method = .minimize
        _ = snoozer.snooze(target(), until: start.addingTimeInterval(3600))
        #expect(try store.load().snoozes.first?.method == .minimize)
    }

    @Test func dueWindowsComeBackTogetherInOneReport() throws {
        _ = snoozer.snooze(target("cg:1"), until: start.addingTimeInterval(3600))
        _ = snoozer.snooze(target("cg:2"), until: start.addingTimeInterval(3600))
        _ = snoozer.snooze(target("cg:3"), until: start.addingTimeInterval(5 * 3600))
        var reports: [ReturnReport] = []
        snoozer.onReturn = { reports.append($0) }

        clock.now = start.addingTimeInterval(3600)
        snoozer.tick()

        #expect(reports.count == 1)
        #expect(reports.first?.returned.count == 2)
        #expect(windows.state["cg:1"] == .visible)
        #expect(windows.state["cg:3"] == .hidden)
        #expect(try store.load().snoozes.count == 1)
    }

    @Test func aWindowTheUserBroughtBackIsForgottenQuietly() {
        _ = snoozer.snooze(target(), until: start.addingTimeInterval(3600))
        var reports = 0
        snoozer.onReturn = { _ in reports += 1 }
        windows.state["cg:1"] = .visible
        snoozer.tick()
        #expect(snoozer.snoozes.isEmpty)
        #expect(reports == 0)
    }

    @Test func aClosedWindowStillGetsItsReminderWhenDue() {
        _ = snoozer.snooze(target(), until: start.addingTimeInterval(3600))
        windows.state["cg:1"] = .gone
        var report: ReturnReport?
        snoozer.onReturn = { report = $0 }

        snoozer.tick()
        #expect(snoozer.snoozes.first?.state == .closed)
        #expect(report == nil)

        clock.now = start.addingTimeInterval(3600)
        snoozer.tick()
        #expect(snoozer.snoozes.isEmpty)
        #expect(report?.closedReminders.count == 1)
    }

    @Test func bringBackAllRestoresEveryHiddenWindow() throws {
        _ = snoozer.snooze(target("cg:1"), until: start.addingTimeInterval(3600))
        _ = snoozer.snooze(target("cg:2"), until: start.addingTimeInterval(2 * 86_400))
        #expect(snoozer.bringBackAll() == 2)
        #expect(windows.state.values.allSatisfy { $0 == .visible })
        #expect(try store.load().snoozes.isEmpty)
    }

    @Test func afterACrashOverdueWindowsComeBackOnTheNextLaunch() throws {
        _ = snoozer.snooze(target(), until: start.addingTimeInterval(3600))
        clock.now = start.addingTimeInterval(3 * 3600)
        let clock = clock
        let relaunched = Snoozer(windows: windows, store: SnoozeStore(directory: directory), now: { clock.now })
        try relaunched.load()
        relaunched.tick()
        #expect(windows.state["cg:1"] == .visible)
        #expect(relaunched.snoozes.isEmpty)
    }

    @Test func reschedulingIsSaved() throws {
        let snooze = try snoozer.snooze(target(), until: start.addingTimeInterval(3600)).get()
        snoozer.reschedule(snooze.id, to: start.addingTimeInterval(86_400))
        #expect(try store.load().snoozes.first?.dueAt == start.addingTimeInterval(86_400))
    }
}

@MainActor
final class FakeWindows: WindowSystem {
    var state: [String: LiveState] = [:]
    var failure: HideFailure?
    var method: Snooze.Method = .park
    var onHide: ((WindowTarget) -> Void)?

    func hide(_ target: WindowTarget) -> Result<Snooze.Method, HideFailure> {
        onHide?(target)
        if let failure { return .failure(failure) }
        state[target.ref] = .hidden
        return .success(method)
    }

    func restore(_ snooze: Snooze) -> Bool {
        guard state[snooze.window.ref] != .gone else { return false }
        state[snooze.window.ref] = .visible
        return true
    }

    func probe(_ snooze: Snooze) -> LiveState {
        state[snooze.window.ref] ?? .gone
    }
}

final class Clock: @unchecked Sendable {
    var now: Date
    init(now: Date) { self.now = now }
}
