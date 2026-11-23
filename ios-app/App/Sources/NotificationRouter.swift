import SwiftUI
import UserNotifications
import ScannerCore

/// Điều hướng toàn app: tab mặc định là Tổng quan (0), nhảy lọc sang màn Đơn, và mở chi tiết từ thông báo.
@MainActor
final class NotificationRouter: ObservableObject {
    static let shared = NotificationRouter()
    static let homeTab = 0
    static let scanTab = 1
    static let ordersTab = 2
    static let historyTab = 3
    static let settingsTab = 4

    @Published var tab = 0
    @Published var openRow: OrderRow?
    @Published var showNotifications = false
    @Published var pendingOrdersFilter: OrderFilter?
    @Published var pendingOrdersPlatform: String?
    private var pendingRowId: String?

    var store: SnapshotStore? { didSet { resolvePending() } }

    /// Mở tab Đơn với bộ lọc và/hoặc sàn chỉ định từ màn Tổng quan.
    func openOrders(filter: OrderFilter, platform: String? = nil) {
        pendingOrdersFilter = filter
        pendingOrdersPlatform = platform
        tab = Self.ordersTab
    }

    /// `rowId` rỗng (thông báo tóm tắt hằng ngày) thì mở màn Thông báo; có `rowId` thì mở luôn chi tiết đơn.
    func handle(rowId: String?) {
        tab = Self.homeTab
        showNotifications = true
        pendingRowId = rowId
        resolvePending()
    }

    private func resolvePending() {
        guard let rid = pendingRowId, let store = store else { return }
        if let r = store.rows.first(where: { $0.id == rid }) { openRow = r; pendingRowId = nil }
        else if !store.rows.isEmpty { pendingRowId = nil }   // đơn không còn trong danh sách
    }
}

final class AppDelegate: NSObject, UIApplicationDelegate, UNUserNotificationCenterDelegate {
    func application(_ application: UIApplication, didFinishLaunchingWithOptions launchOptions: [UIApplication.LaunchOptionsKey: Any]? = nil) -> Bool {
        UNUserNotificationCenter.current().delegate = self
        return true
    }

    /// Đang mở app vẫn hiện banner.
    func userNotificationCenter(_ center: UNUserNotificationCenter, willPresent notification: UNNotification,
                                withCompletionHandler completionHandler: @escaping (UNNotificationPresentationOptions) -> Void) {
        completionHandler([.banner, .list, .sound])
    }

    func userNotificationCenter(_ center: UNUserNotificationCenter, didReceive response: UNNotificationResponse,
                                withCompletionHandler completionHandler: @escaping () -> Void) {
        let rid = response.notification.request.content.userInfo["rowId"] as? String
        Task { @MainActor in NotificationRouter.shared.handle(rowId: rid) }
        completionHandler()
    }
}
