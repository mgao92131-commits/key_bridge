import Foundation
import Darwin

public final class MacAgent {
    public let activeSessions = ActiveSessionManager()
    public let registry: DeviceRegistry

    private let stateSink: AppStateSink
    private let inputInjector: InputInjector
    private let clipboard = ClipboardService()
    private let shortcutProfiles = ShortcutProfileDispatcher()
    private var servers: [TransportServer] = []
    private var processor: SessionProcessor!

    public init(
        stateSink: AppStateSink,
        registry: DeviceRegistry = DeviceRegistry(),
        prompt: @escaping @MainActor (AuthPromptRequest) async -> AuthPromptDecision
    ) {
        self.stateSink = stateSink
        self.registry = registry
        self.inputInjector = InputInjector { [weak stateSink] message in
            stateSink?.postMessage(message)
        }
        let authService = AuthService(registry: registry, prompt: prompt)
        let router = CommandRouter(inputInjector: inputInjector, clipboard: clipboard)
        self.processor = SessionProcessor(
            router: router,
            authService: authService,
            activeSessions: activeSessions,
            shortcutProfiles: shortcutProfiles,
            stateSink: stateSink,
            confirmTakeover: prompt
        )
    }

    public func start() {
        var tcpStarted = false
        var bluetoothStarted = false

        do {
            let tcp = TCPServer()
            try tcp.start { [weak self] connection, transport in
                self?.processor.run(connection: connection, transport: transport)
            }
            servers.append(tcp)
            tcpStarted = true
            stateSink.postMessage("TCP server listening on port \(BlueTypeConstants.tcpPort).")
        } catch {
            stateSink.postMessage("Failed to start TCP server: \(error.localizedDescription)")
        }

        do {
            let bluetooth = BluetoothServer()
            try bluetooth.start { [weak self] connection, transport in
                self?.processor.run(connection: connection, transport: transport)
            }
            servers.append(bluetooth)
            bluetoothStarted = true
            stateSink.postMessage("Bluetooth RFCOMM server listening on channel \(bluetooth.publishedChannelID).")
        } catch {
            stateSink.postMessage("Failed to start Bluetooth server: \(error.localizedDescription)")
        }

        stateSink.updateState(.listening(tcp: tcpStarted, bluetooth: bluetoothStarted))
        if !InputInjector.isTrusted(prompt: true) {
            stateSink.postMessage("Accessibility/Input Monitoring permission is needed before remote input can be injected.")
        }
    }

    public func stop() {
        servers.forEach { $0.stop() }
        servers.removeAll()
        shortcutProfiles.stop()
    }

    public func disconnectActive() -> Bool {
        activeSessions.disconnectActive()
    }
}

public enum NetworkInfo {
    public static func localIPv4Addresses() -> [String] {
        var addresses: [String] = []
        var interfaces: UnsafeMutablePointer<ifaddrs>?
        guard getifaddrs(&interfaces) == 0, let first = interfaces else {
            return []
        }
        defer { freeifaddrs(interfaces) }

        for pointer in sequence(first: first, next: { $0.pointee.ifa_next }) {
            let interface = pointer.pointee
            guard interface.ifa_addr.pointee.sa_family == UInt8(AF_INET) else { continue }
            let name = String(cString: interface.ifa_name)
            guard name.hasPrefix("en") || name.hasPrefix("bridge") else { continue }

            var addr = interface.ifa_addr.pointee
            var hostname = [CChar](repeating: 0, count: Int(NI_MAXHOST))
            let result = getnameinfo(&addr, socklen_t(interface.ifa_addr.pointee.sa_len), &hostname, socklen_t(hostname.count), nil, 0, NI_NUMERICHOST)
            if result == 0 {
                addresses.append(String(cString: hostname))
            }
        }
        return Array(Set(addresses)).sorted()
    }
}
