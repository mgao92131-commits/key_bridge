import AppKit
import Foundation

public final class ShortcutProfileDispatcher {
    private let lock = NSLock()
    private let profiles: [ShortcutProfileDefinition]
    private var task: Task<Void, Never>?
    private var activeSession: ActiveShortcutSession?
    private var observedBundleId: String?
    private var observedSince = Date()
    private var lastSentProfileKey: String?

    public init() {
        profiles = ShortcutProfileStore.load()
        task = Task { [weak self] in
            await self?.pollLoop()
        }
    }

    deinit {
        stop()
    }

    public func registerSession(sessionId: UUID, connection: ClientConnection) {
        lock.withLock {
            activeSession = ActiveShortcutSession(sessionId: sessionId, connection: connection)
            lastSentProfileKey = nil
        }
        Task { await sendCurrent() }
    }

    public func unregisterSession(sessionId: UUID) {
        lock.withLock {
            if activeSession?.sessionId == sessionId {
                activeSession = nil
                lastSentProfileKey = nil
            }
        }
    }

    public func stop() {
        task?.cancel()
        task = nil
    }

    private func pollLoop() async {
        while !Task.isCancelled {
            try? await Task.sleep(nanoseconds: 300_000_000)
            if Task.isCancelled { break }

            let bundleId = await ForegroundApplicationReader.currentBundleId()
            let now = Date()
            var stable = false
            lock.withLock {
                if observedBundleId != bundleId {
                    observedBundleId = bundleId
                    observedSince = now
                } else if now.timeIntervalSince(observedSince) >= 0.5 {
                    stable = true
                }
            }

            if stable {
                await sendCurrent()
            }
        }
    }

    private func sendCurrent() async {
        let bundleId = await ForegroundApplicationReader.currentBundleId()
        let profile = match(bundleId: bundleId)
        let profileKey = profile?.id ?? ""

        let session: ActiveShortcutSession?
        let shouldSend: Bool
        (session, shouldSend) = lock.withLock {
            let session = activeSession
            let shouldSend = session != nil && lastSentProfileKey != profileKey
            if shouldSend {
                lastSentProfileKey = profileKey
            }
            return (session, shouldSend)
        }

        guard shouldSend, let session else { return }

        let envelope = Envelope(
            id: UUID().uuidString,
            type: MessageType.shortcutProfile,
            payload: [
                "name": profile.map { .string($0.name) } ?? .null,
                "profile": profile?.profile ?? .null
            ]
        )

        do {
            try await FrameCodec.write(envelope, to: session.connection)
        } catch {
            NSLog("Failed to send shortcut profile: \(error.localizedDescription)")
        }
    }

    private func match(bundleId: String?) -> ShortcutProfileDefinition? {
        if let bundleId, !bundleId.isEmpty {
            let appProfile = profiles.first { profile in
                profile.match.macBundleIds.contains { $0.caseInsensitiveCompare(bundleId) == .orderedSame }
            }
            if let appProfile {
                return appProfile
            }
        }
        return profiles.first { $0.id.caseInsensitiveCompare("default") == .orderedSame }
    }
}

private struct ActiveShortcutSession {
    let sessionId: UUID
    let connection: ClientConnection
}

private struct ShortcutProfileDefinition {
    let id: String
    let name: String
    let match: ShortcutProfileMatch
    let profile: JSONValue
}

private struct ShortcutProfileMatch {
    let windowsProcesses: [String]
    let macBundleIds: [String]
}

private enum ShortcutProfileStore {
    static func load() -> [ShortcutProfileDefinition] {
        guard let base = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask).first else {
            return []
        }
        let url = base.appendingPathComponent("BlueType/shortcut-profiles.json")
        if !FileManager.default.fileExists(atPath: url.path) {
            do {
                try seedDefaultFile(at: url)
            } catch {
                NSLog("Failed to create default shortcut profile file at \(url.path): \(error.localizedDescription)")
                return []
            }
        }

        do {
            let data = try Data(contentsOf: url)
            guard
                let root = try JSONSerialization.jsonObject(with: data) as? [String: Any],
                let entries = root["profiles"] as? [[String: Any]]
            else {
                return []
            }

            var profiles: [ShortcutProfileDefinition] = entries.compactMap { entry in
                guard
                    let id = entry["id"] as? String,
                    !id.isEmpty,
                    let profileObject = entry["profile"] as? [String: Any]
                else {
                    return nil
                }

                let matchObject = entry["match"] as? [String: Any] ?? [:]
                let match = ShortcutProfileMatch(
                    windowsProcesses: matchObject["windowsProcesses"] as? [String] ?? [],
                    macBundleIds: matchObject["macBundleIds"] as? [String] ?? []
                )

                return ShortcutProfileDefinition(
                    id: id,
                    name: entry["name"] as? String ?? id,
                    match: match,
                    profile: JSONValue.fromJSON(profileObject)
                )
            }
            if !profiles.contains(where: { $0.id.caseInsensitiveCompare("default") == .orderedSame }) {
                profiles.append(
                    ShortcutProfileDefinition(
                        id: "default",
                        name: "Default",
                        match: ShortcutProfileMatch(windowsProcesses: [], macBundleIds: []),
                        profile: JSONValue.fromJSON(defaultProfile())
                    )
                )
            }
            return profiles
        } catch {
            NSLog("Failed to load shortcut profiles from \(url.path): \(error.localizedDescription)")
            return []
        }
    }

    private static func seedDefaultFile(at url: URL) throws {
        let directory = url.deletingLastPathComponent()
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)

        let root: [String: Any] = [
            "profiles": [
                [
                    "id": "default",
                    "name": "Default",
                    "match": [
                        "windowsProcesses": [],
                        "macBundleIds": []
                    ],
                    "profile": defaultProfile()
                ],
                [
                    "id": "terminal",
                    "name": "Terminal",
                    "match": [
                        "windowsProcesses": [],
                        "macBundleIds": ["com.apple.Terminal"]
                    ],
                    "profile": terminalProfile()
                ]
            ]
        ]

        let data = try JSONSerialization.data(withJSONObject: root, options: [.prettyPrinted, .sortedKeys])
        try data.write(to: url, options: .atomic)
        NSLog("Created default shortcut profile file at \(url.path)")
    }

    private static func defaultProfile() -> [String: Any] {
        [
            "leftRail": rail(
                primaryAction: combo(["SHIFT", "TAB"]),
                secondaryAction: keyTap("TAB"),
                stickyModifiers: ["ALT"]
            ),
            "rightRail": rail(
                primaryAction: combo(["SHIFT", "TAB"]),
                secondaryAction: keyTap("TAB"),
                stickyModifiers: ["CTRL"]
            ),
            "bottomRail": rail(
                primaryAction: keyTap("LEFT"),
                secondaryAction: keyTap("RIGHT"),
                stickyModifiers: ["WIN", "CTRL"]
            ),
            "customButtons": [
                button(id: "copy", label: "COPY", action: combo(["CMD", "C"])),
                button(id: "paste", label: "PASTE", action: combo(["CMD", "V"])),
                button(id: "cut", label: "CUT", action: combo(["CMD", "X"])),
                button(id: "undo", label: "UNDO", action: combo(["CMD", "Z"])),
                button(id: "redo", label: "REDO", action: combo(["CMD", "SHIFT", "Z"])),
                button(id: "all", label: "ALL", action: combo(["CMD", "A"])),
                button(id: "save", label: "SAVE", action: combo(["CMD", "S"])),
                button(id: "find", label: "FIND", action: combo(["CMD", "F"]))
            ]
        ]
    }

    private static func terminalProfile() -> [String: Any] {
        [
            "leftRail": rail(
                primaryAction: combo(["SHIFT", "TAB"]),
                secondaryAction: keyTap("TAB"),
                stickyModifiers: ["ALT"]
            ),
            "rightRail": rail(
                primaryAction: combo(["SHIFT", "TAB"]),
                secondaryAction: keyTap("TAB"),
                stickyModifiers: ["CTRL"]
            ),
            "bottomRail": rail(
                primaryAction: keyTap("LEFT"),
                secondaryAction: keyTap("RIGHT"),
                stickyModifiers: ["WIN", "CTRL"]
            ),
            "customButtons": [
                button(id: "copy", label: "COPY", action: combo(["CMD", "C"])),
                button(id: "paste", label: "PASTE", action: combo(["CMD", "V"])),
                button(id: "new_tab", label: "NEW TAB", action: combo(["CMD", "T"])),
                button(id: "prev_tab", label: "PREV TAB", action: combo(["CMD", "SHIFT", "["])),
                button(id: "next_tab", label: "NEXT TAB", action: combo(["CMD", "SHIFT", "]"])),
                button(id: "interrupt", label: "INT", action: combo(["CTRL", "C"])),
                button(id: "clear", label: "CLEAR", action: combo(["CMD", "K"])),
                button(id: "find", label: "FIND", action: combo(["CMD", "F"]))
            ]
        ]
    }

    private static func rail(
        primaryAction: [String: Any],
        secondaryAction: [String: Any],
        stickyModifiers: [String]
    ) -> [String: Any] {
        [
            "primaryAction": primaryAction,
            "secondaryAction": secondaryAction,
            "stickyModifiers": stickyModifiers,
            "stickyDurationMs": 600
        ]
    }

    private static func button(id: String, label: String, action: [String: Any]) -> [String: Any] {
        [
            "id": id,
            "label": label,
            "action": action
        ]
    }

    private static func keyTap(_ key: String) -> [String: Any] {
        [
            "kind": "key_tap",
            "key": key
        ]
    }

    private static func combo(_ keys: [String]) -> [String: Any] {
        [
            "kind": "combo",
            "keys": keys
        ]
    }
}

private enum ForegroundApplicationReader {
    @MainActor
    static func currentBundleId() -> String? {
        NSWorkspace.shared.frontmostApplication?.bundleIdentifier
    }
}

private extension JSONValue {
    static func fromJSON(_ value: Any) -> JSONValue {
        switch value {
        case let value as String:
            return .string(value)
        case let value as Bool:
            return .bool(value)
        case let value as Int:
            return .number(Double(value))
        case let value as Double:
            return .number(value)
        case let value as NSNumber:
            return .number(value.doubleValue)
        case let value as [String: Any]:
            return .object(value.mapValues { JSONValue.fromJSON($0) })
        case let value as [Any]:
            return .array(value.map { JSONValue.fromJSON($0) })
        default:
            return .null
        }
    }
}

private extension NSLock {
    func withLock<T>(_ body: () throws -> T) rethrows -> T {
        lock()
        defer { unlock() }
        return try body()
    }
}
