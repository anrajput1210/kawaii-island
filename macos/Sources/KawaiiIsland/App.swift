import AppKit
import Combine
import KawaiiCore
import ServiceManagement
import SwiftUI

@main
enum Main {
    @MainActor static func main() {
        let args = CommandLine.arguments
        // `kawaii-island uninstall` (npm): remove exactly our Claude Code hooks, then quit.
        if args.contains("--uninstall-hooks") { _ = try? ClaudeSettings.apply(false, port: 47811); return }
        // `kawaii-island install` (npm): open at login by default, like "Start with Windows".
        if args.contains("--enable-login") { try? SMAppService.mainApp.register() }
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
    private var hotKey: HotKey?

    func applicationDidFinishLaunching(_ notification: Notification) {
        controller = IslandController(model: model)
        model.start()
        hotKey = HotKey { [weak self] in self?.model.toggleExpanded() } // Ctrl+Option+I, like Ctrl+Alt+I on Windows
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

        let mascot = NSMenuItem(title: "Mascot", action: nil, keyEquivalent: "")
        mascot.submenu = NSMenu()
        for (i, key) in (MascotArt.order + ["none"]).enumerated() {
            let item = check(key == "none" ? "No mascot" : MascotArt.label(key), model.config.mascot == key, #selector(pickMascot(_:)))
            item.tag = i
            mascot.submenu?.addItem(item)
        }
        menu.addItem(mascot)
        menu.addItem(check("Coding mode", model.config.codeMode, #selector(toggleCode)))
        menu.addItem(check("Connect Claude Code", claudeConnected, #selector(toggleClaude)))
        let city = NSMenuItem(title: model.config.city.isEmpty ? "Set Weather City…" : "Weather: \(model.config.city)…", action: #selector(setCity), keyEquivalent: "")
        city.target = self
        menu.addItem(city)
        menu.addItem(check("Fahrenheit", model.config.fahrenheit, #selector(toggleFahrenheit)))
        menu.addItem(check("Calendar Tab", model.config.calendar, #selector(toggleCalendar)))
        menu.addItem(check("System Tab", model.config.system, #selector(toggleSystem)))
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
    @objc private func pickMascot(_ sender: NSMenuItem) { model.config.mascot = (MascotArt.order + ["none"])[sender.tag] }
    @objc private func toggleFahrenheit() { model.config.fahrenheit.toggle(); model.refreshWeather() }
    @objc private func toggleCalendar() { model.config.calendar.toggle() }
    @objc private func toggleSystem() { model.config.system.toggle() }

    @objc private func toggleCode() {
        if model.config.codeMode { model.stopCodeMode() }
        else if let error = model.startCodeMode() { alert("Coding mode", error) }
    }

    private var claudeConnected: Bool {
        guard let data = try? Data(contentsOf: ClaudeSettings.path),
              let obj = try? JSONSerialization.jsonObject(with: data) as? [String: Any] else { return false }
        return ClaudeSettings.isInstalled(obj)
    }

    /// Adds (or removes) our hooks in ~/.claude/settings.json, exactly like Settings → AI agents on Windows.
    @objc private func toggleClaude() {
        let installed = claudeConnected
        if !installed {
            let a = NSAlert()
            a.messageText = "Connect Claude Code?"
            a.informativeText = "Kawaii Island adds hooks (and a status line, if you have none) to ~/.claude/settings.json so the island shows what Claude is doing and lets you Allow or Deny. A backup is kept. Turning this off removes exactly those entries."
            a.addButton(withTitle: "Connect")
            a.addButton(withTitle: "Cancel")
            NSApp.activate(ignoringOtherApps: true)
            guard a.runModal() == .alertFirstButtonReturn else { return }
        }
        do {
            try ClaudeSettings.apply(!installed, port: model.config.codePort)
            if !installed && !model.config.codeMode { model.startCodeMode() }
        } catch {
            alert("Claude Code", "Couldn't update ~/.claude/settings.json (\(error.localizedDescription)). It was left untouched.")
        }
    }

    @objc private func setCity() {
        let a = NSAlert()
        a.messageText = "Weather city"
        a.informativeText = "Weather comes from Open-Meteo (no account). Only the city name is sent."
        let field = NSTextField(string: model.config.city)
        field.frame = NSRect(x: 0, y: 0, width: 240, height: 24)
        a.accessoryView = field
        a.addButton(withTitle: "Save")
        a.addButton(withTitle: "Cancel")
        NSApp.activate(ignoringOtherApps: true)
        a.window.initialFirstResponder = field
        guard a.runModal() == .alertFirstButtonReturn else { return }
        model.config.city = field.stringValue.trimmingCharacters(in: .whitespaces)
        model.refreshWeather()
    }

    private func alert(_ title: String, _ text: String) {
        let a = NSAlert()
        a.messageText = title
        a.informativeText = text
        NSApp.activate(ignoringOtherApps: true)
        a.runModal()
    }
    @objc private func toggleHover() { model.config.hoverToExpand.toggle() }
    @objc private func toggleSource(_ sender: NSMenuItem) { model.config[keyPath: Self.toggles[sender.tag].1].toggle() }

    @objc private func toggleLogin() {
        let s = SMAppService.mainApp
        try? (s.status == .enabled ? s.unregister() : s.register())
    }
}

private final class IslandPanel: NSPanel {
    var allowsKey = false
    override var canBecomeKey: Bool { allowsKey }
    override var canBecomeMain: Bool { false }
}

private final class FirstMouseHostingView: NSHostingView<IslandView> {
    override func acceptsFirstMouse(for event: NSEvent?) -> Bool { true }
}

/// A transparent, click-through panel over the camera housing. Only the black island itself takes the mouse.
final class IslandController {
    private static let canvas = CGSize(width: 760, height: 480)  // room for the tallest view (Add apps)
    private let model: IslandModel
    private let panel: IslandPanel
    private var picking: AnyCancellable?
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
        picking = model.$picking.sink { [weak self] on in
            guard let self else { return }
            self.panel.allowsKey = on
            if on { self.panel.makeKey() } else { self.panel.resignKey() }
        }

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
