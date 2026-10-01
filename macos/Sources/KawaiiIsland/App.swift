import AppKit
import KawaiiCore
import ServiceManagement
import SwiftUI

@main
enum Main {
    @MainActor static func main() {
        let app = NSApplication.shared
        let delegate = AppDelegate()
        app.delegate = delegate
        app.setActivationPolicy(.accessory) // no Dock icon, like a system feature
        app.run()
    }
}

final class AppDelegate: NSObject, NSApplicationDelegate, NSMenuDelegate {
    private let model = IslandModel()
    private var controller: IslandController?
    private var status: NSStatusItem?

    func applicationDidFinishLaunching(_ notification: Notification) {
        controller = IslandController(model: model)
        model.start()
        let item = NSStatusBar.system.statusItem(withLength: NSStatusItem.squareLength)
        item.button?.image = NSImage(systemSymbolName: "capsule.fill", accessibilityDescription: "Kawaii Island")
        let menu = NSMenu()
        menu.delegate = self
        item.menu = menu
        status = item
    }

    private static let toggles: [(String, WritableKeyPath<Config, Bool>)] = [
        ("Music", \.music), ("Calls (FaceTime, Zoom, Teams…)", \.calls), ("Volume", \.volume),
        ("Charging and low battery", \.charging), ("AirPods and Bluetooth", \.bluetooth),
        ("Unlock", \.unlock), ("Caps Lock", \.capsLock),
    ]

    func menuNeedsUpdate(_ menu: NSMenu) {
        menu.removeAllItems()
        let timer = NSMenuItem(title: "Start Timer", action: nil, keyEquivalent: "")
        timer.submenu = NSMenu()
        for m in [1, 5, 10, 25, 60] {
            let i = NSMenuItem(title: "\(m) min", action: #selector(startTimer(_:)), keyEquivalent: "")
            i.target = self; i.tag = m
            timer.submenu?.addItem(i)
        }
        menu.addItem(timer)
        menu.addItem(.separator())
        menu.addItem(check("Expand on Hover", model.config.hoverToExpand, #selector(toggleHover)))
        menu.addItem(withTitle: "Show", action: nil, keyEquivalent: "").isEnabled = false
        for (i, (title, key)) in Self.toggles.enumerated() {
            let item = check(title, model.config[keyPath: key], #selector(toggleSource(_:)))
            item.tag = i
            menu.addItem(item)
        }
        menu.addItem(.separator())
        menu.addItem(check("Open at Login", SMAppService.mainApp.status == .enabled, #selector(toggleLogin)))
        menu.addItem(withTitle: "Quit Kawaii Island", action: #selector(NSApplication.terminate(_:)), keyEquivalent: "q")
    }

    private func check(_ title: String, _ on: Bool, _ action: Selector) -> NSMenuItem {
        let i = NSMenuItem(title: title, action: action, keyEquivalent: "")
        i.target = self
        i.state = on ? .on : .off
        return i
    }

    @objc private func startTimer(_ sender: NSMenuItem) { model.startTimer(minutes: Double(sender.tag)) }
    @objc private func toggleHover() { model.config.hoverToExpand.toggle() }
    @objc private func toggleSource(_ sender: NSMenuItem) { model.config[keyPath: Self.toggles[sender.tag].1].toggle() }

    @objc private func toggleLogin() {
        let s = SMAppService.mainApp
        try? (s.status == .enabled ? s.unregister() : s.register())
    }
}

private final class IslandPanel: NSPanel {
    override var canBecomeKey: Bool { false }
    override var canBecomeMain: Bool { false }
}

private final class FirstMouseHostingView: NSHostingView<IslandView> {
    override func acceptsFirstMouse(for event: NSEvent?) -> Bool { true }
}

/// A transparent, click-through panel over the camera housing. Only the black island itself takes the mouse.
final class IslandController {
    private static let canvas = CGSize(width: 760, height: 320)
    private let model: IslandModel
    private let panel: NSPanel
    private var pending: DispatchWorkItem?

    init(model: IslandModel) {
        self.model = model
        panel = IslandPanel(contentRect: NSRect(origin: .zero, size: Self.canvas), styleMask: [.borderless, .nonactivatingPanel],
                            backing: .buffered, defer: false)
        panel.isOpaque = false
        panel.backgroundColor = .clear
        panel.hasShadow = false
        panel.level = NSWindow.Level(rawValue: NSWindow.Level.mainMenu.rawValue + 3) // above the menu bar
        panel.collectionBehavior = [.canJoinAllSpaces, .stationary, .fullScreenAuxiliary, .ignoresCycle]
        panel.isMovable = false
        panel.ignoresMouseEvents = true
        panel.contentView = FirstMouseHostingView(rootView: IslandView(model: model))
        place()
        panel.orderFrontRegardless()

        NotificationCenter.default.addObserver(forName: NSApplication.didChangeScreenParametersNotification, object: nil, queue: .main) { [weak self] _ in
            self?.place()
        }
        Timer.scheduledTimer(withTimeInterval: 1.0 / 30, repeats: true) { [weak self] _ in self?.trackMouse() }
        // Clicking anywhere else closes the open island, like tapping outside it on iPhone.
        NSEvent.addGlobalMonitorForEvents(matching: [.leftMouseDown, .rightMouseDown]) { [weak self] _ in
            guard let self, self.model.expanded, !self.hitRect.contains(NSEvent.mouseLocation) else { return }
            self.model.setExpanded(false)
        }
    }

    /// The built-in display with the notch, else the main display (virtual notch).
    private func place() {
        guard let s = NSScreen.screens.first(where: { $0.auxiliaryTopLeftArea != nil }) ?? NSScreen.main ?? NSScreen.screens.first else { return }
        model.notch = Notch.on(screen: s.frame, leftWidth: s.auxiliaryTopLeftArea?.width, rightWidth: s.auxiliaryTopRightArea?.width,
                               safeTop: s.safeAreaInsets.top, menuBar: s.frame.maxY - s.visibleFrame.maxY)
        panel.setFrame(NSRect(x: model.notch.centerX - Self.canvas.width / 2, y: s.frame.maxY - Self.canvas.height,
                              width: Self.canvas.width, height: Self.canvas.height), display: true)
    }

    private var hitRect: NSRect {
        let size = model.size, f = panel.frame
        var r = NSRect(x: f.midX - size.width / 2, y: f.maxY - size.height, width: size.width, height: size.height)
        if model.showsMinimal { r.size.width += model.notch.height + 10 }
        return r.insetBy(dx: -4, dy: -4)
    }

    private func trackMouse() {
        let inside = hitRect.contains(NSEvent.mouseLocation)
        panel.ignoresMouseEvents = !inside
        guard inside != model.hovering else { return }
        model.hovering = inside
        pending?.cancel()
        let work: DispatchWorkItem
        if inside {
            guard model.config.hoverToExpand else { return }
            work = DispatchWorkItem { [weak self] in if self?.model.hovering == true { self?.model.setExpanded(true) } }
            DispatchQueue.main.asyncAfter(deadline: .now() + 0.25, execute: work)
        } else {
            guard model.expanded else { return }
            work = DispatchWorkItem { [weak self] in if self?.model.hovering == false { self?.model.setExpanded(false) } }
            DispatchQueue.main.asyncAfter(deadline: .now() + 0.8, execute: work)
        }
        pending = work
    }
}
