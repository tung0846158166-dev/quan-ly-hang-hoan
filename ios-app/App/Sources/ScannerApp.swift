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
                .environmentObject(engine.photoManager)
                .environmentObject(router)
                .task { router.store = engine.snapshot; engine.start(); engine.snapshot.refreshAuthorization() }
                .onChange(of: scenePhase) { phase in
                    if phase != .active {
                        engine.flushPersist(background: true)
                    } else {
                        engine.handleForeground()
                    }
                }
        }
    }
}

struct ContentView: View {
    @EnvironmentObject var engine: SyncEngine
    @EnvironmentObject var store: SnapshotStore
    @EnvironmentObject var router: NotificationRouter
    @StateObject private var aiVM = AIChatViewModel()
    @State private var showAI = false

    var body: some View {
        VStack(spacing: 0) {
            if store.isDemoMode {
                DemoModeBanner { engine.setDemoMode(false) }
            }
            if engine.cloudActive {
                // kết nối bình thường: không hiện thanh (Tổng quan đã có nhãn trạng thái); có vấn đề thì cảnh báo ở mọi tab
                if engine.cloudState != .connected && engine.cloudState != .syncing {
                    CloudStatusBar()
                }
                CloudReadOnlyBanner()
            }

            TabView(selection: $router.tab) {
                HomeView()
                    .tabItem { Label("Tổng quan", systemImage: "house.fill") }
                    .tag(NotificationRouter.homeTab)
                ScanView()
                    .tabItem { Label("Quét", systemImage: "barcode.viewfinder") }
                    .tag(NotificationRouter.scanTab)
                OrdersView()
                    .tabItem { Label("Đơn", systemImage: "shippingbox.fill") }
                    .badge(store.overdueCount)
                    .tag(NotificationRouter.ordersTab)
                HistoryView()
                    .tabItem { Label("Lịch sử", systemImage: "clock.arrow.circlepath") }
                    .badge(engine.pendingCount)
                    .tag(NotificationRouter.historyTab)
                SettingsView()
                    .tabItem { Label("Cài đặt", systemImage: "gearshape.fill") }
                    .tag(NotificationRouter.settingsTab)
            }
            .tint(Theme.accent)
            .overlay(alignment: .bottomTrailing) {
                // Nút ✨ hỏi AI: có ở mọi tab, nằm trên thanh tab.
                AIFloatingButton { Haptics.tap(); showAI = true }
                    .padding(.trailing, 16)
                    .padding(.bottom, 62)
            }
        }
        .environmentObject(aiVM)
        .onAppear { aiVM.bind(store); aiVM.bindEngine(engine) }
        .sheet(isPresented: $showAI) {
            // Cửa sổ chat dạng popup: kéo xuống để đóng; cuộc trò chuyện giữ nguyên khi đổi tab.
            AIChatView()
                .environmentObject(aiVM)
                .environmentObject(engine)
                .environmentObject(store)
                .environmentObject(engine.photoManager)
                .environmentObject(router)
                .presentationDetents([.medium, .large])
                .presentationDragIndicator(.visible)
        }
    }
}
