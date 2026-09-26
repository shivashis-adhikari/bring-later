import Foundation

public struct Snooze: Codable, Identifiable, Hashable, Sendable {
    public enum State: String, Codable, Sendable {
        case hidden
        /// The window was closed while snoozed. Kept so the reminder still fires.
        case closed
    }

    public enum Method: String, Codable, Sendable {
        /// Moved past a screen corner (macOS).
        case park
        case minimize
        /// `SW_HIDE` (Windows).
        case hide
    }

    public struct App: Codable, Hashable, Sendable {
        public var id: String
        public var name: String
        public var pid: Int32
        /// Guards against the OS reusing the pid for another process.
        public var started: Date?

        public init(id: String, name: String, pid: Int32, started: Date?) {
            self.id = id
            self.name = name
            self.pid = pid
            self.started = started
        }
    }

    public struct Frame: Codable, Hashable, Sendable {
        public var x: Double
        public var y: Double
        public var width: Double
        public var height: Double

        public init(x: Double, y: Double, width: Double, height: Double) {
            self.x = x
            self.y = y
            self.width = width
            self.height = height
        }
    }

    public struct Window: Codable, Hashable, Sendable {
        /// `cg:<CGWindowID>` on macOS, `hwnd:<hex>` on Windows.
        public var ref: String
        public var title: String
        /// Where the window was before it was hidden, in screen coordinates.
        public var frame: Frame?

        public init(ref: String, title: String, frame: Frame?) {
            self.ref = ref
            self.title = title
            self.frame = frame
        }
    }

    public var id: UUID
    public var createdAt: Date
    public var dueAt: Date
    public var state: State
    public var method: Method
    public var app: App
    public var window: Window

    public init(id: UUID, createdAt: Date, dueAt: Date, state: State, method: Method, app: App, window: Window) {
        self.id = id
        self.createdAt = createdAt
        self.dueAt = dueAt
        self.state = state
        self.method = method
        self.app = app
        self.window = window
    }
}

/// Snoozes on disk, as one JSON file replaced atomically on every save.
public struct SnoozeStore: Sendable {
    public struct LoadResult: Sendable {
        public var snoozes: [Snooze]
        /// Where an unreadable file was moved, if there was one. It is never deleted.
        public var quarantined: URL?
    }

    private struct File: Codable {
        var version: Int
        var snoozes: [Snooze]
    }

    static let version = 1
    public let url: URL

    public init(directory: URL) {
        url = directory.appendingPathComponent("snoozes.json")
    }

    public func load() throws -> LoadResult {
        guard FileManager.default.fileExists(atPath: url.path) else {
            return LoadResult(snoozes: [], quarantined: nil)
        }
        let data = try Data(contentsOf: url)
        if let file = try? Self.decoder.decode(File.self, from: data), file.version == Self.version {
            return LoadResult(snoozes: file.snoozes, quarantined: nil)
        }
        let stamp = ISO8601DateFormatter().string(from: Date()).replacingOccurrences(of: ":", with: "-")
        let aside = url.deletingLastPathComponent().appendingPathComponent("snoozes.unreadable-\(stamp).json")
        try FileManager.default.moveItem(at: url, to: aside)
        return LoadResult(snoozes: [], quarantined: aside)
    }

    public func save(_ snoozes: [Snooze]) throws {
        let directory = url.deletingLastPathComponent()
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true, attributes: [.posixPermissions: 0o700])
        try Self.encoder.encode(File(version: Self.version, snoozes: snoozes)).write(to: url, options: .atomic)
        try FileManager.default.setAttributes([.posixPermissions: 0o600], ofItemAtPath: url.path)
    }

    private static let encoder: JSONEncoder = {
        let encoder = JSONEncoder()
        encoder.dateEncodingStrategy = .iso8601
        encoder.outputFormatting = [.prettyPrinted, .sortedKeys]
        return encoder
    }()

    private static let decoder: JSONDecoder = {
        let decoder = JSONDecoder()
        decoder.dateDecodingStrategy = .iso8601
        return decoder
    }()
}
