import Foundation
import IOBluetooth

public final class BluetoothServer: NSObject, TransportServer {
    public let name = "bluetooth"

    private var channelID: BluetoothRFCOMMChannelID = 0
    private var serviceRecord: IOBluetoothSDPServiceRecord?
    private var notification: IOBluetoothUserNotification?
    private var onClient: ((ClientConnection, String) -> Void)?
    private var openChannels: [BluetoothChannelConnection] = []
    public var publishedChannelID: BluetoothRFCOMMChannelID {
        channelID
    }

    public func start(onClient: @escaping (ClientConnection, String) -> Void) throws {
        self.onClient = onClient
        
        var registered = false
        for id in Self.preferredRFCOMMChannels {
            let candidateChannel = BluetoothRFCOMMChannelID(id)
            notification = IOBluetoothRFCOMMChannel.register(
                forChannelOpenNotifications: self,
                selector: #selector(channelOpened(_:channel:)),
                withChannelID: candidateChannel,
                direction: kIOBluetoothUserNotificationChannelDirectionIncoming
            )
            if notification != nil {
                channelID = candidateChannel
                registered = true
                break
            }
        }
        
        guard registered else {
            throw BluetoothServerError.unavailable
        }
        
        serviceRecord = IOBluetoothSDPServiceRecord.publishedServiceRecord(with: serviceDictionary())
        
        if serviceRecord == nil {
            notification?.unregister()
            notification = nil
            throw BluetoothServerError.unavailable
        }

        NSLog("Bluetooth RFCOMM service published on channel \(channelID).")
    }

    public func stop() {
        notification?.unregister()
        notification = nil
        serviceRecord?.remove()
        serviceRecord = nil
        openChannels.forEach { $0.close() }
        openChannels.removeAll()
    }

    @objc private func channelOpened(_ notification: IOBluetoothUserNotification, channel: IOBluetoothRFCOMMChannel) {
        NSLog("Bluetooth RFCOMM channel opened from \(channel.getDevice()?.addressString ?? "unknown device") on channel \(channelID).")
        let connection = BluetoothChannelConnection(channel: channel) { [weak self] closed in
            self?.openChannels.removeAll { $0 === closed }
        }
        openChannels.append(connection)
        onClient?(connection, name)
    }

    private func serviceDictionary() -> [String: Any] {
        let serviceUUID = Self.uuid128(BlueTypeConstants.serviceUUID)
        let serialPort = IOBluetoothSDPUUID.uuid16(0x1101)!
        let l2cap = IOBluetoothSDPUUID.uuid16(0x0100)!
        let rfcomm = IOBluetoothSDPUUID.uuid16(0x0003)!

        return [
            "0001 - ServiceClassIDList": [serviceUUID, serialPort],
            "0004 - ProtocolDescriptorList": [
                [l2cap],
                [rfcomm, NSNumber(value: channelID)],
            ],
            "0005 - BrowseGroupList": [IOBluetoothSDPUUID.uuid16(0x1002)!],
            "0006 - LanguageBaseAttributeIDList": [
                NSNumber(value: 0x656E),
                NSNumber(value: 0x006A),
                NSNumber(value: 0x0100),
            ],
            "0100 - ServiceName": "BlueType",
            "LocalAttributes": [
                "Persistent": false,
            ],
        ]
    }

    private static func uuid128(_ string: String) -> IOBluetoothSDPUUID {
        let uuid = UUID(uuidString: string)!
        var bytes = uuid.uuid
        return withUnsafeBytes(of: &bytes) { pointer in
            IOBluetoothSDPUUID(bytes: pointer.baseAddress!, length: 16)
        }
    }

    private static let preferredRFCOMMChannels: [Int] = Array(23...30) + Array(4...22)
}

public enum BluetoothServerError: Error, LocalizedError {
    case unavailable

    public var errorDescription: String? {
        "Bluetooth RFCOMM service could not be published."
    }
}

public final class BluetoothChannelConnection: NSObject, ClientConnection, IOBluetoothRFCOMMChannelDelegate {
    private let channel: IOBluetoothRFCOMMChannel
    private let onClose: (BluetoothChannelConnection) -> Void
    private let lock = NSLock()
    private var buffer = Data()
    private var waiters: [PendingRead] = []
    private var closed = false

    public var remoteAddress: String {
        channel.getDevice()?.addressString ?? "bluetooth"
    }

    init(channel: IOBluetoothRFCOMMChannel, onClose: @escaping (BluetoothChannelConnection) -> Void) {
        self.channel = channel
        self.onClose = onClose
        super.init()
        channel.setDelegate(self)
    }

    public func readExactly(_ count: Int) async throws -> Data? {
        await withCheckedContinuation { continuation in
            lock.lock()
            if buffer.count >= count {
                let data = buffer.prefix(count)
                buffer.removeFirst(count)
                lock.unlock()
                continuation.resume(returning: Data(data))
                return
            }
            if closed {
                let data = buffer
                buffer.removeAll()
                lock.unlock()
                continuation.resume(returning: data.isEmpty ? nil : data)
                return
            }
            waiters.append(PendingRead(count: count, continuation: continuation))
            lock.unlock()
        }
    }

    public func write(_ data: Data) async throws {
        var offset = 0
        let mtu = max(1, Int(channel.getMTU()))
        while offset < data.count {
            let size = min(mtu, data.count - offset)
            let chunk = data.subdata(in: offset..<(offset + size))
            let result = chunk.withUnsafeBytes { pointer in
                channel.writeSync(UnsafeMutableRawPointer(mutating: pointer.baseAddress!), length: UInt16(size))
            }
            guard result == kIOReturnSuccess else {
                throw BluetoothChannelError.writeFailed(result)
            }
            offset += size
        }
    }

    public func close() {
        channel.close()
        markClosed()
    }

    public func rfcommChannelData(_ rfcommChannel: IOBluetoothRFCOMMChannel!, data dataPointer: UnsafeMutableRawPointer!, length dataLength: Int) {
        guard dataLength > 0 else { return }
        let data = Data(bytes: dataPointer, count: dataLength)
        lock.lock()
        buffer.append(data)
        let completed = collectCompletedReadsLocked()
        lock.unlock()
        completed.forEach { $0.continuation.resume(returning: $0.data) }
    }

    public func rfcommChannelClosed(_ rfcommChannel: IOBluetoothRFCOMMChannel!) {
        markClosed()
    }

    private func markClosed() {
        lock.lock()
        if closed {
            lock.unlock()
            return
        }
        closed = true
        let completed = collectCompletedReadsLocked()
        lock.unlock()
        completed.forEach { $0.continuation.resume(returning: $0.data) }
        onClose(self)
    }

    private func collectCompletedReadsLocked() -> [(continuation: CheckedContinuation<Data?, Never>, data: Data?)] {
        var completed: [(CheckedContinuation<Data?, Never>, Data?)] = []
        var remaining: [PendingRead] = []

        for waiter in waiters {
            if buffer.count >= waiter.count {
                let data = buffer.prefix(waiter.count)
                buffer.removeFirst(waiter.count)
                completed.append((waiter.continuation, Data(data)))
            } else if closed {
                let data = buffer
                buffer.removeAll()
                completed.append((waiter.continuation, data.isEmpty ? nil : data))
            } else {
                remaining.append(waiter)
            }
        }

        waiters = remaining
        return completed
    }
}

private struct PendingRead {
    let count: Int
    let continuation: CheckedContinuation<Data?, Never>
}

public enum BluetoothChannelError: Error, LocalizedError {
    case writeFailed(IOReturn)

    public var errorDescription: String? {
        switch self {
        case .writeFailed(let code):
            "Bluetooth RFCOMM write failed: \(code)"
        }
    }
}
