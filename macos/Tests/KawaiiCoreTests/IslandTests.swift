import XCTest
@testable import KawaiiCore

final class IslandTests: XCTestCase {
    func testRealNotchSitsBetweenTheMenuBarAreas() {
        // 14" MacBook Pro: 1512 pt wide, ~185 pt notch, 38 pt tall.
        let n = Notch.on(screen: CGRect(x: 0, y: 0, width: 1512, height: 982), leftWidth: 663, rightWidth: 664, safeTop: 38, menuBar: 38)
        XCTAssertEqual(n, Notch(centerX: 663 + 92.5, width: 185, height: 38, isReal: true))
    }

    func testVirtualNotchWithoutCameraHousing() {
        let n = Notch.on(screen: CGRect(x: 1512, y: 0, width: 2560, height: 1440), leftWidth: nil, rightWidth: nil, safeTop: 0, menuBar: 25)
        XCTAssertFalse(n.isReal)
        XCTAssertEqual(n.centerX, 1512 + 1280)
        XCTAssertEqual(n.height, 25)
    }

    func testCallBeatsTimerBeatsMusic() {
        XCTAssertEqual(IslandLayout.arrange([.music, .call, .timer]).primary, .call)
        XCTAssertEqual(IslandLayout.arrange([.music, .call, .timer]).minimal, .timer)
        XCTAssertNil(IslandLayout.arrange([.music]).minimal)
        XCTAssertNil(IslandLayout.arrange([]).primary)
    }

    func testCompactWrapsTheCamera() {
        let n = Notch(centerX: 0, width: 185, height: 38, isReal: true)
        XCTAssertEqual(IslandLayout.size(.resting, notch: n), CGSize(width: 185, height: 38))
        XCTAssertEqual(IslandLayout.size(.compact, notch: n).width, 185 + 2 * IslandLayout.side)
        XCTAssertEqual(IslandLayout.size(.expanded(contentHeight: 120), notch: n), CGSize(width: 480, height: 158))
    }

    func testClock() {
        XCTAssertEqual(Format.clock(65), "1:05")
        XCTAssertEqual(Format.clock(3723), "1:02:03")
        XCTAssertEqual(Format.clock(-3), "0:00")
    }

    func testConfigKeepsDefaultsForMissingKeys() throws {
        let c = try JSONDecoder().decode(Config.self, from: Data(#"{"volume": false}"#.utf8))
        XCTAssertFalse(c.volume)
        XCTAssertTrue(c.calls)
        let dir = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        c.save(to: dir)
        XCTAssertEqual(Config.load(from: dir), c)
    }
}
