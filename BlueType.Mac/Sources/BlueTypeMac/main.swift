import AppKit
import BlueTypeMacCore

final class AppDelegate: NSObject, NSApplicationDelegate, AppStateSink {
    private var statusItem: NSStatusItem!
    private var statusWindow: NSWindow!
    private var statusLabel: NSTextField!
    private var trustedDevicesWindowController: TrustedDevicesWindowController?
    private var agent: MacAgent!
    private var currentState: ConnectionState = .idle
    private var lastMessage: String = "Starting..."
    private let logFileURL: URL = {
        let base = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask).first!
            .appendingPathComponent("BlueType/logs", isDirectory: true)
        try? FileManager.default.createDirectory(at: base, withIntermediateDirectories: true)
        return base.appendingPathComponent("mac-agent.log")
    }()

    func applicationDidFinishLaunching(_ notification: Notification) {
        NSApp.setActivationPolicy(.regular)
        statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.variableLength)
        configureStatusButton(for: currentState)
        createStatusWindow()
        rebuildMenu()

        agent = MacAgent(stateSink: self) { request in
            await Self.promptForAuthorization(request)
        }
        agent.start()
        NSApp.activate(ignoringOtherApps: true)
    }

    func applicationWillTerminate(_ notification: Notification) {
        agent?.stop()
    }

    func updateState(_ state: ConnectionState) {
        DispatchQueue.main.async {
            self.currentState = state
            self.configureStatusButton(for: state)
            self.refreshStatusWindow()
            self.rebuildMenu()
        }
    }

    func postMessage(_ message: String) {
        DispatchQueue.main.async {
            self.lastMessage = message
            self.appendLog(message)
            self.refreshStatusWindow()
            self.rebuildMenu()
        }
    }

    private func appendLog(_ message: String) {
        let line = "\(ISO8601DateFormatter().string(from: Date())) \(message)\n"
        guard let data = line.data(using: .utf8) else { return }
        if FileManager.default.fileExists(atPath: logFileURL.path) {
            if let handle = try? FileHandle(forWritingTo: logFileURL) {
                try? handle.seekToEnd()
                try? handle.write(contentsOf: data)
                try? handle.close()
            }
        } else {
            try? data.write(to: logFileURL)
        }
    }

    private func createStatusWindow() {
        statusWindow = NSWindow(
            contentRect: NSRect(x: 0, y: 0, width: 420, height: 180),
            styleMask: [.titled, .closable, .miniaturizable],
            backing: .buffered,
            defer: false
        )
        statusWindow.title = "BlueType Mac Agent"
        statusWindow.center()
        statusWindow.isReleasedWhenClosed = false

        statusLabel = NSTextField(labelWithString: "")
        statusLabel.font = .monospacedSystemFont(ofSize: 13, weight: .regular)
        statusLabel.lineBreakMode = .byWordWrapping
        statusLabel.maximumNumberOfLines = 0
        statusLabel.translatesAutoresizingMaskIntoConstraints = false

        let content = NSView()
        content.addSubview(statusLabel)
        statusWindow.contentView = content
        NSLayoutConstraint.activate([
            statusLabel.leadingAnchor.constraint(equalTo: content.leadingAnchor, constant: 18),
            statusLabel.trailingAnchor.constraint(equalTo: content.trailingAnchor, constant: -18),
            statusLabel.topAnchor.constraint(equalTo: content.topAnchor, constant: 18),
            statusLabel.bottomAnchor.constraint(lessThanOrEqualTo: content.bottomAnchor, constant: -18),
        ])
        refreshStatusWindow()
        statusWindow.makeKeyAndOrderFront(nil)
    }

    private func refreshStatusWindow() {
        let addresses = NetworkInfo.localIPv4Addresses()
        let wifi = addresses.isEmpty ? "No LAN IPv4 address" : addresses.map { "\($0):\(BlueTypeConstants.tcpPort)" }.joined(separator: "\n")
        statusLabel?.stringValue = """
        BlueType Mac Agent is running.

        State:
        \(stateDescription(currentState))

        Wi-Fi:
        \(wifi)

        Last message:
        \(lastMessage)
        """
    }

    private func rebuildMenu() {
        let menu = NSMenu()
        menu.addItem(disabled("State: \(stateDescription(currentState))"))
        menu.addItem(disabled("Message: \(lastMessage)"))

        let addresses = NetworkInfo.localIPv4Addresses()
        menu.addItem(disabled("Wi-Fi: \(addresses.isEmpty ? "No LAN IPv4 address" : addresses.joined(separator: ", ")):\(BlueTypeConstants.tcpPort)"))
        menu.addItem(NSMenuItem.separator())

        let disconnectItem = NSMenuItem(title: "Disconnect Current Device", action: #selector(disconnectCurrentDevice), keyEquivalent: "")
        disconnectItem.target = self
        disconnectItem.isEnabled = agent?.activeSessions.currentSnapshot() != nil
        menu.addItem(disconnectItem)

        let trustedItem = NSMenuItem(title: "Trusted Devices...", action: #selector(showTrustedDevices), keyEquivalent: "")
        trustedItem.target = self
        menu.addItem(trustedItem)

        let windowItem = NSMenuItem(title: "Show Status Window", action: #selector(showStatusWindow), keyEquivalent: "")
        windowItem.target = self
        menu.addItem(windowItem)

        let permissionItem = NSMenuItem(title: "Open Accessibility Settings", action: #selector(openAccessibilitySettings), keyEquivalent: "")
        permissionItem.target = self
        menu.addItem(permissionItem)

        menu.addItem(NSMenuItem.separator())
        let quitItem = NSMenuItem(title: "Quit BlueType", action: #selector(quit), keyEquivalent: "q")
        quitItem.target = self
        menu.addItem(quitItem)
        statusItem.menu = menu
    }

    private func configureStatusButton(for state: ConnectionState) {
        guard let button = statusItem.button else { return }
        statusItem.length = NSStatusItem.squareLength
        button.title = ""
        button.toolTip = "BlueType Mac Agent - \(stateDescription(state))"
        button.image = statusIcon(for: state)
        button.imagePosition = .imageOnly
    }

    private func statusIcon(for state: ConnectionState) -> NSImage? {
        guard let baseImage = NSImage(named: "AppIcon") else { return nil }
        let size = NSSize(width: 18, height: 18)
        let rect = NSRect(origin: .zero, size: size)
        let tintedImage = NSImage(size: size)
        tintedImage.lockFocus()
        statusColor(for: state).setFill()
        rect.fill()
        baseImage.draw(in: rect, from: .zero, operation: .destinationIn, fraction: 1)
        tintedImage.unlockFocus()
        tintedImage.isTemplate = false
        return tintedImage
    }

    @objc private func disconnectCurrentDevice() {
        if agent.disconnectActive() {
            postMessage("Disconnect requested.")
        }
    }

    @objc private func showTrustedDevices() {
        if trustedDevicesWindowController == nil {
            trustedDevicesWindowController = TrustedDevicesWindowController(registry: agent.registry) { [weak self] message in
                self?.postMessage(message)
            }
        }
        trustedDevicesWindowController?.showWindow(nil)
        trustedDevicesWindowController?.refresh()
        NSApp.activate(ignoringOtherApps: true)
    }

    @objc private func showStatusWindow() {
        statusWindow.makeKeyAndOrderFront(nil)
        NSApp.activate(ignoringOtherApps: true)
    }

    @objc private func openAccessibilitySettings() {
        if let url = URL(string: "x-apple.systempreferences:com.apple.preference.security?Privacy_Accessibility") {
            NSWorkspace.shared.open(url)
        }
        _ = InputInjector.isTrusted(prompt: true)
    }

    @objc private func quit() {
        NSApp.terminate(nil)
    }

    private func disabled(_ title: String) -> NSMenuItem {
        let item = NSMenuItem(title: title, action: nil, keyEquivalent: "")
        item.isEnabled = false
        return item
    }

    private func statusColor(for state: ConnectionState) -> NSColor {
        switch state {
        case .error:
            return .systemRed
        case .connected:
            return .systemGreen
        case .clientConnected, .authenticating, .pendingApproval:
            return .systemYellow
        case .listening:
            return .systemBlue
        case .idle:
            return .systemGray
        }
    }

    private func stateDescription(_ state: ConnectionState) -> String {
        switch state {
        case .idle:
            return "Idle"
        case .listening(let tcp, let bluetooth):
            return "Listening TCP=\(tcp ? "on" : "off") Bluetooth=\(bluetooth ? "on" : "off")"
        case .clientConnected(let transport, let remoteAddress):
            return "Connected from \(transport) \(remoteAddress)"
        case .authenticating(let deviceName):
            return "Authenticating \(deviceName)"
        case .pendingApproval(let deviceName):
            return "Awaiting approval for \(deviceName)"
        case .connected(let deviceName, let transport, _):
            return "\(deviceName) via \(transport)"
        case .error(let message):
            return "Error: \(message)"
        }
    }

    @MainActor
    private static func promptForAuthorization(_ request: AuthPromptRequest) async -> AuthPromptDecision {
        let alert = NSAlert()
        switch request.mode {
        case .authorizeDevice:
            alert.messageText = "Allow \(request.hello.deviceName) to control this Mac?"
            alert.informativeText = "Transport: \(request.transport)\nAddress: \(request.remoteAddress)"
            alert.addButton(withTitle: "Always Allow")
            alert.addButton(withTitle: "Allow Once")
            alert.addButton(withTitle: "Deny")
        case .switchActiveDevice(let activeDeviceName):
            alert.messageText = "Switch control to \(request.hello.deviceName)?"
            alert.informativeText = "\(activeDeviceName) is currently controlling this Mac."
            alert.addButton(withTitle: "Switch")
            alert.addButton(withTitle: "Keep Current")
        }

        NSApp.setActivationPolicy(.regular)
        NSApp.activate(ignoringOtherApps: true)
        alert.window.level = .floating
        alert.window.center()
        alert.window.orderFrontRegardless()
        let response = alert.runModal()
        switch request.mode {
        case .authorizeDevice:
            if response == .alertFirstButtonReturn { return .alwaysAllow }
            if response == .alertSecondButtonReturn { return .allowOnce }
            return .deny
        case .switchActiveDevice:
            return response == .alertFirstButtonReturn ? .allowOnce : .deny
        }
    }
}

private final class TrustedDevicesWindowController: NSWindowController, NSTableViewDataSource, NSTableViewDelegate {
    private let registry: DeviceRegistry
    private let messageSink: (String) -> Void
    private var devices: [TrustedDevice] = []
    private let tableView = NSTableView()
    private let emptyLabel = NSTextField(labelWithString: "No trusted devices yet.")
    private let removeButton = NSButton(title: "Remove", target: nil, action: nil)
    private let dateFormatter: DateFormatter = {
        let formatter = DateFormatter()
        formatter.dateStyle = .medium
        formatter.timeStyle = .short
        return formatter
    }()

    init(registry: DeviceRegistry, messageSink: @escaping (String) -> Void) {
        self.registry = registry
        self.messageSink = messageSink
        let window = NSWindow(
            contentRect: NSRect(x: 0, y: 0, width: 680, height: 360),
            styleMask: [.titled, .closable, .miniaturizable, .resizable],
            backing: .buffered,
            defer: false
        )
        window.title = "Trusted Devices"
        window.isReleasedWhenClosed = false
        super.init(window: window)
        buildContent()
        refresh()
    }

    required init?(coder: NSCoder) {
        nil
    }

    func refresh() {
        devices = registry.allDevices()
        tableView.reloadData()
        emptyLabel.isHidden = !devices.isEmpty
        removeButton.isEnabled = tableView.selectedRow >= 0 && tableView.selectedRow < devices.count
    }

    func numberOfRows(in tableView: NSTableView) -> Int {
        devices.count
    }

    func tableViewSelectionDidChange(_ notification: Notification) {
        removeButton.isEnabled = tableView.selectedRow >= 0 && tableView.selectedRow < devices.count
    }

    func tableView(_ tableView: NSTableView, viewFor tableColumn: NSTableColumn?, row: Int) -> NSView? {
        guard row < devices.count, let identifier = tableColumn?.identifier else { return nil }
        let value = value(for: devices[row], column: identifier.rawValue)
        let cell = tableView.makeView(withIdentifier: identifier, owner: self) as? NSTableCellView ?? NSTableCellView()
        cell.identifier = identifier

        let textField: NSTextField
        if let existing = cell.textField {
            textField = existing
        } else {
            textField = NSTextField(labelWithString: "")
            textField.lineBreakMode = .byTruncatingMiddle
            textField.translatesAutoresizingMaskIntoConstraints = false
            cell.addSubview(textField)
            cell.textField = textField
            NSLayoutConstraint.activate([
                textField.leadingAnchor.constraint(equalTo: cell.leadingAnchor, constant: 6),
                textField.trailingAnchor.constraint(equalTo: cell.trailingAnchor, constant: -6),
                textField.centerYAnchor.constraint(equalTo: cell.centerYAnchor),
            ])
        }
        textField.stringValue = value
        return cell
    }

    @objc private func removeSelectedDevice() {
        let row = tableView.selectedRow
        guard row >= 0 && row < devices.count else { return }
        let device = devices[row]

        let alert = NSAlert()
        alert.messageText = "Remove \(device.deviceName)?"
        alert.informativeText = "This device will need approval the next time it connects."
        alert.addButton(withTitle: "Remove")
        alert.addButton(withTitle: "Cancel")
        alert.alertStyle = .warning
        guard alert.runModal() == .alertFirstButtonReturn else { return }

        do {
            try registry.remove(deviceId: device.deviceId)
            messageSink("Removed trusted device: \(device.deviceName)")
            refresh()
        } catch {
            let errorAlert = NSAlert(error: error)
            errorAlert.messageText = "Could not remove trusted device"
            errorAlert.runModal()
        }
    }

    private func buildContent() {
        guard let window else { return }

        let content = NSView()
        window.contentView = content

        let explanation = NSTextField(labelWithString: "Trusted devices can reconnect with their saved token without asking for approval each time.")
        explanation.lineBreakMode = .byWordWrapping
        explanation.maximumNumberOfLines = 2
        explanation.translatesAutoresizingMaskIntoConstraints = false

        let scrollView = NSScrollView()
        scrollView.hasVerticalScroller = true
        scrollView.borderType = .bezelBorder
        scrollView.translatesAutoresizingMaskIntoConstraints = false

        tableView.headerView = NSTableHeaderView()
        tableView.usesAlternatingRowBackgroundColors = true
        tableView.allowsMultipleSelection = false
        tableView.dataSource = self
        tableView.delegate = self
        tableView.addTableColumn(column("deviceName", "Device", 180))
        tableView.addTableColumn(column("deviceId", "Device ID", 210))
        tableView.addTableColumn(column("lastTransport", "Transport", 90))
        tableView.addTableColumn(column("lastSeenAt", "Last Seen", 140))
        scrollView.documentView = tableView

        emptyLabel.alignment = .center
        emptyLabel.textColor = .secondaryLabelColor
        emptyLabel.translatesAutoresizingMaskIntoConstraints = false

        removeButton.target = self
        removeButton.action = #selector(removeSelectedDevice)
        removeButton.isEnabled = false
        removeButton.translatesAutoresizingMaskIntoConstraints = false

        let closeButton = NSButton(title: "Close", target: window, action: #selector(NSWindow.close))
        closeButton.keyEquivalent = "\u{1b}"
        closeButton.translatesAutoresizingMaskIntoConstraints = false

        content.addSubview(explanation)
        content.addSubview(scrollView)
        content.addSubview(emptyLabel)
        content.addSubview(removeButton)
        content.addSubview(closeButton)

        NSLayoutConstraint.activate([
            explanation.leadingAnchor.constraint(equalTo: content.leadingAnchor, constant: 18),
            explanation.trailingAnchor.constraint(equalTo: content.trailingAnchor, constant: -18),
            explanation.topAnchor.constraint(equalTo: content.topAnchor, constant: 18),

            scrollView.leadingAnchor.constraint(equalTo: content.leadingAnchor, constant: 18),
            scrollView.trailingAnchor.constraint(equalTo: content.trailingAnchor, constant: -18),
            scrollView.topAnchor.constraint(equalTo: explanation.bottomAnchor, constant: 14),
            scrollView.bottomAnchor.constraint(equalTo: removeButton.topAnchor, constant: -14),

            emptyLabel.centerXAnchor.constraint(equalTo: scrollView.centerXAnchor),
            emptyLabel.centerYAnchor.constraint(equalTo: scrollView.centerYAnchor),

            removeButton.leadingAnchor.constraint(equalTo: content.leadingAnchor, constant: 18),
            removeButton.bottomAnchor.constraint(equalTo: content.bottomAnchor, constant: -18),

            closeButton.trailingAnchor.constraint(equalTo: content.trailingAnchor, constant: -18),
            closeButton.bottomAnchor.constraint(equalTo: content.bottomAnchor, constant: -18),
        ])
    }

    private func column(_ id: String, _ title: String, _ width: CGFloat) -> NSTableColumn {
        let column = NSTableColumn(identifier: NSUserInterfaceItemIdentifier(id))
        column.title = title
        column.width = width
        return column
    }

    private func value(for device: TrustedDevice, column: String) -> String {
        switch column {
        case "deviceName":
            return device.deviceName
        case "deviceId":
            return device.deviceId
        case "lastTransport":
            return device.lastTransport
        case "lastSeenAt":
            return dateFormatter.string(from: device.lastSeenAt)
        default:
            return ""
        }
    }
}

let app = NSApplication.shared
let delegate = AppDelegate()
app.delegate = delegate
app.run()
