# Báo cáo T031 - App iPhone: giao diện hiện đại + màn "Tổng quan" mở đầu

## 1. Phạm vi & tệp mới / sửa
Chỉ sửa trong `ios-app/` (và cập nhật báo cáo + `TASKS.md`). Không đụng `ios-native/`, `v2/src/QuanLyHangHoan.Core/`, `v2/src/QuanLyHangHoan.Data/` hay các tệp test ngoài phạm vi.

### Tệp mới
- `ios-app/ScannerCore/Sources/ScannerCore/DashboardMetrics.swift`: Logic thuần tính số liệu màn Tổng quan (`DashboardMetrics`, `DashboardSummary`, `ReturnWatchSummary`, `UrgentOrder`, `PlatformSummary`, `DailyReceiveCount`): lời chào theo giờ, đếm Đang chờ / Đã nhận hôm nay / Quá hạn / Sắp đến hạn, Hàng hoàn về 4 chặng, top 5 đơn cần xử lý ngay (đỏ trước rồi vàng, xếp theo hạn gần nhất), thống kê theo sàn Shopee / TikTok / Lazada, đếm Hoàn một phần (`rn` không rỗng), dãy 7 ngày gần nhất (ẩn khi không có dữ liệu, không chia cho 0), cảnh báo lệch số đơn từ `stats` (`dropped` / lệch `sent`).
- `ios-app/ScannerCore/Tests/ScannerCoreTests/DashboardMetricsTests.swift`: 8 test XCTest (dữ liệu GIẢ) kiểm thử toàn bộ hàm thuần ở `DashboardMetrics`, gồm dữ liệu rỗng không chia cho 0.
- `ios-app/App/Sources/HomeView.swift`: Màn "Tổng quan" mở đầu.

### Tệp sửa
- `ios-app/App/Sources/Theme.swift`: Bộ giao diện v2 hiện đại — nhấn cam thương hiệu `Theme.accent`, bảng màu ngữ nghĩa (`danger`, `warning`, `success`, `info`, `teal`), thẻ bo góc 18pt nổi nhẹ kèm viền mảnh thích ứng sáng/tối, `AnimatedCounter` (tôn trọng `accessibilityReduceMotion`, dùng `.contentTransition(.numericText)` có `#available(iOS 17.0, *)`), `Haptics`, `PlatformBadge`, `StatusPill`, `SectionTitleRow`, nâng cấp `StatTile`, `Chip`, `ModePicker` (bỏ tham chiếu `.cancelOrder`).
- `ios-app/App/Sources/ScannerApp.swift`: Đưa **Tổng quan** (`HomeView`) làm tab đầu tiên (mặc định khi mở app, badge `store.alertCount`), giữ 5 tab gọn gàng cho iPhone (`Tổng quan`, `Quét`, `Đơn`, `Lịch sử`, `Cài đặt`) để không bị gom vào tab "More".
- `ios-app/App/Sources/NotificationRouter.swift`: Cập nhật chỉ số tab, thêm `openOrders(filter:platform:)` để bấm thẻ ở Tổng quan nhảy thẳng sang màn Đơn đã lọc, và `showNotifications` để mở màn Thông báo từ nút chuông ở đầu trang Tổng quan hoặc từ thông báo cục bộ.
- `ios-app/App/Sources/OrdersView.swift`: Thêm bộ lọc `Hoàn một phần` (`DashboardMetrics.isPartialReturn`), nhận bộ lọc/sàn truyền từ Tổng quan, làm mới thẻ lối vào bảng phụ và hàng đơn dạng thẻ (huy hiệu sàn, chấm màu cảnh báo, tô màu hoàn một phần qua `ProductLines`).
- `ios-app/App/Sources/OrderTablesView.swift`: Xóa `InboxView` đã bỏ ở v2; làm mới giao diện thẻ cho `CancelledTableView`, `CancelAfterPrepView`, `ReturnWatchView` (thêm chip lọc đủ 4 chặng + thanh tiến trình 4 chặng).
- `ios-app/App/Sources/NotificationsView.swift`: Làm mới danh sách thông báo dạng thẻ nổi (vẫn giữ thao tác vuốt để ẩn), bỏ `NavigationStack` lồng nhau khi đẩy từ Tổng quan.
- `ios-app/App/Sources/HistoryView.swift`: Bỏ tham chiếu `.cancelOrder` thừa, làm mới ô số liệu hôm nay (`AnimatedCounter`) và thẻ lịch sử quét.
- `ios-app/App/Sources/SettingsView.swift`: Thay `Form` mặc định bằng `ScrollView` + các nhóm thẻ nổi (Trạng thái, Mã phòng có nút Dán/Sao chép/Lưu, Trạng thái kết nối, Dữ liệu & thông báo, Đối chiếu với máy tính, Tùy chọn quét, Giới thiệu); bỏ nhắc `SHP-2026`.
- `ios-app/App/Sources/SnapshotStore.swift` & `ios-app/App/Sources/SyncEngine.swift`: Gỡ các trường/hàm chết `inbox` / `removeInboxLocal` / `pushInbox` / `pushToMain` trong `App/Sources/`.

## 2. Mô tả từng khối của màn "Tổng quan" (`HomeView`)
1. **Đầu trang**: Lời chào theo khung giờ (`Chào buổi sáng/trưa/chiều/tối`), giờ cập nhật dữ liệu gần nhất + tổng số đơn, viên thuốc trạng thái trực tiếp (`PC đang online` / `PC offline` / `Chưa kết nối`), nút làm mới và nút chuông mở Thông báo. Hỗ trợ kéo xuống (`.refreshable`) để gửi `snapshot_request`.
2. **Trạng thái rỗng thân thiện**: Khi chưa nhập mã phòng hiển thị hướng dẫn 2 bước rõ ràng + nút lớn **Đi tới Cài đặt**; khi đã có mã phòng nhưng chưa có dữ liệu hiển thị trạng thái ghép + nút **Xin lại danh sách**.
3. **Cảnh báo lệch đơn**: Tự hiện khi `stats["dropped"] > 0` hoặc `sent != rows.count`, kèm nút tải lại danh sách và lối tắt xem bảng đối chiếu ở Cài đặt.
4. **4 thẻ số lớn (lưới 2×2)**: **Đang chờ về**, **Đã nhận hôm nay**, **Quá hạn (đỏ)**, **Sắp đến hạn (vàng)** — bấm vào nhảy sang tab **Đơn** với đúng bộ lọc.
5. **Hàng hoàn về (đường ống 4 chặng)**: Hiển thị số đếm từng chặng `Đã đồng ý -> Đang về -> Về shop -> Nhận kho` từ `rw`/`rws`/`rv`; bấm mở thẳng `ReturnWatchView`.
6. **Cần xử lý ngay**: Tối đa 5 đơn ưu tiên đỏ trước rồi vàng, sắp theo hạn gần nhất (huy hiệu sàn, mã vận đơn, sản phẩm, nhãn quá/còn bao lâu); bấm mở `DetailView`.
7. **Theo sàn**: Shopee / TikTok / Lazada với thanh ngang tỉ lệ số đơn chờ + huy hiệu số quá hạn; bấm mở màn Đơn đã lọc đúng sàn.
8. **Hoàn một phần**: Đếm đơn có `rn` không rỗng; bấm mở màn Đơn đã lọc `Hoàn một phần`.
9. **7 ngày gần nhất**: Biểu đồ cột thuần SwiftUI (`HStack`/`Capsule`) theo ngày nhận `rcd`; tự ẩn khi không có dữ liệu nhận trong 7 ngày, không bịa số. Kèm lối tắt mở `StatsView`.
10. **Nút "Quét ngay" ở cạnh dưới**: Nút lớn cam thương hiệu trong `.safeAreaInset(edge: .bottom)` dễ bấm một tay, chuyển thẳng sang tab **Quét**.

## 3. Kiểm tra tĩnh & lưu ý về `ScannerCore`
- Đã kiểm tra `ios-app/App/`: **0 tham chiếu** tới `Inbox`, `inbox_push`, `cancelOrder`, `SHP-2026`.
- Đã giữ nguyên các tệp cũ trong `ios-app/ScannerCore/` (theo quy định mục 5 của phiếu, không phá các test giải mã dữ liệu cũ hiện có trong `ScannerCoreTests`).
- Đã kiểm tra cân bằng ngoặc `{}`, `()`, `[]` trên toàn bộ 13 tệp Swift mới/sửa và kiểm tra API iOS 16.0 (mọi API iOS 17 như `.contentTransition(.numericText)` và `.snappy` đều được bọc trong `if #available(iOS 17.0, *)`).
- Không thiếu API nào ở `v2/src/QuanLyHangHoan.Data/` (toàn bộ trường `rn`, `rw`, `rws`, `rv`, `rcd`, `lv`, `due`, `stats` đều đã có sẵn trong `PhoneSnapshot.cs`).

## 4. CHƯA BIÊN DỊCH (không có Swift trên máy này)
- Máy Windows hiện tại không có Swift/Xcode nên **chưa chạy `swift test` và chưa build IPA**.
- Cần Claude kiểm tra và đẩy lên GitHub để workflow `Build iPhone App v2` chạy thật `swift test` và biên dịch IPA.

## 5. Việc cần kiểm trên iPhone thật
1. Mở app lần đầu (chưa có mã phòng): kiểm tra màn Tổng quan hiện hướng dẫn 2 bước, bấm "Đi tới Cài đặt", dán mã phòng từ PC v2 và lưu.
2. Sau khi đồng bộ dữ liệu thật: kiểm tra 4 thẻ số lớn, bấm từng thẻ xem tab Đơn lọc đúng; bấm "Hoàn một phần" xem danh sách lọc đúng đơn có `rn` và sản phẩm hoàn tô cam.
3. Bấm khối "Hàng hoàn về" mở màn 4 chặng; bấm 1 đơn trong "Cần xử lý ngay" mở Chi tiết đơn; bấm từng sàn trong "Theo sàn" mở Đơn đã lọc đúng sàn.
4. Kiểm tra biểu đồ 7 ngày gần nhất (hiện đúng số ngày có đơn nhận, ẩn khi rỗng), kéo xuống làm mới (`snapshot_request`), bấm nút "Quét ngay" chuyển sang tab Quét và quét thử kiện thật.
5. Kiểm tra chế độ Tối (Dark Mode), cỡ chữ lớn (Dynamic Type) và tùy chọn Giảm chuyển động (Reduce Motion).

TRẠNG THÁI: XONG
