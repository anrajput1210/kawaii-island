import KawaiiCore
import SwiftUI

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
        }
    }

    @ViewBuilder private func trail(_ k: ActivityKind) -> some View {
        switch k {
        case .music: Visualizer(playing: model.music?.playing == true, tint: model.musicTint)
        case .call: Text(Format.clock(model.now.timeIntervalSince(model.call?.since ?? model.now))).foregroundColor(.green).font(.system(size: 13, weight: .semibold).monospacedDigit())
        case .timer: Text(Format.clock(model.timer?.remaining ?? 0)).foregroundColor(.orange).font(.system(size: 13, weight: .semibold).monospacedDigit())
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
            switch model.expandedKind {
            case .call?: call
            case .timer?: timer
            case .music?: music
            case nil: idle
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
                .padding(.trailing, 22).padding(.top, 2)
                .transition(.opacity)
            }
        }
    }

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

    private var idle: some View {
        HStack(alignment: .bottom) {
            VStack(alignment: .leading, spacing: 1) {
                Text(model.now, format: .dateTime.weekday(.wide).month().day()).font(.system(size: 13)).foregroundColor(.gray)
                Text(model.now, format: .dateTime.hour().minute()).font(.system(size: 28, weight: .semibold)).foregroundColor(.white)
            }
            Spacer()
            VStack(alignment: .trailing, spacing: 6) {
                if model.battery.hasBattery {
                    Label("\(model.battery.percent)%", systemImage: model.battery.onAC ? "battery.100percent.bolt" : "battery.75percent")
                        .font(.system(size: 12, weight: .medium)).foregroundColor(model.battery.onAC ? .green : .gray)
                }
                HStack(spacing: 12) {
                    Image(systemName: "timer").foregroundColor(.orange)
                    ForEach([1, 5, 10, 25], id: \.self) { m in
                        Button("\(m)m") { model.startTimer(minutes: Double(m)) }.buttonStyle(.plain).foregroundColor(.white)
                    }
                }
                .font(.system(size: 12, weight: .semibold))
            }
        }
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
