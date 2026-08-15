import XCTest
@testable import BlueTypeMacCore

final class FrameCodecTests: XCTestCase {
    func testEncodeUsesBigEndianLengthPrefix() throws {
        let envelope = Envelope(id: "1", type: MessageType.ping)
        let frame = try FrameCodec.encode(envelope)
        let length = frame.prefix(4).reduce(0) { ($0 << 8) | Int($1) }

        XCTAssertEqual(length, frame.count - 4)
        let decoded = try FrameCodec.decodePayload(frame.dropFirst(4))
        XCTAssertEqual(decoded.type, MessageType.ping)
        XCTAssertEqual(decoded.id, "1")
    }

    func testReadRejectsInvalidLength() async throws {
        let connection = MemoryConnection(readData: Data([0, 0, 0, 0]))
        do {
            _ = try await FrameCodec.readFrame(from: connection)
            XCTFail("Expected invalid length.")
        } catch FrameCodecError.invalidLength(0) {
        } catch {
            XCTFail("Unexpected error: \(error)")
        }
    }

    func testReadRejectsTruncatedPayload() async throws {
        let connection = MemoryConnection(readData: Data([0, 0, 0, 4, 1, 2]))
        do {
            _ = try await FrameCodec.readFrame(from: connection)
            XCTFail("Expected truncated payload rejection.")
        } catch FrameCodecError.unexpectedEOF {
        } catch {
            XCTFail("Unexpected error: \(error)")
        }
    }

    func testRoundTripThroughPartialReads() async throws {
        let envelope = Envelope(id: "hello", type: MessageType.hello, payload: ["deviceId": .string("android"), "deviceName": .string("Pixel")])
        let frame = try FrameCodec.encode(envelope)
        let connection = MemoryConnection(readData: frame, maxReadSize: 2)

        let decoded = try await FrameCodec.readFrame(from: connection)
        XCTAssertEqual(decoded, envelope)
    }

    func testOversizedFrameFails() throws {
        let text = String(repeating: "x", count: BlueTypeConstants.maxFrameSize + 1)
        let envelope = Envelope(id: "large", type: MessageType.textInsert, payload: ["text": .string(text)])
        XCTAssertThrowsError(try FrameCodec.encode(envelope))
    }
}

final class MemoryConnection: ClientConnection {
    let remoteAddress = "memory"
    private var readData: Data
    private let maxReadSize: Int
    private(set) var written = Data()

    init(readData: Data = Data(), maxReadSize: Int = Int.max) {
        self.readData = readData
        self.maxReadSize = maxReadSize
    }

    func readExactly(_ count: Int) async throws -> Data? {
        guard !readData.isEmpty else { return nil }
        var result = Data()
        while result.count < count, !readData.isEmpty {
            let size = min(count - result.count, maxReadSize, readData.count)
            let chunk = readData.prefix(size)
            readData.removeFirst(size)
            result.append(chunk)
        }
        return result
    }

    func write(_ data: Data) async throws {
        written.append(data)
    }

    func close() {}
}
