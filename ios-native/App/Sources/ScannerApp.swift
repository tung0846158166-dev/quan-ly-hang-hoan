import SwiftUI

@main
struct ScannerApp: App {
    @Environment(\.scenePhase) private var scenePhase
    @UIApplicationDelegateAdaptor(AppDelegate.self) private var appDelegate
    @StateObject private var engine = SyncEngine()
    @StateObject private var router = NotificationRouter.shared

    var body: some Scene {
        WindowGroup {
            ContentView()
                .environmentObject(engine)
                .environmentObject(engine.snapshot)
                .environmentObject(router)
                .task { router.store = engine.snapshot; engine.start(); engine.snapshot.refreshAuthorization() }
                .onChange(of: scenePhase) { phase in if phase != .active { engine.flushPersist() } }
        }
    }
}

struct ContentView: View {
    @EnvironmentObject var engine: SyncEngine
    @EnvironmentObject var store: SnapshotStore
    @EnvironmentObject var router: NotificationRouter

    var body: some View {
        TabView(selection: $router.tab) {
            ScanView()
                .tabItem { Label("Quét", systemImage: "barcode.viewfinder") }
                .tag(0)
            OrdersView()
                .tabItem { Label("Đơn", systemImage: "shippingbox") }
                .tag(1)
            NotificationsView()
                .tabItem { Label("Thông báo", systemImage: "bell") }
                .badge(store.alertCount)
                .tag(2)
            HistoryView()
                .tabItem { Label("Lịch sử", systemImage: "clock.arrow.circlepath") }
                .badge(engine.pendingCount)
                .tag(3)
            SettingsView()
                .tabItem { Label("Cài đặt", systemImage: "gearshape") }
                .tag(4)
        }
    }
}
