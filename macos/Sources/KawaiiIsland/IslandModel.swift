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
        return s
    }

    var arranged: (primary: ActivityKind?, minimal: ActivityKind?) { IslandLayout.arrange(active) }

    /// What the open island shows: the top activity, else paused music, else the clock.
    var expandedKind: ActivityKind? { arranged.primary ?? (config.music && music != nil ? .music : nil) }

    var state: IslandLayout.State {
        if expanded { return .expanded(contentHeight: expandedKind == .music ? 128 : 78) }
        if hud != nil { return .hud }
        return arranged.primary == nil ? .resting : .compact
    }

    var size: CGSize { IslandLayout.size(state, notch: notch) }
    var showsMinimal: Bool { !expanded && hud == nil && arranged.minimal != nil }

    func setExpanded(_ open: Bool) {
        guard open != expanded else { return }
        expanded = open
        if open { media?.syncPosition() }
    }

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
        Timer.scheduledTimer(withTimeInterval: 0.2, repeats: true) { [weak self] _ in self?.checkCapsLock() }
    }

    private func tick() {
        now = Date()
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

    /// Ends the call the way the red button does on iPhone: quitting the call app hangs up.
    func endCall() { call?.app.terminate() }
}
