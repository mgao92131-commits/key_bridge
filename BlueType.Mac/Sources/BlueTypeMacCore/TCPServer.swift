import Foundation
import Network

public protocol TransportServer: AnyObject {
    var name: String { get }
    func start(onClient: @escaping (ClientConnection, String) -> Void) throws
    func stop()
}

public final class TCPServer: TransportServer {
    public let name = "wifi"

    private var listener: NWListener?
    private let queue = DispatchQueue(label: "BlueType.TCPServer")
    private let port: NWEndpoint.Port

    public init(port: UInt16 = BlueTypeConstants.tcpPort) {
        self.port = NWEndpoint.Port(rawValue: port)!
    }

    public func start(onClient: @escaping (ClientConnection, String) -> Void) throws {
        let listener = try NWListener(using: .tcp, on: port)
        listener.newConnectionHandler = { connection in
            NSLog("Incoming connection attempt from: \(connection.endpoint)")
            var handedOff = false
            connection.stateUpdateHandler = { state in
                NSLog("Connection state for \(connection.endpoint) updated to: \(state)")
                if case .ready = state, !handedOff {
                    handedOff = true
                    onClient(NWClientConnection(connection: connection), self.name)
                }
            }
            connection.start(queue: self.queue)
        }
        listener.start(queue: queue)
        self.listener = listener
    }

    public func stop() {
        listener?.cancel()
        listener = nil
    }
}

public final class NWClientConnection: ClientConnection, @unchecked Sendable {
    private let connection: NWConnection
    private let writeQueue = DispatchQueue(label: "BlueType.NWClientConnection.Write")

    public var remoteAddress: String {
        switch connection.endpoint {
        case .hostPort(let host, let port):
            return "\(host):\(port)"
        default:
            return "\(connection.endpoint)"
        }
    }

    public init(connection: NWConnection) {
        self.connection = connection
    }

    public func readExactly(_ count: Int) async throws -> Data? {
        var result = Data()
        while result.count < count {
            let chunk = try await receive(maximumLength: count - result.count)
            guard let chunk else {
                return result.isEmpty ? nil : result
            }
            if chunk.isEmpty {
                continue
            }
            result.append(chunk)
        }
        return result
    }

    public func write(_ data: Data) async throws {
        try await withCheckedThrowingContinuation { (continuation: CheckedContinuation<Void, Error>) in
            writeQueue.async {
                self.connection.send(content: data, completion: .contentProcessed { error in
                    if let error {
                        continuation.resume(throwing: error)
                    } else {
                        continuation.resume(returning: ())
                    }
                })
            }
        }
    }

    public func close() {
        connection.cancel()
    }

    private func receive(maximumLength: Int) async throws -> Data? {
        try await withCheckedThrowingContinuation { continuation in
            connection.receive(minimumIncompleteLength: 1, maximumLength: maximumLength) { data, _, isComplete, error in
                if let error {
                    continuation.resume(throwing: error)
                } else if let data, !data.isEmpty {
                    continuation.resume(returning: data)
                } else if isComplete {
                    continuation.resume(returning: nil)
                } else {
                    continuation.resume(returning: Data())
                }
            }
        }
    }
}
