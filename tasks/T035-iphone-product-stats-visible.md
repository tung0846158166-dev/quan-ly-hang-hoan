# T035 - App iPhone: thống kê sản phẩm phải THẤY NGAY (giao cho: antigravity, người kiểm tra: claude)

PHẠM VI: CHỈ `ios-app/`. Đọc trước: `tasks/T033-iphone-detail-and-product-stats.md`, `tasks/T034-iphone-premium-merge.md`, `tasks/reports/T033-report.md`, `tasks/reports/T034-report.md`, `ios-app/App/Sources/HomeView.swift`, `ProductStatsView.swift`, `OrdersView.swift`, `ScannerApp.swift`, `ios-app/ScannerCore/Sources/ScannerCore/ProductMetrics.swift`.
GIỚI HẠN THẬT: không có Swift/Xcode trên máy này; iOS 16 là mức tối thiểu (API iOS 17+ bọc `#available`, KHÔNG dùng `.buttonBorderShape(.circle)`); lớp/hàm `@MainActor` không gọi từ chỗ không phải main actor; tự đọc lại từng tệp tìm lỗi kiểu/nhãn trước khi báo xong. KHÔNG `git commit`/`git push`. Claude đẩy lên GitHub build thật.

## Phản hồi của chủ dự án
"Thống kê sản phẩm vẫn chưa thấy" (cả trên máy tính và điện thoại). Bản máy tính đã đưa khối "Sản phẩm bị hoàn nhiều nhất" lên ngay dưới các thẻ số liệu và đổi tên mục thanh bên thành "Thống kê đơn & sản phẩm". Trên điện thoại, thẻ "Sản phẩm" hiện chỉ nằm cuối trang Tổng quan và ẩn khi chưa có dữ liệu nên người dùng không thấy.

## Việc cần làm
1. **Tổng quan**: đặt thẻ "Sản phẩm hoàn nhiều nhất" ngay SAU 4 thẻ số lớn (trước Hàng hoàn về), KHÔNG ẩn khi chưa có dữ liệu: khi trống hiện tiêu đề + dòng "Chưa có dữ liệu sản phẩm. Kết nối máy tính hoặc bật dữ liệu mẫu để xem" (có nút "Xem thử với dữ liệu mẫu" như các trạng thái rỗng khác). Có nút "Xem tất cả" rõ ràng.
2. **Tab Đơn**: thêm một lối tắt có biểu tượng + chữ "Sản phẩm" cạnh các lối tắt Đơn hủy / Hủy sau CB / Hoàn về, bấm mở màn thống kê sản phẩm đầy đủ.
3. **Màn thống kê sản phẩm đầy đủ**: tiêu đề lớn "Thống kê sản phẩm", dòng tóm tắt đầu trang giống bản máy tính ("N sản phẩm • M cái bị hoàn trong K đơn"), bộ lọc sàn; mỗi sản phẩm hiện "Premium X • Thường Y" (như T034). Kiểm tra lại cách tính khớp bản PC (`v2/src/QuanLyHangHoan.Core/Stats/ProductStatistics.cs`).
4. Chế độ DỮ LIỆU MẪU: thấy đủ mục này ở cả Tổng quan, tab Đơn và màn đầy đủ.

## Tiêu chí hoàn thành
- Chỉ sửa trong `ios-app/`. Giữ NGUYÊN mọi chức năng/hợp đồng; test lõi hiện có vẫn đạt (`swift test`). Nếu thêm logic thuần thì kèm test XCTest.
- Báo cáo `tasks/reports/T035-report.md` (tệp sửa, vị trí mới của thẻ/lối tắt, mục "CHƯA BIÊN DỊCH", mục "việc cần kiểm trên iPhone thật", dòng cuối `TRẠNG THÁI: XONG` hoặc `CHƯA XONG (lý do)`). Cập nhật `TASKS.md`.
