import AppKit
import AudioToolbox
import CoreAudio
import CoreMediaIO
import IOBluetooth
import IOKit.ps

// Everything the island listens to. Public APIs only, nothing leaves the Mac.

// MARK: - Audio (volume HUD, mic in use, mute)

enum Audio {
    static func address(_ selector: AudioObjectPropertySelector, _ scope: AudioObjectPropertyScope = kAudioObjectPropertyScopeGlobal) -> AudioObjectPropertyAddress {
        AudioObjectPropertyAddress(mSelector: selector, mScope: scope, mElement: kAudioObjectPropertyElementMain)
    }

    static func defaultDevice(input: Bool) -> AudioObjectID {
        var addr = address(input ? kAudioHardwarePropertyDefaultInputDevice : kAudioHardwarePropertyDefaultOutputDevice)
        var id = AudioObjectID(0), size = UInt32(MemoryLayout<AudioObjectID>.size)
        AudioObjectGetPropertyData(AudioObjectID(kAudioObjectSystemObject), &addr, 0, nil, &size, &id)
        return id
    }

    private static func get<T>(_ device: AudioObjectID, _ addr: AudioObjectPropertyAddress, _ initial: T) -> T? {
        var a = addr, value = initial, size = UInt32(MemoryLayout<T>.size)
        return AudioObjectGetPropertyData(device, &a, 0, nil, &size, &value) == noErr ? value : nil
    }

    /// Is any app recording from the microphone right now? (What lights the orange dot.)
    static func micInUse() -> Bool {
        (get(defaultDevice(input: true), address(kAudioDevicePropertyDeviceIsRunningSomewhere), UInt32(0)) ?? 0) != 0
    }

    static func output() -> (volume: Float, muted: Bool) {
        let d = defaultDevice(input: false)
        let v = get(d, address(kAudioHardwareServiceDeviceProperty_VirtualMainVolume, kAudioDevicePropertyScopeOutput), Float32(0)) ?? 0
        let m = get(d, address(kAudioDevicePropertyMute, kAudioDevicePropertyScopeOutput), UInt32(0)) ?? 0
        return (v, m != 0)
    }

    /// Mutes the microphone for every app (the call's Mute button). Falls back to input volume 0 on mics without a mute switch.
    private static var savedInputVolume: Float32 = 1
    static func setMicMuted(_ muted: Bool) {
        let d = defaultDevice(input: true)
        var mute = address(kAudioDevicePropertyMute, kAudioDevicePropertyScopeInput)
        var flag = UInt32(muted ? 1 : 0)
        if AudioObjectSetPropertyData(d, &mute, 0, nil, UInt32(MemoryLayout<UInt32>.size), &flag) == noErr { return }
        var vol = address(kAudioDevicePropertyVolumeScalar, kAudioDevicePropertyScopeInput)
        if muted { savedInputVolume = get(d, vol, Float32(1)) ?? 1 }
        var v: Float32 = muted ? 0 : savedInputVolume
        AudioObjectSetPropertyData(d, &vol, 0, nil, UInt32(MemoryLayout<Float32>.size), &v)
    }

    /// Calls back on every volume or mute change of the current output device (follows AirPods switching etc.).
    final class VolumeWatcher {
        private var device = AudioObjectID(0)
        private let onChange: (Float, Bool) -> Void
        private lazy var block: AudioObjectPropertyListenerBlock = { [weak self] _, _ in
            let o = Audio.output(); self?.onChange(o.volume, o.muted)
        }

        init(onChange: @escaping (Float, Bool) -> Void) {
            self.onChange = onChange
            attach()
            var def = Audio.address(kAudioHardwarePropertyDefaultOutputDevice)
            AudioObjectAddPropertyListenerBlock(AudioObjectID(kAudioObjectSystemObject), &def, .main) { [weak self] _, _ in self?.attach() }
        }

        private func attach() {
            for sel in [kAudioHardwareServiceDeviceProperty_VirtualMainVolume, kAudioDevicePropertyMute] {
                var a = Audio.address(sel, kAudioDevicePropertyScopeOutput)
                if device != 0 { AudioObjectRemovePropertyListenerBlock(device, &a, .main, block) }
            }
            device = Audio.defaultDevice(input: false)
            for sel in [kAudioHardwareServiceDeviceProperty_VirtualMainVolume, kAudioDevicePropertyMute] {
                var a = Audio.address(sel, kAudioDevicePropertyScopeOutput)
                AudioObjectAddPropertyListenerBlock(device, &a, .main, block)
            }
        }
    }
}

// MARK: - Camera in use

enum Camera {
    static func inUse() -> Bool {
        func addr(_ s: Int) -> CMIOObjectPropertyAddress {
            CMIOObjectPropertyAddress(mSelector: CMIOObjectPropertySelector(s), mScope: CMIOObjectPropertyScope(kCMIOObjectPropertyScopeGlobal),
                                      mElement: CMIOObjectPropertyElement(kCMIOObjectPropertyElementMain))
        }
        var devices = addr(kCMIOHardwarePropertyDevices)
        let system = CMIOObjectID(kCMIOObjectSystemObject)
        var size: UInt32 = 0
        guard CMIOObjectGetPropertyDataSize(system, &devices, 0, nil, &size) == noErr, size > 0 else { return false }
        var ids = [CMIOObjectID](repeating: 0, count: Int(size) / MemoryLayout<CMIOObjectID>.size)
        var used: UInt32 = 0
        guard CMIOObjectGetPropertyData(system, &devices, 0, nil, size, &used, &ids) == noErr else { return false }
        return ids.contains { id in
            var running = addr(kCMIODevicePropertyDeviceIsRunningSomewhere)
            var on: UInt32 = 0, got: UInt32 = 0
            return CMIOObjectGetPropertyData(id, &running, 0, nil, UInt32(MemoryLayout<UInt32>.size), &got, &on) == noErr && on != 0
        }
    }
}

// MARK: - Calls (FaceTime, iPhone calls relayed through FaceTime, and the big VoIP apps)

/// macOS has no public API for another app's call state, so a call is "a call app is running and the microphone is
/// live". Incoming calls still ring through macOS's own banner; the island picks the call up once it's answered.
enum Calls {
    static let apps: [(bundle: String, name: String)] = [
        ("com.apple.FaceTime", "FaceTime"), ("us.zoom.xos", "Zoom"), ("com.microsoft.teams2", "Teams"),
        ("com.microsoft.teams", "Teams"), ("net.whatsapp.WhatsApp", "WhatsApp"), ("com.hnc.Discord", "Discord"),
    ]

    static func active() -> (app: NSRunningApplication, name: String, video: Bool)? {
        guard Audio.micInUse() else { return nil }
        for (bundle, name) in apps {
            if let app = NSRunningApplication.runningApplications(withBundleIdentifier: bundle).first {
                return (app, name, Camera.inUse())
            }
        }
        return nil
    }
}

// MARK: - Power (charging, low battery)

final class Power {
    struct State: Equatable { var onAC: Bool; var percent: Int; var hasBattery: Bool }
    private let onChange: (State) -> Void
    private var source: CFRunLoopSource?

    init(onChange: @escaping (State) -> Void) {
        self.onChange = onChange
        let ctx = Unmanaged.passUnretained(self).toOpaque()
        source = IOPSNotificationCreateRunLoopSource({ ctx in
            guard let ctx else { return }
            let me = Unmanaged<Power>.fromOpaque(ctx).takeUnretainedValue()
            me.onChange(Power.read())
        }, ctx)?.takeRetainedValue()
        if let source { CFRunLoopAddSource(CFRunLoopGetMain(), source, .defaultMode) }
    }

    static func read() -> State {
        let info = IOPSCopyPowerSourcesInfo().takeRetainedValue()
        let list = IOPSCopyPowerSourcesList(info).takeRetainedValue() as [CFTypeRef]
        for ps in list {
            guard let d = IOPSGetPowerSourceDescription(info, ps)?.takeUnretainedValue() as? [String: Any],
                  d[kIOPSTypeKey] as? String == kIOPSInternalBatteryType else { continue }
            let cur = d[kIOPSCurrentCapacityKey] as? Int ?? 0, max = d[kIOPSMaxCapacityKey] as? Int ?? 100
            return State(onAC: d[kIOPSPowerSourceStateKey] as? String == kIOPSACPowerValue,
                         percent: max > 0 ? cur * 100 / max : cur, hasBattery: true)
        }
        return State(onAC: true, percent: 100, hasBattery: false)
    }
}

// MARK: - Bluetooth (AirPods & co. connected)

final class Bluetooth: NSObject {
    private let onConnect: (String) -> Void
    private var note: IOBluetoothUserNotification?
    private let started = Date()

    init(onConnect: @escaping (String) -> Void) {
        self.onConnect = onConnect
        super.init()
        note = IOBluetoothDevice.register(forConnectNotifications: self, selector: #selector(connected(_:device:)))
    }

    @objc private func connected(_ n: IOBluetoothUserNotification, device: IOBluetoothDevice) {
        // Registration replays every device that's already connected; only announce real new connections.
        guard Date().timeIntervalSince(started) > 3 else { return }
        onConnect(device.name ?? "Bluetooth device")
    }

    /// SF Symbol for a device name, the way iPhone shows AirPods.
    static func symbol(for name: String) -> String {
        let n = name.lowercased()
        if n.contains("airpods max") { return "airpodsmax" }
        if n.contains("airpods pro") { return "airpodspro" }
        if n.contains("airpods") { return "airpods" }
        if n.contains("beats") { return "beats.headphones" }
        if n.contains("keyboard") { return "keyboard" }
        if n.contains("mouse") { return "magicmouse" }
        return "headphones"
    }
}

// MARK: - Music (Apple Music, Spotify)

struct NowPlaying: Equatable {
    var player: Player
    var title: String
    var artist: String
    var playing: Bool
    var duration: Double
    var position: Double
    var positionAt: Date
    var artwork: NSImage?

    var elapsed: Double { min(duration, position + (playing ? Date().timeIntervalSince(positionAt) : 0)) }

    static func == (a: Self, b: Self) -> Bool {
        a.player == b.player && a.title == b.title && a.artist == b.artist && a.playing == b.playing
            && a.duration == b.duration && a.position == b.position && a.artwork === b.artwork
    }
}

enum Player: String, CaseIterable {
    case music = "com.apple.Music", spotify = "com.spotify.client"
    var scriptName: String { self == .music ? "Music" : "Spotify" }
    var notification: String { self == .music ? "com.apple.Music.playerInfo" : "com.spotify.client.PlaybackStateChanged" }
    var isRunning: Bool { !NSRunningApplication.runningApplications(withBundleIdentifier: rawValue).isEmpty }
}

/// Track changes arrive as the players' own distributed notifications (instant, no permission). Position, artwork and
/// the play/next/previous buttons use AppleScript (macOS asks once for "Automation" permission).
final class Media {
    private let onChange: (NowPlaying?) -> Void
    private(set) var current: NowPlaying?

    init(onChange: @escaping (NowPlaying?) -> Void) {
        self.onChange = onChange
        for p in Player.allCases {
            DistributedNotificationCenter.default().addObserver(forName: .init(p.notification), object: nil, queue: .main) { [weak self] n in
                self?.handle(p, n.userInfo ?? [:])
            }
        }
        for p in Player.allCases where p.isRunning { refresh(p) }
    }

    private func handle(_ p: Player, _ info: [AnyHashable: Any]) {
        let state = info["Player State"] as? String ?? "Stopped"
        if state == "Stopped" || (info["Name"] as? String ?? "").isEmpty {
            if current?.player == p { set(nil) }
            return
        }
        let ms = (info[p == .music ? "Total Time" : "Duration"] as? NSNumber)?.doubleValue ?? 0
        var np = NowPlaying(player: p, title: info["Name"] as? String ?? "", artist: info["Artist"] as? String ?? "",
                            playing: state == "Playing", duration: ms / 1000,
                            position: (info["Playback Position"] as? NSNumber)?.doubleValue ?? current?.elapsed ?? 0,
                            positionAt: Date(), artwork: nil)
        let sameTrack = current?.player == p && current?.title == np.title && current?.artist == np.artist
        if sameTrack { np.artwork = current?.artwork } else { np.position = 0 }
        // Another player that's still playing keeps the island; a paused one gives way.
        if let c = current, c.player != p, c.playing, !np.playing { return }
        set(np)
        syncPosition()
        if !sameTrack { loadArtwork(p) }
    }

    private func set(_ np: NowPlaying?) {
        guard np != current else { return }
        current = np
        onChange(np)
    }

    /// The first state after launch (players only post on change).
    private func refresh(_ p: Player) {
        let s = "tell application \"\(p.scriptName)\" to if player state is not stopped then return {name of current track, artist of current track, player state as text, duration of current track, player position}"
        guard let r = Self.run(s), r.numberOfItems == 5 else { return }
        var dur = r.atIndex(4)?.doubleValue ?? 0
        if p == .spotify { dur /= 1000 } // Spotify reports milliseconds
        let np = NowPlaying(player: p, title: r.atIndex(1)?.stringValue ?? "", artist: r.atIndex(2)?.stringValue ?? "",
                            playing: r.atIndex(3)?.stringValue == "playing", duration: dur,
                            position: r.atIndex(5)?.doubleValue ?? 0, positionAt: Date(), artwork: nil)
        guard !np.title.isEmpty, current == nil || np.playing else { return }
        set(np)
        loadArtwork(p)
    }

    /// Re-reads the playhead (seeking in the player doesn't post a notification).
    func syncPosition() {
        guard var np = current, np.player.isRunning,
              let pos = Self.run("tell application \"\(np.player.scriptName)\" to return player position")?.doubleValue else { return }
        np.position = pos; np.positionAt = Date()
        current = np
        onChange(np)
    }

    private func loadArtwork(_ p: Player) {
        if p == .music {
            if let d = Self.run("tell application \"Music\" to return data of artwork 1 of current track")?.data, let img = NSImage(data: d) {
                setArtwork(img, for: p)
            }
        } else if let s = Self.run("tell application \"Spotify\" to return artwork url of current track")?.stringValue, let url = URL(string: s) {
            URLSession.shared.dataTask(with: url) { [weak self] data, _, _ in
                guard let data, let img = NSImage(data: data) else { return }
                DispatchQueue.main.async { self?.setArtwork(img, for: p) }
            }.resume()
        }
    }

    private func setArtwork(_ img: NSImage, for p: Player) {
        guard var np = current, np.player == p else { return }
        np.artwork = img
        set(np)
    }

    func command(_ c: String) {
        guard let p = current?.player, p.isRunning else { return }
        Self.run("tell application \"\(p.scriptName)\" to \(c)")
    }

    func seek(to seconds: Double) {
        guard let p = current?.player else { return }
        Self.run("tell application \"\(p.scriptName)\" to set player position to \(seconds)")
        syncPosition()
    }

    @discardableResult
    static func run(_ source: String) -> NSAppleEventDescriptor? {
        var error: NSDictionary?
        return NSAppleScript(source: source)?.executeAndReturnError(&error)
    }
}

extension NSImage {
    /// The artwork's average colour, brightened: tints the visualizer like iPhone does.
    var tint: NSColor? {
        guard let cg = cgImage(forProposedRect: nil, context: nil, hints: nil) else { return nil }
        let ci = CIImage(cgImage: cg)
        guard let out = CIFilter(name: "CIAreaAverage", parameters: [kCIInputImageKey: ci, kCIInputExtentKey: CIVector(cgRect: ci.extent)])?.outputImage
        else { return nil }
        var px = [UInt8](repeating: 0, count: 4)
        CIContext().render(out, toBitmap: &px, rowBytes: 4, bounds: CGRect(x: 0, y: 0, width: 1, height: 1), format: .RGBA8,
                           colorSpace: CGColorSpaceCreateDeviceRGB())
        let c = NSColor(red: CGFloat(px[0]) / 255, green: CGFloat(px[1]) / 255, blue: CGFloat(px[2]) / 255, alpha: 1)
        return NSColor(hue: c.hueComponent, saturation: min(1, c.saturationComponent * 1.2), brightness: max(0.75, c.brightnessComponent), alpha: 1)
    }
}
