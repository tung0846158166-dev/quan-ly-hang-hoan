import SwiftUI

@main
struct ScannerApp: App {
    @StateObject private var engine = SyncEngine()

    var body: some Scene {
        WindowGroup {
            ContentView()
                .environmentObject(engine)
                .environmentObject(engine.snapshot)
                .task { engine.start(); engine.snapshot.refreshAuthorization() }
        }
    }
}

struct ContentView: View {
    @EnvironmentObject var engine: SyncEngine

    var body: some View {
        TabView {
            ScanView()
                .tabItem { Label("Quét", systemImage: "barcode.viewfinder") }
            OrdersView()
                .tabItem { Label("Đơn", systemImage: "shippingbox") }
                .badge(engine.snapshot.overdueCount)
            HistoryView()
                .tabItem { Label("Lịch sử", systemImage: "clock.arrow.circlepath") }
                .badge(engine.pendingCount)
            SettingsView()
                .tabItem { Label("Cài đặt", systemImage: "gearshape") }
        }
    }
}
