import Foundation
import XCTest
@testable import BlueTypeMacCore

final class ProtocolExamplesTests: XCTestCase {
    func testProtocolExamplesDecodeAndRoundTripThroughFrameCodec() throws {
        let manifest = try readManifest()
        let examplesDirectory = try findExamplesDirectory()
        let exampleFiles = try FileManager.default.contentsOfDirectory(
            at: examplesDirectory,
            includingPropertiesForKeys: nil
        )
            .filter { $0.pathExtension == "json" }
            .sorted { $0.lastPathComponent < $1.lastPathComponent }

        XCTAssertFalse(exampleFiles.isEmpty)

        for file in exampleFiles {
            let payload = try Data(contentsOf: file)
            let envelope = try JSONDecoder().decode(Envelope.self, from: payload)

            XCTAssertEqual(envelope.v, manifest.version, file.lastPathComponent)
            XCTAssertFalse(envelope.id.isEmpty, file.lastPathComponent)
            XCTAssertTrue(
                (manifest.commands + manifest.responses).contains(envelope.type),
                "Unknown type \(envelope.type) in \(file.lastPathComponent)"
            )

            if envelope.type == MessageType.error {
                let code = envelope.payload["code"]?.stringValue
                XCTAssertNotNil(code, file.lastPathComponent)
                XCTAssertTrue(
                    manifest.errorCodes.contains(code ?? ""),
                    "Unknown error code \(code ?? "nil") in \(file.lastPathComponent)"
                )
            }

            let frame = try FrameCodec.encode(envelope)
            let decoded = try FrameCodec.decodePayload(frame.dropFirst(4))
            XCTAssertEqual(decoded, envelope, file.lastPathComponent)
        }
    }

    func testProtocolConstantsMatchManifest() throws {
        let manifest = try readManifest()

        XCTAssertEqual(Set(manifest.commands + manifest.responses), Set(MessageType.all))
    }

    func testInvalidProtocolExamplesAreRejectedByV1Contract() throws {
        let manifest = try readManifest()
        let invalidDirectory = try findInvalidDirectory()
        let invalidFiles = try FileManager.default.contentsOfDirectory(
            at: invalidDirectory,
            includingPropertiesForKeys: nil
        )
            .filter { $0.pathExtension == "json" }
            .sorted { $0.lastPathComponent < $1.lastPathComponent }

        XCTAssertFalse(invalidFiles.isEmpty)

        for file in invalidFiles {
            let root = try XCTUnwrap(JSONSerialization.jsonObject(with: Data(contentsOf: file)) as? [String: Any])
            XCTAssertFalse(isValidEnvelope(root, manifest: manifest), file.lastPathComponent)
        }
    }

    func testFrameFixturesEncodeAndDecodeWithCanonicalBytes() throws {
        let framesDirectory = try findSpecDirectory().appendingPathComponent("frames")
        let frameFiles = try FileManager.default.contentsOfDirectory(
            at: framesDirectory,
            includingPropertiesForKeys: nil
        )
            .filter { $0.pathExtension == "json" }
            .sorted { $0.lastPathComponent < $1.lastPathComponent }

        XCTAssertFalse(frameFiles.isEmpty)

        for file in frameFiles {
            let fixture = try JSONDecoder().decode(FrameFixture.self, from: Data(contentsOf: file))
            let expected = try JSONDecoder().decode(Envelope.self, from: Data(fixture.json.utf8))
            let expectedFrame = try dataFromHex(fixture.frameHex)
            let decoded = try FrameCodec.decodePayload(expectedFrame.dropFirst(4))

            XCTAssertEqual(decoded, expected, file.lastPathComponent)
            XCTAssertEqual(frameLength(expectedFrame), expectedFrame.count - 4, file.lastPathComponent)

            let encoded = try FrameCodec.encode(expected)
            XCTAssertEqual(frameLength(encoded), encoded.count - 4, file.lastPathComponent)
            let redecoded = try FrameCodec.decodePayload(encoded.dropFirst(4))
            XCTAssertEqual(redecoded, expected, file.lastPathComponent)
        }
    }

    private struct ProtocolManifest: Decodable {
        let version: Int
        let commands: [String]
        let responses: [String]
        let errorCodes: [String]
    }

    private struct FrameFixture: Decodable {
        let json: String
        let frameHex: String
    }

    private func readManifest() throws -> ProtocolManifest {
        let manifestURL = try findSpecDirectory().appendingPathComponent("protocol-v1.json")
        return try JSONDecoder().decode(ProtocolManifest.self, from: Data(contentsOf: manifestURL))
    }

    private func isValidEnvelope(_ root: [String: Any], manifest: ProtocolManifest) -> Bool {
        guard let version = root["v"] as? Int,
              let id = root["id"] as? String,
              let type = root["type"] as? String,
              let payload = root["payload"] as? [String: Any],
              version == manifest.version,
              !id.isEmpty,
              !type.isEmpty,
              (manifest.commands + manifest.responses).contains(type) else {
            return false
        }

        switch type {
        case MessageType.hello:
            return stringField(payload, "deviceId", nonEmpty: true) != nil &&
                stringField(payload, "deviceName", nonEmpty: true) != nil
        case MessageType.textInsert:
            return stringField(payload, "text") != nil
        case MessageType.keyTap, MessageType.keyDown, MessageType.keyUp:
            return stringField(payload, "key", nonEmpty: true) != nil
        case MessageType.combo:
            return stringArrayField(payload, "keys")
        case MessageType.mouseMove:
            return intField(payload, "dx") != nil && intField(payload, "dy") != nil
        case MessageType.mouseButton:
            return stringField(payload, "button", nonEmpty: true) != nil &&
                oneOf(payload, "action", values: ["down", "up"])
        case MessageType.mouseClick:
            return stringField(payload, "button", nonEmpty: true) != nil && optionalIntField(payload, "repeat")
        case MessageType.mouseScroll:
            return optionalIntField(payload, "deltaX") && optionalIntField(payload, "deltaY")
        case MessageType.clipboardSet:
            return stringField(payload, "text") != nil
        case MessageType.clipboardGet, MessageType.ping, MessageType.pong:
            return true
        case MessageType.ack:
            return boolField(payload, "ok")
        case MessageType.error:
            guard let code = stringField(payload, "code") else { return false }
            return stringField(payload, "message") != nil && manifest.errorCodes.contains(code)
        case MessageType.authPending:
            return intField(payload, "timeoutSec") != nil && stringField(payload, "message") != nil
        case MessageType.authResult:
            return boolField(payload, "ok") &&
                boolField(payload, "persistToken") &&
                boolField(payload, "trusted") &&
                optionalNullableStringField(payload, "token")
        case MessageType.clipboardValue:
            return stringField(payload, "text") != nil
        case MessageType.shortcutProfile:
            return nullableStringField(payload, "name") && nullableObjectField(payload, "profile")
        default:
            return false
        }
    }

    private func stringField(_ object: [String: Any], _ name: String, nonEmpty: Bool = false) -> String? {
        guard let value = object[name] as? String else { return nil }
        return nonEmpty && value.isEmpty ? nil : value
    }

    private func intField(_ object: [String: Any], _ name: String) -> Int? {
        return object[name] as? Int
    }

    private func optionalIntField(_ object: [String: Any], _ name: String) -> Bool {
        return object[name] == nil || intField(object, name) != nil
    }

    private func boolField(_ object: [String: Any], _ name: String) -> Bool {
        return object[name] as? Bool != nil
    }

    private func stringArrayField(_ object: [String: Any], _ name: String) -> Bool {
        guard let values = object[name] as? [Any] else { return false }
        return values.allSatisfy { $0 is String }
    }

    private func nullableStringField(_ object: [String: Any], _ name: String) -> Bool {
        return object[name] is NSNull || stringField(object, name) != nil
    }

    private func optionalNullableStringField(_ object: [String: Any], _ name: String) -> Bool {
        return object[name] == nil || nullableStringField(object, name)
    }

    private func nullableObjectField(_ object: [String: Any], _ name: String) -> Bool {
        return object[name] is NSNull || object[name] is [String: Any]
    }

    private func oneOf(_ object: [String: Any], _ name: String, values: [String]) -> Bool {
        guard let value = stringField(object, name) else { return false }
        return values.contains(value)
    }

    private func dataFromHex(_ hex: String) throws -> Data {
        let bytes = Array(hex.utf8)
        guard bytes.count.isMultiple(of: 2) else {
            throw NSError(domain: "BlueTypeMacCoreTests", code: 2, userInfo: [NSLocalizedDescriptionKey: "Frame hex has an incomplete byte."])
        }

        var data = Data()
        for index in stride(from: 0, to: bytes.count, by: 2) {
            let pair = String(bytes: bytes[index..<(index + 2)], encoding: .utf8)!
            guard let value = UInt8(pair, radix: 16) else {
                throw NSError(domain: "BlueTypeMacCoreTests", code: 3, userInfo: [NSLocalizedDescriptionKey: "Frame hex contains an invalid byte."])
            }
            data.append(value)
        }
        return data
    }

    private func frameLength(_ frame: Data) -> Int {
        frame.prefix(4).reduce(0) { ($0 << 8) | Int($1) }
    }

    private func findSpecDirectory() throws -> URL {
        var current = URL(fileURLWithPath: FileManager.default.currentDirectoryPath)
        while true {
            let candidate = current
                .appendingPathComponent("protocol")
                .appendingPathComponent("spec")
            var isDirectory: ObjCBool = false
            if FileManager.default.fileExists(atPath: candidate.path, isDirectory: &isDirectory), isDirectory.boolValue {
                return candidate
            }

            let parent = current.deletingLastPathComponent()
            if parent.path == current.path {
                break
            }
            current = parent
        }

        throw NSError(
            domain: "BlueTypeMacCoreTests",
            code: 1,
            userInfo: [NSLocalizedDescriptionKey: "Could not find protocol/spec from \(FileManager.default.currentDirectoryPath)."]
        )
    }

    private func findExamplesDirectory() throws -> URL {
        return try findSpecDirectory().appendingPathComponent("examples")
    }

    private func findInvalidDirectory() throws -> URL {
        return try findSpecDirectory().appendingPathComponent("invalid")
    }
}
