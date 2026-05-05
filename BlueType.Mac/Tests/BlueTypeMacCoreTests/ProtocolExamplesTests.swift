import Foundation
import XCTest
@testable import BlueTypeMacCore

final class ProtocolExamplesTests: XCTestCase {
    private let knownTypes: Set<String> = [
        MessageType.hello,
        MessageType.textInsert,
        MessageType.keyTap,
        MessageType.keyDown,
        MessageType.keyUp,
        MessageType.combo,
        MessageType.mouseMove,
        MessageType.mouseButton,
        MessageType.mouseClick,
        MessageType.mouseScroll,
        MessageType.clipboardSet,
        MessageType.clipboardGet,
        MessageType.ping,
        MessageType.pong,
        MessageType.ack,
        MessageType.error,
        MessageType.authPending,
        MessageType.authResult,
        MessageType.clipboardValue,
        MessageType.shortcutProfile,
    ]

    private let knownErrorCodes: Set<String> = [
        "BUSY",
        "NOT_AUTHORIZED",
        "AUTH_TIMEOUT",
        "AUTH_UI_UNAVAILABLE",
        "INVALID_PAYLOAD",
        "SERVER_ERROR",
        "SESSION_REPLACED",
        "INPUT_BLOCKED",
        "CLIPBOARD_FAILED",
    ]

    func testProtocolExamplesDecodeAndRoundTripThroughFrameCodec() throws {
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

            XCTAssertEqual(envelope.v, 1, file.lastPathComponent)
            XCTAssertFalse(envelope.id.isEmpty, file.lastPathComponent)
            XCTAssertTrue(knownTypes.contains(envelope.type), "Unknown type \(envelope.type) in \(file.lastPathComponent)")

            if envelope.type == MessageType.error {
                let code = envelope.payload["code"]?.stringValue
                XCTAssertNotNil(code, file.lastPathComponent)
                XCTAssertTrue(knownErrorCodes.contains(code ?? ""), "Unknown error code \(code ?? "nil") in \(file.lastPathComponent)")
            }

            let frame = try FrameCodec.encode(envelope)
            let decoded = try FrameCodec.decodePayload(frame.dropFirst(4))
            XCTAssertEqual(decoded, envelope, file.lastPathComponent)
        }
    }

    private func findExamplesDirectory() throws -> URL {
        var current = URL(fileURLWithPath: FileManager.default.currentDirectoryPath)
        while true {
            let candidate = current
                .appendingPathComponent("protocol")
                .appendingPathComponent("spec")
                .appendingPathComponent("examples")
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
            userInfo: [NSLocalizedDescriptionKey: "Could not find protocol/spec/examples from \(FileManager.default.currentDirectoryPath)."]
        )
    }
}
