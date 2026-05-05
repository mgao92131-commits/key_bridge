import Foundation

public final class SessionProcessor {
    private let router: CommandRouter
    private let authService: AuthService
    private let activeSessions: ActiveSessionManager
    private let shortcutProfiles: ShortcutProfileDispatcher
    private weak var stateSink: AppStateSink?
    private let confirmTakeover: @MainActor (AuthPromptRequest) async -> AuthPromptDecision

    public init(
        router: CommandRouter,
        authService: AuthService,
        activeSessions: ActiveSessionManager,
        shortcutProfiles: ShortcutProfileDispatcher,
        stateSink: AppStateSink?,
        confirmTakeover: @escaping @MainActor (AuthPromptRequest) async -> AuthPromptDecision
    ) {
        self.router = router
        self.authService = authService
        self.activeSessions = activeSessions
        self.shortcutProfiles = shortcutProfiles
        self.stateSink = stateSink
        self.confirmTakeover = confirmTakeover
    }

    public func run(connection: ClientConnection, transport: String) {
        Task.detached { [self] in
            await self.process(connection: connection, transport: transport)
        }
    }

    private func process(connection: ClientConnection, transport: String) async {
        let sessionId = UUID()
        let remoteAddress = connection.remoteAddress
        var authorized = false
        var lastInboundAt = Date()
        var helloInfo: HelloInfo?
        let sessionTask = Task.currentPriority
        _ = sessionTask

        func disconnect() {
            connection.close()
        }

        stateSink?.updateState(.clientConnected(transport: transport, remoteAddress: remoteAddress))
        stateSink?.postMessage("\(transport) client connected from \(remoteAddress).")

        let heartbeat = Task {
            while !Task.isCancelled {
                try? await Task.sleep(nanoseconds: UInt64(BlueTypeConstants.heartbeatInterval * 1_000_000_000))
                if Task.isCancelled { break }
                let silence = Date().timeIntervalSince(lastInboundAt)
                if silence >= BlueTypeConstants.heartbeatTimeout {
                    stateSink?.postMessage("\(transport) client \(remoteAddress) timed out after \(Int(BlueTypeConstants.heartbeatTimeout)) seconds.")
                    connection.close()
                    break
                }
                try? await FrameCodec.write(Envelope(id: UUID().uuidString, type: MessageType.ping), to: connection)
            }
        }

        defer {
            heartbeat.cancel()
            shortcutProfiles.unregisterSession(sessionId: sessionId)
            activeSessions.deactivate(sessionId)
            router.releaseAllInputs()
            stateSink?.updateState(.listening(tcp: true, bluetooth: true))
            stateSink?.postMessage("\(transport) client disconnected from \(remoteAddress).")
            connection.close()
        }

        do {
            while !Task.isCancelled {
                guard let envelope = try await FrameCodec.readFrame(from: connection) else {
                    stateSink?.postMessage("\(transport) client \(remoteAddress) closed before next frame.")
                    break
                }
                lastInboundAt = Date()
                stateSink?.postMessage(receivedMessage(envelope, transport: transport, remoteAddress: remoteAddress))

                if envelope.type == MessageType.ping {
                    try await FrameCodec.write(Envelope(id: envelope.id, type: MessageType.pong), to: connection)
                    continue
                }
                if envelope.type == MessageType.pong {
                    continue
                }

                if envelope.type == MessageType.hello {
                    if authorized {
                        try await FrameCodec.write(Envelope.error(id: envelope.id, code: "INVALID_PAYLOAD", message: "HELLO already completed."), to: connection)
                        continue
                    }

                    let result = try await handleHello(
                        envelope,
                        connection: connection,
                        transport: transport,
                        remoteAddress: remoteAddress,
                        sessionId: sessionId,
                        disconnectCurrentSession: disconnect
                    )
                    authorized = result
                    if !authorized { break }
                    helloInfo = try? parseHello(envelope.payload)
                    shortcutProfiles.registerSession(sessionId: sessionId, connection: connection)
                    continue
                }

                guard authorized else {
                    try await FrameCodec.write(Envelope.error(id: envelope.id, code: "NOT_AUTHORIZED", message: "Send HELLO and complete authorization first."), to: connection)
                    continue
                }

                guard activeSessions.isActive(sessionId) else {
                    try await FrameCodec.write(Envelope.error(id: envelope.id, code: "SESSION_REPLACED", message: "This connection is no longer the active control session."), to: connection)
                    break
                }

                let response = router.route(envelope)
                if response.type == MessageType.error {
                    let code = response.payload["code"]?.stringValue ?? "UNKNOWN_ERROR"
                    let message = response.payload["message"]?.stringValue ?? "Command failed."
                    stateSink?.postMessage("\(transport) command \(envelope.type) id=\(envelope.id) failed: \(code) - \(message)")
                }
                try await FrameCodec.write(response, to: connection)
            }
        } catch {
            stateSink?.postMessage("\(transport) session failed for \(helloInfo?.deviceName ?? remoteAddress): \(error.localizedDescription)")
        }
    }

    private func receivedMessage(_ envelope: Envelope, transport: String, remoteAddress: String) -> String {
        let details: String
        switch envelope.type {
        case MessageType.keyTap, MessageType.keyDown, MessageType.keyUp:
            details = envelope.payload["key"]?.stringValue.map { " key=\($0)" } ?? ""
        case MessageType.combo:
            let keys = envelope.payload["keys"]?.stringArrayValue?.joined(separator: "+")
            details = keys.map { " keys=\($0)" } ?? ""
        case MessageType.textInsert:
            let byteCount = envelope.payload["text"]?.stringValue.map { Data($0.utf8).count } ?? 0
            details = " bytes=\(byteCount)"
        case MessageType.mouseMove:
            let dx = envelope.payload["dx"]?.intValue ?? 0
            let dy = envelope.payload["dy"]?.intValue ?? 0
            details = " dx=\(dx) dy=\(dy)"
        case MessageType.mouseButton:
            let button = envelope.payload["button"]?.stringValue ?? "?"
            let action = envelope.payload["action"]?.stringValue ?? "?"
            details = " button=\(button) action=\(action)"
        case MessageType.mouseClick:
            let button = envelope.payload["button"]?.stringValue ?? "?"
            let repeatCount = envelope.payload["repeat"]?.intValue ?? 1
            details = " button=\(button) repeat=\(repeatCount)"
        case MessageType.mouseScroll:
            let deltaX = envelope.payload["deltaX"]?.intValue ?? 0
            let deltaY = envelope.payload["deltaY"]?.intValue ?? 0
            details = " deltaX=\(deltaX) deltaY=\(deltaY)"
        default:
            details = ""
        }
        return "\(transport) received \(envelope.type) id=\(envelope.id)\(details) from \(remoteAddress)."
    }

    private func handleHello(
        _ envelope: Envelope,
        connection: ClientConnection,
        transport: String,
        remoteAddress: String,
        sessionId: UUID,
        disconnectCurrentSession: @escaping () -> Void
    ) async throws -> Bool {
        let hello: HelloInfo
        do {
            hello = try parseHello(envelope.payload)
        } catch {
            try await FrameCodec.write(Envelope.error(id: envelope.id, code: "INVALID_PAYLOAD", message: error.localizedDescription), to: connection)
            return false
        }

        stateSink?.updateState(.authenticating(deviceName: hello.deviceName))
        stateSink?.postMessage("Received HELLO from \(hello.deviceName) via \(transport).")

        let known = try authService.tryAuthorizeKnownDevice(hello, token: envelope.token, remoteAddress: remoteAddress, transport: transport)
        let authResult: AuthResult
        if known.authorized {
            authResult = known
        } else {
            try await FrameCodec.write(
                Envelope(
                    id: envelope.id,
                    type: MessageType.authPending,
                    payload: [
                        "timeoutSec": .number(BlueTypeConstants.authorizationTimeout),
                        "message": .string("Please confirm on Mac"),
                    ]
                ),
                to: connection
            )
            stateSink?.updateState(.pendingApproval(deviceName: hello.deviceName))
            stateSink?.postMessage("Waiting for approval for \(hello.deviceName).")
            authResult = try await withTimeout(seconds: BlueTypeConstants.authorizationTimeout) {
                try await self.authService.requestApproval(hello, remoteAddress: remoteAddress, transport: transport)
            } ?? .error("AUTH_TIMEOUT", "Authorization timed out.")
            stateSink?.postMessage("Approval result for \(hello.deviceName): \(authResult.authorized ? "authorized" : authResult.errorCode ?? "denied").")
        }

        guard authResult.authorized else {
            try await FrameCodec.write(
                Envelope.error(
                    id: envelope.id,
                    code: authResult.errorCode ?? "NOT_AUTHORIZED",
                    message: authResult.errorMessage ?? "Authorization failed."
                ),
                to: connection
            )
            return false
        }

        let activation = await activeSessions.activate(
            ActiveSessionManager.Candidate(
                sessionId: sessionId,
                deviceId: hello.deviceId,
                deviceName: hello.deviceName,
                transport: transport,
                remoteAddress: remoteAddress,
                disconnect: disconnectCurrentSession
            ),
            confirmTakeover: { [confirmTakeover] active in
                let decision = await confirmTakeover(
                    AuthPromptRequest(
                        mode: .switchActiveDevice(activeDeviceName: active.deviceName),
                        hello: hello,
                        remoteAddress: remoteAddress,
                        transport: transport
                    )
                )
                return decision == .allowOnce || decision == .alwaysAllow
            }
        )

        switch activation {
        case .activated:
            break
        case .takeover(let replaced, let disconnect):
            disconnect()
            stateSink?.postMessage("Switched active session from \(replaced.deviceName) to \(hello.deviceName).")
        case .denied(let active):
            try await FrameCodec.write(Envelope.error(id: envelope.id, code: "BUSY", message: "Another device is already controlling this Mac: \(active.deviceName)."), to: connection)
            return false
        }

        try await FrameCodec.write(
            Envelope(
                id: envelope.id,
                type: MessageType.authResult,
                payload: [
                    "ok": .bool(true),
                    "token": authResult.token.map(JSONValue.string) ?? .null,
                    "persistToken": .bool(authResult.persistToken),
                    "trusted": .bool(authResult.persistToken),
                ]
            ),
            to: connection
        )
        stateSink?.updateState(.connected(deviceName: hello.deviceName, transport: transport, remoteAddress: remoteAddress))
        stateSink?.postMessage("Authorized \(transport) device: \(hello.deviceName)")
        return true
    }

    private func parseHello(_ payload: [String: JSONValue]) throws -> HelloInfo {
        guard let deviceId = payload["deviceId"]?.stringValue, !deviceId.isEmpty else {
            throw SessionError.invalidHello("Missing hello.deviceId.")
        }
        guard let deviceName = payload["deviceName"]?.stringValue, !deviceName.isEmpty else {
            throw SessionError.invalidHello("Missing hello.deviceName.")
        }
        return HelloInfo(deviceId: deviceId, deviceName: deviceName, appVersion: payload["appVersion"]?.stringValue)
    }

    private func withTimeout<T>(seconds: TimeInterval, operation: @escaping () async throws -> T) async throws -> T? {
        try await withThrowingTaskGroup(of: T?.self) { group in
            group.addTask {
                try await operation()
            }
            group.addTask {
                try await Task.sleep(nanoseconds: UInt64(seconds * 1_000_000_000))
                return nil
            }
            let value = try await group.next()!
            group.cancelAll()
            return value
        }
    }
}

private enum SessionError: Error, LocalizedError {
    case invalidHello(String)

    var errorDescription: String? {
        switch self {
        case .invalidHello(let message):
            message
        }
    }
}
