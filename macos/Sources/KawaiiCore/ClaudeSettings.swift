import Foundation

/// Adds/removes Kawaii Island's entries in ~/.claude/settings.json and touches nothing else (same as Windows).
/// Every entry we write contains `marker`, which is how removal finds exactly ours. Hooks are async `curl` calls to
/// the island's local listener; PermissionRequest waits for Allow / Deny on the island. Unit-tested.
public enum ClaudeSettings {
    public static let marker = "/kawaii/"
    public static let approvalSeconds = 120
    static let events = ["SessionStart", "SessionEnd", "UserPromptSubmit", "PreToolUse", "PostToolUse", "PostToolUseFailure",
                         "PermissionRequest", "Notification", "Stop"]

    public static var path: URL { FileManager.default.homeDirectoryForCurrentUser.appendingPathComponent(".claude/settings.json") }

    /// PermissionRequest hook output for "allow"/"deny"; anything else → "" so Claude Code asks as usual.
    public static func permissionReply(_ behavior: String?) -> String {
        guard behavior == "allow" || behavior == "deny" else { return "" }
        let decision: [String: Any] = behavior == "deny" ? ["behavior": "deny", "message": "Denied from Kawaii Island"] : ["behavior": "allow"]
        let obj: [String: Any] = ["hookSpecificOutput": ["hookEventName": "PermissionRequest", "decision": decision]]
        return (try? JSONSerialization.data(withJSONObject: obj)).flatMap { String(data: $0, encoding: .utf8) } ?? ""
    }

    /// Adds our hooks (idempotent); the status line only if the user has none. Returns whether ours went in.
    @discardableResult
    public static func install(_ settings: inout [String: Any], port: Int) -> Bool {
        uninstall(&settings)
        var hooks = settings["hooks"] as? [String: Any] ?? [:]
        for ev in events {
            var groups = hooks[ev] as? [Any] ?? []
            let ask = ev == "PermissionRequest"
            let handler: [String: Any] = [
                "type": "command", "command": "curl",
                "args": ["-s", "-m", ask ? "\(approvalSeconds + 5)" : "2", "-H", "Content-Type: application/json",
                         "--data-binary", "@-", "http://127.0.0.1:\(port)\(marker)\(ask ? "permission" : "hook")"],
                "async": !ask, "timeout": ask ? approvalSeconds + 10 : 5,
            ]
            groups.append(["hooks": [handler]])
            hooks[ev] = groups
        }
        settings["hooks"] = hooks
        guard settings["statusLine"] == nil else { return false } // never replace the user's own status line
        settings["statusLine"] = ["type": "command",
                                  "command": "curl -s -m 1 -H \"Content-Type: application/json\" --data-binary @- http://127.0.0.1:\(port)\(marker)status"]
        return true
    }

    /// Removes only our entries; drops groups/events/"hooks" that become empty.
    public static func uninstall(_ settings: inout [String: Any]) {
        if var hooks = settings["hooks"] as? [String: Any] {
            for (ev, node) in hooks {
                guard let groups = node as? [Any] else { continue }
                let kept: [Any] = groups.compactMap { g in
                    guard var group = g as? [String: Any], let handlers = group["hooks"] as? [Any] else { return g }
                    let mine = handlers.filter { !isOurs($0) }
                    if mine.isEmpty { return nil }
                    group["hooks"] = mine
                    return group
                }
                hooks[ev] = kept.isEmpty ? nil : kept
            }
            settings["hooks"] = hooks.isEmpty ? nil : hooks
        }
        if isOurs(settings["statusLine"] as Any) { settings["statusLine"] = nil }
    }

    public static func isInstalled(_ settings: [String: Any]) -> Bool { isOurs(settings["hooks"] as Any) }

    /// Reads, edits and atomically rewrites the real file (backup kept before installing).
    @discardableResult
    public static func apply(_ enable: Bool, port: Int, at url: URL = path) throws -> Bool {
        try FileManager.default.createDirectory(at: url.deletingLastPathComponent(), withIntermediateDirectories: true)
        var settings: [String: Any] = [:]
        if let data = try? Data(contentsOf: url) {
            guard let obj = try JSONSerialization.jsonObject(with: data) as? [String: Any] else {
                throw CocoaError(.fileReadCorruptFile) // not a JSON object: leave it alone
            }
            settings = obj
            if enable { try? data.write(to: url.appendingPathExtension("kawaii-backup")) }
        }
        var line = false
        if enable { line = install(&settings, port: port) } else { uninstall(&settings) }
        let out = try JSONSerialization.data(withJSONObject: settings, options: [.prettyPrinted, .sortedKeys])
        try out.write(to: url, options: .atomic)
        return line
    }

    static func isOurs(_ node: Any) -> Bool {
        guard JSONSerialization.isValidJSONObject(node), let d = try? JSONSerialization.data(withJSONObject: node),
              let s = String(data: d, encoding: .utf8) else { return (node as? String)?.contains(marker) == true }
        return s.replacingOccurrences(of: "\/", with: "/").contains(marker)
    }
}
