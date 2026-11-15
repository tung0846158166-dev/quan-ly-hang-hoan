import SwiftUI

@main
struct ScannerApp: App {
    @StateObject private var engine = SyncEngine()

    var body: some Scene {
        WindowGroup {
            ContentView()
                .environmentObject(engine)
                .task { engine.start() }
        }
    }
}

struct ContentView: View {
    @EnvironmentObject var engine: SyncEngine

    var body: some View {
        TabView {
            ScanView()
                .tabItem { Label("Quét", systemImage: "barcode.viewfinder") }
            HistoryView()
                .tabItem { Label("Lịch sử", systemImage: "clock.arrow.circlepath") }
                .badge(engine.pendingCount)
            SettingsView()
                .tabItem { Label("Cài đặt", systemImage: "gearshape") }
        }
    }
}
