import Foundation
import Testing

/// Every translation of the Mac app has every string, with the same placeholders as English.
struct LocalizationTests {
    private static let folder = URL(fileURLWithPath: #filePath)
        .deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent()
        .appendingPathComponent("Localization")

    private static func strings(_ language: String) throws -> [String: String] {
        let url = folder.appendingPathComponent("\(language).lproj/Localizable.strings")
        return try #require(NSDictionary(contentsOf: url) as? [String: String])
    }

    private static func placeholders(_ text: String) -> [String] {
        text.matches(of: /%(?:\d+\$)?@/).map { String($0.output) }.map { $0.replacingOccurrences(of: #"\d+\$"#, with: "", options: .regularExpression) }.sorted()
    }

    @Test(arguments: ["zh-Hans", "es", "hi"])
    func translationMatchesEnglish(language: String) throws {
        let english = try Self.strings("en")
        let translation = try Self.strings(language)
        #expect(Set(english.keys) == Set(translation.keys))
        for (key, text) in english {
            let translated = try #require(translation[key], "\(language): missing \(key)")
            #expect(Self.placeholders(text) == Self.placeholders(translated), "\(language): \(key) has different placeholders")
            #expect(!translated.trimmingCharacters(in: .whitespaces).isEmpty, "\(language): \(key) is empty")
        }
    }
}
