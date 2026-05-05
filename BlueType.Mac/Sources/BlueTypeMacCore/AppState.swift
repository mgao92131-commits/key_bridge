import Foundation

public enum ConnectionState: Equatable {
    case idle
    case listening(tcp: Bool, bluetooth: Bool)
    case clientConnected(transport: String, remoteAddress: String)
    case authenticating(deviceName: String)
    case pendingApproval(deviceName: String)
    case connected(deviceName: String, transport: String, remoteAddress: String)
    case error(String)
}

public protocol AppStateSink: AnyObject {
    func updateState(_ state: ConnectionState)
    func postMessage(_ message: String)
}

public final class ActiveSessionManager {
    public struct Candidate {
        public let sessionId: UUID
        public let deviceId: String
        public let deviceName: String
        public let transport: String
        public let remoteAddress: String
        public let disconnect: () -> Void
    }

    public struct Snapshot: Equatable {
        public let sessionId: UUID
        public let deviceId: String
        public let deviceName: String
        public let transport: String
        public let remoteAddress: String
    }

    public enum ActivationResult {
        case activated
        case takeover(replaced: Snapshot, disconnect: () -> Void)
        case denied(active: Snapshot)
    }

    private struct Registration {
        let candidate: Candidate

        var snapshot: Snapshot {
            Snapshot(
                sessionId: candidate.sessionId,
                deviceId: candidate.deviceId,
                deviceName: candidate.deviceName,
                transport: candidate.transport,
                remoteAddress: candidate.remoteAddress
            )
        }
    }

    private let lock = NSLock()
    private var active: Registration?

    private enum ActivationStep {
        case activated
        case takeover(Registration)
        case needsConfirmation(Snapshot)
        case retry
    }

    public init() {}

    public func currentSnapshot() -> Snapshot? {
        lock.withLock { active?.snapshot }
    }

    public func isActive(_ sessionId: UUID) -> Bool {
        lock.withLock { active?.candidate.sessionId == sessionId }
    }

    public func activate(
        _ candidate: Candidate,
        confirmTakeover: @MainActor (Snapshot) async -> Bool
    ) async -> ActivationResult {
        while true {
            switch beginActivation(candidate) {
            case .activated:
                return .activated
            case .takeover(let previous):
                return .takeover(replaced: previous.snapshot, disconnect: previous.candidate.disconnect)
            case .retry:
                continue
            case .needsConfirmation(let snapshot):
                guard await confirmTakeover(snapshot) else {
                    return .denied(active: snapshot)
                }
                switch finishConfirmedActivation(candidate, expected: snapshot.sessionId) {
                case .activated:
                    return .activated
                case .takeover(let previous):
                    return .takeover(replaced: previous.snapshot, disconnect: previous.candidate.disconnect)
                case .retry, .needsConfirmation:
                    continue
                }
            }
        }
    }

    private func beginActivation(_ candidate: Candidate) -> ActivationStep {
        lock.withLock {
            if active == nil || active?.candidate.sessionId == candidate.sessionId {
                active = Registration(candidate: candidate)
                return .activated
            }

            if active?.candidate.deviceId.caseInsensitiveCompare(candidate.deviceId) == .orderedSame {
                let previous = active!
                active = Registration(candidate: candidate)
                return .takeover(previous)
            }

            guard let snapshot = active?.snapshot else {
                return .retry
            }
            return .needsConfirmation(snapshot)
        }
    }

    private func finishConfirmedActivation(_ candidate: Candidate, expected sessionId: UUID) -> ActivationStep {
        lock.withLock {
            if active == nil || active?.candidate.sessionId == candidate.sessionId {
                active = Registration(candidate: candidate)
                return .activated
            }

            guard active?.candidate.sessionId == sessionId else {
                return .retry
            }

            let previous = active!
            active = Registration(candidate: candidate)
            return .takeover(previous)
        }
    }

    public func deactivate(_ sessionId: UUID) {
        lock.withLock {
            if active?.candidate.sessionId == sessionId {
                active = nil
            }
        }
    }

    public func disconnectActive() -> Bool {
        guard let disconnect = lock.withLock({ active?.candidate.disconnect }) else {
            return false
        }
        disconnect()
        return true
    }
}

private extension NSLock {
    func withLock<T>(_ body: () throws -> T) rethrows -> T {
        lock()
        defer { unlock() }
        return try body()
    }
}
