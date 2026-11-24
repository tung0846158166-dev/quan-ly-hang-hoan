# Báo cáo T034 - App iPhone: gộp Premium/Thường trong thống kê sản phẩm

## 1. Phạm vi & tệp sửa
Chỉ sửa trong `ios-app/` (cùng báo cáo này và `TASKS.md`). Không đụng `v2/src/QuanLyHangHoan.Data/`, `v2/src/QuanLyHangHoan.Core/`, hay bất kỳ tệp test nào ngoài `ios-app/`. Không thiếu API nào ở `v2/src/QuanLyHangHoan.Data/`.

### Tệp sửa
- `ios-app/ScannerCore/Sources/ScannerCore/ProductMetrics.swift`:
  - Thêm `ProductMetrics.isPremium(name:color:size:)`: trả `true` khi tên (đã làm sạch), size hoặc màu chứa `"premium"` (không phân biệt hoa/thường), khớp `ProductStatistics.IsPremium` trên PC.
  - Thêm `ProductMetrics.stripPremium(_:)`: bỏ đuôi khớp regex không phân biệt hoa/thường `\s*[-–—]?\s*premium(\s+cao\s+c[aấ]p)?\s*$` (chuẩn hóa NFC trước khi khớp); nếu bỏ xong chuỗi rỗng thì giữ nguyên tên gốc (không bao giờ thành rỗng), khớp `ProductStatistics.StripPremium` trên PC.
  - Cập nhật `ProductMetrics.compute(rows:)` và `ProductMetrics.orders(forProductKey:in:)`: tính `isPremium` trên tên/màu/size gốc, tính `groupKey` trên tên đã bỏ đuôi `stripPremium` (giữ nguyên so khớp hoàn một phần `rn` trên tên gốc trước khi gộp).
  - Thêm hai trường `premiumUnits` và `standardUnits` (`= units - premiumUnits`, mặc định `0`) vào `ProductStatItem`.
- `ios-app/App/Sources/ProductStatsView.swift`:
  - Trong `productRankCard`: thêm nhãn nhỏ `"Premium X • Thường Y"` (kèm biểu tượng `sparkles` màu `Theme.warning`), tự ẩn khi `item.premiumUnits == 0`.
  - Trong `ProductStatDetailView`: thêm thẻ `premiumBreakdownCard` ("Premium / Thường") với thanh ngang 2 phần (Premium màu chuyển vàng-cam, Thường màu `Theme.info`) kèm số cái và tỷ lệ phần trăm từng loại; tự ẩn khi `stat.premiumUnits == 0`.
- `ios-app/App/Sources/HomeView.swift`:
  - Trong thẻ Tổng quan `"Sản phẩm hoàn nhiều nhất"` (`topProductsCard`): mỗi dòng sản phẩm có thêm nhãn nhỏ `"Premium X • Thường Y"` bên dưới tên sản phẩm, tự ẩn khi `item.premiumUnits == 0`.
- `ios-app/ScannerCore/Sources/ScannerCore/DemoData.swift`:
  - Bổ sung đuôi `" - Premium Cao Cấp"` cho một phần các đơn của `"Áo thun cotton basic"`, `"Quần jean ống rộng"` và `"Áo polo dệt kim"` (kèm bản thường của chính các mẫu này, vẫn giữ nguyên 60 đơn và mọi bộ đếm đã kiểm trong `DemoDataTests`) để chế độ dữ liệu mẫu hiển thị trực tiếp cả gộp dòng lẫn tách Premium/Thường.
- `ios-app/ScannerCore/Tests/ScannerCoreTests/ProductMetricsTests.swift`:
  - Thêm 3 bài test XCTest tương đương 3 test PC (`testPremiumAndStandardVersionsMergeIntoOneRowWithSplit`, `testPremiumMarkedInSizeOrColorCountsAsPremiumWithoutChangingName`, `testNameThatIsOnlyWordPremiumIsKeptAndNeverBecomesEmpty`) và bổ sung kiểm tra `premiumUnits + standardUnits == units` + có sản phẩm chứa cả hai bản trong `testDemoDataProducesValidProductStats`.

## 2. CHƯA BIÊN DỊCH
- **CHƯA BIÊN DỊCH**: Máy Windows này không có Swift/Xcode nên chưa chạy được `swift test` và chưa build IPA. Cần Claude kiểm tra và đẩy lên GitHub để CI chạy `swift test` và build thật.
- Đã tự rà soát tĩnh toàn bộ 5 tệp Swift đã sửa:
  - Cân bằng ngoặc `{}`, `()`, `[]` đạt 100% trên cả 5 tệp.
  - Không dùng API iOS 17+ chưa bọc `#available` (không dùng `.buttonBorderShape(.circle)`, `navigationDestination(item:)`, `ContentUnavailableView`, `sensoryFeedback`).
  - Không gọi hàm `@MainActor` từ ngữ cảnh ngoài main actor; mọi chữ ký hàm/tham số khớp hoàn toàn.

## 3. Việc cần kiểm trên iPhone thật
1. Bật **Chế độ dữ liệu mẫu** → kiểm tra thẻ **"Sản phẩm hoàn nhiều nhất"** trên màn **Tổng quan**: các mẫu như `"Áo thun cotton basic"`, `"Áo polo dệt kim"`, `"Quần jean ống rộng"` phải gộp thành một dòng duy nhất (không còn đuôi `"- Premium Cao Cấp"`) và hiện nhãn nhỏ `"Premium X • Thường Y"`; các mẫu chỉ có bản thường không hiện nhãn này.
2. Mở màn **Thống kê sản phẩm** (`ProductStatsView`): kiểm tra mỗi dòng có bản Premium hiện đúng nhãn `"Premium X • Thường Y"` và ẩn ở các mẫu thường; thử lọc theo sàn và khoảng thời gian xem số Premium/Thường cập nhật khớp tổng số cái.
3. Chạm vào một sản phẩm có bản Premium để mở **Chi tiết sản phẩm** (`ProductStatDetailView`): kiểm tra thẻ **"Premium / Thường"** hiện thanh ngang 2 phần đẹp trên cả Light/Dark Mode, danh sách **"Đơn có sản phẩm này"** gồm đủ cả đơn bản Premium lẫn bản thường, và chạm vào một đơn mở được màn Chi tiết đơn.
4. Kết nối PC v2 có dữ liệu thật: đối chiếu số lượng sản phẩm, tổng số cái, và số `Premium / Thường` của cùng một sản phẩm giữa màn Thống kê PC và app iPhone để xác nhận hai nơi khớp nhau.

TRẠNG THÁI: XONG
