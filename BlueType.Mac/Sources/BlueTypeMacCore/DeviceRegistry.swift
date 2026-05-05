import CryptoKit
import Foundation
import Security

public struct HelloInfo: Equatable {
    public let deviceId: String
    public let deviceName: String
    public let appVersion: String?
}

public struct TrustedDevice: Codable, Equatable {
    public var deviceId: String
    public var deviceName: String
    public var lastAddress: String?
    public var lastTransport: String
    public var tokenHash: String
    public var lastSeenAt: Date
}

public final class DeviceRegistry {
    private struct Document: Codable {
        var devices: [TrustedDevice] = []
    }

    private let fileURL: URL
    private let queue = DispatchQueue(label: "BlueType.DeviceRegistry")
    private var devices: [String: TrustedDevice]

    public init(fileURL: URL? = nil) {
        self.fileURL = fileURL ?? Self.defaultFileURL()
        self.devices = Self.load(from: self.fileURL)
    }

    public func allDevices() -> [TrustedDevice] {
        queue.sync {
            devices.values.sorted { $0.deviceName.localizedCaseInsensitiveCompare($1.deviceName) == .orderedAscending }
        }
    }

    public func trustedDevice(deviceId: String) -> TrustedDevice? {
        queue.sync { devices[deviceId.lowercased()] }
    }

    public func upsert(_ device: TrustedDevice) throws {
        try queue.sync {
            devices[device.deviceId.lowercased()] = device
            try persistLocked()
        }
    }

    public func remove(deviceId: String) throws {
        try queue.sync {
            devices.removeValue(forKey: deviceId.lowercased())
            try persistLocked()
        }
    }

    public static func hashToken(_ token: String) -> String {
        let digest = SHA256.hash(data: Data(token.utf8))
        return "sha256:" + digest.map { String(format: "%02X", $0) }.joined()
    }

    public static func createToken() -> String {
        var bytes = [UInt8](repeating: 0, count: 32)
        _ = SecRandomCopyBytes(kSecRandomDefault, bytes.count, &bytes)
        return Data(bytes).base64EncodedString()
    }

    private static func defaultFileURL() -> URL {
        let base = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask).first!
        return base.appendingPathComponent("BlueType", isDirectory: true).appendingPathComponent("devices.json")
    }

    private static func load(from url: URL) -> [String: TrustedDevice] {
        let decoder = JSONDecoder()
        decoder.dateDecodingStrategy = .iso8601
        guard let data = try? Data(contentsOf: url),
              let document = try? decoder.decode(Document.self, from: data) else {
            return [:]
        }
        return Dictionary(uniqueKeysWithValues: document.devices.map { ($0.deviceId.lowercased(), $0) })
    }

    private func persistLocked() throws {
        try FileManager.default.createDirectory(at: fileURL.deletingLastPathComponent(), withIntermediateDirectories: true)
        let document = Document(devices: Array(devices.values).sorted { $0.deviceId < $1.deviceId })
        let encoder = JSONEncoder()
        encoder.outputFormatting = [.prettyPrinted, .sortedKeys, .withoutEscapingSlashes]
        encoder.dateEncodingStrategy = .iso8601
        let data = try encoder.encode(document)
        let tempURL = fileURL.appendingPathExtension(UUID().uuidString + ".tmp")
        try data.write(to: tempURL, options: [.atomic])
        if FileManager.default.fileExists(atPath: fileURL.path) {
            _ = try FileManager.default.replaceItemAt(fileURL, withItemAt: tempURL)
        } else {
            try FileManager.default.moveItem(at: tempURL, to: fileURL)
        }
    }
}

public enum AuthPromptDecision {
    case deny
    case allowOnce
    case alwaysAllow
}

public struct AuthPromptRequest {
    public enum Mode {
        case authorizeDevice
        case switchActiveDevice(activeDeviceName: String)
    }

    public let mode: Mode
    public let hello: HelloInfo
    public let remoteAddress: String
    public let transport: String
}

public struct AuthResult {
    public let authorized: Bool
    public let token: String?
    public let persistToken: Bool
    public let errorCode: String?
    public let errorMessage: String?

    public static func authorized(token: String?, persistToken: Bool) -> AuthResult {
        AuthResult(authorized: true, token: token, persistToken: persistToken, errorCode: nil, errorMessage: nil)
    }

    public static func error(_ code: String, _ message: String) -> AuthResult {
        AuthResult(authorized: false, token: nil, persistToken: false, errorCode: code, errorMessage: message)
    }
}

public final class AuthService {
    private let registry: DeviceRegistry
    private let prompt: @MainActor (AuthPromptRequest) async -> AuthPromptDecision

    public init(registry: DeviceRegistry, prompt: @escaping @MainActor (AuthPromptRequest) async -> AuthPromptDecision) {
        self.registry = registry
        self.prompt = prompt
    }

    public func tryAuthorizeKnownDevice(_ hello: HelloInfo, token: String?, remoteAddress: String, transport: String) throws -> AuthResult {
        guard let token, !token.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else {
            return .error("NOT_AUTHORIZED", "Device is not authorized.")
        }
        guard var device = registry.trustedDevice(deviceId: hello.deviceId) else {
            return .error("NOT_AUTHORIZED", "Device is not authorized.")
        }
        guard device.tokenHash == DeviceRegistry.hashToken(token) else {
            return .error("NOT_AUTHORIZED", "Invalid token.")
        }

        device.deviceName = hello.deviceName
        device.lastAddress = remoteAddress
        device.lastTransport = transport
        device.lastSeenAt = Date()
        try registry.upsert(device)
        return .authorized(token: token, persistToken: true)
    }

    public func requestApproval(_ hello: HelloInfo, remoteAddress: String, transport: String) async throws -> AuthResult {
        let decision = await prompt(AuthPromptRequest(mode: .authorizeDevice, hello: hello, remoteAddress: remoteAddress, transport: transport))
        switch decision {
        case .deny:
            return .error("NOT_AUTHORIZED", "Device was not approved.")
        case .allowOnce:
            return .authorized(token: nil, persistToken: false)
        case .alwaysAllow:
            let token = DeviceRegistry.createToken()
            let device = TrustedDevice(
                deviceId: hello.deviceId,
                deviceName: hello.deviceName,
                lastAddress: remoteAddress,
                lastTransport: transport,
                tokenHash: DeviceRegistry.hashToken(token),
                lastSeenAt: Date()
            )
            try registry.upsert(device)
            return .authorized(token: token, persistToken: true)
        }
    }
}
