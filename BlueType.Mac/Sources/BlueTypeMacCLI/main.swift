import BlueTypeMacCore
import Foundation

final class ConsoleStateSink: AppStateSink {
    func updateState(_ state: ConnectionState) {
        print("[state] \(describe(state))")
        fflush(stdout)
    }

    func postMessage(_ message: String) {
        print("[message] \(message)")
        fflush(stdout)
    }

    private func describe(_ state: ConnectionState) -> String {
        switch state {
        case .idle:
            return "idle"
        case .listening(let tcp, let bluetooth):
            return "listening tcp=\(tcp) bluetooth=\(bluetooth)"
        case .clientConnected(let transport, let remoteAddress):
            return "clientConnected transport=\(transport) remote=\(remoteAddress)"
        case .authenticating(let deviceName):
            return "authenticating device=\(deviceName)"
        case .pendingApproval(let deviceName):
            return "pendingApproval device=\(deviceName)"
        case .connected(let deviceName, let transport, let remoteAddress):
            return "connected device=\(deviceName) transport=\(transport) remote=\(remoteAddress)"
        case .error(let message):
            return "error \(message)"
        }
    }
}

let sink = ConsoleStateSink()
let agent = MacAgent(stateSink: sink) { request in
    switch request.mode {
    case .authorizeDevice:
        print("[approval] Allow \(request.hello.deviceName) to control this Mac? (Always/Once/Deny) [default Once]:")
        fflush(stdout)
        return .allowOnce
    case .switchActiveDevice(let activeDeviceName):
        print("[takeover] Switch control from \(activeDeviceName) to \(request.hello.deviceName)? (Allow/Deny) [default Allow]:")
        fflush(stdout)
        return .allowOnce
    }
}

print("BlueTypeMacCLI starting. Wi-Fi addresses: \(NetworkInfo.localIPv4Addresses().map { "\($0):\(BlueTypeConstants.tcpPort)" }.joined(separator: ", "))")
agent.start()

dispatchMain()
