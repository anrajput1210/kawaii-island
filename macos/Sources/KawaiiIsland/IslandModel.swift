import AppKit
import KawaiiCore
import SwiftUI

struct Hud: Equatable {
    var symbol: String
    var tint: Color
    var title: String
    var value: String = ""
    var level: Double? = nil
}

struct CallState: Equatable {
    var name: String
    var since: Date
    var video: Bool
    var muted = false
    var app: NSRunningApplication
}

struct TimerState: Equatable {
    var end: Date
    var pausedRemaining: Double?
    var remaining: Double { pausedRemaining ?? end.timeIntervalSinceNow }
}

/// Tabs of the open island (same set as Windows; Home = clock, widgets and your apps).
enum IslandTab: String, CaseIterable { case home = "Home", music = "Music", code = "Code", timer = "Timer", calendar = "Calendar", system = "System" }

/// Everything the island shows. Sources call in on the main thread; the view redraws from the published state.
final class IslandModel: ObservableObject {
    static let dir = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
        .appendingPathComponent("KawaiiIsland")

    @Published var notch = Notch(centerX: 0, width: 185, height: 32, isReal: false)
    @Published private(set) var expanded = false
    @Published var hovering = false
    @Published private(set) var hud: Hud?
    @Published private(set) var music: NowPlaying?
    @Published private(set) var musicTint = Color.white
    @Published private(set) var call: CallState?
    @Published private(set) var timer: TimerState?
    @Published private(set) var battery = Power.read()
    @Published private(set) var now = Date()
    @Published var config = Config.load(from: IslandModel.dir) { didSet { config.save(to: Self.dir) } }
    // Parity with Windows
    @Published var picked: IslandTab?
    @Published private(set) var mood: String?            // annoyed, dizzy, wow, happy…
    @Published private(set) var blinking = false
    @Published private(set) var sleepy = false
    @Published private(set) var squish = false
    @Published private(set) var hop = false
    @Published private(set) var code: CodeSession?
    @Published private(set) var fiveHour: Double?
    @Published private(set) var week: Double?
    @Published private(set) var approval: String?        // the question waiting for Allow / Deny
    @Published private(set) var lockedInSince: Date?
    @Published var picking = false
    @Published var search = ""
    @Published private(set) var catalog: [PinnedApp] = []
    @Published private(set) var weather: Weather?
    @Published private(set) var stats = SystemStats.Snapshot()
    private let tracker = CodeTracker()
    private var server: CodeServer?
    private var answer: ((String?) -> Void)?
    private let systemStats = SystemStats()
    private var pokes: [Date] = []
    private var moodWork: DispatchWorkItem?
    private var reminded = Set<String>()

    private(set) var media: Media?
    private var volume: Audio.VolumeWatcher?
    private var power: Power?
    private var bluetooth: Bluetooth?
    private var hudWork: DispatchWorkItem?
    private var musicPausedAt: Date?
    private var capsOn = NSEvent.modifierFlags.contains(.capsLock)

    // MARK: layout

    var active: Set<ActivityKind> {
        var s = Set<ActivityKind>()
        if config.music, let m = music, m.playing || now.timeIntervalSince(musicPausedAt ?? now) < 60 { s.insert(.music) }
        if timer != nil { s.insert(.timer) }
        if call != nil { s.insert(.call) }
        if codeBusy { s.insert(.code) }
        return s
    }

    var arranged: (primary: ActivityKind?, minimal: ActivityKind?) { IslandLayout.arrange(active) }

    /// An agent is working, needs you, or just finished (shown for a minute).
    var codeBusy: Bool {
        guard config.codeMode, let c = code else { return false }
        return c.state != .idle && (c.state != .done || now.timeIntervalSince(c.updated) < 60)
    }

    /// Tabs that have something to show, in Windows' order.
    var tabs: [IslandTab] {
        var t: [IslandTab] = [.home]
        if config.music && music != nil { t.append(.music) }
        if config.codeMode { t.append(.code) }
        if timer != nil { t.append(.timer) }
        if config.calendar { t.append(.calendar) }
        if config.system { t.append(.system) }
        return t
    }

    /// The open island's view: your pick, else what's happening (agent/timer/music), else Home.
    var tab: IslandTab {
        if let p = picked, tabs.contains(p) { return p }
        if codeBusy || approval != nil { return .code }
        if timer != nil { return .timer }
        if music?.playing == true { return .music }
        return .home
    }

    /// A call takes over the open island (like iPhone) until you pick a tab.
    var showsCall: Bool { call != nil && picked == nil && !picking }

    var mascotExpression: String {
        if let m = mood { return m }
        if blinking { return "blink" }
        if sleepy { return "sleepy" }
        return hovering ? "wow" : "idle"
    }

    /// Height of the open island's content, so it always fits (Windows' "dynamic height").
    var contentHeight: CGFloat {
        if picking { return 360 }
        if showsCall { return 78 }
        let header: CGFloat = 28
        switch tab {
        case .home: return header + (config.apps.isEmpty ? 70 : 140)
        case .music: return header + 120
        case .code: return header + (approval != nil ? 120 : 98)
        case .timer: return header + 70
        case .calendar: return header + 70 + CGFloat(min(5, max(1, config.tasks.count))) * 22 + 24
        case .system: return header + 96
        }
    }

    var state: IslandLayout.State {
        if expanded { return .expanded(contentHeight: contentHeight) }
        if hud != nil { return .hud }
        return arranged.primary == nil ? .resting : .compact
    }

    var size: CGSize { IslandLayout.size(state, notch: notch) }
    var showsMinimal: Bool { !expanded && hud == nil && arranged.minimal != nil }

    func setExpanded(_ open: Bool) {
        guard open != expanded else { return }
        expanded = open
        if open { media?.syncPosition(); stats = systemStats.read() } else { picking = false; picked = nil }
    }

    func toggleExpanded() { setExpanded(!expanded) }

    func show(_ h: Hud, seconds: Double = 1.6) {
        hud = h
        hudWork?.cancel()
        let w = DispatchWorkItem { [weak self] in self?.hud = nil }
        hudWork = w
        DispatchQueue.main.asyncAfter(deadline: .now() + seconds, execute: w)
    }

    // MARK: sources

    func start() {
        media = Media { [weak self] np in self?.musicChanged(np) }
        volume = Audio.VolumeWatcher { [weak self] v, muted in
            guard let self, self.config.volume else { return }
            let symbol = muted || v == 0 ? "speaker.slash.fill" : v < 0.34 ? "speaker.wave.1.fill" : v < 0.67 ? "speaker.wave.2.fill" : "speaker.wave.3.fill"
            self.show(Hud(symbol: symbol, tint: .white, title: "", value: "\(Int((v * 100).rounded()))", level: muted ? 0 : Double(v)))
        }
        power = Power { [weak self] s in self?.powerChanged(s) }
        bluetooth = Bluetooth { [weak self] name in
            guard let self, self.config.bluetooth else { return }
            self.show(Hud(symbol: Bluetooth.symbol(for: name), tint: .white, title: name, value: "Connected"), seconds: 2.5)
        }
        // Touch ID / password unlock: the iPhone Face ID lock animation.
        DistributedNotificationCenter.default().addObserver(forName: .init("com.apple.screenIsUnlocked"), object: nil, queue: .main) { [weak self] _ in
            guard let self, self.config.unlock else { return }
            self.show(Hud(symbol: "lock.open.fill", tint: .white, title: ""), seconds: 1.2)
        }
        Timer.scheduledTimer(withTimeInterval: 1, repeats: true) { [weak self] _ in self?.tick() }
        Timer.scheduledTimer(withTimeInterval: 4.2, repeats: true) { [weak self] _ in self?.blink() }
        Timer.scheduledTimer(withTimeInterval: 1800, repeats: true) { [weak self] _ in self?.refreshWeather() }
        refreshWeather()
        if config.codeMode { startCodeMode() }
        Timer.scheduledTimer(withTimeInterval: 0.2, repeats: true) { [weak self] _ in self?.checkCapsLock() }
    }

    private func tick() {
        now = Date()
        if expanded && tab == .system { stats = systemStats.read() }
        sleepy = idleSeconds() > 300
        remindTasks()
        // Calls
        if config.calls, let c = Calls.active() {
            if call?.app != c.app { call = CallState(name: c.name, since: now, video: c.video, app: c.app) }
            else if call?.video != c.video { call?.video = c.video }
        } else if let c = call {
            if c.muted { Audio.setMicMuted(false) }
            call = nil
        }
        // Timer finished
        if let t = timer, t.pausedRemaining == nil, t.remaining <= 0 {
            timer = nil
            NSSound(named: "Glass")?.play()
            show(Hud(symbol: "timer", tint: .orange, title: "Timer", value: "Done"), seconds: 5)
        }
    }

    private func musicChanged(_ np: NowPlaying?) {
        if np?.artwork !== music?.artwork { musicTint = np?.artwork?.tint.map(Color.init) ?? .white }
        if let np, !np.playing { if music?.playing != false { musicPausedAt = Date() } } else { musicPausedAt = nil }
        music = np
    }

    private func powerChanged(_ s: Power.State) {
        defer { battery = s }
        guard config.charging, s.hasBattery else { return }
        if s.onAC && !battery.onAC {
            show(Hud(symbol: "battery.100percent.bolt", tint: .green, title: "Charging", value: "\(s.percent)%"), seconds: 2.5)
        } else if !s.onAC, let threshold = [20, 10].first(where: { battery.percent > $0 && s.percent <= $0 }) {
            show(Hud(symbol: "battery.25percent", tint: .red, title: "Low Battery", value: "\(threshold)%"), seconds: 4)
        }
    }

    private func checkCapsLock() {
        let on = NSEvent.modifierFlags.contains(.capsLock)
        guard on != capsOn else { return }
        capsOn = on
        if config.capsLock { show(Hud(symbol: on ? "capslock.fill" : "capslock", tint: on ? .green : .white, title: "Caps Lock", value: on ? "On" : "Off")) }
    }

    // MARK: actions

    func startTimer(minutes: Double) { timer = TimerState(end: Date().addingTimeInterval(minutes * 60)) }

    func toggleTimer() {
        guard var t = timer else { return }
        if let r = t.pausedRemaining { t.end = Date().addingTimeInterval(r); t.pausedRemaining = nil } else { t.pausedRemaining = t.remaining }
        timer = t
    }

    func cancelTimer() { timer = nil }

    func toggleMute() {
        guard var c = call else { return }
        c.muted.toggle()
        Audio.setMicMuted(c.muted)
        call = c
    }

    func openCall() { call?.app.activate(options: []) }

    // MARK: mascot

    private func blink() {
        guard mood == nil else { return }
        blinking = true
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.14) { [weak self] in self?.blinking = false }
    }

    /// Click: squish + annoyed; three quick clicks: dizzy for 3 s (same as Windows).
    func pokeMascot() {
        let t = Date()
        pokes = pokes.filter { t.timeIntervalSince($0) < 0.9 } + [t]
        if mood == "dizzy" { return }
        if pokes.count >= 3 { setMood("dizzy", for: 3) } else { setMood("annoyed", for: 1.2) }
        squish = true
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.18) { [weak self] in self?.squish = false }
    }

    func setMood(_ m: String?, for seconds: Double) {
        mood = m
        moodWork?.cancel()
        let w = DispatchWorkItem { [weak self] in self?.mood = nil }
        moodWork = w
        DispatchQueue.main.asyncAfter(deadline: .now() + seconds, execute: w)
    }

    private func hopOnce() {
        hop = true
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.22) { [weak self] in self?.hop = false }
        setMood("happy", for: 1.5)
    }

    private func idleSeconds() -> Double {
        guard let any = CGEventType(rawValue: ~0) else { return 0 }
        return CGEventSource.secondsSinceLastEventType(.combinedSessionState, eventType: any)
    }

    // MARK: coding mode (lock in)

    /// Starts the local listener; returns an error message if the port is taken.
    @discardableResult
    func startCodeMode() -> String? {
        server?.stop()
        server = nil
        do {
            let s = try CodeServer(port: config.codePort)
            s.onJSON = { [weak self] path, json in self?.agentEvent(path, json) }
            s.status = { [weak self] _ in self?.statusLine ?? "" }
            s.ask = { [weak self] json, reply in self?.ask(json, reply) }
            server = s
        } catch {
            return "Port \(config.codePort) is already in use."
        }
        if lockedInSince == nil { lockedInSince = Date() }
        if !config.codeMode { config.codeMode = true }
        return nil
    }

    func stopCodeMode() {
        respond(nil)
        server?.stop()
        server = nil
        lockedInSince = nil
        if config.codeMode { config.codeMode = false }
    }

    /// Text Claude Code shows in its status bar, e.g. "🏝 ctx 23% · 5h 41% · wk 12%".
    var statusLine: String {
        var parts: [String] = []
        if let c = code?.contextPct { parts.append("ctx \(Int(c))%") }
        if let h = fiveHour { parts.append("5h \(Int(h))%") }
        if let w = week { parts.append("wk \(Int(w))%") }
        return parts.isEmpty ? "🏝" : "🏝 " + parts.joined(separator: " · ")
    }

    private func agentEvent(_ path: String, _ json: [String: Any]) {
        let wasDone = code?.state == .done
        switch path {
        case "/kawaii/hook": tracker.onHook(json)
        case "/kawaii/event": tracker.onEvent(json)
        case "/kawaii/codex": tracker.onCodex(json)
        case "/kawaii/status": tracker.onStatus(json)
        default: break
        }
        code = tracker.active
        fiveHour = tracker.fiveHour
        week = tracker.week
        if code?.state == .done && !wasDone { hopOnce() }
    }

    private func ask(_ json: [String: Any], _ reply: @escaping (String?) -> Void) {
        respond(nil) // a newer question replaces an unanswered one (that one goes back to the terminal)
        answer = reply
        let detail = (json["detail"] as? String) ?? code?.detail ?? ""
        approval = detail.isEmpty ? "Allow?" : detail
        setMood("surprised", for: 2)
    }

    /// "allow", "deny", or nil = answer in the terminal.
    func respond(_ behavior: String?) {
        answer?(behavior)
        answer = nil
        approval = nil
    }

    var lockInText: String {
        guard let since = lockedInSince else { return "" }
        let m = Int(now.timeIntervalSince(since) / 60)
        let n = tracker.sessions.count
        let time = m >= 60 ? "\(m / 60)h \(m % 60)m" : "\(m)m"
        return "Locked in \(time) · \(n) session\(n == 1 ? "" : "s")"
    }

    // MARK: apps

    func showPicker() {
        catalog = AppCatalog.all()
        search = ""
        picking = true
        setExpanded(true)
    }

    var filteredCatalog: [PinnedApp] {
        search.isEmpty ? catalog : catalog.filter { $0.name.localizedCaseInsensitiveContains(search) }
    }

    func pin(_ app: PinnedApp) {
        guard !config.apps.contains(where: { $0.path == app.path }), config.apps.count < 12 else { return }
        config.apps.append(app)
    }

    func unpin(_ app: PinnedApp) { config.apps.removeAll { $0.path == app.path } }

    /// A file dropped on the island: pin it (apps, documents, folders).
    func pinFile(_ url: URL) {
        pin(PinnedApp(name: url.deletingPathExtension().lastPathComponent, path: url.path))
        hopOnce()
    }

    // MARK: calendar + weather

    func addTask(_ text: String) {
        let parts = Tasks.split(text)
        guard !parts.name.isEmpty else { return }
        config.tasks.append(TaskItem(name: parts.name, time: parts.time))
    }

    func toggleTask(_ i: Int) { if config.tasks.indices.contains(i) { config.tasks[i].done.toggle() } }
    func removeTask(_ i: Int) { if config.tasks.indices.contains(i) { config.tasks.remove(at: i) } }

    /// Today's tasks in time order (untimed last), max 5.
    var agenda: [(index: Int, task: TaskItem, at: Date?)] {
        let all = config.tasks.enumerated().map { (index: $0.offset, task: $0.element, at: Tasks.time($0.element.time, on: now)) }
        return Array(all.sorted { ($0.at ?? .distantFuture) < ($1.at ?? .distantFuture) }.prefix(5))
    }

    /// A heads-up 10 minutes before a timed task (once each).
    private func remindTasks() {
        guard config.calendar else { return }
        for a in agenda where !a.task.done {
            guard let at = a.at else { continue }
            let left = at.timeIntervalSince(now)
            let key = "\(a.task.name)@\(at.timeIntervalSince1970)"
            if left > 0, left <= 600, reminded.insert(key).inserted {
                show(Hud(symbol: "calendar", tint: .red, title: a.task.name, value: "in \(max(1, Int((left / 60).rounded()))) min"), seconds: 8)
            }
        }
    }

    func refreshWeather() {
        let city = config.city, f = config.fahrenheit
        Task { @MainActor in self.weather = await Weather.fetch(city: city, fahrenheit: f) }
    }

    /// Ends the call the way the red button does on iPhone: quitting the call app hangs up.
    func endCall() { call?.app.terminate() }
}
