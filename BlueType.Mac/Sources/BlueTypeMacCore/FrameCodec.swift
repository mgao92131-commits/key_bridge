import Foundation

public enum FrameCodecError: Error, LocalizedError, Equatable {
    case invalidLength(Int)
    case frameTooLarge(Int)
    case unexpectedEOF

    public var errorDescription: String? {
        switch self {
        case .invalidLength(let length):
            "Invalid frame length: \(length)"
        case .frameTooLarge(let length):
            "Frame too large: \(length)"
        case .unexpectedEOF:
            "Unexpected end of stream while reading frame."
        }
    }
}

public protocol ClientConnection: AnyObject {
    var remoteAddress: String { get }
    func readExactly(_ count: Int) async throws -> Data?
    func write(_ data: Data) async throws
    func close()
}

public enum FrameCodec {
    private static let encoder: JSONEncoder = {
        let encoder = JSONEncoder()
        encoder.outputFormatting = [.withoutEscapingSlashes]
        return encoder
    }()

    private static let decoder = JSONDecoder()

    public static func encode(_ envelope: Envelope) throws -> Data {
        let payload = try encoder.encode(envelope)
        guard payload.count <= BlueTypeConstants.maxFrameSize else {
            throw FrameCodecError.frameTooLarge(payload.count)
        }

        var frame = Data()
        var length = UInt32(payload.count).bigEndian
        frame.append(Data(bytes: &length, count: MemoryLayout<UInt32>.size))
        frame.append(payload)
        return frame
    }

    public static func decodePayload(_ payload: Data) throws -> Envelope {
        do {
            return try decoder.decode(Envelope.self, from: payload)
        } catch {
            let string = String(data: payload, encoding: .utf8) ?? "binary data"
            NSLog("Failed to decode envelope: \(error.localizedDescription). Raw payload: \(string)")
            throw error
        }
    }

    public static func write(_ envelope: Envelope, to connection: ClientConnection) async throws {
        try await connection.write(try encode(envelope))
    }

    public static func readFrame(from connection: ClientConnection) async throws -> Envelope? {
        guard let lengthBytes = try await connection.readExactly(4) else {
            return nil
        }
        guard lengthBytes.count == 4 else {
            throw FrameCodecError.unexpectedEOF
        }

        let length = lengthBytes.reduce(0) { ($0 << 8) | Int($1) }
        if length > BlueTypeConstants.maxFrameSize || length <= 0 {
            let hex = lengthBytes.map { String(format: "%02x", $0) }.joined()
            NSLog("Invalid frame length: \(length). Hex header: \(hex)")
            throw FrameCodecError.invalidLength(length)
        }
        
        guard let payload = try await connection.readExactly(length) else {
            throw FrameCodecError.unexpectedEOF
        }
        guard payload.count == length else {
            throw FrameCodecError.unexpectedEOF
        }
        return try decodePayload(payload)
    }
}
