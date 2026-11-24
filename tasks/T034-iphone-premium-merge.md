# T034 - App iPhone: gộp Premium/Thường trong thống kê sản phẩm (giao cho: antigravity, người kiểm tra: claude)

PHẠM VI: CHỈ `ios-app/`. Đọc trước: `tasks/T033-iphone-detail-and-product-stats.md`, `tasks/reports/T033-report.md`, `ios-app/ScannerCore/Sources/ScannerCore/ProductMetrics.swift`, `ProductMetricsTests.swift`, `ProductStatsView.swift`, và bản PC làm sẵn: `v2/src/QuanLyHangHoan.Core/Stats/ProductStatistics.cs` (hàm `StripPremium`, `IsPremium`, `PremiumUnits`, `StandardUnits`) cùng test `v2/tests/QuanLyHangHoan.Tests/ProductStatisticsTests.cs` (3 test Premium mới).
GIỚI HẠN THẬT: không có Swift/Xcode trên máy này; iOS 16 là mức tối thiểu (API iOS 17+ bọc `#available`, KHÔNG dùng `.buttonBorderShape(.circle)`); lớp/hàm `@MainActor` không gọi từ chỗ không phải main actor; tự đọc lại từng tệp tìm lỗi kiểu/nhãn trước khi báo xong. KHÔNG `git commit`/`git push`. Claude đẩy lên GitHub build thật.

## Quyết định của chủ dự án (24/11/2026)
Hàng chủ yếu là Premium. Cùng một mẫu áo xuất hiện hai dòng vì tên có/không có đuôi "- Premium Cao Cấp" (vd áo Polo STRIPES hiện 76 + 76 thay vì một mẫu 152 cái). Phải GỘP thành MỘT dòng và hiện riêng "Premium X • Thường Y". Bản PC đã làm xong và chạy trên dữ liệu thật (62 sản phẩm xuống 42, tổng vẫn 376).

## Việc cần làm (làm Y HỆT bản PC để hai nơi ra cùng con số)
1. `ProductMetrics.swift`:
   - `isPremium` = tên (đã làm sạch) chứa "premium" không phân biệt hoa/thường, HOẶC size, HOẶC màu chứa "premium".
   - Tên gộp = tên bỏ đuôi khớp regex không phân biệt hoa/thường `\s*[-–—]?\s*premium(\s+cao\s+c[aấ]p)?\s*$`; nếu bỏ xong tên rỗng thì giữ nguyên tên gốc. KHÓA NHÓM tính trên tên đã bỏ đuôi.
   - Mỗi sản phẩm thêm `premiumUnits` và `standardUnits` (= số cái - premiumUnits).
2. Giao diện (`ProductStatsView.swift`, thẻ Tổng quan): mỗi dòng sản phẩm có nhãn nhỏ "Premium 76 • Thường 76" (ẩn khi không có bản Premium nào); màn con của sản phẩm có mục tách Premium/Thường (thanh ngang 2 phần). Cập nhật dữ liệu mẫu (`DemoData.swift`) để có vài sản phẩm có cả bản "- Premium Cao Cấp" lẫn bản thường, để xem được.
3. Test XCTest (`ProductMetricsTests.swift`) tương đương 3 test PC: gộp hai bản thành một dòng + tách Premium/Thường (6 cái = 3 + 3, màu/size cộng gộp); Premium ghi ở size/màu thì tính Premium mà không đổi tên; tên chỉ là "Premium" thì giữ nguyên, không rỗng.

## Tiêu chí hoàn thành
- Chỉ sửa trong `ios-app/`. Báo cáo `tasks/reports/T034-report.md` (tệp sửa, mục "CHƯA BIÊN DỊCH", mục "việc cần kiểm trên iPhone thật", dòng cuối `TRẠNG THÁI: XONG` hoặc `CHƯA XONG (lý do)`). Cập nhật `TASKS.md`.
