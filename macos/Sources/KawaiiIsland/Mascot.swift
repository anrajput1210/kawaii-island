import KawaiiCore
import SwiftUI

/// The mascot art (generated from design/mascot/mascot.js into MascotData.swift), composed exactly like the
/// Windows app: [hoodie] + head + eyes + face + [glasses] + expression extras. Parsed once per pose, then cached.
enum MascotArt {
    static let order = ["kiko", "miso", "bun", "bolt", "ribbit",
                        "valor", "rumble", "forge", "brick", "trick", "gloom", "scruff",
                        "sunny", "kit", "snow", "rosy", "grit", "tidy", "sprout", "clover"]
    private static let data = (try? JSONSerialization.jsonObject(with: Data(mascotJSON.utf8))) as? [String: Any] ?? [:]
    private static var skins: [String: [String: Any]] { data["skins"] as? [String: [String: Any]] ?? [:] }
    private static var cache: [String: [SVGShape]] = [:]

    /// "Kiko · anime girl"
    static func label(_ key: String) -> String {
        guard let s = skins[key] else { return key }
        return "\(s["name"] as? String ?? key) · \((s["kind"] as? String ?? "").lowercased())"
    }

    static func shapes(_ skin: String, _ expression: String, code: Bool) -> [SVGShape] {
        let key = "\(skin).\(expression).\(code)"
        if let c = cache[key] { return c }
        guard let s = skins[skin] ?? skins["kiko"], let faces = s["faces"] as? [String: [String: String]],
              let face = faces[expression] ?? faces["idle"] else { return [] }
        let extra = (data["extra"] as? [String: String])?[expression] ?? ""
        let markup = (code ? data["hoodie"] as? String ?? "" : "") + (s["back"] as? String ?? "") + (face["eyes"] ?? "")
            + (face["over"] ?? "") + (code ? s["glasses"] as? String ?? "" : "") + extra
        let shapes = SVG.parse(markup)
        cache[key] = shapes
        return shapes
    }
}

extension Color {
    init(rgb: UInt32, opacity: Double = 1) {
        self.init(.sRGB, red: Double(rgb >> 16 & 0xFF) / 255, green: Double(rgb >> 8 & 0xFF) / 255, blue: Double(rgb & 0xFF) / 255, opacity: opacity)
    }
}

/// Draws one pose of a mascot, scaled from its 64×64 art box.
struct MascotFace: View {
    let skin: String, expression: String, code: Bool
    var body: some View {
        Canvas { ctx, size in
            let k = min(size.width, size.height) / 64
            ctx.translateBy(x: (size.width - 64 * k) / 2, y: (size.height - 64 * k) / 2)
            ctx.scaleBy(x: k, y: k)
            for s in MascotArt.shapes(skin, expression, code: code) {
                let path = Path(s.path)
                if let f = s.fill { ctx.fill(path, with: .color(Color(rgb: f, opacity: s.opacity))) }
                if let st = s.stroke {
                    ctx.stroke(path, with: .color(Color(rgb: st, opacity: s.opacity)),
                               style: StrokeStyle(lineWidth: s.strokeWidth, lineCap: s.roundCaps ? .round : .butt, lineJoin: s.roundJoins ? .round : .miter))
                }
            }
        }
    }
}

/// The live mascot: blinks, says "wow" under the pointer, squishes when clicked, gets dizzy after three quick
/// clicks, dozes when you're away, hops when an agent finishes, and wears the hoodie in coding mode.
struct MascotView: View {
    @ObservedObject var model: IslandModel
    var size: CGFloat
    var interactive = true

    var body: some View {
        MascotFace(skin: model.config.mascot, expression: model.mascotExpression, code: model.config.codeMode)
            .frame(width: size, height: size)
            .scaleEffect(x: model.squish ? 1.12 : 1, y: model.squish ? 0.86 : 1, anchor: .bottom)
            .rotationEffect(.degrees(model.mood == "dizzy" ? 8 : 0))
            .offset(y: model.hop ? -size * 0.18 : 0)
            .animation(.easeOut(duration: 0.18), value: model.squish)
            .animation(model.mood == "dizzy" ? .easeInOut(duration: 0.35).repeatForever(autoreverses: true) : .easeOut(duration: 0.2), value: model.mood)
            .animation(.easeOut(duration: 0.22), value: model.hop)
            .contentShape(Rectangle())
            .onTapGesture { if interactive { model.pokeMascot() } }
            .opacity(model.config.mascot == "none" ? 0 : 1)
    }
}
