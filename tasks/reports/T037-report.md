# Báo cáo T037 - App iPhone: thời gian "Đã nhận tại kho" + thống kê sản phẩm theo ngày

## 1. Phạm vi & đối chiếu giao thức PC (`v2/src/QuanLyHangHoan.Data/Sync/`)
- **Phạm vi sửa**: CHỈ sửa trong `ios-app/` (cùng báo cáo này và `TASKS.md`). Không sửa bất kỳ tệp nào trong `v2/src/QuanLyHangHoan.Data/`, `v2/src/QuanLyHangHoan.Core/` hay thư mục test ngoài `ios-app/`. Không `git commit` / `git push`. Không dùng dữ liệu đơn thật.
- **Đối chiếu giao thức đồng bộ phía PC (`v2/src/QuanLyHangHoan.Data/Sync/PhoneSnapshot.cs`)**:
  - **Đã nhận tại kho (`rcd`)**: **PC ĐÃ GỬI SẴN** ở `PhoneSnapshot.BuildRow` dòng 98 (`if (!string.IsNullOrEmpty(o.RecvDate)) row["rcd"] = Clip(o.RecvDate, 24);`, định dạng `dd/MM/yyyy HH:mm:ss` từ `ScanReceiver.cs` và `OrderEditor.cs`). `OrderRow.rcd` trên iOS đã giải mã trường này, tương thích ngược hoàn toàn với bản cũ.
  - **Nguyên nhân trước đây chưa thấy trên iPhone**:
    1. `OrderDetailView` chỉ hiển thị dòng "Đã nhận tại kho" khi `!live.rcd.isEmpty` → đơn chưa nhận không có dòng này, và đơn vừa quét nhận trên iPhone cũng không thấy vì lý do (2).
    2. `SnapshotStore.applyLocalEdit` và `SnapshotStore.apply` (khi gộp lượt quét chưa được PC xác nhận) đánh dấu `rv = true` nhưng **không điền `rcd`**, khiến đơn vừa quét nhận trên chính điện thoại bị trống thời gian nhận kho.
  - **Ghi chú API phía Data cho Claude (nếu cần bổ sung sau)**: `PhoneSnapshot.cs` hiện **chưa gửi** trường "Ngày giao" (`shipDate`) sang iPhone, và chỉ gửi trường `ar` ("Hàng về shop") cho đơn nằm trong `return_watch` (không gửi `ar` cho các đơn ngoài `return_watch`). Riêng trường `rcd` ("Đã nhận tại kho") đã có đủ từ PC.

## 2. Thay đổi từng màn hình & mô-đun

### 2.1. Chi tiết đơn (`OrderDetailView.swift`), `SnapshotStore.swift`, `SyncEngine.swift`, `Snapshot.swift`
- **Luôn hiển thị dòng "Đã nhận tại kho"** trong thẻ **Thời gian** (nằm cạnh "Ngày hoàn / hủy", "Bắt đầu theo dõi hoàn về", "Hàng về shop", "Hạn xử lý"):
  - Khi đã nhận có ngày giờ (`rcd` từ PC hoặc giờ quét cục bộ): hiển thị ngày giờ đồng nhất định dạng `dd/MM/yyyy HH:mm` (hoặc `dd/MM/yyyy` nếu nguồn chỉ có ngày) với chấm xanh `Theme.success`.
  - Khi đơn vừa quét nhận trên chính điện thoại mà `rcd` từ PC chưa về: tự động dùng giờ quét của điện thoại (gắn thêm nhãn gọn `"Quét trên máy này"` khi suy ra từ lịch sử quét cục bộ).
  - Khi đã nhận (`rv == true`) nhưng cả `rcd` và lịch sử quét đều trống: hiển thị `"Đã nhận (chưa rõ giờ)"` màu phụ.
  - Khi chưa nhận (`rv == false`, kể cả nếu dữ liệu cũ còn sót `rcd`): hiển thị `"Chưa nhận"` bằng màu phụ (`.secondary`) và chấm xám mờ.
- **Đồng nhất định dạng thời gian**: mọi mốc trong thẻ **Thời gian** và thẻ **Hành trình hàng hoàn** dùng chung `OrderRow.formatDisplayDate(_:includeTime:calendar:)` (`dd/MM/yyyy HH:mm` / `dd/MM/yyyy`).
- **Điền giờ quét ngay khi quét / cập nhật cục bộ**:
  - `SnapshotStore.applyLocalEdit(..., receivedAt:)` điền `rcd` theo giờ quét/thao tác khi chuyển sang trạng thái đã nhận (nếu chưa có `rcd`), và xóa `rcd` nếu chuyển về trạng thái chưa nhận.
  - `SyncEngine.submit(...)` truyền đúng `scanDate` khớp `ScanItem.time` (cả chế độ thật lẫn dữ liệu mẫu), cung cấp `localReturnScanTimeMs(for:)` và truyền `unconfirmedReturnScanTimes` sang `SnapshotStore.apply` để giữ nguyên giờ quét khi gói snapshot từ PC về trước lúc PC xác nhận.

### 2.2. Thống kê sản phẩm (`ProductStatsView.swift`) & Thẻ Tổng quan (`HomeView.swift`)
- **Bộ chọn khoảng thời gian dạng chip gọn**: `Hôm nay` / `7 ngày` / `30 ngày` / `Tháng này` / `Tất cả` / `Chọn ngày` (mở thẻ 2 ô `DatePicker` Từ ngày – Đến ngày theo lịch Việt Nam `vi_VN`, tự chuẩn hóa nếu chọn ngược).
- **Căn cứ ngày rõ ràng**: mặc định tính theo **Ngày hoàn** (`rd`), có nút chuyển gọn sang **Ngày nhận kho** (`rcd` của đơn `rv == true`) và ghi chú ngay dưới dòng tóm tắt (`"Theo ngày hoàn · ..."` / `"Theo ngày nhận kho · ..."`).
- **Tính lại đầy đủ số liệu & dòng tóm tắt**: tổng cái hoàn, số đơn, số mặt hàng, tách **Premium / Thường**, tỷ trọng `%` và danh sách đơn khi bấm vào sản phẩm đều tính lại đúng theo khoảng đang chọn. Hiển thị tóm tắt `"N sản phẩm • M cái trong khoảng này"` (hoặc `"trong ngày dd/MM/yyyy"` khi đang lọc một ngày).
- **Chế độ xem "Theo ngày"**:
  - Bộ chuyển phân đoạn `Xếp hạng` / `Theo ngày`.
  - Danh sách ngày xếp **mới nhất trước**, mỗi ngày một dòng gọn gồm: nhãn ngày (`Hôm nay · 24/11`, `Hôm qua · 23/11`, `T2, 22/11/2026`), số sản phẩm (`N SP`), sản phẩm đứng đầu kèm số cái, và tổng số cái nổi bật bên phải.
  - Bấm vào một ngày lập tức lọc bảng xếp hạng sản phẩm riêng cho ngày đó, kèm thanh báo `"Đang lọc: ..."` có nút quay lại `"Theo ngày"` hoặc `"✕"` để bỏ lọc.
- **Thẻ "Sản phẩm hoàn nhiều nhất" ở Tổng quan (`HomeView.swift`)**:
  - Thêm dải chip thời gian gọn (`Hôm nay / 7 ngày / 30 ngày / Tháng này / Tất cả`) tính theo ngày hoàn, cập nhật ngay Top 3 + dòng `"N sản phẩm • M cái trong khoảng này · Theo ngày hoàn"`.
  - Khi bấm `"Xem tất cả"` truyền luôn khoảng thời gian đang chọn sang `ProductStatsView(initialDateRange:)`; khi bấm một sản phẩm truyền đúng tập đơn đã lọc sang `ProductStatDetailView`.

### 2.3. Logic thuần `ScannerCore` & Dữ liệu mẫu (`DemoData.swift`)
- `ProductMetrics.swift`: thêm `ProductDateBasis`, mở rộng `ProductDateRange` (`.today`, `.days7`, `.days30`, `.thisMonth`, `.all`, `.custom`), thêm `DailyProductBucket`, `vietnamCalendar` (`Asia/Ho_Chi_Minh`), `effectiveDate`, `dateBounds`, `filter` (hỗ trợ custom & `onDay:`), `groupByDay`, `rangeSummaryText`. Đơn không có ngày hợp lệ chỉ được tính ở `.all`. Giữ nguyên tương thích mọi chữ ký hàm/test cũ.
- `Snapshot.swift`: thêm `ReceivedTimeDisplay`, `OrderRow.formatDisplayDate`, `OrderRow.resolvedReceivedTime(localScanTimeMs:calendar:)`.
- `DemoData.swift`: giữ nguyên tổng 60 đơn và mọi chỉ số đếm cốt lõi (`overdueCount = 7`, `soonCount = 8`, `pendingCount = 26`, `receivedTodayCount = 6`, 7 ngày liên tiếp có dữ liệu), đồng thời rải ngày hoàn (`rd`) và ngày nhận kho (`rcd`) đều qua nhiều ngày (từ hôm nay đến 42 ngày trước) để mọi mốc lọc `Hôm nay / 7 ngày / 30 ngày / Tháng này / Tất cả / Chọn ngày` và chế độ `Theo ngày` đều có số liệu rõ ràng.
- XCTest mới:
  - `ProductMetricsTests.swift`: 3 test mới kiểm tra ranh giới ngày múi giờ Việt Nam (`00:30 +07:00` khác ngày UTC, `00:00:00` vs `23:59:59`), đơn không có ngày chỉ tính ở `Tất cả`, `Chọn ngày` kể cả khi truyền ngược, gom `Theo ngày` (mới nhất trước, hoàn một phần, Premium/Thường, lọc 1 ngày), và chuyển đổi căn cứ `Ngày hoàn` vs `Ngày nhận kho`.
  - `SnapshotTests.swift`: `ReceivedTimeDisplayTests` kiểm tra đủ 5 trường hợp (chưa nhận dù còn `rcd` cũ, có `rcd` đủ giờ từ PC, có `rcd` chỉ ngày, vừa quét cục bộ theo giờ Việt Nam, đã nhận nhưng không rõ giờ).
  - `DemoDataTests.swift`: kiểm tra mọi đơn mẫu đã nhận đều có `rcd` hợp lệ, trải nhiều ngày ở cả 2 căn cứ ngày, và tổng số cái tăng dần `Hôm nay < 7 ngày < 30 ngày < Tất cả`.

## 3. Danh sách tệp sửa
- `ios-app/ScannerCore/Sources/ScannerCore/Snapshot.swift`
- `ios-app/ScannerCore/Sources/ScannerCore/ProductMetrics.swift`
- `ios-app/ScannerCore/Sources/ScannerCore/DemoData.swift`
- `ios-app/ScannerCore/Tests/ScannerCoreTests/ProductMetricsTests.swift`
- `ios-app/ScannerCore/Tests/ScannerCoreTests/SnapshotTests.swift`
- `ios-app/ScannerCore/Tests/ScannerCoreTests/DemoDataTests.swift`
- `ios-app/App/Sources/SnapshotStore.swift`
- `ios-app/App/Sources/SyncEngine.swift`
- `ios-app/App/Sources/OrderDetailView.swift`
- `ios-app/App/Sources/ProductStatsView.swift`
- `ios-app/App/Sources/HomeView.swift`
- `TASKS.md`
- `tasks/reports/T037-report.md`

## 4. CHƯA BIÊN DỊCH
- **CHƯA BIÊN DỊCH**: Máy Windows này không có Swift/Xcode nên **chưa chạy được `swift test`** và **chưa biên dịch IPA**. Cần Claude kiểm tra và đẩy lên GitHub để CI chạy `swift test` và build thật (chỉ khi CI xanh mới là bằng chứng biên dịch & test đạt).
- **Đã tự rà soát tĩnh toàn bộ 11 tệp Swift đã sửa**:
  - Cân bằng ngoặc `{}`, `()`, `[]` đều bằng `0` trên cả 11 tệp.
  - Tương thích tối thiểu iOS 16: không dùng API iOS 17+ chưa bọc `#available` (mọi `.onChange(of:)` đều dùng closure 1 tham số `{ _ in ... }` / `{ newMode in ... }`, không dùng `.buttonBorderShape(.circle)`, `ContentUnavailableView`, `sensoryFeedback`, `navigationDestination(item:)`).
  - Kiểm tra thủ công toàn bộ kiểu dữ liệu, nhãn tham số, mức truy cập `public` của `ScannerCore`, tính chỉ số mảng trong `DemoData.swift`, và tính khớp số học của tất cả test XCTest mới.
  - Không thêm dữ liệu đơn thật nào; mọi mã mẫu đều dùng tiền tố `DEMO-` / `DHM-` / `VN-` / `DAY-` / `B-`.

## 5. Việc cần kiểm trên iPhone thật
1. **Chi tiết đơn — mục Thời gian**:
   - Mở một đơn **chưa nhận**: thấy dòng `"Đã nhận tại kho"` hiện `"Chưa nhận"` bằng màu phụ.
   - Mở một đơn **đã nhận** (đồng bộ từ PC hoặc dữ liệu mẫu): thấy dòng `"Đã nhận tại kho"` hiển thị đúng ngày giờ (`dd/MM/yyyy HH:mm`) đồng nhất định dạng với các dòng thời gian khác.
   - **Quét nhận trực tiếp một đơn trên iPhone** (hoặc bấm nút `"Đã nhận"` trong Chi tiết đơn): mở lại Chi tiết đơn kiểm tra `"Đã nhận tại kho"` hiện ngay đúng giờ vừa quét (kể cả khi PC chưa kịp gửi snapshot mới).
   - Mở một đơn thuộc **Hàng hoàn về** đã nhận kho: kiểm tra cả thẻ Hành trình và thẻ Thời gian hiển thị cùng định dạng ngày giờ nhận kho.
2. **Thống kê sản phẩm theo ngày (`ProductStatsView`)**:
   - Thử từng chip `Hôm nay / 7 ngày / 30 ngày / Tháng này / Tất cả / Chọn ngày` (thử chọn khoảng từ–đến bằng `DatePicker`): kiểm tra số cái, số đơn, số mặt hàng, dòng `"N sản phẩm • M cái trong khoảng này"`, Premium/Thường và tỷ trọng `%` tính lại đúng.
   - Thử chuyển căn cứ `Tính theo: Ngày hoàn` ↔ `Ngày nhận kho`: kiểm tra số liệu đổi tương ứng.
   - Chuyển sang chế độ **"Theo ngày"**: kiểm tra danh sách ngày xếp mới nhất trước, mỗi ngày 1 dòng gọn (ngày, số SP, sản phẩm đứng đầu, tổng cái); bấm vào một ngày để xem riêng sản phẩm của ngày đó, bấm vào sản phẩm để xem đúng các đơn trong ngày đó, rồi bấm `"Theo ngày"` hoặc `"✕"` để quay lại.
3. **Tổng quan (`HomeView`)**:
   - Thử bấm chip `Hôm nay / 7 ngày / 30 ngày / Tháng này / Tất cả` ngay trên thẻ `"Sản phẩm hoàn nhiều nhất"`, sau đó bấm `"Xem tất cả"` kiểm tra màn `ProductStatsView` mở ra giữ đúng khoảng thời gian đã chọn.
4. **Sáng / Tối & Dynamic Type**: kiểm tra hiển thị trên cả Light/Dark mode và cỡ chữ lớn, đảm bảo chip và `DatePicker` không bị cắt chữ.

TRẠNG THÁI: XONG
