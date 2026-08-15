import Foundation

public enum BlueTypeConstants {
    public static let tcpPort: UInt16 = 24_862
    public static let serviceUUID = "5F8C2C1D-9A25-4A20-9F0B-30D8D0F7E913"
    public static let maxFrameSize = 64 * 1024
    public static let heartbeatInterval: TimeInterval = 15
    public static let heartbeatTimeout: TimeInterval = 90
    public static let authorizationTimeout: TimeInterval = 60
}

public enum MessageType {
    public static let hello = "hello"
    public static let textInsert = "text_insert"
    public static let keyTap = "key_tap"
    public static let keyDown = "key_down"
    public static let keyUp = "key_up"
    public static let combo = "combo"
    public static let mouseMove = "mouse_move"
    public static let mouseButton = "mouse_button"
    public static let mouseClick = "mouse_click"
    public static let mouseScroll = "mouse_scroll"
    public static let clipboardSet = "clipboard_set"
    public static let clipboardGet = "clipboard_get"
    public static let ping = "ping"
    public static let pong = "pong"
    public static let ack = "ack"
    public static let error = "error"
    public static let authPending = "auth_pending"
    public static let authResult = "auth_result"
    public static let clipboardValue = "clipboard_value"
    public static let shortcutProfile = "shortcut_profile"

    public static let all: [String] = [
        hello,
        textInsert,
        keyTap,
        keyDown,
        keyUp,
        combo,
        mouseMove,
        mouseButton,
        mouseClick,
        mouseScroll,
        clipboardSet,
        clipboardGet,
        ping,
        pong,
        ack,
        error,
        authPending,
        authResult,
        clipboardValue,
        shortcutProfile,
    ]
}

public enum JSONValue: Codable, Equatable {
    case string(String)
    case number(Double)
    case bool(Bool)
    case object([String: JSONValue])
    case array([JSONValue])
    case null

    public init(from decoder: Decoder) throws {
        let container = try decoder.singleValueContainer()
        if container.decodeNil() {
            self = .null
        } else if let value = try? container.decode(Bool.self) {
            self = .bool(value)
        } else if let value = try? container.decode(Double.self) {
            self = .number(value)
        } else if let value = try? container.decode(String.self) {
            self = .string(value)
        } else if let value = try? container.decode([String: JSONValue].self) {
            self = .object(value)
        } else {
            self = .array(try container.decode([JSONValue].self))
        }
    }

    public func encode(to encoder: Encoder) throws {
        var container = encoder.singleValueContainer()
        switch self {
        case .string(let value):
            try container.encode(value)
        case .number(let value):
            try container.encode(value)
        case .bool(let value):
            try container.encode(value)
        case .object(let value):
            try container.encode(value)
        case .array(let value):
            try container.encode(value)
        case .null:
            try container.encodeNil()
        }
    }

    public var stringValue: String? {
        if case .string(let value) = self { value } else { nil }
    }

    public var intValue: Int? {
        if case .number(let value) = self { Int(value) } else { nil }
    }

    public var boolValue: Bool? {
        if case .bool(let value) = self { value } else { nil }
    }

    public var stringArrayValue: [String]? {
        guard case .array(let values) = self else { return nil }
        return values.map(\.stringValue).compactMap { $0 }
    }
}

public struct Envelope: Codable, Equatable {
    public var v: Int
    public var id: String
    public var type: String
    public var token: String?
    public var payload: [String: JSONValue]

    public init(v: Int = 1, id: String, type: String, token: String? = nil, payload: [String: JSONValue] = [:]) {
        self.v = v
        self.id = id
        self.type = type
        self.token = token
        self.payload = payload
    }

    public static func ack(id: String) -> Envelope {
        Envelope(id: id, type: MessageType.ack, payload: ["ok": .bool(true)])
    }

    public static func error(id: String, code: String, message: String) -> Envelope {
        Envelope(id: id, type: MessageType.error, payload: ["code": .string(code), "message": .string(message)])
    }
}
