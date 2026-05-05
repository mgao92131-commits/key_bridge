import XCTest
@testable import BlueTypeMacCore

final class AuthAndRoutingTests: XCTestCase {
    func testKnownDeviceReconnectsWithToken() async throws {
        let url = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString).appendingPathComponent("devices.json")
        let registry = DeviceRegistry(fileURL: url)
        let token = "secret"
        try registry.upsert(
            TrustedDevice(
                deviceId: "device-1",
                deviceName: "Pixel",
                lastAddress: nil,
                lastTransport: "wifi",
                tokenHash: DeviceRegistry.hashToken(token),
                lastSeenAt: Date()
            )
        )

        let service = AuthService(registry: registry) { _ in .deny }
        let result = try service.tryAuthorizeKnownDevice(
            HelloInfo(deviceId: "device-1", deviceName: "Pixel 9", appVersion: "1.0"),
            token: token,
            remoteAddress: "127.0.0.1:24862",
            transport: "wifi"
        )

        XCTAssertTrue(result.authorized)
        XCTAssertTrue(result.persistToken)
        XCTAssertEqual(result.token, token)
    }

    func testInvalidTokenIsRejected() async throws {
        let url = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString).appendingPathComponent("devices.json")
        let registry = DeviceRegistry(fileURL: url)
        try registry.upsert(
            TrustedDevice(
                deviceId: "device-1",
                deviceName: "Pixel",
                lastAddress: nil,
                lastTransport: "wifi",
                tokenHash: DeviceRegistry.hashToken("secret"),
                lastSeenAt: Date()
            )
        )

        let service = AuthService(registry: registry) { _ in .deny }
        let result = try service.tryAuthorizeKnownDevice(
            HelloInfo(deviceId: "device-1", deviceName: "Pixel", appVersion: nil),
            token: "wrong",
            remoteAddress: "127.0.0.1",
            transport: "wifi"
        )

        XCTAssertFalse(result.authorized)
        XCTAssertEqual(result.errorCode, "NOT_AUTHORIZED")
    }

    func testActiveSessionSameDeviceTakesOver() async {
        let manager = ActiveSessionManager()
        var firstDisconnected = false
        let first = ActiveSessionManager.Candidate(
            sessionId: UUID(),
            deviceId: "device-1",
            deviceName: "Pixel",
            transport: "wifi",
            remoteAddress: "a",
            disconnect: { firstDisconnected = true }
        )
        let second = ActiveSessionManager.Candidate(
            sessionId: UUID(),
            deviceId: "DEVICE-1",
            deviceName: "Pixel",
            transport: "bluetooth",
            remoteAddress: "b",
            disconnect: {}
        )

        _ = await manager.activate(first) { _ in false }
        let result = await manager.activate(second) { _ in false }
        if case .takeover(_, let disconnect) = result {
            disconnect()
        } else {
            XCTFail("Expected takeover.")
        }
        XCTAssertTrue(firstDisconnected)
    }

    func testCtrlAndCmdKeepDistinctMacSemantics() throws {
        let control = try InputInjector.resolveKey("CTRL")
        let command = try InputInjector.resolveKey("CMD")

        XCTAssertTrue(control.flags.contains(.maskControl))
        XCTAssertFalse(control.flags.contains(.maskCommand))
        XCTAssertTrue(command.flags.contains(.maskCommand))
        XCTAssertFalse(command.flags.contains(.maskControl))
    }

    func testModifierKeyUpDropsReleasedModifierFlag() throws {
        let command = try InputInjector.resolveKey("CMD")
        let flags = InputInjector.eventFlags(
            definition: command,
            down: false,
            currentFlags: [.maskCommand],
            extraFlags: []
        )

        XCTAssertFalse(flags.contains(.maskCommand))
    }

    func testRegularKeyTapKeepsCurrentModifierFlag() throws {
        let c = try InputInjector.resolveKey("C")
        let flags = InputInjector.eventFlags(
            definition: c,
            down: true,
            currentFlags: [.maskCommand],
            extraFlags: []
        )

        XCTAssertTrue(flags.contains(.maskCommand))
    }

    func testMouseTargetMovesRightForPositiveDx() {
        let target = InputInjector.targetMousePosition(
            current: CGPoint(x: 100, y: 100),
            dx: 12,
            dy: 0,
            bounds: CGRect(x: 0, y: 0, width: 500, height: 400)
        )

        XCTAssertEqual(target, CGPoint(x: 112, y: 100))
    }

    func testMouseTargetMovesDownForPositiveDy() {
        let target = InputInjector.targetMousePosition(
            current: CGPoint(x: 100, y: 100),
            dx: 0,
            dy: 12,
            bounds: CGRect(x: 0, y: 0, width: 500, height: 400)
        )

        XCTAssertEqual(target, CGPoint(x: 100, y: 112))
    }

    func testMouseTargetMovesUpForNegativeDy() {
        let target = InputInjector.targetMousePosition(
            current: CGPoint(x: 100, y: 100),
            dx: 0,
            dy: -12,
            bounds: CGRect(x: 0, y: 0, width: 500, height: 400)
        )

        XCTAssertEqual(target, CGPoint(x: 100, y: 88))
    }

    func testMouseTargetClampsToDisplayBounds() {
        let target = InputInjector.targetMousePosition(
            current: CGPoint(x: 100, y: 100),
            dx: 1_000,
            dy: -1_000,
            bounds: CGRect(x: 20, y: 30, width: 200, height: 100)
        )

        XCTAssertEqual(target, CGPoint(x: 219, y: 30))
    }
}
