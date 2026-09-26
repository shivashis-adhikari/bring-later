import AppKit
import Carbon.HIToolbox

/// A global keyboard shortcut: a key code plus modifiers, with the label shown for the key.
struct Shortcut: Codable, Equatable {
    var keyCode: UInt32
    var modifiers: NSEvent.ModifierFlags.RawValue
    var keyLabel: String

    /// ⌃⌥Z. Z for snooze. It includes ⌃ because macOS 15 ignores shortcuts that use only ⌥ and ⇧.
    static let standard = Shortcut(keyCode: UInt32(kVK_ANSI_Z), modifiers: NSEvent.ModifierFlags([.control, .option]).rawValue, keyLabel: "Z")

    var flags: NSEvent.ModifierFlags { NSEvent.ModifierFlags(rawValue: modifiers) }

    /// Needs ⌘ or ⌃. Since macOS 15, shortcuts with only ⌥ or ⇧ never fire.
    var isValid: Bool { !flags.intersection([.command, .control]).isEmpty }

    /// "⌃⌥Z", in Apple's modifier order.
    var display: String {
        var text = ""
        if flags.contains(.control) { text += "⌃" }
        if flags.contains(.option) { text += "⌥" }
        if flags.contains(.shift) { text += "⇧" }
        if flags.contains(.command) { text += "⌘" }
        return text + keyLabel
    }

    var carbonModifiers: UInt32 {
        var result: UInt32 = 0
        if flags.contains(.command) { result |= UInt32(cmdKey) }
        if flags.contains(.option) { result |= UInt32(optionKey) }
        if flags.contains(.control) { result |= UInt32(controlKey) }
        if flags.contains(.shift) { result |= UInt32(shiftKey) }
        return result
    }

    init(keyCode: UInt32, modifiers: NSEvent.ModifierFlags.RawValue, keyLabel: String) {
        self.keyCode = keyCode
        self.modifiers = modifiers
        self.keyLabel = keyLabel
    }

    init?(event: NSEvent) {
        let flags = event.modifierFlags.intersection([.command, .option, .control, .shift])
        guard let label = Self.label(for: event) else { return nil }
        self.init(keyCode: UInt32(event.keyCode), modifiers: flags.rawValue, keyLabel: label)
    }

    private static func label(for event: NSEvent) -> String? {
        if let function = functionKeys[Int(event.keyCode)] { return function }
        switch Int(event.keyCode) {
        case kVK_Space: return "Space"
        case kVK_Return: return "↩"
        case kVK_Tab: return "⇥"
        case kVK_Delete: return "⌫"
        case kVK_ForwardDelete: return "⌦"
        case kVK_LeftArrow: return "←"
        case kVK_RightArrow: return "→"
        case kVK_UpArrow: return "↑"
        case kVK_DownArrow: return "↓"
        default:
            guard let characters = event.charactersIgnoringModifiers?.uppercased(), !characters.isEmpty,
                  characters.unicodeScalars.allSatisfy({ !CharacterSet.controlCharacters.contains($0) }) else { return nil }
            return characters
        }
    }

    private static let functionKeys: [Int: String] = [
        kVK_F1: "F1", kVK_F2: "F2", kVK_F3: "F3", kVK_F4: "F4", kVK_F5: "F5", kVK_F6: "F6", kVK_F7: "F7",
        kVK_F8: "F8", kVK_F9: "F9", kVK_F10: "F10", kVK_F11: "F11", kVK_F12: "F12", kVK_F13: "F13",
        kVK_F14: "F14", kVK_F15: "F15", kVK_F16: "F16", kVK_F17: "F17", kVK_F18: "F18", kVK_F19: "F19", kVK_F20: "F20",
    ]
}

/// Registers one system-wide shortcut through Carbon, which needs no extra permission.
@MainActor
final class Hotkey {
    private var reference: EventHotKeyRef?
    private var handler: EventHandlerRef?
    var onPress: (() -> Void)?

    init() {
        var spec = EventTypeSpec(eventClass: OSType(kEventClassKeyboard), eventKind: UInt32(kEventHotKeyPressed))
        InstallEventHandler(GetApplicationEventTarget(), hotkeyHandler, 1, &spec, Unmanaged.passUnretained(self).toOpaque(), &handler)
    }

    /// Replaces the current shortcut. False if the system refused it, usually because another app has it.
    @discardableResult
    func register(_ shortcut: Shortcut) -> Bool {
        unregister()
        let id = EventHotKeyID(signature: OSType(0x424C_7472), id: 1) // "BLtr"
        return RegisterEventHotKey(shortcut.keyCode, shortcut.carbonModifiers, id, GetApplicationEventTarget(), 0, &reference) == noErr
    }

    func unregister() {
        if let reference { UnregisterEventHotKey(reference) }
        reference = nil
    }

    fileprivate func pressed() { onPress?() }
}

private func hotkeyHandler(_ call: EventHandlerCallRef?, _ event: EventRef?, _ context: UnsafeMutableRawPointer?) -> OSStatus {
    guard let context else { return OSStatus(eventNotHandledErr) }
    let hotkey = Unmanaged<Hotkey>.fromOpaque(context).takeUnretainedValue()
    MainActor.assumeIsolated { hotkey.pressed() }
    return noErr
}
