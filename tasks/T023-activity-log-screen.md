# T023 - Màn "Nhật ký hoạt động" (giao cho: antigravity, người kiểm tra: claude)

Phạm vi: CHỈ `v2/src/QuanLyHangHoan.App/` và `v2/tests/QuanLyHangHoan.Tests/UiTests/`. KHÔNG sửa `Core/`, `Data/` và test ngoài `UiTests/`. Thiếu API ở Data thì ghi vào báo cáo.
Đọc trước: `AGENTS.md`, `v2/src/QuanLyHangHoan.Data/AuditLogReader.cs`, `tasks/reports/T013-review.md` (khung lịch sử sửa). Test dùng `WpfTestHost`, DB tạm + dữ liệu GIẢ; `dotnet test` toàn bộ 10 lần liên tiếp 0 lần đỏ.

## Vấn đề
Mọi thay đổi (nạp đơn, sửa tay, quét nhận kiện, xóa, đơn từ điện thoại, Salework) đã được ghi vào bảng `audit_log` nhưng người dùng không xem được ở đâu. Cần để tự kiểm tra "ai/cái gì đã đổi dữ liệu" (quy tắc không xóa/ghi đè dữ liệu cũ).

## API có sẵn (claude đã viết, có test)
`AuditLogReader.Read(dbPath, limit = 200, offset = 0, type = "", text = "")` -> `AuditEntry(Seq, Time, Type, Label, Detail)` mới nhất trước (`type` rỗng = mọi loại; `text` lọc theo chi tiết, đã thoát ký tự `%`/`_`); `Types(dbPath)` (các loại đang có); `Count(dbPath)`; `LabelOf(type)` (nhãn tiếng Việt). Chỉ ĐỌC. Gọi qua `Task.Run`, bọc interface mới ở `App/Services` có bản giả cho test/`--demo`.

## Việc cần làm
1. Mục sidebar **"Nhật ký hoạt động"** (nhóm CHUNG, trước "Cài đặt" nếu có): bảng gồm Thời gian, Loại (nhãn), Chi tiết; mới nhất trên cùng; phân trang "Tải thêm 200" (không tải hết 5.000 dòng một lần); ô chọn loại (danh sách từ `Types`, kèm "Tất cả"), ô lọc văn bản (chờ ~300 ms sau lần gõ cuối), nút "Làm mới", dòng tổng "Hiển thị N / tổng M".
2. Màu/biểu tượng theo loại (nạp đơn, sửa, quét, xóa, đơn từ điện thoại, Salework, khác); chi tiết dài xuống dòng, chọn và sao chép được (Ctrl+C trên dòng, hoặc menu "Sao chép chi tiết" qua `IClipboardService`).
3. Tự làm mới nhẹ khi có sự kiện đồng bộ/nạp đơn mới (gộp, không quá 1 lần/2 giây) nếu màn đang mở và đang ở trang đầu; không làm nhảy vị trí cuộn của người đang đọc.
4. Chỉ XEM: không có thao tác sửa/xóa nhật ký. Bảng trống: "Chưa có hoạt động nào".
5. Theme sáng/tối, bàn phím. `--demo`: dữ liệu giả, không đọc đĩa.

## Tiêu chí hoàn thành
- `dotnet build` 0 lỗi 0 cảnh báo; `dotnet test` xanh 10/10.
- Test mới trong `UiTests/` (DB tạm: tạo đơn giả rồi dùng `OrderEditor`/importer để có nhật ký thật): hiển thị đúng thứ tự mới nhất trước; lọc theo loại; lọc văn bản (có ký tự `%`, `_`); "Tải thêm" nạp đúng trang kế; làm mới thấy dòng mới sau một thao tác sửa; không có thao tác ghi; sao chép gọi `IClipboardService`; `--demo` không chạm đĩa.
- Ảnh chụp `tasks/reports/T023-screens/`: màn có dữ liệu, đang lọc, trống.
- Báo cáo `tasks/reports/T023-report.md`, dòng cuối `TRẠNG THÁI: XONG` hoặc `CHƯA XONG (lý do)`. Cập nhật `TASKS.md`.
