import Foundation

public enum CodeState: Equatable { case idle, thinking, tool, needsYou, done }

public struct CodeSession: Equatable {
    public var id: String
    /// "Claude Code", "Codex", "Gemini CLI", "Cursor"… whatever the agent calls itself.
    public var agent = "Claude Code"
    public var project = ""
    public var state = CodeState.idle
    public var detail = ""
    public var model: String?
    public var contextPct: Double?
    public var updated = Date.distantPast

    public init(id: String) { self.id = id }
}

/// Turns agent activity into per-session state, exactly like Windows' CodeTracker: Claude Code hook events and
/// status line, Codex's notify payload, and the agent-neutral event any tool can POST. Pure: no IO. Unit-tested.
public final class CodeTracker {
    public private(set) var sessions: [String: CodeSession] = [:]
    public private(set) var fiveHour: Double?
    public private(set) var week: Double?

    public init() {}

    /// The session with the most recent activity.
    public var active: CodeSession? { sessions.values.max { $0.updated < $1.updated } }

    public func onHook(_ e: [String: Any], now: Date = Date()) {
        let id = str(e, "session_id"), evt = str(e, "hook_event_name")
        guard !id.isEmpty else { return }
        if evt == "SessionEnd" { sessions[id] = nil; return }
        var s = get(id, e)
        switch evt {
        case "SessionStart":
            set(&s, .idle, "")
            if !str(e, "model").isEmpty { s.model = str(e, "model") }
        case "UserPromptSubmit": set(&s, .thinking, "")
        case "PreToolUse": set(&s, .tool, Self.describe(e))
        case "PostToolUse", "PostToolUseFailure": set(&s, .thinking, "")
        case "PermissionRequest": set(&s, .needsYou, "Allow \(Self.describe(e))?")
        case "Notification": set(&s, .needsYou, str(e, "message"))
        case "Stop": set(&s, .done, "")
        default: return
        }
        s.updated = now
        sessions[id] = s
    }

    /// { "agent": "Gemini CLI", "session": "abc", "cwd": "/code/app", "state": "working|tool|needs_input|done|idle|end", "detail": "npm test" }
    public func onEvent(_ e: [String: Any], now: Date = Date()) {
        let agentRaw = Self.clip(str(e, "agent"), 24)
        let agent = agentRaw.isEmpty ? "Agent" : agentRaw
        let sid = str(e, "session")
        let id = sid.isEmpty ? "\(agent):\(str(e, "cwd"))\(str(e, "project"))" : sid
        let state = str(e, "state").lowercased()
        if ["end", "exit", "closed"].contains(state) { sessions[id] = nil; return }
        let mapped: CodeState?
        switch state {
        case "thinking", "working", "running", "start": mapped = .thinking
        case "tool": mapped = .tool
        case "needs_input", "needs_you", "waiting", "permission": mapped = .needsYou
        case "done", "finished", "complete", "stop": mapped = .done
        case "idle": mapped = .idle
        default: mapped = nil
        }
        guard let st = mapped else { return }
        var s = get(id, e)
        s.agent = agent
        if !str(e, "project").isEmpty { s.project = Self.clip(str(e, "project"), 40) }
        set(&s, st, Self.clip(str(e, "detail").replacingOccurrences(of: "\n", with: " "), 60))
        s.updated = now
        sessions[id] = s
    }

    /// Codex CLI `notify` payload: agent-turn-complete → Done.
    public func onCodex(_ e: [String: Any], now: Date = Date()) {
        guard str(e, "type") == "agent-turn-complete" else { return }
        let t = str(e, "thread-id")
        let id = "codex:" + (t.isEmpty ? str(e, "cwd") : t)
        var s = get(id, e)
        s.agent = "Codex"
        set(&s, .done, Self.clip(str(e, "last-assistant-message").replacingOccurrences(of: "\n", with: " "), 60))
        s.updated = now
        sessions[id] = s
    }

    /// Claude Code status line: model and context % per session, 5-hour / weekly plan usage.
    public func onStatus(_ e: [String: Any], now: Date = Date()) {
        let id = str(e, "session_id")
        if !id.isEmpty {
            var s = get(id, e)
            let model = str(e, "model", "display_name")
            if !model.isEmpty { s.model = model }
            s.contextPct = num(e, "context_window", "used_percentage") ?? s.contextPct
            if s.updated == .distantPast { s.updated = now }
            sessions[id] = s
        }
        fiveHour = num(e, "rate_limits", "five_hour", "used_percentage")
        week = num(e, "rate_limits", "seven_day", "used_percentage")
    }

    /// "Bash · npm test", "Edit · App.swift".
    public static func describe(_ e: [String: Any]) -> String {
        let tool = str(e, "tool_name")
        var arg: String
        switch tool {
        case "Bash", "PowerShell": arg = str(e, "tool_input", "command")
        case "Edit", "Write", "Read", "MultiEdit", "NotebookEdit": arg = (str(e, "tool_input", "file_path") as NSString).lastPathComponent
        case "Grep", "Glob": arg = str(e, "tool_input", "pattern")
        case "WebFetch": arg = URL(string: str(e, "tool_input", "url"))?.host ?? ""
        case "Task", "Agent": arg = str(e, "tool_input", "description")
        default: arg = ""
        }
        arg = arg.replacingOccurrences(of: "\n", with: " ").trimmingCharacters(in: .whitespaces)
        if arg.count > 40 { arg = String(arg.prefix(39)) + "…" }
        return arg.isEmpty ? tool : "\(tool) · \(arg)"
    }

    static func clip(_ t: String, _ max: Int) -> String {
        let s = t.trimmingCharacters(in: .whitespaces)
        return s.count > max ? String(s.prefix(max - 1)).trimmingCharacters(in: .whitespaces) + "…" : s
    }

    private func get(_ id: String, _ e: [String: Any]) -> CodeSession {
        var s = sessions[id] ?? CodeSession(id: id)
        let cwd = str(e, "cwd")
        if !cwd.isEmpty { s.project = (cwd as NSString).lastPathComponent }
        return s
    }

    private func set(_ s: inout CodeSession, _ state: CodeState, _ detail: String) { s.state = state; s.detail = detail }
}

private func find(_ e: [String: Any], _ path: [String]) -> Any? {
    var cur: Any? = e
    for k in path { cur = (cur as? [String: Any])?[k] }
    return cur
}
func str(_ e: [String: Any], _ path: String...) -> String { find(e, path) as? String ?? "" }
func num(_ e: [String: Any], _ path: String...) -> Double? { (find(e, path) as? NSNumber)?.doubleValue }

/// Calendar tab helpers (same rules as Windows). Unit-tested.
public enum Tasks {
    /// 0…1 through the current month.
    public static func monthProgress(_ now: Date, calendar: Calendar = .current) -> Double {
        let days = Double(calendar.range(of: .day, in: .month, for: now)?.count ?? 30)
        let start = calendar.dateInterval(of: .month, for: now)?.start ?? now
        return min(1, max(0, now.timeIntervalSince(start) / (days * 86400)))
    }

    /// "15:00", "3:30 PM", "5pm" → that time on `day`; nil when there is none.
    public static func time(_ text: String, on day: Date, calendar: Calendar = .current) -> Date? {
        let t = text.trimmingCharacters(in: .whitespaces).uppercased()
        guard !t.isEmpty else { return nil }
        let f = DateFormatter()
        f.locale = Locale(identifier: "en_US_POSIX")
        for format in ["h:mm a", "h:mma", "H:mm", "h a", "ha"] {
            f.dateFormat = format
            if let d = f.date(from: t) {
                let c = calendar.dateComponents([.hour, .minute], from: d)
                return calendar.date(bySettingHour: c.hour ?? 0, minute: c.minute ?? 0, second: 0, of: day)
            }
        }
        return nil
    }

    /// "Gym 5:30 PM" → ("Gym", "5:30 PM"); "Read" → ("Read", "").
    public static func split(_ text: String) -> (name: String, time: String) {
        let parts = text.trimmingCharacters(in: .whitespaces).split(separator: " ").map(String.init)
        var take = min(2, parts.count - 1)
        while take >= 1 {
            let time = parts.suffix(take).joined(separator: " ")
            if Self.time(time, on: Date()) != nil { return (parts.dropLast(take).joined(separator: " "), time) }
            take -= 1
        }
        return (parts.joined(separator: " "), "")
    }
}
