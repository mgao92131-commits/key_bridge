import AppKit
import ApplicationServices
import Foundation

public enum InputInjectorError: Error, LocalizedError, Equatable {
    case accessibilityPermissionMissing
    case unsupportedKey(String)
    case unsupportedMouseButton(String)
    case invalidMouseAction(String)

    public var errorDescription: String? {
        switch self {
        case .accessibilityPermissionMissing:
            "Accessibility/Input Monitoring permission is required."
        case .unsupportedKey(let key):
            "Unsupported key: \(key)"
        case .unsupportedMouseButton(let button):
            "Unsupported mouse button: \(button)"
        case .invalidMouseAction(let action):
            "Unsupported mouse button action: \(action)"
        }
    }
}

public final class InputInjector {
    public struct KeyDefinition: Equatable {
        public let keyCode: CGKeyCode
        public let flags: CGEventFlags
        public let isModifier: Bool
    }

    private let queue = DispatchQueue(label: "BlueType.InputInjector")
    private let log: (String) -> Void
    private var pressedKeys: [String: KeyDefinition] = [:]
    private var pressedMouseButtons: Set<String> = []

    private var currentModifierFlags: CGEventFlags {
        var flags: CGEventFlags = []
        for definition in pressedKeys.values where definition.isModifier {
            flags.formUnion(definition.flags)
        }
        return flags
    }

    public init(log: @escaping (String) -> Void = { _ in }) {
        self.log = log
    }

    public static func isTrusted(prompt: Bool = false) -> Bool {
        let options = [kAXTrustedCheckOptionPrompt.takeUnretainedValue() as String: prompt] as CFDictionary
        return AXIsProcessTrustedWithOptions(options)
    }

    public func sendText(_ text: String) throws {
        guard !text.isEmpty else { return }
        try requirePermission()
        queue.sync {
            for scalar in text.unicodeScalars {
                var value = UniChar(scalar.value)
                guard let down = CGEvent(keyboardEventSource: nil, virtualKey: 0, keyDown: true),
                      let up = CGEvent(keyboardEventSource: nil, virtualKey: 0, keyDown: false) else {
                    continue
                }
                down.keyboardSetUnicodeString(stringLength: 1, unicodeString: &value)
                up.keyboardSetUnicodeString(stringLength: 1, unicodeString: &value)
                down.post(tap: .cghidEventTap)
                up.post(tap: .cghidEventTap)
            }
        }
    }

    public func tapKey(_ key: String) throws {
        try requirePermission()
        let definition = try Self.resolveKey(key)
        queue.sync {
            logKeyEvent("tap", key: key, definition: definition, flags: currentModifierFlags)
            postKey(definition, down: true)
            postKey(definition, down: false)
        }
    }

    public func pressKey(_ key: String) throws {
        try requirePermission()
        let normalized = Self.normalize(key)
        let definition = try Self.resolveKey(key)
        queue.sync {
            guard pressedKeys[normalized] == nil else { return }
            logKeyEvent("down", key: key, definition: definition, flags: currentModifierFlags.union(definition.flags))
            postKey(definition, down: true)
            pressedKeys[normalized] = definition
        }
    }

    public func releaseKey(_ key: String) throws {
        try requirePermission()
        let normalized = Self.normalize(key)
        queue.sync {
            guard let definition = pressedKeys.removeValue(forKey: normalized) else { return }
            logKeyEvent("up", key: key, definition: definition, flags: currentModifierFlags)
            postKey(definition, down: false)
        }
    }

    public func releaseAllKeys() {
        queue.sync {
            for key in pressedKeys.keys.reversed() {
                guard let definition = pressedKeys.removeValue(forKey: key) else { continue }
                postKey(definition, down: false)
            }
        }
    }

    public func sendCombo(_ keys: [String]) throws {
        guard !keys.isEmpty else { return }
        try requirePermission()
        let definitions = try keys.map { try Self.resolveKey($0) }
        queue.sync {
            var activeFlags = definitions.reduce(CGEventFlags()) { $0.union($1.flags) }
            log("Input combo keys=\(keys.joined(separator: "+")) flags=\(Self.describeFlags(activeFlags))")
            for definition in definitions where definition.isModifier {
                postKey(definition, down: true, extraFlags: activeFlags)
            }
            if definitions.contains(where: \.isModifier) {
                usleep(10_000)
            }
            for definition in definitions where !definition.isModifier {
                postKey(definition, down: true, extraFlags: activeFlags)
                postKey(definition, down: false, extraFlags: activeFlags)
            }
            if definitions.contains(where: { !$0.isModifier }) {
                usleep(10_000)
            }
            for definition in definitions.reversed() where definition.isModifier {
                if definition.isModifier {
                    activeFlags.subtract(definition.flags)
                }
                postKey(definition, down: false, extraFlags: activeFlags)
            }
        }
    }

    private func logKeyEvent(_ action: String, key: String, definition: KeyDefinition, flags: CGEventFlags) {
        log("Input key_\(action) key=\(Self.normalize(key)) code=0x\(String(definition.keyCode, radix: 16)) flags=\(Self.describeFlags(flags))")
    }

    public func moveMouse(dx: Int, dy: Int) throws {
        guard dx != 0 || dy != 0 else { return }
        try requirePermission()
        queue.sync {
            let target = Self.targetMousePosition(
                current: Self.currentMousePosition(),
                dx: dx,
                dy: dy,
                bounds: Self.activeDisplayBounds()
            )
            CGWarpMouseCursorPosition(target)
            if let event = CGEvent(mouseEventSource: nil, mouseType: .mouseMoved, mouseCursorPosition: target, mouseButton: .left) {
                event.flags.formUnion(currentModifierFlags)
                event.post(tap: .cghidEventTap)
            }
        }
    }

    public func mouseButton(_ button: String, down: Bool) throws {
        try requirePermission()
        let mouseButton = try Self.resolveMouseButton(button)
        queue.sync {
            let position = Self.currentMousePosition()
            let type = Self.mouseEventType(button: mouseButton, down: down)
            Self.postMouseButtonEvent(
                mouseButton: mouseButton,
                type: type,
                position: position,
                clickState: 1,
                flags: currentModifierFlags
            )
            let key = button.uppercased()
            if down {
                pressedMouseButtons.insert(key)
            } else {
                pressedMouseButtons.remove(key)
            }
        }
    }

    public func clickMouse(_ button: String, repeat count: Int) throws {
        guard count > 0 else { return }
        try requirePermission()
        let mouseButton = try Self.resolveMouseButton(button)
        queue.sync {
            let position = Self.currentMousePosition()
            let downType = Self.mouseEventType(button: mouseButton, down: true)
            let upType = Self.mouseEventType(button: mouseButton, down: false)
            let flags = currentModifierFlags
            for index in 0..<count {
                let clickState = min(index + 1, 2)
                Self.postMouseButtonEvent(
                    mouseButton: mouseButton,
                    type: downType,
                    position: position,
                    clickState: clickState,
                    flags: flags
                )
                usleep(10_000)
                Self.postMouseButtonEvent(
                    mouseButton: mouseButton,
                    type: upType,
                    position: position,
                    clickState: clickState,
                    flags: flags
                )
                usleep(40_000)
            }
        }
    }

    public func releaseAllMouseButtons() {
        let buttons = queue.sync { Array(pressedMouseButtons) }
        for button in buttons {
            try? mouseButton(button, down: false)
        }
    }

    public func scroll(deltaX: Int, deltaY: Int) throws {
        guard deltaX != 0 || deltaY != 0 else { return }
        try requirePermission()
        queue.sync {
            if let event = CGEvent(
                scrollWheelEvent2Source: nil,
                units: .line,
                wheelCount: 2,
                wheel1: Int32(deltaY),
                wheel2: Int32(deltaX),
                wheel3: 0
            ) {
                event.flags.formUnion(currentModifierFlags)
                event.post(tap: .cghidEventTap)
            }
        }
    }

    public static func resolveKey(_ key: String) throws -> KeyDefinition {
        let normalized = normalize(key)
        if let definition = namedKeys[normalized] {
            return definition
        }
        if normalized.count == 1, let scalar = normalized.unicodeScalars.first {
            if let code = letterAndDigitKeyCodes[Character(String(scalar).uppercased())] {
                return KeyDefinition(keyCode: code, flags: [], isModifier: false)
            }
        }
        throw InputInjectorError.unsupportedKey(key)
    }

    private func requirePermission() throws {
        guard Self.isTrusted(prompt: false) else {
            throw InputInjectorError.accessibilityPermissionMissing
        }
    }

    private func postKey(_ definition: KeyDefinition, down: Bool, extraFlags: CGEventFlags = []) {
        let source = CGEventSource(stateID: .privateState)
        guard let event = CGEvent(keyboardEventSource: source, virtualKey: definition.keyCode, keyDown: down) else {
            return
        }
        event.flags = Self.eventFlags(
            definition: definition,
            down: down,
            currentFlags: currentModifierFlags,
            extraFlags: extraFlags
        )
        event.post(tap: .cghidEventTap)
    }

    internal static func eventFlags(
        definition: KeyDefinition,
        down: Bool,
        currentFlags: CGEventFlags,
        extraFlags: CGEventFlags = []
    ) -> CGEventFlags {
        var flags = currentFlags
        flags.formUnion(extraFlags)
        if down {
            flags.formUnion(definition.flags)
        } else if definition.isModifier {
            flags.subtract(definition.flags)
        }
        return flags
    }

    private static func describeFlags(_ flags: CGEventFlags) -> String {
        var values: [String] = []
        if flags.contains(.maskControl) { values.append("CTRL") }
        if flags.contains(.maskCommand) { values.append("CMD") }
        if flags.contains(.maskAlternate) { values.append("ALT") }
        if flags.contains(.maskShift) { values.append("SHIFT") }
        return values.isEmpty ? "none" : values.joined(separator: "+")
    }

    private static func normalize(_ key: String) -> String {
        key.replacingOccurrences(of: "_", with: "")
            .replacingOccurrences(of: "+", with: "")
            .trimmingCharacters(in: .whitespacesAndNewlines)
            .uppercased()
    }

    private static func resolveMouseButton(_ button: String) throws -> CGMouseButton {
        switch normalize(button) {
        case "LEFT":
            return .left
        case "RIGHT":
            return .right
        case "MIDDLE":
            return .center
        default:
            throw InputInjectorError.unsupportedMouseButton(button)
        }
    }

    private static func mouseEventType(button: CGMouseButton, down: Bool) -> CGEventType {
        switch (button, down) {
        case (.left, true): return .leftMouseDown
        case (.left, false): return .leftMouseUp
        case (.right, true): return .rightMouseDown
        case (.right, false): return .rightMouseUp
        default: return down ? .otherMouseDown : .otherMouseUp
        }
    }

    private static func postMouseButtonEvent(
        mouseButton: CGMouseButton,
        type: CGEventType,
        position: CGPoint,
        clickState: Int,
        flags: CGEventFlags = []
    ) {
        let source = CGEventSource(stateID: .hidSystemState)
        guard let event = CGEvent(
            mouseEventSource: source,
            mouseType: type,
            mouseCursorPosition: position,
            mouseButton: mouseButton
        ) else {
            return
        }
        event.flags.formUnion(flags)
        event.setIntegerValueField(.mouseEventButtonNumber, value: Int64(mouseButton.rawValue))
        event.setIntegerValueField(.mouseEventClickState, value: Int64(clickState))
        event.post(tap: .cghidEventTap)
    }

    internal static func targetMousePosition(current: CGPoint, dx: Int, dy: Int, bounds: CGRect) -> CGPoint {
        let proposed = CGPoint(
            x: current.x + CGFloat(dx),
            y: current.y + CGFloat(dy)
        )
        guard !bounds.isNull, !bounds.isEmpty else {
            return proposed
        }

        return CGPoint(
            x: min(max(proposed.x, bounds.minX), bounds.maxX - 1),
            y: min(max(proposed.y, bounds.minY), bounds.maxY - 1)
        )
    }

    private static func currentMousePosition() -> CGPoint {
        CGEvent(source: nil)?.location ?? .zero
    }

    private static func activeDisplayBounds() -> CGRect {
        var displayCount: UInt32 = 0
        guard CGGetActiveDisplayList(0, nil, &displayCount) == .success, displayCount > 0 else {
            return .null
        }

        var displays = [CGDirectDisplayID](repeating: 0, count: Int(displayCount))
        guard CGGetActiveDisplayList(displayCount, &displays, &displayCount) == .success else {
            return .null
        }

        return displays
            .prefix(Int(displayCount))
            .map(CGDisplayBounds)
            .reduce(CGRect.null) { $0.union($1) }
    }

    private static let letterAndDigitKeyCodes: [Character: CGKeyCode] = [
        "A": 0x00, "S": 0x01, "D": 0x02, "F": 0x03, "H": 0x04, "G": 0x05, "Z": 0x06, "X": 0x07,
        "C": 0x08, "V": 0x09, "B": 0x0B, "Q": 0x0C, "W": 0x0D, "E": 0x0E, "R": 0x0F, "Y": 0x10,
        "T": 0x11, "1": 0x12, "2": 0x13, "3": 0x14, "4": 0x15, "6": 0x16, "5": 0x17, "=": 0x18,
        "9": 0x19, "7": 0x1A, "-": 0x1B, "8": 0x1C, "0": 0x1D, "]": 0x1E, "O": 0x1F, "U": 0x20,
        "[": 0x21, "I": 0x22, "P": 0x23, "L": 0x25, "J": 0x26, "'": 0x27, "K": 0x28, ";": 0x29,
        "\\": 0x2A, ",": 0x2B, "/": 0x2C, "N": 0x2D, "M": 0x2E, ".": 0x2F, "`": 0x32,
    ]

    private static let namedKeys: [String: KeyDefinition] = [
        "ENTER": KeyDefinition(keyCode: 0x24, flags: [], isModifier: false),
        "RETURN": KeyDefinition(keyCode: 0x24, flags: [], isModifier: false),
        "ESC": KeyDefinition(keyCode: 0x35, flags: [], isModifier: false),
        "TAB": KeyDefinition(keyCode: 0x30, flags: [], isModifier: false),
        "BACKSPACE": KeyDefinition(keyCode: 0x33, flags: [], isModifier: false),
        "DELETE": KeyDefinition(keyCode: 0x33, flags: [], isModifier: false),
        "FORWARDDELETE": KeyDefinition(keyCode: 0x75, flags: [], isModifier: false),
        "SPACE": KeyDefinition(keyCode: 0x31, flags: [], isModifier: false),
        "LEFT": KeyDefinition(keyCode: 0x7B, flags: [], isModifier: false),
        "RIGHT": KeyDefinition(keyCode: 0x7C, flags: [], isModifier: false),
        "DOWN": KeyDefinition(keyCode: 0x7D, flags: [], isModifier: false),
        "UP": KeyDefinition(keyCode: 0x7E, flags: [], isModifier: false),
        "HOME": KeyDefinition(keyCode: 0x73, flags: [], isModifier: false),
        "END": KeyDefinition(keyCode: 0x77, flags: [], isModifier: false),
        "PAGEUP": KeyDefinition(keyCode: 0x74, flags: [], isModifier: false),
        "PAGEDOWN": KeyDefinition(keyCode: 0x79, flags: [], isModifier: false),
        "CTRL": KeyDefinition(keyCode: 0x3B, flags: .maskControl, isModifier: true),
        "CONTROL": KeyDefinition(keyCode: 0x3B, flags: .maskControl, isModifier: true),
        "WIN": KeyDefinition(keyCode: 0x37, flags: .maskCommand, isModifier: true),
        "LWIN": KeyDefinition(keyCode: 0x37, flags: .maskCommand, isModifier: true),
        "RWIN": KeyDefinition(keyCode: 0x37, flags: .maskCommand, isModifier: true),
        "CMD": KeyDefinition(keyCode: 0x37, flags: .maskCommand, isModifier: true),
        "COMMAND": KeyDefinition(keyCode: 0x37, flags: .maskCommand, isModifier: true),
        "SHIFT": KeyDefinition(keyCode: 0x38, flags: .maskShift, isModifier: true),
        "ALT": KeyDefinition(keyCode: 0x3A, flags: .maskAlternate, isModifier: true),
        "OPTION": KeyDefinition(keyCode: 0x3A, flags: .maskAlternate, isModifier: true),
        "F1": KeyDefinition(keyCode: 0x7A, flags: [], isModifier: false),
        "F2": KeyDefinition(keyCode: 0x78, flags: [], isModifier: false),
        "F3": KeyDefinition(keyCode: 0x63, flags: [], isModifier: false),
        "F4": KeyDefinition(keyCode: 0x76, flags: [], isModifier: false),
        "F5": KeyDefinition(keyCode: 0x60, flags: [], isModifier: false),
        "F6": KeyDefinition(keyCode: 0x61, flags: [], isModifier: false),
        "F7": KeyDefinition(keyCode: 0x62, flags: [], isModifier: false),
        "F8": KeyDefinition(keyCode: 0x64, flags: [], isModifier: false),
        "F9": KeyDefinition(keyCode: 0x65, flags: [], isModifier: false),
        "F10": KeyDefinition(keyCode: 0x6D, flags: [], isModifier: false),
        "F11": KeyDefinition(keyCode: 0x67, flags: [], isModifier: false),
        "F12": KeyDefinition(keyCode: 0x6F, flags: [], isModifier: false),
    ]
}
