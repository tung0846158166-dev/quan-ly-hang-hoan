# T037 - App iPhone: thời gian "đã nhận tại kho" + thống kê sản phẩm theo ngày (giao cho: antigravity, người kiểm tra: claude)

PHẠM VI: CHỈ `ios-app/`. Đọc trước: `tasks/T036-iphone-compact-modern.md`, `tasks/reports/T036-report.md`, `ios-app/App/Sources/OrderDetailView.swift`, `ProductStatsView.swift`, `ProductMetrics.swift` (+ test), `HomeView.swift`, `DemoData.swift`, và mô hình dữ liệu đơn trong `ScannerCore` (tìm trường ngày nhận kho: `recvDate`/`receivedAt`/giờ quét nhận, cũng xem giao thức đồng bộ trong `ios-app` và `v2/src/QuanLyHangHoan.Data/Sync/` để biết PC có gửi trường này không).
GIỚI HẠN: không có Swift/Xcode trên máy này; iOS 16 tối thiểu (API iOS 17+ bọc `#available`); tự đọc lại từng tệp tìm lỗi kiểu/nhãn trước khi báo xong; KHÔNG `git commit`/`git push`. Giữ nguyên mọi chức năng cũ.

## Phản hồi của chủ dự án
"Trên điện thoại mục thời gian trong chi tiết sản phẩm chưa có thời gian đã nhận tại kho, thống kê sản phẩm chưa có xem theo ngày."

## Việc cần làm
1. **Chi tiết đơn, mục Thời gian**: thêm dòng "Đã nhận tại kho" (ngày giờ, định dạng như các dòng thời gian khác) cạnh "Hàng về / Ngày hoàn / Ngày giao". Chưa nhận thì hiện "Chưa nhận" (màu phụ). Nếu đơn vừa quét nhận trên chính điện thoại, dùng giờ quét. Nếu trường này chưa có trong mô hình/giao thức: thêm trường tùy chọn (không phá tương thích bản cũ), điền từ dữ liệu PC nếu có, và ghi rõ trong báo cáo nếu PC chưa gửi (Claude sẽ bổ sung phía PC).
2. **Thống kê sản phẩm theo ngày** (màn Thống kê sản phẩm và thẻ "Sản phẩm hoàn nhiều nhất" ở Tổng quan nếu hợp lý): thêm bộ chọn khoảng thời gian dạng chip gọn: Hôm nay / 7 ngày / 30 ngày / Tháng này / Tất cả / Chọn ngày (từ - đến, `DatePicker`). Tính theo ngày hàng hoàn/nhận kho (nêu rõ dùng ngày nào, mặc định ngày hoàn). Số liệu, Premium/Thường, tỷ trọng tính lại đúng theo khoảng đã chọn; hiển thị tóm tắt "N sản phẩm • M cái trong khoảng này". Thêm chế độ xem "Theo ngày": danh sách ngày (mới nhất trước) mỗi ngày một dòng gọn: ngày, tổng số cái, sản phẩm đứng đầu; bấm vào ngày để lọc sản phẩm của ngày đó.
3. Logic thuần (lọc theo khoảng ngày, gom theo ngày) viết trong `ProductMetrics.swift` kèm test XCTest (ranh giới ngày, múi giờ Việt Nam, đơn không có ngày thì chỉ tính ở "Tất cả").
4. Cập nhật `DemoData.swift` để dữ liệu mẫu có ngày nhận kho và trải nhiều ngày.

## Tiêu chí hoàn thành
- Chỉ sửa trong `ios-app/`; `swift test` lõi vẫn đạt; không dùng dữ liệu đơn thật.
- Báo cáo `tasks/reports/T037-report.md`: thay đổi từng màn, tệp sửa, mục "CHƯA BIÊN DỊCH", việc cần kiểm trên iPhone thật, dòng cuối `TRẠNG THÁI: XONG` hoặc `CHƯA XONG (lý do)`. Cập nhật `TASKS.md`.
