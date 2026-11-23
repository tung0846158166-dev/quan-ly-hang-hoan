# Báo cáo T032 - App iPhone: làm lại giao diện lần 2 (thật sự hiện đại) + chế độ dữ liệu mẫu

## 1. Phạm vi & tệp mới / sửa
Chỉ sửa trong `ios-app/` (cùng báo cáo này và `TASKS.md`). Không đụng `v2/src/QuanLyHangHoan.Data/`, `v2/src/QuanLyHangHoan.Core/`, hay bất kỳ tệp test nào ngoài `ios-app/`. Không thiếu API nào ở `v2/src/QuanLyHangHoan.Data/`.

### Tệp mới
- `ios-app/ScannerCore/Sources/ScannerCore/DemoData.swift`: Tạo 60 dòng `OrderRow` GIẢ (`DEMO-SPX-*`, `DEMO-TT-*`, `DEMO-LZ-*`, `DEMO-CXL-*`, `DEMO-ARC-*`, mã đơn `DHM-*`) đủ mọi trường hợp (3 sàn Shopee/TikTok/Lazada, chưa nhận/đã nhận `rcd` rải đều 7 ngày, 7 đơn quá hạn đỏ, 8 đơn sắp đến hạn vàng, hoàn một phần có `rn`, đang theo dõi `wl`/`wr`, Hàng hoàn về đủ 4 chặng `rw`/`rws`/`ar`/`rv`, đơn Hủy sau chuẩn bị `ps`, 8 đơn hủy `k == "c"`, 6 đơn lưu trữ `k == "a"`), kèm `stats` đối chiếu khớp 100% và 10 mục lịch sử quét giả `DemoData.scanItems()` rải qua Hôm nay / Hôm qua / 2 ngày trước.
- `ios-app/ScannerCore/Tests/ScannerCoreTests/DemoDataTests.swift`: 3 test XCTest kiểm tra số lượng từng nhóm, 4 chặng Hàng hoàn về, dãy 7 ngày, đối chiếu `stats`, giải mã Codable round-trip, tiền tố mã giả, và phân nhóm ngày lịch sử quét.

### Tệp sửa
- `ios-app/ScannerCore/Sources/ScannerCore/DashboardMetrics.swift`: Thêm `todayTargetCount`, `todayProgress`, `average7Days`, `average7DaysRatio` vào `DashboardSummary`, và hàm thuần `DashboardMetrics.daySectionTitle(for:now:calendar:)` để nhóm lịch sử theo "Hôm nay" / "Hôm qua" / "dd/MM/yyyy".
- `ios-app/ScannerCore/Tests/ScannerCoreTests/DashboardMetricsTests.swift`: Thêm test cho `todayProgress`, `average7Days`, `average7DaysRatio` và `daySectionTitle`.
- `ios-app/App/Sources/Theme.swift`: Nền tối sâu (`Theme.pageBackground`), thẻ mờ `.ultraThinMaterial` + viền mảnh (`CardModifier`), biểu tượng SF Symbol trong ô vuông bo màu (`IconTile`), dải báo chế độ mẫu (`DemoModeBanner`), ô lọc có số đếm (`FilterTile`), `ModePicker` dạng phân đoạn gọn, helper snap ngang bọc `#available(iOS 17.0, *)`.
- `ios-app/App/Sources/SnapshotStore.swift`: Thêm cờ `isDemoMode` trong bộ nhớ, lưu tạm dữ liệu thật (`savedRealRows`/`savedRealStats`/`savedRealUpdatedAt`/`savedRealDismissed`), khôi phục nguyên vẹn khi tắt, chặn ghi đĩa & lên lịch thông báo khi đang ở chế độ mẫu.
- `ios-app/App/Sources/SyncEngine.swift`: Thêm `isDemoMode` + `setDemoMode(_:)`, chặn tuyệt đối mọi thao tác phát MQTT/ntfy (`broadcast`, `transmit`, `transmitPhoto`, `requestSnapshot`, `resend*`, `tick`) và ghi đĩa (`persist`, `flushPersist`) khi bật dữ liệu mẫu; các thao tác quét / sửa trạng thái / ghi chú / xóa chỉ chạy cục bộ trên dữ liệu mẫu; đổi toàn bộ thông báo kết quả quét sang chữ thường dễ đọc (không còn CHỮ HOA dài).
- `ios-app/App/Sources/ScannerApp.swift`: Gắn `DemoModeBanner` ("DỮ LIỆU MẪU, không gửi gì cho máy tính") ở `.safeAreaInset(edge: .top)` để luôn hiện rõ ở đầu mọi màn hình khi bật chế độ mẫu.
- `ios-app/App/Sources/HomeView.swift`: Thiết kế lại toàn bộ bố cục Tổng quan.
- `ios-app/App/Sources/ScanView.swift`: Thiết kế lại bố cục Quét.
- `ios-app/App/Sources/OrdersView.swift`: Thiết kế lại bố cục Đơn hàng & thêm nút xem thử ở trạng thái rỗng.
- `ios-app/App/Sources/OrderTablesView.swift`: Thiết kế lại `ReturnWatchView` (đường ống 4 chặng bấm lọc + thanh 4 điểm).
- `ios-app/App/Sources/HistoryView.swift`: Thiết kế lại Lịch sử (1 thẻ số liệu hôm nay + nhóm theo ngày + dòng gọn có biểu tượng).
- `ios-app/App/Sources/SettingsView.swift`: Thiết kế lại Cài đặt kiểu `insetGrouped`, thêm công tắc dữ liệu mẫu, tách bảng Đối chiếu sang màn con `ReconcileDetailView`, sửa triệt để lỗi đè chữ tiêu đề.

## 2. Từng màn hình đã đổi gì (Trước → Sau)
1. **Tổng quan (`HomeView`)**:
   - *Trước*: Có nút chữ `"Tổng quan"` ở góc trái trên bị cắt thành `"Tổ…"`; 4 ô KPI 2×2 kiểu cũ xếp dọc dài; trạng thái rỗng chỉ hiện hướng dẫn.
   - *Sau*: Dùng `.navigationTitle("Tổng quan")` + `.large` (hết lỗi `"Tổ…"`), thanh trên chỉ có nút làm mới và nút chuông. Khối **Hero** có gradient cam nhạt, tiêu đề `"Hôm nay"` + ngày, vòng tiến độ `"Đã nhận X / Cần nhận Y"` và 2 chỉ số phụ (Quá hạn / Sắp hạn) bên phải. Dải **thẻ cuộn ngang** (snap iOS 17+) gồm Hàng hoàn về (thanh 4 đoạn liền + số), Theo sàn (3 ô nhỏ Shopee/TikTok/Lazada), Hoàn một phần. Danh sách **Cần xử lý ngay** có thanh màu trái (đỏ/vàng) + hỗ trợ vuốt phải **Ghi chú**, vuốt trái **Theo dõi**. Biểu đồ **7 ngày** có số trên cột và đường trung bình nét đứt. Trạng thái rỗng có nút nổi bật **"Xem thử với dữ liệu mẫu"**.
2. **Quét (`ScanView`)**:
   - *Trước*: Khung camera chiếm gần hết màn; hai biểu tượng gạch chéo không nhãn khó hiểu; banner kết quả toàn CHỮ HOA dài nằm cứng dưới khung.
   - *Sau*: Bộ chọn phân đoạn nhỏ `"Hoàn kho / Đơn mới"` ở trên; trạng thái kết nối rút gọn thành **1 chấm màu + chữ** (`"Đã nối PC"` / `"Mất kết nối PC"` / `"Chế độ dữ liệu mẫu"`) kèm `"Đã quét hôm nay: N"`. Camera chiếm ~54% chiều cao màn hình, góc bo 24pt. Kết quả là **thẻ trượt từ dưới lên** có biểu tượng lớn, mã vận đơn đậm, giải thích chữ thường dễ đọc, tự thu gọn sau 4,5 giây. Hàng điều khiển bên dưới là 4 nút tròn có nhãn nhỏ (Đèn, Zoom, Tạm dừng, Dán mã).
3. **Đơn (`OrdersView`)**:
   - *Trước*: Hàng chip bo tròn dày đặc, nút bảng phụ dạng chữ dày, trạng thái rỗng đơn điệu.
   - *Sau*: Hàng lối tắt bảng phụ (Hoàn về, Đơn hủy, Sau CB) gọn với `IconTile` + số; một hàng `FilterTile` cuộn ngang có số đếm; thẻ đơn rộng rãi với dòng trên (huy hiệu sàn + hạn tô màu), mã vận đơn đậm, sản phẩm (giữ nguyên tô màu hoàn một phần `ProductLines`), chân thẻ hiện 4 chấm chặng Hàng hoàn về hoặc trạng thái. Trạng thái rỗng có nút **"Xem thử với dữ liệu mẫu"**.
4. **Hàng hoàn về (`ReturnWatchView`)**:
   - *Trước*: Dãy chip ngang + thanh 4 đoạn dạng viên thuốc.
   - *Sau*: Đường ống 4 chặng lớn ở đầu màn (bấm từng chặng để lọc, bấm lại về Đang về); mỗi thẻ đơn có **thanh tiến trình 4 điểm nối liền**.
5. **Lịch sử (`HistoryView`)**:
   - *Trước*: 4 thẻ số liệu rời nhau, danh sách phẳng không phân ngày.
   - *Sau*: 4 số liệu hôm nay gom gọn vào **1 thẻ duy nhất**; danh sách **nhóm theo ngày** (`"Hôm nay"`, `"Hôm qua"`, `"dd/MM/yyyy"`); mỗi dòng gọn có ô biểu tượng màu theo kết quả.
6. **Cài đặt (`SettingsView`)**:
   - *Trước*: `ScrollView` tự xếp thẻ bị tiêu đề lớn `"Cài đặt"` đè chữ mờ lên thẻ đầu; danh sách đối chiếu 12 dòng dài dày đặc.
   - *Sau*: Dùng `List` với `.listStyle(.insetGrouped)` và `.navigationBarTitleDisplayMode(.large)` (sửa triệt để lỗi đè tiêu đề), mỗi dòng có `IconTile` màu bên trái (Chế độ xem thử, Mã phòng có Dán/Sao chép, Kết nối với chấm màu + chữ, Dữ liệu + lối vào màn Đối chiếu riêng, Thông báo, Quét, Giới thiệu).

## 3. Cách bật chế độ dữ liệu mẫu
- **Cách 1 (từ trạng thái rỗng)**: Mở app vào tab **Tổng quan** (hoặc tab **Đơn**) khi chưa nối PC → bấm nút cam **"Xem thử với dữ liệu mẫu"**.
- **Cách 2 (từ Cài đặt)**: Mở tab **Cài đặt** → mục **Chế độ xem thử** → bật công tắc **"Xem thử với dữ liệu mẫu"**.
- Khi bật: dải `"DỮ LIỆU MẪU, không gửi gì cho máy tính"` luôn hiện trên đầu mọi màn hình kèm nút **Tắt** nhanh. Mọi thao tác quét/ghi chú/đổi trạng thái/xóa chỉ chạy cục bộ trong bộ nhớ, tuyệt đối không phát MQTT/ntfy và không ghi đè dữ liệu thật; tắt công tắc sẽ trở lại dữ liệu thật nguyên vẹn.

## 4. Kiểm tra tĩnh & CHƯA BIÊN DỊCH
- **CHƯA BIÊN DỊCH**: Máy Windows này không có Swift/Xcode nên chưa chạy được `swift test` và chưa build IPA. Cần Claude kiểm tra và đẩy lên GitHub để CI chạy `swift test` và build thật.
- Đã tự rà soát tĩnh toàn bộ tệp mới/sửa:
  - 0 tham chiếu tới `Inbox`, `inbox_push`, `cancelOrder`, `SHP-2026` trong `ios-app/App/Sources/`, `DemoData.swift`, `DemoDataTests.swift`.
  - Mọi API iOS 17+ (`scrollTargetBehavior`, `scrollTargetLayout`, `contentTransition(.numericText)`, `.snappy`) đều bọc trong `if #available(iOS 17.0, *)`.
  - Cân bằng ngoặc `{}`, `()`, `[]` trên toàn bộ tệp Swift.
  - Không gọi hàm `@MainActor` (`SyncEngine.pref`) từ ngữ cảnh ngoài main actor.

## 5. Việc cần kiểm trên iPhone thật
1. Mở app chưa nối PC: bấm **"Xem thử với dữ liệu mẫu"** ở Tổng quan → kiểm tra dải `"DỮ LIỆU MẪU, không gửi gì cho máy tính"` hiện trên cả 5 tab, không che tiêu đề lớn.
2. Kiểm tra **Tổng quan**: không còn nút `"Tổ…"`, vòng tiến độ Hero, dải thẻ cuộn ngang (Hàng hoàn về / Theo sàn / Hoàn một phần), vuốt phải/trái trên dòng "Cần xử lý ngay", biểu đồ 7 ngày có đường trung bình nét đứt.
3. Kiểm tra **Quét**: camera ~55% màn, 1 chấm trạng thái + `"Đã quét hôm nay"`, nhập thử mã `DEMO-SPX-1001` để xem thẻ kết quả trượt từ dưới lên bằng chữ thường và tự thu sau ~4,5 giây; kiểm tra 4 nút tròn (Đèn, Zoom, Tạm dừng, Dán mã).
4. Kiểm tra **Đơn** & **Hàng hoàn về**: hàng ô lọc có số đếm, thẻ đơn hoàn một phần tô màu cam `ProductLines`, bấm từng chặng trong đường ống 4 chặng ở Hàng hoàn về.
5. Kiểm tra **Lịch sử**: 4 số liệu gộp 1 thẻ, nhóm đúng "Hôm nay" / "Hôm qua".
6. Kiểm tra **Cài đặt**: tiêu đề `"Cài đặt"` không còn đè lên thẻ đầu; tắt công tắc **"Xem thử với dữ liệu mẫu"** → kiểm tra dữ liệu thật trở lại nguyên vẹn và không có gói tin nào gửi sang PC trong lúc bật mẫu.
7. Kiểm tra Dark Mode, Light Mode, cỡ chữ lớn (Dynamic Type) và Giảm chuyển động (Reduce Motion).

TRẠNG THÁI: XONG
