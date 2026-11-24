# Báo cáo T035 - App iPhone: thống kê sản phẩm phải THẤY NGAY

## 1. Phạm vi & tệp sửa
Chỉ sửa trong `ios-app/` (cùng báo cáo này và `TASKS.md`). Không đụng `v2/src/QuanLyHangHoan.Data/`, `v2/src/QuanLyHangHoan.Core/`, hay bất kỳ tệp test nào ngoài `ios-app/`. Không thiếu API nào ở `v2/src/QuanLyHangHoan.Data/`.

### Tệp sửa
- `ios-app/App/Sources/HomeView.swift`:
  - Đưa thẻ **"Sản phẩm hoàn nhiều nhất"** (`topProductsCard`) lên **ngay SAU khối 4 thẻ số lớn (`heroCard`, trước `horizontalDeck` / Hàng hoàn về)**.
  - **Không còn ẩn khi chưa có dữ liệu**:
    - Khi `!store.hasData`: hiện `topProductsCard` ngay bên dưới `emptyStateSection`.
    - Khi `store.hasData` nhưng `productStats.products.isEmpty`: vẫn hiển thị `topProductsCard` thay vì bỏ qua.
    - Trạng thái rỗng của thẻ hiện tiêu đề **"Sản phẩm hoàn nhiều nhất"**, dòng *"Chưa có dữ liệu sản phẩm. Kết nối máy tính hoặc bật dữ liệu mẫu để xem"*, và nút **"Xem thử với dữ liệu mẫu"** (khi chưa ở chế độ mẫu).
  - Nút **"Xem tất cả"** được nâng cấp thành viên thuốc nổi bật (chữ đậm + mũi tên `chevron.right` trên nền `Theme.accent.opacity(0.14)`), luôn hiện rõ ở góc trên phải thẻ.
  - Khi có dữ liệu, thẻ hiện thêm dòng tóm tắt `productStats.summaryLine` (`"N sản phẩm • M cái bị hoàn trong K đơn"`) và top 5 sản phẩm kèm nhãn `"Premium X • Thường Y"` (như T034).
- `ios-app/App/Sources/OrdersView.swift`:
  - Đưa thanh lối tắt `shortcutRow` ra ngoài điều kiện `if store.hasData` để **luôn hiển thị** trên đầu tab **Đơn** ngay cả khi chưa kết nối PC / chưa có dữ liệu.
  - Giữ lối tắt biểu tượng + chữ **"Sản phẩm"** (`tshirt.fill`, `Theme.accent`, có `alwaysTint: true` để luôn nổi bật kể cả khi đếm = 0) ngay cạnh **Hoàn về / Đơn hủy / Sau CB**, bấm mở thẳng `ProductStatsView`.
- `ios-app/App/Sources/ProductStatsView.swift`:
  - Giữ tiêu đề lớn **"Thống kê sản phẩm"** (`.navigationBarTitleDisplayMode(.large)`), bộ lọc sàn (`Tất cả / Shopee / TikTok / Lazada`) + khoảng thời gian, và nhãn `"Premium X • Thường Y"` trên từng sản phẩm (như T034).
  - Thêm dòng tóm tắt đầu trang giống bản PC (`summary.summaryLine`: `"N sản phẩm • M cái bị hoàn trong K đơn"`, tự kèm phần chưa rõ sản phẩm nếu có) ngay đầu thẻ `overviewCard`.
- `ios-app/ScannerCore/Sources/ScannerCore/ProductMetrics.swift`:
  - Đối chiếu toàn bộ logic với `v2/src/QuanLyHangHoan.Core/Stats/ProductStatistics.cs` và `PhoneSnapshot.cs`: cách chuẩn hóa khóa nhóm (`groupKey`), lọc hoàn một phần (`rn`), bỏ `"Chưa rõ sản phẩm..."`, tách `isPremium` / `stripPremium` / `premiumUnits` / `standardUnits`, tính `totalUnits` / `ordersWithItems` / `share` đều khớp PC.
  - Sửa một lỗi biên tìm thấy khi đối chiếu với `PhoneSnapshot.cs`: `splitPipe(_:)` trước đây gọi `trimmingCharacters` trước khi tách `" | "` nên nếu màu/size phần tử đầu hoặc cuối rỗng (vd `" | Đen"` hoặc `"L | "`) sẽ bị mất dấu cách quanh `|` và lệch số phần tử. Nay kiểm tra rỗng trước rồi tách `" | "` giữ đúng vị trí từng phần tử.
  - Thêm `ProductMetrics.summaryText(for:)` và thuộc tính `ProductStatsSummary.summaryLine` định dạng giống hệt `StatisticsViewModel.ProductSummaryText` trên PC.
- `ios-app/ScannerCore/Tests/ScannerCoreTests/ProductMetricsTests.swift`:
  - Thêm 2 bài test XCTest mới (`testSummaryLineMatchesPcFormat` và `testSplitPipePreservesEmptyLeadingOrTrailingColorAndSize`) và bổ sung kiểm tra `summaryLine` trong `testDemoDataProducesValidProductStats`.

## 2. Vị trí mới của thẻ / lối tắt
1. **Màn Tổng quan (`HomeView`)**:
   - *Có dữ liệu*: `heroCard` (4 số lớn: Đã nhận hôm nay / Quá hạn / Sắp đến hạn / Còn chờ) → `discrepancyCard` (nếu có) → **`topProductsCard` ("Sản phẩm hoàn nhiều nhất")** → `horizontalDeck` (Hàng hoàn về / Theo sàn / Hoàn một phần) → Cần xử lý ngay → 7 ngày gần nhất → Thống kê chi tiết.
   - *Chưa có dữ liệu (`!store.hasData`)*: `emptyStateSection` → **`topProductsCard` ("Sản phẩm hoàn nhiều nhất" với trạng thái rỗng + nút "Xem thử với dữ liệu mẫu" + nút "Xem tất cả")**.
2. **Tab Đơn (`OrdersView`)**:
   - Thanh lối tắt ngang `shortcutRow` ở ngay đầu màn (luôn hiện cả khi chưa có dữ liệu): **Hoàn về** | **Sản phẩm** (biểu tượng `tshirt.fill` màu nhấn, bấm mở `ProductStatsView`) | **Đơn hủy** | **Sau CB**.
3. **Màn Thống kê sản phẩm đầy đủ (`ProductStatsView`)**:
   - Tiêu đề lớn **"Thống kê sản phẩm"** → bộ lọc sàn & khoảng thời gian → thẻ tổng quan mở đầu bằng dòng `"N sản phẩm • M cái bị hoàn trong K đơn"` + 3 ô số liệu → danh sách xếp hạng sản phẩm có `"Premium X • Thường Y"`.

## 3. CHƯA BIÊN DỊCH
- **CHƯA BIÊN DỊCH**: Máy Windows này không có Swift/Xcode nên chưa chạy được `swift test` và chưa build IPA. Cần Claude kiểm tra và đẩy lên GitHub để CI chạy `swift test` và build thật.
- Đã rà soát tĩnh cả 5 tệp Swift đã sửa:
  - Cân bằng ngoặc `{}`, `()`, `[]` đều bằng `0` trên cả 5 tệp.
  - Không dùng API iOS 17+ chưa bọc `#available` (không có `.buttonBorderShape(.circle)`, `ContentUnavailableView`, `sensoryFeedback`, `navigationDestination(item:)`).
  - Không gọi hàm/lớp `@MainActor` từ ngữ cảnh ngoài main actor; mọi nhãn tham số và kiểu dữ liệu khớp hoàn toàn.

## 4. Việc cần kiểm trên iPhone thật
1. **Khi chưa kết nối PC (chưa có dữ liệu)**:
   - Mở tab **Tổng quan**: thấy ngay thẻ **"Sản phẩm hoàn nhiều nhất"** với dòng *"Chưa có dữ liệu sản phẩm. Kết nối máy tính hoặc bật dữ liệu mẫu để xem"*, nút **"Xem thử với dữ liệu mẫu"** và nút **"Xem tất cả"** rõ ràng.
   - Mở tab **Đơn**: thấy ngay lối tắt **"Sản phẩm"** có biểu tượng + chữ ở hàng lối tắt trên cùng; bấm vào mở màn **Thống kê sản phẩm** (hiện trạng thái rỗng + nút xem thử dữ liệu mẫu).
2. **Bật Chế độ DỮ LIỆU MẪU**:
   - Ở **Tổng quan**: thẻ **"Sản phẩm hoàn nhiều nhất"** nằm ngay dưới khối 4 số lớn (trên khối **Hàng hoàn về**), hiện dòng `"N sản phẩm • M cái bị hoàn trong K đơn"`, top 5 sản phẩm có `"Premium X • Thường Y"`, nút **"Xem tất cả"** mở màn đầy đủ, chạm từng dòng mở chi tiết sản phẩm.
   - Ở tab **Đơn**: lối tắt **"Sản phẩm"** hiện đủ số mặt hàng mẫu, bấm mở màn đầy đủ.
   - Ở màn **Thống kê sản phẩm**: kiểm tra tiêu đề lớn, dòng tóm tắt đầu trang khớp số liệu, bộ lọc sàn (`Tất cả / Shopee / TikTok / Lazada`) cập nhật đúng dòng tóm tắt và từng dòng `"Premium X • Thường Y"`.
3. **Khi nối PC thật**: đối chiếu dòng `"N sản phẩm • M cái bị hoàn trong K đơn"` và số `Premium / Thường` giữa màn Thống kê trên PC v2 và app iPhone.

TRẠNG THÁI: XONG
