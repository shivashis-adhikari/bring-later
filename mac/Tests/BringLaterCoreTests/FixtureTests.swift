import Foundation
import Testing
@testable import BringLaterCore

/// Runs the shared behavior contract in `fixtures/`. The Windows test suite loads the same files,
/// so a change here without a matching change there fails CI.
struct FixtureTests {
    @Test func timeGrammar() throws {
        let file = try Fixture.load("time-grammar.json")
        let defaults = file["defaults"] as! [String: String]
        for case let item as [String: String] in file["cases"] as! [Any] {
            let zone = TimeZone(identifier: item["zone"] ?? defaults["zone"]!)!
            let now = Fixture.instant(item["now"] ?? defaults["now"]!, zone: zone)
            let prefs = TimePrefs(
                morning: ClockTime(item["morning"] ?? defaults["morning"]!)!,
                evening: ClockTime(item["evening"] ?? defaults["evening"]!)!
            )
            let input = item["input"]!
            let result = TimeGrammar.parse(input, now: now, zone: zone, prefs: prefs)

            switch (result, item["expect"], item["error"]) {
            case (.success(let date), let expect?, nil):
                #expect(Fixture.matches(date, expect, zone: zone), "\"\(input)\" gave \(Fixture.describe(date, zone: zone)), expected \(expect)")
            case (.failure(let error), nil, let expected?):
                #expect(error.rawValue == expected, "\"\(input)\" failed with \(error), expected \(expected)")
            case (.success(let date), nil, let expected?):
                Issue.record("\"\(input)\" gave \(Fixture.describe(date, zone: zone)), expected error \(expected)")
            case (.failure(let error), let expect?, nil):
                Issue.record("\"\(input)\" failed with \(error), expected \(expect)")
            default:
                Issue.record("Malformed fixture case for \"\(input)\"")
            }
        }
    }

    @Test func presets() throws {
        let file = try Fixture.load("presets.json")
        let defaults = file["defaults"] as! [String: String]
        for case let item as [String: Any] in file["cases"] as! [Any] {
            let name = item["name"] as! String
            let zone = TimeZone(identifier: item["zone"] as? String ?? defaults["zone"]!)!
            let now = Fixture.instant(item["now"] as! String, zone: zone)
            let prefs = TimePrefs(
                morning: ClockTime(item["morning"] as? String ?? defaults["morning"]!)!,
                evening: ClockTime(item["evening"] as? String ?? defaults["evening"]!)!
            )
            let expected = item["expect"] as! [[String: String]]
            let actual = Presets.compute(now: now, zone: zone, prefs: prefs)

            #expect(actual.map(\.kind.rawValue) == expected.map { $0["kind"]! }, "\(name): kinds")
            for (preset, want) in zip(actual, expected) {
                #expect(Fixture.matches(preset.date, want["at"]!, zone: zone),
                        "\(name): \(preset.kind) gave \(Fixture.describe(preset.date, zone: zone)), expected \(want["at"]!)")
            }
        }
    }

    @Test func reconcile() throws {
        let file = try Fixture.load("reconcile.json")
        let now = Fixture.utc(file["now"] as! String)
        for case let item as [String: String] in file["cases"] as! [Any] {
            let snooze = Snooze.sample(due: Fixture.utc(item["due"]!), state: Snooze.State(rawValue: item["state"]!)!)
            let action = Reconcile.action(for: snooze, live: LiveState(rawValue: item["live"]!)!, now: now)
            #expect(action.rawValue == item["expect"], "\(item["name"]!)")
        }
        for case let item as [String: Any] in file["nextCheck"] as! [Any] {
            let dues = (item["dues"] as! [String]).map(Fixture.utc)
            let expected = (item["expect"] as? String).map(Fixture.utc)
            #expect(Reconcile.nextCheck(dues: dues, now: now) == expected, "\(item["name"]!)")
        }
    }
}

enum Fixture {
    static func load(_ name: String) throws -> [String: Any] {
        let root = URL(fileURLWithPath: #filePath)
            .deletingLastPathComponent().deletingLastPathComponent()
            .deletingLastPathComponent().deletingLastPathComponent()
        let data = try Data(contentsOf: root.appendingPathComponent("fixtures/\(name)"))
        return try JSONSerialization.jsonObject(with: data) as! [String: Any]
    }

    /// "2026-09-23T15:14:37" or "2026-09-23T15:14" as wall-clock time in `zone`.
    static func instant(_ text: String, zone: TimeZone) -> Date {
        let (date, time, second) = wall(text)
        return LocalTime.resolve(date, time, second: second, zone: zone)
    }

    static func utc(_ text: String) -> Date {
        instant(String(text.dropLast()), zone: TimeZone(identifier: "UTC")!)
    }

    /// Expectations without an offset compare wall-clock minutes; with one, they compare instants.
    static func matches(_ date: Date, _ expect: String, zone: TimeZone) -> Bool {
        guard expect.count > 16 else { return describe(date, zone: zone) == expect }
        let sign: Double = expect.dropFirst(16).first == "-" ? -1 : 1
        let offset = expect.suffix(5).split(separator: ":").compactMap { Double($0) }
        let local = instant(String(expect.prefix(16)), zone: TimeZone(identifier: "UTC")!)
        return date == local.addingTimeInterval(-sign * (offset[0] * 3600 + offset[1] * 60))
    }

    static func describe(_ date: Date, zone: TimeZone) -> String {
        let p = LocalTime.parts(of: date, zone: zone)
        return String(format: "%04d-%02d-%02dT%02d:%02d", p.date.year, p.date.month, p.date.day, p.time.hour, p.time.minute)
    }

    private static func wall(_ text: String) -> (CivilDate, ClockTime, Int) {
        let n = text.split(whereSeparator: { "-T:".contains($0) }).map { Int($0)! }
        return (CivilDate(year: n[0], month: n[1], day: n[2]), ClockTime(hour: n[3], minute: n[4]), n.count > 5 ? n[5] : 0)
    }
}

extension Snooze {
    static func sample(due: Date, state: State) -> Snooze {
        Snooze(
            id: UUID(),
            createdAt: due.addingTimeInterval(-3600),
            dueAt: due,
            state: state,
            method: .park,
            app: .init(id: "com.example.app", name: "Example", pid: 42, started: nil),
            window: .init(ref: "cg:1", title: "Example", frame: nil)
        )
    }
}
