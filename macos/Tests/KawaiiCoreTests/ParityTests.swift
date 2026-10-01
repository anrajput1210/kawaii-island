import CoreGraphics
import XCTest
@testable import KawaiiCore

final class SVGTests: XCTestCase {
    func testShapesAndPaint() {
        let shapes = SVG.parse(##"<g fill="#FFFFFF" stroke="#F3B6C8"><ellipse cx="23" cy="16" rx="6.5" ry="14"/></g><rect x="5" y="29" width="6" height="13" rx="2.5" fill="#8C98B3"/><path d="M20 39 Q24 42 28 39" stroke="#2B2B4A" stroke-width="2" stroke-linecap="round" fill="none"/>"##)
        XCTAssertEqual(shapes.count, 3)
        XCTAssertEqual(shapes[0].fill, 0xFFFFFF)               // inherited from <g>
        XCTAssertEqual(shapes[0].stroke, 0xF3B6C8)
        XCTAssertEqual(shapes[0].path.boundingBox, CGRect(x: 16.5, y: 2, width: 13, height: 28))
        XCTAssertEqual(shapes[1].fill, 0x8C98B3)
        XCTAssertNil(shapes[2].fill)
        XCTAssertTrue(shapes[2].roundCaps)
        XCTAssertEqual(shapes[2].strokeWidth, 2)
    }

    func testRelativePathsAndClose() {
        let b = SVG.path("M10 10 h20 v10 h-20 Z").boundingBox
        XCTAssertEqual(b, CGRect(x: 10, y: 10, width: 20, height: 10))
    }

    func testArcEndsWhereItShould() {
        // The dizzy spiral: a relative arc from (24, 37.2) to (22.2, 39).
        let p = SVG.path("M24 37.2 a1.8 1.8 0 1 1 -1.8 1.8")
        XCTAssertEqual(p.currentPoint.x, 22.2, accuracy: 0.01)
        XCTAssertEqual(p.currentPoint.y, 39, accuracy: 0.01)
    }

    func testRotateTransform() {
        let s = SVG.parse(#"<rect x="0" y="0" width="10" height="2" transform="rotate(90 0 0)"/>"#)
        XCTAssertEqual(s[0].path.boundingBox.width, 2, accuracy: 0.001)
        XCTAssertEqual(s[0].fill, 0x000000)                    // SVG default fill
    }
}

final class CodeTrackerTests: XCTestCase {
    func testClaudeHookFlow() {
        let t = CodeTracker()
        t.onHook(["session_id": "a", "hook_event_name": "UserPromptSubmit", "cwd": "/Users/me/kawaii-island"])
        XCTAssertEqual(t.active?.state, .thinking)
        XCTAssertEqual(t.active?.project, "kawaii-island")
        t.onHook(["session_id": "a", "hook_event_name": "PreToolUse", "tool_name": "Bash", "tool_input": ["command": "npm test"]])
        XCTAssertEqual(t.active?.detail, "Bash · npm test")
        t.onHook(["session_id": "a", "hook_event_name": "PermissionRequest", "tool_name": "Edit", "tool_input": ["file_path": "/x/App.swift"]])
        XCTAssertEqual(t.active?.state, .needsYou)
        XCTAssertEqual(t.active?.detail, "Allow Edit · App.swift?")
        t.onHook(["session_id": "a", "hook_event_name": "SessionEnd"])
        XCTAssertNil(t.active)
    }

    func testAgentNeutralEvent() {
        let t = CodeTracker()
        t.onEvent(["agent": "Gemini CLI", "state": "working", "detail": "npm test", "cwd": "/code/app"])
        XCTAssertEqual(t.active?.agent, "Gemini CLI")
        XCTAssertEqual(t.active?.state, .thinking)
        t.onEvent(["agent": "Gemini CLI", "state": "done", "cwd": "/code/app"])
        XCTAssertEqual(t.sessions.count, 1)
        XCTAssertEqual(t.active?.state, .done)
        t.onEvent(["agent": "Gemini CLI", "state": "nonsense", "cwd": "/code/app"])
        XCTAssertEqual(t.active?.state, .done)                 // unknown states are ignored
    }

    func testCodexAndStatus() {
        let t = CodeTracker()
        t.onCodex(["type": "agent-turn-complete", "thread-id": "x", "last-assistant-message": "Fixed it"])
        XCTAssertEqual(t.active?.agent, "Codex")
        XCTAssertEqual(t.active?.detail, "Fixed it")
        t.onStatus(["session_id": "s", "context_window": ["used_percentage": 23], "rate_limits": ["five_hour": ["used_percentage": 41]]])
        XCTAssertEqual(t.sessions["s"]?.contextPct, 23)
        XCTAssertEqual(t.fiveHour, 41)
    }
}

final class TaskTests: XCTestCase {
    func testSplitNameAndTime() {
        XCTAssertEqual(Tasks.split("Gym 5:30 PM").name, "Gym")
        XCTAssertEqual(Tasks.split("Gym 5:30 PM").time, "5:30 PM")
        XCTAssertEqual(Tasks.split("Project sync 14:00").time, "14:00")
        XCTAssertEqual(Tasks.split("Read a book").time, "")
    }

    func testMonthProgress() {
        var c = Calendar(identifier: .gregorian); c.timeZone = TimeZone(identifier: "UTC")!
        let mid = c.date(from: DateComponents(year: 2026, month: 4, day: 16))!   // 15 of 30 days done
        XCTAssertEqual(Tasks.monthProgress(mid, calendar: c), 0.5, accuracy: 0.001)
    }

    func testConfigKeepsOldFilesWorking() throws {
        let c = try JSONDecoder().decode(Config.self, from: Data(#"{"music":false}"#.utf8))
        XCTAssertFalse(c.music)
        XCTAssertEqual(c.mascot, "kiko")
        XCTAssertEqual(c.codePort, 47811)
    }
}

final class ClaudeSettingsTests: XCTestCase {
    func testInstallKeepsUserHooksAndUninstallRemovesOnlyOurs() {
        var s: [String: Any] = ["hooks": ["Stop": [["hooks": [["type": "command", "command": "say done"]]]]], "model": "opus"]
        XCTAssertTrue(ClaudeSettings.install(&s, port: 47811))
        XCTAssertTrue(ClaudeSettings.isInstalled(s))
        XCTAssertEqual(((s["hooks"] as! [String: Any])["Stop"] as! [Any]).count, 2)
        ClaudeSettings.uninstall(&s)
        XCTAssertFalse(ClaudeSettings.isInstalled(s))
        XCTAssertNil(s["statusLine"])
        XCTAssertEqual(((s["hooks"] as! [String: Any])["Stop"] as! [Any]).count, 1)   // the user's own hook survives
        XCTAssertEqual(s["model"] as? String, "opus")
    }

    func testNeverReplacesTheUsersStatusLine() {
        var s: [String: Any] = ["statusLine": ["type": "command", "command": "my-line"]]
        XCTAssertFalse(ClaudeSettings.install(&s, port: 47811))
        XCTAssertEqual((s["statusLine"] as? [String: Any])?["command"] as? String, "my-line")
    }

    func testPermissionReply() {
        XCTAssertTrue(ClaudeSettings.permissionReply("allow").contains("\"allow\""))
        XCTAssertEqual(ClaudeSettings.permissionReply(nil), "")
    }
}
