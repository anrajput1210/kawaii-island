import CoreGraphics
import Foundation

/// One drawable piece of mascot art: a path plus how to paint it.
public struct SVGShape {
    public var path: CGPath
    public var fill: UInt32?        // 0xRRGGBB, nil = none
    public var stroke: UInt32?
    public var strokeWidth: CGFloat
    public var opacity: CGFloat
    public var roundCaps: Bool
    public var roundJoins: Bool
}

/// The small SVG subset the mascot art uses (design/mascot/mascot.js): ellipse, circle, rect, path (M L H V Q C A Z,
/// absolute and relative), g with inherited paint, and rotate() transforms. Same subset as the Windows XAML export.
public enum SVG {
    public static func parse(_ markup: String) -> [SVGShape] {
        var shapes: [SVGShape] = []
        var groups: [[String: String]] = [[:]]
        let tag = try! NSRegularExpression(pattern: #"<(/?)([a-z]+)([^>]*?)(/?)>"#)
        let ns = markup as NSString
        for m in tag.matches(in: markup, range: NSRange(location: 0, length: ns.length)) {
            let closing = ns.substring(with: m.range(at: 1)) == "/"
            let name = ns.substring(with: m.range(at: 2))
            let attrs = attributes(ns.substring(with: m.range(at: 3)))
            let selfClosing = ns.substring(with: m.range(at: 4)) == "/"
            if name == "g" {
                if closing { if groups.count > 1 { groups.removeLast() } }
                else if !selfClosing { groups.append(groups.last!.merging(attrs) { _, new in new }) }
                continue
            }
            if closing || name == "svg" { continue }
            let a = groups.last!.merging(attrs) { _, new in new }
            guard var path = geometry(name, a) else { continue }
            if let t = a["transform"], let r = rotation(t) {
                var xf = CGAffineTransform(translationX: r.cx, y: r.cy).rotated(by: r.deg * .pi / 180).translatedBy(x: -r.cx, y: -r.cy)
                path = path.copy(using: &xf) ?? path
            }
            let fill = a["fill"] ?? "#000000"
            shapes.append(SVGShape(path: path, fill: color(fill), stroke: a["stroke"].flatMap(color),
                                   strokeWidth: num(a["stroke-width"]) ?? 1, opacity: num(a["opacity"]) ?? 1,
                                   roundCaps: a["stroke-linecap"] == "round", roundJoins: a["stroke-linejoin"] == "round"))
        }
        return shapes
    }

    static func attributes(_ s: String) -> [String: String] {
        let re = try! NSRegularExpression(pattern: #"([\w-]+)="([^"]*)""#)
        let ns = s as NSString
        var out: [String: String] = [:]
        for m in re.matches(in: s, range: NSRange(location: 0, length: ns.length)) {
            out[ns.substring(with: m.range(at: 1))] = ns.substring(with: m.range(at: 2))
        }
        return out
    }

    static func num(_ s: String?) -> CGFloat? { s.flatMap { Double($0) }.map { CGFloat($0) } }

    /// "#RRGGBB" → 0xRRGGBB; "none" → nil.
    public static func color(_ s: String) -> UInt32? {
        guard s.hasPrefix("#"), s.count == 7 else { return nil }
        return UInt32(s.dropFirst(), radix: 16)
    }

    static func rotation(_ t: String) -> (deg: CGFloat, cx: CGFloat, cy: CGFloat)? {
        let n = numbers(in: t)
        guard t.contains("rotate"), n.count >= 1 else { return nil }
        return (n[0], n.count > 2 ? n[1] : 0, n.count > 2 ? n[2] : 0)
    }

    static func geometry(_ name: String, _ a: [String: String]) -> CGPath? {
        func v(_ k: String) -> CGFloat { num(a[k]) ?? 0 }
        switch name {
        case "ellipse": return CGPath(ellipseIn: CGRect(x: v("cx") - v("rx"), y: v("cy") - v("ry"), width: 2 * v("rx"), height: 2 * v("ry")), transform: nil)
        case "circle": return CGPath(ellipseIn: CGRect(x: v("cx") - v("r"), y: v("cy") - v("r"), width: 2 * v("r"), height: 2 * v("r")), transform: nil)
        case "rect":
            let r = CGRect(x: v("x"), y: v("y"), width: v("width"), height: v("height"))
            let rx = min(v("rx"), r.width / 2, r.height / 2)
            return CGPath(roundedRect: r, cornerWidth: rx, cornerHeight: rx, transform: nil)
        case "path": return a["d"].map(path)
        default: return nil
        }
    }

    static func numbers(in s: String) -> [CGFloat] {
        let re = try! NSRegularExpression(pattern: #"-?(?:\d+\.?\d*|\.\d+)"#)
        let ns = s as NSString
        return re.matches(in: s, range: NSRange(location: 0, length: ns.length)).compactMap { Double(ns.substring(with: $0.range)).map { CGFloat($0) } }
    }

    /// SVG path data → CGPath. Unit-tested.
    public static func path(_ d: String) -> CGPath {
        let re = try! NSRegularExpression(pattern: #"[MmLlHhVvQqCcAaZz]|-?(?:\d+\.?\d*|\.\d+)"#)
        let ns = d as NSString
        let tokens = re.matches(in: d, range: NSRange(location: 0, length: ns.length)).map { ns.substring(with: $0.range) }
        let p = CGMutablePath()
        var i = 0, cmd = "M"
        var cur = CGPoint.zero, start = CGPoint.zero
        func isNum(_ t: String) -> Bool { Double(t) != nil }
        func next() -> CGFloat {
            defer { i += 1 }
            return i < tokens.count ? CGFloat(Double(tokens[i]) ?? 0) : 0
        }
        while i < tokens.count {
            if !isNum(tokens[i]) { cmd = tokens[i]; i += 1 }
            let rel = cmd == cmd.lowercased()
            func pt() -> CGPoint { let x = next(), y = next(); return rel ? CGPoint(x: cur.x + x, y: cur.y + y) : CGPoint(x: x, y: y) }
            switch cmd.uppercased() {
            case "M":
                cur = pt(); start = cur; p.move(to: cur)
                cmd = rel ? "l" : "L" // further pairs are line-tos
            case "L": cur = pt(); p.addLine(to: cur)
            case "H": let x = next(); cur.x = rel ? cur.x + x : x; p.addLine(to: cur)
            case "V": let y = next(); cur.y = rel ? cur.y + y : y; p.addLine(to: cur)
            case "Q": let c = pt(), e = pt(); p.addQuadCurve(to: e, control: c); cur = e
            case "C": let c1 = pt(), c2 = pt(), e = pt(); p.addCurve(to: e, control1: c1, control2: c2); cur = e
            case "A":
                let rx = abs(next()), ry = abs(next()), phi = next(), large = next() != 0, sweep = next() != 0
                let e = pt()
                arc(p, from: cur, to: e, rx: rx, ry: ry, phi: phi * .pi / 180, large: large, sweep: sweep)
                cur = e
            case "Z":
                p.closeSubpath(); cur = start
                while i < tokens.count, isNum(tokens[i]) { i += 1 }
            default: i += 1 // unknown: skip a token so we never loop forever
            }
        }
        return p
    }

    /// Endpoint arc → centre form (SVG spec F.6.5), drawn as a transformed unit-circle arc.
    static func arc(_ p: CGMutablePath, from a: CGPoint, to b: CGPoint, rx: CGFloat, ry: CGFloat, phi: CGFloat, large: Bool, sweep: Bool) {
        guard rx > 0, ry > 0, a != b else { p.addLine(to: b); return }
        var rx = rx, ry = ry
        let dx = (a.x - b.x) / 2, dy = (a.y - b.y) / 2
        let x1 = cos(phi) * dx + sin(phi) * dy, y1 = -sin(phi) * dx + cos(phi) * dy
        let lambda = x1 * x1 / (rx * rx) + y1 * y1 / (ry * ry)
        if lambda > 1 { rx *= sqrt(lambda); ry *= sqrt(lambda) }
        let num = rx * rx * ry * ry - rx * rx * y1 * y1 - ry * ry * x1 * x1
        let den = rx * rx * y1 * y1 + ry * ry * x1 * x1
        let coef: CGFloat = (large == sweep ? -1 : 1) * sqrt(max(0, num / den))
        let cx1 = coef * rx * y1 / ry, cy1 = -coef * ry * x1 / rx
        let c = CGPoint(x: cos(phi) * cx1 - sin(phi) * cy1 + (a.x + b.x) / 2, y: sin(phi) * cx1 + cos(phi) * cy1 + (a.y + b.y) / 2)
        let t1 = atan2((y1 - cy1) / ry, (x1 - cx1) / rx)
        var dt = atan2((-y1 - cy1) / ry, (-x1 - cx1) / rx) - t1
        if sweep && dt < 0 { dt += 2 * .pi }
        if !sweep && dt > 0 { dt -= 2 * .pi }
        let xf = CGAffineTransform(translationX: c.x, y: c.y).rotated(by: phi).scaledBy(x: rx, y: ry)
        p.addArc(center: .zero, radius: 1, startAngle: t1, endAngle: t1 + dt, clockwise: dt < 0, transform: xf)
    }
}
