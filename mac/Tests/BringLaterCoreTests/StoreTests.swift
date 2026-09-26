import Foundation
import Testing
@testable import BringLaterCore

struct StoreTests {
    private func temporaryStore() -> (SnoozeStore, URL) {
        let dir = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        return (SnoozeStore(directory: dir), dir)
    }

    @Test func missingFileLoadsEmpty() throws {
        let (store, _) = temporaryStore()
        let result = try store.load()
        #expect(result.snoozes.isEmpty)
        #expect(result.quarantined == nil)
    }

    @Test func roundTrips() throws {
        let (store, _) = temporaryStore()
        let snooze = Snooze.sample(due: Date(timeIntervalSince1970: 1_790_000_000), state: .hidden)
        try store.save([snooze])
        #expect(try store.load().snoozes == [snooze])
    }

    @Test func unreadableFileIsSetAsideNotLost() throws {
        let (store, dir) = temporaryStore()
        try FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        try Data("{ not json".utf8).write(to: store.url)

        let result = try store.load()
        #expect(result.snoozes.isEmpty)
        let moved = try #require(result.quarantined)
        #expect(FileManager.default.fileExists(atPath: moved.path))
        #expect(!FileManager.default.fileExists(atPath: store.url.path))
    }

    @Test func savedFileIsPrivate() throws {
        let (store, _) = temporaryStore()
        try store.save([])
        let mode = try FileManager.default.attributesOfItem(atPath: store.url.path)[.posixPermissions] as? Int
        #expect(mode == 0o600)
    }
}
