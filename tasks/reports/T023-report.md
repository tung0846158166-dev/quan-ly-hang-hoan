# T023 - Màn "Nhật ký hoạt động"

**Người làm: claude (agy đã dừng).** Không có người thứ hai kiểm tra độc lập.

## Đã làm
- Mục sidebar "Nhật ký hoạt động" (nhóm CHUNG, trước "Cài đặt"); `ActivityLogViewModel`, `ActivityLogView`, `IActivityLogProvider` (`SqliteActivityLogProvider` đọc `audit_log` qua `AuditLogReader`; `FakeActivityLogProvider` cho demo/test).
- Bảng Thời gian / Loại (biểu tượng + nhãn tiếng Việt) / Chi tiết, mới nhất trên cùng; lọc theo loại (danh sách lấy từ dữ liệu) và theo chữ (chờ 300 ms sau lần gõ cuối; ký tự `%` `_` không thành ký tự đại diện); "Làm mới"; "Tải thêm 200 dòng" (không bao giờ tải hết một lần); dòng "Hiển thị N / tổng M"; menu chuột phải "Sao chép dòng này" (qua `IClipboardService`).
- Tự làm mới nhẹ khi có hoạt động mới và đang xem nhật ký (không quá 1 lần/2 giây, không làm nhảy vị trí khi đang đọc trang sau).
- CHỈ XEM: không có lệnh sửa/xóa nhật ký. Trống: "Chưa có hoạt động nào".
- Thêm nhãn "Hàng hoàn về" cho loại `return_watch` trong `AuditLogReader.LabelOf`.

## Kiểm chứng
- Build 0 lỗi, 0 cảnh báo. 10 test mới (`UiTests/T023ActivityLogUiTests.cs`) + 11 test cũ liên quan xanh: thứ tự mới nhất trước, nhãn/biểu tượng, lọc loại và chữ, phân trang không trùng dòng, làm mới và giới hạn 2 giây, sao chép, đọc nhật ký THẬT từ DB tạm và thấy dòng mới sau một thao tác sửa, DB không tồn tại thì trống không lỗi, vị trí trong sidebar.
- Ảnh chụp `tasks/reports/T023-screens/` (sáng, tối, trống); claude đã xem ảnh sáng.
- Chưa chạy toàn bộ bộ test 10 lần cho phiếu này (sẽ chạy gộp sau T024/T025).

## Chưa kiểm chứng
- Chưa xem với nhật ký thật nhiều nghìn dòng (hiệu năng cuộn); cột chi tiết chưa cho chọn-sao chép nhiều dòng.

TRẠNG THÁI: XONG
