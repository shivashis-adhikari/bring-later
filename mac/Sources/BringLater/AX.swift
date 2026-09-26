import AppKit
import ApplicationServices

/// The one private call: the window server's id for an Accessibility window element. AeroSpace,
/// AltTab and Hammerspoon rely on it too. It's how a window is recognized again after a relaunch.
@_silgen_name("_AXUIElementGetWindow")
private func _AXUIElementGetWindow(_ element: AXUIElement, _ id: UnsafeMutablePointer<CGWindowID>) -> AXError

extension AXUIElement {
    func value<T>(_ attribute: String) -> T? {
        var value: CFTypeRef?
        guard AXUIElementCopyAttributeValue(self, attribute as CFString, &value) == .success else { return nil }
        return value as? T
    }

    var windowID: CGWindowID? {
        var id: CGWindowID = 0
        return _AXUIElementGetWindow(self, &id) == .success && id != 0 ? id : nil
    }

    /// False once the element's window has been destroyed.
    var isAlive: Bool {
        var value: CFTypeRef?
        return AXUIElementCopyAttributeValue(self, kAXRoleAttribute as CFString, &value) != .invalidUIElement
    }

    /// Position and size in Accessibility coordinates: origin at the top left of the main display, y down.
    var frame: CGRect? {
        guard let position: AXValue = value(kAXPositionAttribute), let size: AXValue = value(kAXSizeAttribute) else { return nil }
        var point = CGPoint.zero
        var extent = CGSize.zero
        guard AXValueGetValue(position, .cgPoint, &point), AXValueGetValue(size, .cgSize, &extent) else { return nil }
        return CGRect(origin: point, size: extent)
    }

    @discardableResult
    func setPosition(_ point: CGPoint) -> Bool {
        var point = point
        guard let value = AXValueCreate(.cgPoint, &point) else { return false }
        return AXUIElementSetAttributeValue(self, kAXPositionAttribute as CFString, value) == .success
    }

    @discardableResult
    func setSize(_ size: CGSize) -> Bool {
        var size = size
        guard let value = AXValueCreate(.cgSize, &size) else { return false }
        return AXUIElementSetAttributeValue(self, kAXSizeAttribute as CFString, value) == .success
    }

    @discardableResult
    func set(_ attribute: String, _ value: Bool) -> Bool {
        AXUIElementSetAttributeValue(self, attribute as CFString, (value ? kCFBooleanTrue : kCFBooleanFalse) as CFTypeRef) == .success
    }
}

enum Screens {
    /// Every display's frame in Accessibility coordinates.
    @MainActor
    static var frames: [CGRect] {
        guard let primary = NSScreen.screens.first else { return [] }
        return NSScreen.screens.map { axRect(fromCocoa: $0.frame, primaryHeight: primary.frame.height) }
    }

    @MainActor
    static func axRect(fromCocoa rect: CGRect) -> CGRect {
        axRect(fromCocoa: rect, primaryHeight: NSScreen.screens.first?.frame.height ?? rect.maxY)
    }

    /// Cocoa and Accessibility coordinates mirror each other around the main display's height.
    static func axRect(fromCocoa rect: CGRect, primaryHeight: CGFloat) -> CGRect {
        CGRect(x: rect.minX, y: primaryHeight - rect.maxY, width: rect.width, height: rect.height)
    }

    @MainActor
    static func cocoaRect(fromAX rect: CGRect) -> CGRect {
        axRect(fromCocoa: rect)
    }

    /// How much of `rect` is on some display, as a fraction of its area.
    @MainActor
    static func visibleFraction(of rect: CGRect) -> CGFloat {
        guard rect.width > 0, rect.height > 0 else { return 0 }
        let visible = frames.reduce(CGFloat(0)) { total, screen in
            let overlap = screen.intersection(rect)
            return total + (overlap.isNull ? 0 : overlap.width * overlap.height)
        }
        return visible / (rect.width * rect.height)
    }
}
