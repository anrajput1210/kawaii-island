import CoreGraphics
import Foundation

/// The camera housing the island grows out of. On notched MacBooks it's the real notch; elsewhere a virtual one.
public struct Notch: Equatable {
    public var centerX: CGFloat   // screen coordinates
    public var width: CGFloat
    public var height: CGFloat
    public var isReal: Bool

    public init(centerX: CGFloat, width: CGFloat, height: CGFloat, isReal: Bool) {
        self.centerX = centerX; self.width = width; self.height = height; self.isReal = isReal
    }

    /// From NSScreen: `frame`, the widths of `auxiliaryTopLeftArea` / `auxiliaryTopRightArea` (nil without a notch),
    /// `safeAreaInsets.top`, and the menu bar height.
    public static func on(screen: CGRect, leftWidth: CGFloat?, rightWidth: CGFloat?, safeTop: CGFloat, menuBar: CGFloat) -> Notch {
        if let l = leftWidth, let r = rightWidth, safeTop > 0 {
            let w = screen.width - l - r
            return Notch(centerX: screen.minX + l + w / 2, width: w, height: safeTop, isReal: true)
        }
        // No camera housing: an iPhone-proportioned resting island in the menu bar.
        return Notch(centerX: screen.midX, width: 150, height: max(menuBar, 24), isReal: false)
    }
}

/// Live activities in iPhone priority order (higher wins the compact island; the runner-up becomes the minimal bubble).
public enum ActivityKind: Int, Comparable, CaseIterable {
    case music = 1, timer = 2, code = 3, call = 4
    public static func < (a: Self, b: Self) -> Bool { a.rawValue < b.rawValue }
}

public enum IslandLayout {
    /// Width of the leading/trailing slots on either side of the camera in the compact island.
    public static let side: CGFloat = 58
    /// Wider slots for a transient alert (volume bar, "Charging 80%").
    public static let hudSide: CGFloat = 96

    public static func arrange(_ active: Set<ActivityKind>) -> (primary: ActivityKind?, minimal: ActivityKind?) {
        let sorted = active.sorted(by: >)
        return (sorted.first, sorted.dropFirst().first)
    }

    public enum State: Equatable { case resting, compact, hud, expanded(contentHeight: CGFloat) }

    public static func size(_ state: State, notch n: Notch) -> CGSize {
        switch state {
        case .resting: return CGSize(width: n.width, height: n.height)
        case .compact: return CGSize(width: n.width + 2 * side, height: n.height)
        case .hud: return CGSize(width: n.width + 2 * hudSide, height: n.height)
        case .expanded(let h): return CGSize(width: max(n.width + 260, 480), height: n.height + h)
        }
    }
}

public enum Format {
    /// 65 → "1:05", 3723 → "1:02:03".
    public static func clock(_ seconds: Double) -> String {
        let s = max(0, Int(seconds.rounded(.down)))
        return s >= 3600 ? String(format: "%d:%02d:%02d", s / 3600, s / 60 % 60, s % 60)
                         : String(format: "%d:%02d", s / 60, s % 60)
    }
}

public struct PinnedApp: Codable, Equatable, Hashable {
    public var name: String
    public var path: String   // /Applications/Foo.app
    public init(name: String, path: String) { self.name = name; self.path = path }
}

public struct TaskItem: Codable, Equatable {
    public var name: String
    public var time: String
    public var done = false
    public init(name: String, time: String, done: Bool = false) { self.name = name; self.time = time; self.done = done }
}

/// Settings, stored as JSON in ~/Library/Application Support/KawaiiIsland/config.json (local only).
public struct Config: Codable, Equatable {
    public var hoverToExpand = false
    public var volume = true
    public var charging = true
    public var bluetooth = true
    public var unlock = true
    public var capsLock = true
    public var calls = true
    public var music = true
    // Parity with Windows
    public var mascot = "kiko"              // a skin key from the mascot art, or "none"
    public var apps: [PinnedApp] = []       // app shortcuts on the Home view
    public var tasks: [TaskItem] = []       // Calendar tab
    public var city = ""                    // weather (Open-Meteo); empty = no weather
    public var fahrenheit = false
    public var codeMode = false             // coding mode: hoodie mascot, agent status, Allow / Deny
    public var codePort = 47811
    public var calendar = true
    public var system = true

    public init() {}

    public init(from d: Decoder) throws {
        let c = try d.container(keyedBy: CodingKeys.self)
        func get(_ k: CodingKeys, _ v: Bool) -> Bool { (try? c.decodeIfPresent(Bool.self, forKey: k)) ?? v }
        let def = Config()
        hoverToExpand = get(.hoverToExpand, def.hoverToExpand); volume = get(.volume, def.volume)
        charging = get(.charging, def.charging); bluetooth = get(.bluetooth, def.bluetooth)
        unlock = get(.unlock, def.unlock); capsLock = get(.capsLock, def.capsLock)
        calls = get(.calls, def.calls); music = get(.music, def.music)
        mascot = (try? c.decodeIfPresent(String.self, forKey: .mascot)) ?? def.mascot
        apps = (try? c.decodeIfPresent([PinnedApp].self, forKey: .apps)) ?? []
        tasks = (try? c.decodeIfPresent([TaskItem].self, forKey: .tasks)) ?? []
        city = (try? c.decodeIfPresent(String.self, forKey: .city)) ?? ""
        fahrenheit = get(.fahrenheit, def.fahrenheit); codeMode = get(.codeMode, def.codeMode)
        codePort = (try? c.decodeIfPresent(Int.self, forKey: .codePort)) ?? def.codePort
        calendar = get(.calendar, def.calendar); system = get(.system, def.system)
    }

    public static func load(from dir: URL) -> Config {
        guard let data = try? Data(contentsOf: dir.appendingPathComponent("config.json")),
              let c = try? JSONDecoder().decode(Config.self, from: data) else { return Config() }
        return c
    }

    public func save(to dir: URL) {
        try? FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        let e = JSONEncoder(); e.outputFormatting = [.prettyPrinted, .sortedKeys]
        try? e.encode(self).write(to: dir.appendingPathComponent("config.json"), options: .atomic)
    }
}
