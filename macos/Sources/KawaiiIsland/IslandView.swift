import AppKit
import KawaiiCore
import SwiftUI
import UniformTypeIdentifiers

/// iPhone's island attached to the Mac's camera housing: flush with the top edge (small outward "ears" blend it into
/// the bezel), rounded bottom corners.
struct NotchShape: Shape {
    var radius: CGFloat
    var ear: CGFloat = 6

    var animatableData: CGFloat {
        get { radius }
        set { radius = newValue }
    }

    func path(in r: CGRect) -> Path {
        let b = max(0, min(radius, r.height / 2, (r.width - 2 * ear) / 2))
        var p = Path()
        p.move(to: CGPoint(x: r.minX, y: r.minY))
        p.addArc(tangent1End: CGPoint(x: r.minX + ear, y: r.minY), tangent2End: CGPoint(x: r.minX + ear, y: r.maxY), radius: ear)
        p.addArc(tangent1End: CGPoint(x: r.minX + ear, y: r.maxY), tangent2End: CGPoint(x: r.maxX - ear, y: r.maxY), radius: b)
        p.addArc(tangent1End: CGPoint(x: r.maxX - ear, y: r.maxY), tangent2End: CGPoint(x: r.maxX - ear, y: r.minY), radius: b)
        p.addArc(tangent1End: CGPoint(x: r.maxX - ear, y: r.minY), tangent2End: CGPoint(x: r.maxX, y: r.minY), radius: ear)
        p.closeSubpath()
        return p
    }
}

/// Apple-style motion: quick, smooth, no bounce.
let islandSpring = Animation.spring(response: 0.42, dampingFraction: 0.9)

struct IslandView: View {
    @ObservedObject var model: IslandModel
    private let ear: CGFloat = 6

    var body: some View {
        let size = model.size
        ZStack(alignment: .top) {
            island(size)
            if model.showsMinimal, let k = model.arranged.minimal {
                bubble(k).offset(x: size.width / 2 + 8 + model.notch.height / 2)
                    .transition(.scale(scale: 0.3, anchor: .leading).combined(with: .opacity))
            }
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .top)
        .animation(islandSpring, value: model.state)
        .animation(islandSpring, value: model.showsMinimal)
        .animation(islandSpring, value: model.hovering)
        .environment(\.colorScheme, .dark)
    }

    private func island(_ size: CGSize) -> some View {
        let radius: CGFloat = model.expanded ? 32 : model.notch.isReal ? 12 : size.height / 2
        return NotchShape(radius: radius, ear: ear)
            .fill(Color.black)
            .frame(width: size.width + 2 * ear, height: size.height)
            .overlay(alignment: .top) {
                content.frame(width: size.width, height: size.height, alignment: .top).clipped()
            }
            .shadow(color: .black.opacity(model.expanded ? 0.45 : 0), radius: 18, y: 6)
            // A gentle grow under the pointer, so the (otherwise invisible) island feels touchable.
            .scaleEffect(model.hovering && !model.expanded ? 1.04 : 1, anchor: .top)
            .contentShape(Rectangle())
            .onTapGesture { if !model.expanded { model.setExpanded(true) } }
            .onDrop(of: [UTType.fileURL], isTargeted: nil) { providers in
                for p in providers {
                    _ = p.loadObject(ofClass: URL.self) { url, _ in
                        if let url { DispatchQueue.main.async { model.pinFile(url) } }
                    }
                }
                return true
            }
    }

    @ViewBuilder private var content: some View {
        switch model.state {
        case .expanded:
            ExpandedView(model: model).padding(.top, model.notch.height).transition(.opacity)
        case .hud:
            if let h = model.hud { compact(lead: hudLead(h), trail: hudTrail(h)).transition(.opacity) }
        case .compact:
            if let k = model.arranged.primary { compact(lead: lead(k), trail: trail(k)).transition(.opacity) }
        case .resting:
            EmptyView()
        }
    }

    /// Leading content left of the camera, trailing content right of it — exactly like iPhone's compact island.
    private func compact<L: View, T: View>(lead: L, trail: T) -> some View {
        HStack(spacing: 0) {
            lead.frame(maxWidth: .infinity, alignment: .leading)
            Color.clear.frame(width: model.notch.width)
            trail.frame(maxWidth: .infinity, alignment: .trailing)
        }
        .padding(.horizontal, 12)
        .frame(height: model.notch.height)
    }

    @ViewBuilder private func lead(_ k: ActivityKind) -> some View {
        switch k {
        case .music: Artwork(image: model.music?.artwork, size: 22, radius: 6)
        case .call: Image(systemName: model.call?.video == true ? "video.fill" : "phone.fill").foregroundColor(.green)
        case .timer: Image(systemName: "timer").foregroundColor(.orange)
        case .code: MascotFace(skin: model.config.mascot == "none" ? "kiko" : model.config.mascot, expression: model.mascotExpression, code: true).frame(width: 22, height: 22)
        }
    }

    @ViewBuilder private func trail(_ k: ActivityKind) -> some View {
        switch k {
        case .music: Visualizer(playing: model.music?.playing == true, tint: model.musicTint)
        case .call: Text(Format.clock(model.now.timeIntervalSince(model.call?.since ?? model.now))).foregroundColor(.green).font(.system(size: 13, weight: .semibold).monospacedDigit())
        case .timer: Text(Format.clock(model.timer?.remaining ?? 0)).foregroundColor(.orange).font(.system(size: 13, weight: .semibold).monospacedDigit())
        case .code: Circle().fill(codeColor(model.code?.state)).frame(width: 8, height: 8)
        }
    }

    private func hudLead(_ h: Hud) -> some View {
        HStack(spacing: 6) {
            Image(systemName: h.symbol).foregroundColor(h.tint)
            if !h.title.isEmpty { Text(h.title).foregroundColor(.white).font(.system(size: 12, weight: .semibold)).lineLimit(1) }
        }
        .font(.system(size: 13, weight: .semibold))
    }

    @ViewBuilder private func hudTrail(_ h: Hud) -> some View {
        if let level = h.level { LevelBar(level: level, width: 66) }
        else { Text(h.value).foregroundColor(h.tint == .white ? .gray : h.tint).font(.system(size: 12, weight: .semibold).monospacedDigit()) }
    }

    /// The second activity: iPhone's detached "minimal" circle.
    private func bubble(_ k: ActivityKind) -> some View {
        let h = model.notch.height
        return NotchShape(radius: h / 2, ear: ear).fill(Color.black)
            .frame(width: h + 2 * ear, height: h)
            .overlay { lead(k).font(.system(size: 12, weight: .semibold)).scaleEffect(k == .music ? 0.8 : 1) }
            .onTapGesture { model.setExpanded(true) }
    }
}

struct ExpandedView: View {
    @ObservedObject var model: IslandModel

    var body: some View {
        Group {
            if model.picking { PickerView(model: model) }
            else if model.showsCall { call }
            else {
                VStack(alignment: .leading, spacing: 8) {
                    header
                    switch model.tab {
                    case .home: home
                    case .music: music
                    case .code: codeView
                    case .timer: timer
                    case .calendar: calendar
                    case .system: system
                    }
                }
            }
        }
        .padding(.horizontal, 22)
        .padding(.vertical, 10)
        .overlay(alignment: .topTrailing) {
            // Alerts that arrive while the island is open (volume, Caps Lock…) appear in the corner.
            if let h = model.hud {
                HStack(spacing: 6) {
                    Image(systemName: h.symbol).foregroundColor(h.tint)
                    if let l = h.level { LevelBar(level: l, width: 54) } else { Text(h.value).foregroundColor(.gray) }
                }
                .font(.system(size: 11, weight: .semibold))
                .padding(.horizontal, 10).padding(.vertical, 4)
                .background(Color.black)
                .padding(.trailing, 22).padding(.top, 2)
                .transition(.opacity)
            }
        }
    }

    /// Tabs as plain words (selected = bold white), small clock, the coding-mode toggle and the mascot.
    private var header: some View {
        HStack(spacing: 14) {
            if model.tabs.count > 1 {
                ForEach(model.tabs, id: \.self) { t in
                    Button(t.rawValue) { model.picked = t }
                        .buttonStyle(.plain)
                        .font(.system(size: 12.5, weight: model.tab == t ? .bold : .medium))
                        .foregroundColor(model.tab == t ? .white : .gray)
                }
            }
            Spacer(minLength: 6)
            if model.tab != .home {
                Text(model.now, format: .dateTime.hour().minute()).font(.system(size: 12, weight: .semibold)).foregroundColor(.gray)
            }
            Glyph("chevron.left.forwardslash.chevron.right", size: 12, color: model.config.codeMode ? Color(rgb: 0xFF8FB1) : .gray) {
                if model.config.codeMode { model.stopCodeMode() } else { model.startCodeMode() }
            }
            .help("Coding mode")
            if model.tab != .home { MascotView(model: model, size: 22) }
        }
        .frame(height: 20)
    }

    // MARK: Home: clock, widgets, your apps

    private var home: some View {
        VStack(alignment: .leading, spacing: 12) {
            HStack(alignment: .center, spacing: 16) {
                VStack(alignment: .leading, spacing: 1) {
                    Text(model.now, format: .dateTime.hour().minute()).font(.system(size: 30, weight: .semibold)).foregroundColor(.white)
                    Text(model.now, format: .dateTime.weekday(.wide).month().day()).font(.system(size: 12)).foregroundColor(.gray)
                }
                if let w = model.weather {
                    Rectangle().fill(Color.white.opacity(0.15)).frame(width: 1, height: 34)
                    VStack(alignment: .leading, spacing: 2) {
                        Label("\(w.temp)°", systemImage: w.symbol).font(.system(size: 17, weight: .semibold)).foregroundColor(.white)
                        Text("\(w.text) · \(w.city)").font(.system(size: 11)).foregroundColor(.gray).lineLimit(1)
                    }
                }
                if model.battery.hasBattery {
                    Rectangle().fill(Color.white.opacity(0.15)).frame(width: 1, height: 34)
                    VStack(alignment: .leading, spacing: 2) {
                        Label("\(model.battery.percent)%", systemImage: model.battery.onAC ? "battery.100percent.bolt" : "battery.75percent")
                            .font(.system(size: 17, weight: .semibold)).foregroundColor(.white)
                        Text(model.battery.onAC ? "Charging" : "Battery").font(.system(size: 11)).foregroundColor(.gray)
                    }
                }
                Spacer(minLength: 0)
                MascotView(model: model, size: 44)
            }
            apps
        }
    }

    private var apps: some View {
        HStack(alignment: .top, spacing: 14) {
            ForEach(model.config.apps, id: \.self) { app in
                Button { AppCatalog.launch(app) } label: {
                    VStack(spacing: 4) {
                        Image(nsImage: AppCatalog.icon(app.path)).resizable().frame(width: 32, height: 32)
                        Text(app.name).font(.system(size: 10.5, weight: .medium)).foregroundColor(.gray).lineLimit(1).frame(width: 54)
                    }
                }
                .buttonStyle(.plain)
                .contextMenu { Button("Remove from the island") { model.unpin(app) } }
            }
            if model.config.apps.count < 12 {
                Button { model.showPicker() } label: {
                    VStack(spacing: 4) {
                        Image(systemName: "plus").font(.system(size: 18, weight: .light)).foregroundColor(.white).frame(width: 32, height: 32)
                        Text("Add").font(.system(size: 10.5, weight: .medium)).foregroundColor(.gray)
                    }
                }
                .buttonStyle(.plain)
            }
        }
    }

    // MARK: Music

    @ViewBuilder private var music: some View {
        if let m = model.music {
            VStack(spacing: 9) {
                HStack(spacing: 12) {
                    Artwork(image: m.artwork, size: 50, radius: 11)
                    VStack(alignment: .leading, spacing: 2) {
                        Text(m.title).font(.system(size: 14, weight: .semibold)).foregroundColor(.white).lineLimit(1)
                        Text(m.artist).font(.system(size: 13)).foregroundColor(.gray).lineLimit(1)
                    }
                    Spacer(minLength: 8)
                    Visualizer(playing: m.playing, tint: model.musicTint)
                }
                let elapsed = m.elapsed
                HStack(spacing: 9) {
                    Text(Format.clock(elapsed)).frame(width: 38, alignment: .leading)
                    Scrubber(progress: m.duration > 0 ? elapsed / m.duration : 0) { model.media?.seek(to: $0 * m.duration) }
                    Text("-" + Format.clock(m.duration - elapsed)).frame(width: 42, alignment: .trailing)
                }
                .font(.system(size: 11, weight: .medium).monospacedDigit())
                .foregroundColor(.gray)
                HStack(spacing: 38) {
                    Glyph("backward.fill", size: 18) { model.media?.command("previous track") }
                    Glyph(m.playing ? "pause.fill" : "play.fill", size: 24) { model.media?.command("playpause") }
                    Glyph("forward.fill", size: 18) { model.media?.command("next track") }
                }
            }
        }
    }

    // MARK: Coding mode

    private var codeView: some View {
        let c = model.code
        return VStack(alignment: .leading, spacing: 6) {
            Text(c.map { $0.project.isEmpty ? $0.agent : "\($0.agent) · \($0.project)" } ?? "Waiting for your agent…")
                .font(.system(size: 14, weight: .semibold)).foregroundColor(.white).lineLimit(1)
            HStack(spacing: 7) {
                Circle().fill(codeColor(c?.state)).frame(width: 8, height: 8)
                Text(model.approval ?? codeText(c)).font(.system(size: 12)).foregroundColor(.white).lineLimit(1)
            }
            if model.approval != nil {
                HStack(spacing: 16) {
                    Button("Allow") { model.respond("allow") }.foregroundColor(Color(rgb: 0x30D158))
                    Button("Deny") { model.respond("deny") }.foregroundColor(Color(rgb: 0xFF453A))
                    Button("Answer in terminal") { model.respond(nil) }.foregroundColor(.gray)
                }
                .buttonStyle(.plain).font(.system(size: 13, weight: .semibold))
            }
            HStack(spacing: 14) {
                meter("Context", c?.contextPct)
                meter("5-hour", model.fiveHour)
                meter("Week", model.week)
            }
            Text(model.lockInText).font(.system(size: 11)).foregroundColor(.gray)
        }
    }

    private func codeText(_ c: CodeSession?) -> String {
        guard let c else { return "Coding mode is on. Agents report here." }
        switch c.state {
        case .idle: return "Idle"
        case .thinking: return c.detail.isEmpty ? "Thinking…" : c.detail
        case .tool: return "Running \(c.detail)"
        case .needsYou: return c.detail.isEmpty ? "Needs you" : c.detail
        case .done: return c.detail.isEmpty ? "Finished — your turn" : c.detail
        }
    }

    private func meter(_ label: String, _ pct: Double?) -> some View {
        VStack(alignment: .leading, spacing: 4) {
            HStack {
                Text(label).foregroundColor(.gray)
                Spacer()
                Text(pct.map { "\(Int($0))%" } ?? "—").foregroundColor(.gray)
            }
            .font(.system(size: 10.5, weight: .medium))
            LevelBar(level: (pct ?? 0) / 100, width: 120)
        }
        .frame(maxWidth: .infinity)
    }

    // MARK: Timer

    @ViewBuilder private var timer: some View {
        if let t = model.timer {
            HStack(spacing: 22) {
                VStack(alignment: .leading, spacing: 2) {
                    Text("Timer").font(.system(size: 13)).foregroundColor(.gray)
                    Text(Format.clock(t.remaining)).font(.system(size: 28, weight: .semibold).monospacedDigit()).foregroundColor(.orange)
                }
                Spacer()
                Glyph(t.pausedRemaining == nil ? "pause.fill" : "play.fill", size: 20, color: .orange) { model.toggleTimer() }
                Glyph("xmark", size: 17, color: .gray) { model.cancelTimer() }
            }
        }
    }

    // MARK: Calendar: month progress + your tasks

    private var calendar: some View {
        let progress = Tasks.monthProgress(model.now)
        return VStack(alignment: .leading, spacing: 6) {
            HStack {
                Text(model.now, format: .dateTime.weekday(.wide).month().day()).font(.system(size: 14, weight: .semibold)).foregroundColor(.white)
                Spacer()
                Text("\(Int(progress * 100))% of \(model.now.formatted(.dateTime.month(.wide)))").font(.system(size: 11)).foregroundColor(.gray)
            }
            GeometryReader { g in
                ZStack(alignment: .leading) {
                    Capsule().fill(Color.white.opacity(0.15))
                    Capsule().fill(Color(rgb: 0xFF8FB1)).frame(width: g.size.width * progress)
                }
            }
            .frame(height: 3)
            if model.agenda.isEmpty {
                Text("Nothing planned. Add a task below.").font(.system(size: 12)).foregroundColor(.gray)
            }
            ForEach(model.agenda, id: \.index) { a in
                HStack(spacing: 9) {
                    Circle().fill(a.task.done ? Color.gray : Color(rgb: 0x30D158)).frame(width: 7, height: 7)
                    Text(a.at.map { $0.formatted(date: .omitted, time: .shortened) } ?? "").font(.system(size: 12)).foregroundColor(.gray).frame(width: 64, alignment: .leading)
                    Text(a.task.name).font(.system(size: 12.5, weight: .medium)).foregroundColor(.white).strikethrough(a.task.done)
                    Spacer()
                }
                .contentShape(Rectangle())
                .onTapGesture { model.toggleTask(a.index) }
                .contextMenu { Button("Remove") { model.removeTask(a.index) } }
            }
            Button("+ Add task") { askTask() }.buttonStyle(.plain).font(.system(size: 12.5, weight: .semibold)).foregroundColor(.gray)
        }
    }

    private func askTask() {
        let alert = NSAlert()
        alert.messageText = "Add a task"
        alert.informativeText = "For example \"Gym 5:30 PM\""
        let field = NSTextField(frame: NSRect(x: 0, y: 0, width: 240, height: 24))
        alert.accessoryView = field
        alert.addButton(withTitle: "Add")
        alert.addButton(withTitle: "Cancel")
        NSApp.activate(ignoringOtherApps: true)
        alert.window.initialFirstResponder = field
        if alert.runModal() == .alertFirstButtonReturn { model.addTask(field.stringValue) }
    }

    // MARK: System: CPU / memory / disk / network, power

    private var system: some View {
        VStack(alignment: .leading, spacing: 10) {
            HStack(spacing: 14) {
                stat("CPU", model.stats.cpu)
                stat("Memory", model.stats.ram)
                stat("Disk", model.stats.disk)
                VStack(alignment: .leading, spacing: 4) {
                    Text("Network").font(.system(size: 10.5, weight: .medium)).foregroundColor(.gray)
                    Text("↓ " + SystemStats.rate(model.stats.down)).font(.system(size: 14, weight: .semibold)).foregroundColor(.white)
                        .help("Up " + SystemStats.rate(model.stats.up))
                }
                .frame(maxWidth: .infinity, alignment: .leading)
            }
            HStack(spacing: 16) {
                Button("Lock") { PowerActions.lock() }
                Button("Sleep") { PowerActions.sleep() }
                Button("Restart") { confirm("Restart your Mac now?", PowerActions.restart) }
                Button("Shut Down") { confirm("Shut down your Mac now?", PowerActions.shutDown) }
            }
            .buttonStyle(.plain).font(.system(size: 12.5, weight: .semibold)).foregroundColor(.gray)
        }
    }

    private func stat(_ label: String, _ pct: Double) -> some View {
        VStack(alignment: .leading, spacing: 4) {
            Text(label).font(.system(size: 10.5, weight: .medium)).foregroundColor(.gray)
            Text("\(Int(pct))%").font(.system(size: 14, weight: .semibold)).foregroundColor(.white)
            // Same colours as Windows: calm accent, amber under pressure, red when nearly full.
            GeometryReader { g in
                ZStack(alignment: .leading) {
                    Capsule().fill(Color.white.opacity(0.15))
                    Capsule().fill(pct >= 90 ? Color(rgb: 0xFF453A) : pct >= 75 ? Color(rgb: 0xFF9F0A) : Color(rgb: 0xFF8FB1))
                        .frame(width: g.size.width * min(1, pct / 100))
                }
            }
            .frame(height: 4)
        }
        .frame(maxWidth: .infinity, alignment: .leading)
    }

    private func confirm(_ question: String, _ action: @escaping () -> Void) {
        let alert = NSAlert()
        alert.messageText = question
        alert.informativeText = "Unsaved work in other apps may be lost."
        alert.addButton(withTitle: "No")
        alert.addButton(withTitle: "Yes")
        NSApp.activate(ignoringOtherApps: true)
        if alert.runModal() == .alertSecondButtonReturn { action() }
    }

    // MARK: Call

    @ViewBuilder private var call: some View {
        if let c = model.call {
            HStack(spacing: 22) {
                VStack(alignment: .leading, spacing: 2) {
                    Text(c.name).font(.system(size: 13)).foregroundColor(.gray)
                    Text(Format.clock(model.now.timeIntervalSince(c.since)))
                        .font(.system(size: 24, weight: .semibold).monospacedDigit()).foregroundColor(.green)
                }
                Spacer()
                Glyph(c.muted ? "mic.slash.fill" : "mic.fill", size: 19, color: c.muted ? .orange : .white) { model.toggleMute() }
                Glyph(c.video ? "video.fill" : "rectangle.inset.filled.and.person.filled", size: 18) { model.openCall() }
                Glyph("phone.down.fill", size: 20, color: .red) { model.endCall() }
            }
        }
    }
}

/// "Add apps" inside the island: it grows into a searchable list of installed apps (no separate window).
struct PickerView: View {
    @ObservedObject var model: IslandModel
    @FocusState private var focused: Bool

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            Text("Add apps").font(.system(size: 15, weight: .semibold)).foregroundColor(.white)
            HStack(spacing: 6) {
                Image(systemName: "magnifyingglass").foregroundColor(.gray)
                TextField("Search", text: $model.search).textFieldStyle(.plain).foregroundColor(.white).focused($focused)
            }
            .font(.system(size: 13))
            .padding(.bottom, 4)
            .overlay(alignment: .bottom) { Rectangle().fill(Color.white.opacity(0.15)).frame(height: 1) }
            ScrollView(showsIndicators: false) {
                LazyVStack(alignment: .leading, spacing: 2) {
                    ForEach(model.filteredCatalog, id: \.self) { app in
                        row(app)
                    }
                }
            }
            HStack {
                Button("Browse for a file…") { browse() }
                Spacer()
                Button("Done") { model.picking = false }.foregroundColor(.white)
            }
            .buttonStyle(.plain).font(.system(size: 13, weight: .semibold)).foregroundColor(.gray)
        }
        .onAppear { focused = true }
    }

    private func row(_ app: PinnedApp) -> some View {
        let pinned = model.config.apps.contains(app)
        return Button { if pinned { model.unpin(app) } else { model.pin(app) } } label: {
            HStack(spacing: 10) {
                Image(nsImage: AppCatalog.icon(app.path)).resizable().frame(width: 22, height: 22)
                Text(app.name).font(.system(size: 13, weight: pinned ? .semibold : .regular)).foregroundColor(.white).lineLimit(1)
                Spacer()
                if pinned { Text("On the island").font(.system(size: 11)).foregroundColor(.gray) }
            }
            .padding(.horizontal, 8).padding(.vertical, 5)
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
    }

    private func browse() {
        let panel = NSOpenPanel()
        panel.allowsMultipleSelection = true
        panel.canChooseDirectories = true
        NSApp.activate(ignoringOtherApps: true)
        if panel.runModal() == .OK { for url in panel.urls { model.pinFile(url) } }
    }
}

/// Status dot colours (same as Windows): orange working, yellow needs you, green done.
func codeColor(_ s: CodeState?) -> Color {
    switch s {
    case .thinking?, .tool?: return Color(rgb: 0xFF9F0A)
    case .needsYou?: return Color(rgb: 0xFFD60A)
    case .done?: return Color(rgb: 0x30D158)
    default: return .gray
    }
}

// MARK: - Pieces

/// A plain glyph button (no circle behind it).
struct Glyph: View {
    let name: String, size: CGFloat, color: Color, action: () -> Void
    init(_ name: String, size: CGFloat, color: Color = .white, action: @escaping () -> Void) {
        self.name = name; self.size = size; self.color = color; self.action = action
    }
    var body: some View {
        Button(action: action) { Image(systemName: name).font(.system(size: size, weight: .semibold)).foregroundColor(color) }
            .buttonStyle(.plain)
    }
}

struct Artwork: View {
    let image: NSImage?, size: CGFloat, radius: CGFloat
    var body: some View {
        Group {
            if let image { Image(nsImage: image).resizable().aspectRatio(contentMode: .fill) }
            else { Color(white: 0.18).overlay(Image(systemName: "music.note").foregroundColor(.gray)) }
        }
        .frame(width: size, height: size)
        .clipShape(RoundedRectangle(cornerRadius: radius, style: .continuous))
    }
}

/// iPhone's coloured waveform, tinted by the album art.
struct Visualizer: View {
    let playing: Bool, tint: Color
    var body: some View {
        TimelineView(.animation(minimumInterval: 0.16, paused: !playing)) { ctx in
            let t = ctx.date.timeIntervalSinceReferenceDate
            HStack(spacing: 2.5) {
                ForEach(0..<4, id: \.self) { i in
                    RoundedRectangle(cornerRadius: 1.5).fill(tint)
                        .frame(width: 3, height: playing ? 4 + 12 * abs(sin(t * (2.3 + Double(i) * 0.9) + Double(i) * 1.7)) : 3)
                }
            }
            .frame(height: 16)
            .animation(.easeInOut(duration: 0.16), value: t)
        }
    }
}

struct LevelBar: View {
    let level: Double, width: CGFloat
    var body: some View {
        ZStack(alignment: .leading) {
            RoundedRectangle(cornerRadius: 2).fill(Color.white.opacity(0.2))
            RoundedRectangle(cornerRadius: 2).fill(Color.white).frame(width: width * min(1, max(0, level)))
        }
        .frame(width: width, height: 4)
        .animation(.easeOut(duration: 0.15), value: level)
    }
}

struct Scrubber: View {
    let progress: Double
    let seek: (Double) -> Void
    var body: some View {
        GeometryReader { g in
            ZStack(alignment: .leading) {
                RoundedRectangle(cornerRadius: 2).fill(Color.white.opacity(0.2))
                RoundedRectangle(cornerRadius: 2).fill(Color.white).frame(width: g.size.width * min(1, max(0, progress)))
            }
            .frame(height: 4)
            .frame(maxHeight: .infinity)
            .contentShape(Rectangle())
            .gesture(DragGesture(minimumDistance: 0).onEnded { v in seek(min(1, max(0, v.location.x / g.size.width))) })
        }
        .frame(height: 12)
    }
}
