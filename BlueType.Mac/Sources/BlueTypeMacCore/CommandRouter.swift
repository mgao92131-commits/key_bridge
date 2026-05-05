import AppKit
import Foundation

public final class ClipboardService {
    public init() {}

    public func setText(_ text: String) {
        let pasteboard = NSPasteboard.general
        pasteboard.clearContents()
        pasteboard.setString(text, forType: .string)
    }

    public func getText() -> String {
        NSPasteboard.general.string(forType: .string) ?? ""
    }
}

public final class CommandRouter {
    private let inputInjector: InputInjector
    private let clipboard: ClipboardService

    public init(inputInjector: InputInjector, clipboard: ClipboardService) {
        self.inputInjector = inputInjector
        self.clipboard = clipboard
    }

    public func releaseAllInputs() {
        inputInjector.releaseAllKeys()
        inputInjector.releaseAllMouseButtons()
    }

    public func route(_ envelope: Envelope) -> Envelope {
        do {
            switch envelope.type {
            case MessageType.ping:
                return Envelope(id: envelope.id, type: MessageType.pong)
            case MessageType.textInsert:
                let text = try requiredString(envelope, "text")
                guard Data(text.utf8).count <= 8 * 1024 else {
                    return Envelope.error(id: envelope.id, code: "INVALID_PAYLOAD", message: "Text payload exceeds 8 KB.")
                }
                try inputInjector.sendText(text)
                return .ack(id: envelope.id)
            case MessageType.keyTap:
                try inputInjector.tapKey(try requiredString(envelope, "key"))
                return .ack(id: envelope.id)
            case MessageType.keyDown:
                try inputInjector.pressKey(try requiredString(envelope, "key"))
                return .ack(id: envelope.id)
            case MessageType.keyUp:
                try inputInjector.releaseKey(try requiredString(envelope, "key"))
                return .ack(id: envelope.id)
            case MessageType.combo:
                try inputInjector.sendCombo(try requiredStringArray(envelope, "keys"))
                return .ack(id: envelope.id)
            case MessageType.mouseMove:
                try inputInjector.moveMouse(dx: try requiredInt(envelope, "dx"), dy: try requiredInt(envelope, "dy"))
                return .ack(id: envelope.id)
            case MessageType.mouseButton:
                let action = try requiredString(envelope, "action").lowercased()
                switch action {
                case "down":
                    try inputInjector.mouseButton(try requiredString(envelope, "button"), down: true)
                case "up":
                    try inputInjector.mouseButton(try requiredString(envelope, "button"), down: false)
                default:
                    throw InputInjectorError.invalidMouseAction(action)
                }
                return .ack(id: envelope.id)
            case MessageType.mouseClick:
                try inputInjector.clickMouse(
                    try requiredString(envelope, "button"),
                    repeat: optionalInt(envelope, "repeat", defaultValue: 1)
                )
                return .ack(id: envelope.id)
            case MessageType.mouseScroll:
                try inputInjector.scroll(
                    deltaX: optionalInt(envelope, "deltaX", defaultValue: 0),
                    deltaY: optionalInt(envelope, "deltaY", defaultValue: 0)
                )
                return .ack(id: envelope.id)
            case MessageType.clipboardSet:
                clipboard.setText(try requiredString(envelope, "text"))
                return .ack(id: envelope.id)
            case MessageType.clipboardGet:
                return Envelope(id: envelope.id, type: MessageType.clipboardValue, payload: ["text": .string(clipboard.getText())])
            default:
                return Envelope.error(id: envelope.id, code: "INVALID_PAYLOAD", message: "Unsupported message type: \(envelope.type)")
            }
        } catch InputInjectorError.accessibilityPermissionMissing {
            return Envelope.error(id: envelope.id, code: "INPUT_BLOCKED", message: "Accessibility/Input Monitoring permission is required on macOS.")
        } catch {
            return Envelope.error(id: envelope.id, code: "INVALID_PAYLOAD", message: error.localizedDescription)
        }
    }

    private func requiredString(_ envelope: Envelope, _ key: String) throws -> String {
        guard let value = envelope.payload[key]?.stringValue else {
            throw PayloadError.invalid("Missing or invalid payload.\(key).")
        }
        return value
    }

    private func requiredInt(_ envelope: Envelope, _ key: String) throws -> Int {
        guard let value = envelope.payload[key]?.intValue else {
            throw PayloadError.invalid("Missing or invalid payload.\(key).")
        }
        return value
    }

    private func optionalInt(_ envelope: Envelope, _ key: String, defaultValue: Int) -> Int {
        envelope.payload[key]?.intValue ?? defaultValue
    }

    private func requiredStringArray(_ envelope: Envelope, _ key: String) throws -> [String] {
        guard let values = envelope.payload[key]?.stringArrayValue, !values.isEmpty else {
            throw PayloadError.invalid("Missing or invalid payload.\(key).")
        }
        return values
    }
}

private enum PayloadError: Error, LocalizedError {
    case invalid(String)

    var errorDescription: String? {
        switch self {
        case .invalid(let message):
            message
        }
    }
}
