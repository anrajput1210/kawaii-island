import AppKit
import Carbon
import Darwin
import KawaiiCore
import Network

// MARK: - Apps

/// Installed apps for the in-island "Add apps" list (Applications folders, like Launchpad).
enum AppCatalog {
    static func all() -> [PinnedApp] {
        let fm = FileManager.default
        let roots = ["/Applications", "/Applications/Utilities", "/System/Applications", "/System/Applications/Utilities",
                     fm.homeDirectoryForCurrentUser.appendingPathComponent("Applications").path]
        var seen = Set<String>(), out: [PinnedApp] = []
        for root in roots {
            for item in (try? fm.contentsOfDirectory(atPath: root)) ?? [] where item.hasSuffix(".app") {
                let name = String(item.dropLast(4))
                if seen.insert(name).inserted { out.append(PinnedApp(name: name, path: root + "/" + item)) }
            }
        }
        return out.sorted { $0.name.localizedCaseInsensitiveCompare($1.name) == .orderedAscending }
    }

    private static var icons: [String: NSImage] = [:]
    static func icon(_ path: String) -> NSImage {
        if let i = icons[path] { return i }
        let i = NSWorkspace.shared.icon(forFile: path)
        icons[path] = i
        return i
    }

    static func launch(_ app: PinnedApp) {
        let url = URL(fileURLWithPath: app.path)
        if app.path.hasSuffix(".app") { NSWorkspace.shared.openApplication(at: url, configuration: .init()) }
        else { NSWorkspace.shared.open(url) }
    }
}

// MARK: - System

/// CPU, memory, disk and network for the System tab.
final class SystemStats {
    struct Snapshot: Equatable { var cpu = 0.0, ram = 0.0, disk = 0.0, down = 0.0, up = 0.0 }
    private var lastTicks: (busy: UInt64, total: UInt64)?
    private var lastNet: (rx: UInt64, tx: UInt64, at: Date)?

    func read() -> Snapshot {
        var s = Snapshot()
        // CPU: busy share of all ticks since the last read.
        var load = host_cpu_load_info()
        var count = mach_msg_type_number_t(MemoryLayout<host_cpu_load_info>.size / MemoryLayout<integer_t>.size)
        let cpuOK = withUnsafeMutablePointer(to: &load) { ptr in
            ptr.withMemoryRebound(to: integer_t.self, capacity: Int(count)) { host_statistics(mach_host_self(), HOST_CPU_LOAD_INFO, $0, &count) }
        }
        if cpuOK == KERN_SUCCESS {
            let t = load.cpu_ticks
            let busy = UInt64(t.0) + UInt64(t.1) + UInt64(t.3), total = busy + UInt64(t.2)
            if let l = lastTicks, total > l.total { s.cpu = Double(busy - l.busy) / Double(total - l.total) * 100 }
            lastTicks = (busy, total)
        }
        // Memory: active + wired + compressed of physical memory.
        var vm = vm_statistics64()
        var vmCount = mach_msg_type_number_t(MemoryLayout<vm_statistics64>.size / MemoryLayout<integer_t>.size)
        let vmOK = withUnsafeMutablePointer(to: &vm) { ptr in
            ptr.withMemoryRebound(to: integer_t.self, capacity: Int(vmCount)) { host_statistics64(mach_host_self(), HOST_VM_INFO64, $0, &vmCount) }
        }
        if vmOK == KERN_SUCCESS {
            let pages = UInt64(vm.active_count) + UInt64(vm.wire_count) + UInt64(vm.compressor_page_count)
            s.ram = Double(pages * UInt64(vm_kernel_page_size)) / Double(ProcessInfo.processInfo.physicalMemory) * 100
        }
        // Disk: the startup volume.
        if let v = try? URL(fileURLWithPath: "/").resourceValues(forKeys: [.volumeTotalCapacityKey, .volumeAvailableCapacityForImportantUsageKey]),
           let total = v.volumeTotalCapacity, total > 0, let free = v.volumeAvailableCapacityForImportantUsage {
            s.disk = (1 - Double(free) / Double(total)) * 100
        }
        // Network: bytes across all non-loopback interfaces.
        var rx: UInt64 = 0, tx: UInt64 = 0
        var addrs: UnsafeMutablePointer<ifaddrs>?
        if getifaddrs(&addrs) == 0 {
            var p = addrs
            while let a = p {
                if a.pointee.ifa_addr?.pointee.sa_family == UInt8(AF_LINK), String(cString: a.pointee.ifa_name) != "lo0",
                   let d = a.pointee.ifa_data?.assumingMemoryBound(to: if_data.self) {
                    rx += UInt64(d.pointee.ifi_ibytes); tx += UInt64(d.pointee.ifi_obytes)
                }
                p = a.pointee.ifa_next
            }
            freeifaddrs(addrs)
        }
        let now = Date()
        if let n = lastNet, now > n.at, rx >= n.rx, tx >= n.tx {
            let dt = now.timeIntervalSince(n.at)
            s.down = Double(rx - n.rx) / dt
            s.up = Double(tx - n.tx) / dt
        }
        lastNet = (rx, tx, now)
        return s
    }

    static func rate(_ bps: Double) -> String {
        bps >= 1_000_000 ? String(format: "%.1f MB/s", bps / 1_000_000) : bps >= 1000 ? String(format: "%.0f KB/s", bps / 1000) : String(format: "%.0f B/s", bps)
    }
}

enum PowerActions {
    static func lock() { run("/usr/bin/pmset", ["displaysleepnow"]) }   // locks when "require password" is on (the default)
    static func sleep() { run("/usr/bin/pmset", ["sleepnow"]) }
    static func restart() { _ = Media.run("tell application \"System Events\" to restart") }
    static func shutDown() { _ = Media.run("tell application \"System Events\" to shut down") }
    private static func run(_ tool: String, _ args: [String]) {
        let p = Process()
        p.executableURL = URL(fileURLWithPath: tool)
        p.arguments = args
        try? p.run()
    }
}

// MARK: - Weather (Open-Meteo, no account)

struct Weather: Equatable {
    var temp: Int, symbol: String, text: String, city: String

    static func fetch(city: String, fahrenheit: Bool) async -> Weather? {
        guard !city.isEmpty,
              let geoURL = URL(string: "https://geocoding-api.open-meteo.com/v1/search?count=1&name=" + (city.addingPercentEncoding(withAllowedCharacters: .urlQueryAllowed) ?? "")),
              let (gd, _) = try? await URLSession.shared.data(from: geoURL),
              let g = (try? JSONSerialization.jsonObject(with: gd)) as? [String: Any],
              let place = (g["results"] as? [[String: Any]])?.first,
              let lat = place["latitude"] as? Double, let lon = place["longitude"] as? Double,
              let url = URL(string: "https://api.open-meteo.com/v1/forecast?latitude=\(lat)&longitude=\(lon)&current=temperature_2m,weather_code" + (fahrenheit ? "&temperature_unit=fahrenheit" : "")),
              let (d, _) = try? await URLSession.shared.data(from: url),
              let j = (try? JSONSerialization.jsonObject(with: d)) as? [String: Any],
              let cur = j["current"] as? [String: Any], let t = cur["temperature_2m"] as? Double else { return nil }
        let (symbol, text) = describe(cur["weather_code"] as? Int ?? 0)
        return Weather(temp: Int(t.rounded()), symbol: symbol, text: text, city: place["name"] as? String ?? city)
    }

    /// WMO weather code → SF Symbol + words.
    static func describe(_ code: Int) -> (String, String) {
        switch code {
        case 0: return ("sun.max.fill", "Clear")
        case 1, 2: return ("cloud.sun.fill", "Partly cloudy")
        case 3: return ("cloud.fill", "Cloudy")
        case 45, 48: return ("cloud.fog.fill", "Fog")
        case 51...67, 80...82: return ("cloud.rain.fill", "Rain")
        case 71...77, 85, 86: return ("cloud.snow.fill", "Snow")
        case 95...99: return ("cloud.bolt.rain.fill", "Storm")
        default: return ("cloud.fill", "Cloudy")
        }
    }
}

// MARK: - Hotkey (Ctrl+Option+I, like Ctrl+Alt+I on Windows)

final class HotKey {
    private static var action: (() -> Void)?
    private var ref: EventHotKeyRef?

    init(action: @escaping () -> Void) {
        HotKey.action = action
        var spec = EventTypeSpec(eventClass: OSType(kEventClassKeyboard), eventKind: UInt32(kEventHotKeyPressed))
        InstallEventHandler(GetApplicationEventTarget(), { _, _, _ in
            DispatchQueue.main.async { HotKey.action?() }
            return noErr
        }, 1, &spec, nil, nil)
        RegisterEventHotKey(UInt32(kVK_ANSI_I), UInt32(controlKey | optionKey), EventHotKeyID(signature: OSType(0x4B41_5749), id: 1),
                            GetApplicationEventTarget(), 0, &ref)
    }
}

// MARK: - Coding mode listener (same endpoints as Windows)

/// Local HTTP on 127.0.0.1:<port>: /kawaii/hook, /status, /event, /codex (fire and forget) and /ask, /permission
/// (held open until Allow / Deny on the island, or `approvalSeconds`). Loopback only.
final class CodeServer {
    var onJSON: ((String, [String: Any]) -> Void)?
    var status: (([String: Any]) -> String)?
    var ask: (([String: Any], @escaping (String?) -> Void) -> Void)?
    private let listener: NWListener

    init(port: Int) throws {
        let params = NWParameters.tcp
        params.requiredLocalEndpoint = NWEndpoint.hostPort(host: "127.0.0.1", port: NWEndpoint.Port(rawValue: UInt16(port)) ?? 47811)
        listener = try NWListener(using: params)
        listener.newConnectionHandler = { [weak self] c in self?.accept(c) }
        listener.start(queue: .main)
    }

    func stop() { listener.cancel() }

    private func accept(_ c: NWConnection) {
        c.start(queue: .main)
        read(c, Data())
    }

    private func read(_ c: NWConnection, _ buffer: Data) {
        c.receive(minimumIncompleteLength: 1, maximumLength: 65536) { [weak self] data, _, done, error in
            guard let self else { return }
            var buf = buffer
            if let data { buf.append(data) }
            if buf.count > 1_000_000 || error != nil { c.cancel(); return }
            guard let headerEnd = buf.range(of: Data("\r\n\r\n".utf8)) else {
                if done { c.cancel() } else { self.read(c, buf) }
                return
            }
            let head = String(decoding: buf[..<headerEnd.lowerBound], as: UTF8.self)
            var length = 0
            for line in head.components(separatedBy: "\r\n") where line.lowercased().hasPrefix("content-length:") {
                length = Int(line.dropFirst("content-length:".count).trimmingCharacters(in: .whitespaces)) ?? 0
            }
            let body = buf[headerEnd.upperBound...]
            if body.count < length && !done { self.read(c, buf); return }
            let parts = head.components(separatedBy: " ")
            let path = parts.count > 1 ? parts[1] : ""
            let json = (try? JSONSerialization.jsonObject(with: Data(body))) as? [String: Any] ?? [:]
            self.handle(c, path, json)
        }
    }

    private func handle(_ c: NWConnection, _ path: String, _ json: [String: Any]) {
        switch path {
        case "/kawaii/hook", "/kawaii/event", "/kawaii/codex":
            onJSON?(path, json)
            respond(c, "")
        case "/kawaii/status":
            onJSON?(path, json)
            respond(c, status?(json) ?? "")
        case "/kawaii/ask", "/kawaii/permission":
            onJSON?(path == "/kawaii/ask" ? "/kawaii/event" : "/kawaii/hook", json)
            var answered = false
            let reply: (String?) -> Void = { [weak self] answer in
                guard !answered else { return }
                answered = true
                self?.respond(c, path == "/kawaii/permission" ? ClaudeSettings.permissionReply(answer) : answer ?? "")
            }
            ask?(json, reply)
            DispatchQueue.main.asyncAfter(deadline: .now() + .seconds(ClaudeSettings.approvalSeconds)) { reply(nil) }
        default:
            respond(c, "", status: "404 Not Found")
        }
    }

    private func respond(_ c: NWConnection, _ body: String, status: String = "200 OK") {
        let data = Data(body.utf8)
        let head = "HTTP/1.1 \(status)\r\nContent-Type: application/json\r\nContent-Length: \(data.count)\r\nConnection: close\r\n\r\n"
        c.send(content: Data(head.utf8) + data, completion: .contentProcessed { _ in c.cancel() })
    }
}
