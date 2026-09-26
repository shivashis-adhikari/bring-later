import Foundation

/// Compares this build with the newest GitHub release. Runs only when the user asks; Bring Later
/// makes no other network requests.
enum Updates {
    static let releasesPage = URL(string: "https://github.com/shivashis-adhikari/bring-later/releases")!
    private static let api = URL(string: "https://api.github.com/repos/shivashis-adhikari/bring-later/releases?per_page=20")!

    static var current: String { Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? "0.0.0" }

    enum Result {
        case upToDate
        case available(version: String, url: URL)
        case failed
    }

    private struct Release: Decodable {
        let tag_name: String
        let html_url: URL
        let draft: Bool
    }

    static func check() async -> Result {
        var request = URLRequest(url: api, timeoutInterval: 10)
        request.setValue("application/vnd.github+json", forHTTPHeaderField: "Accept")
        request.setValue("BringLater/\(current)", forHTTPHeaderField: "User-Agent")
        guard let (data, _) = try? await URLSession.shared.data(for: request),
              let releases = try? JSONDecoder().decode([Release].self, from: data) else { return .failed }
        let newest = releases.filter { !$0.draft }.max { version($0.tag_name).lexicographicallyPrecedes(version($1.tag_name)) }
        guard let newest, version(newest.tag_name).lexicographicallyPrecedes(version(current)) == false,
              version(newest.tag_name) != version(current) else { return .upToDate }
        return .available(version: String(newest.tag_name.drop { $0 == "v" }), url: newest.html_url)
    }

    /// "v1.2.3-beta" reads as [1, 2, 3].
    private static func version(_ text: String) -> [Int] {
        let core = text.drop { $0 == "v" || $0 == "V" }.split(whereSeparator: { $0 == "-" || $0 == "+" }).first ?? ""
        let parts = core.split(separator: ".").map { Int($0) ?? 0 }
        return parts + Array(repeating: 0, count: max(0, 3 - parts.count))
    }
}
