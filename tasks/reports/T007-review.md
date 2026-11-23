# T007 - Kiểm tra của claude (2026-11-23)

Kết luận: **ĐẠT**.

Đã tự kiểm tra:
- `dotnet build`: 0 lỗi, 0 cảnh báo. `dotnet test`: 74/74 xanh (có `UiTests/T007WireRealDataTests.cs`).
- Phạm vi: so mã ngoài `App/` + `UiTests/` (Core, Data, ImporterTests, test gốc) với ảnh chụp trước khi chạy: KHÔNG tệp nào đổi.
- Xem ảnh `02-nhap-du-lieu-cu-ket-qua.png`: đúng luồng 3 bước (chọn nguồn + dry-run -> xem báo cáo -> tick xác nhận -> nhập thật), nút nhập thật chỉ bật sau dry-run, báo cáo hiện nhóm trùng khóa "giữ riêng không gộp", chuẩn hóa sàn, đối chiếu "9/9" sau nhập.

## Chưa kiểm chứng (đừng coi là PASS)
1. Chưa chạy trên DỮ LIỆU THẬT (4.668 bản ghi, ảnh 65 MB): thời gian nhập, bộ nhớ khi tải 2.300+ đơn lên bảng, `LoadActive` trên DB thật. Cần user chạy thử và so số liệu với app cũ.
2. Chưa chạy app thật qua các lần mở/đóng (chỉ test + ảnh chụp do agy tạo; claude xem 1/3 ảnh).
3. Kho lưu trữ hiện đọc cờ `IsArchived`; app cũ đã dời đơn nhận >= 24h tự động, v2 chưa có tác vụ tự dời: việc của Core, chưa làm.
4. Ảnh: bảng `photos` đã có trong DB nhưng giao diện chưa hiển thị ảnh (chưa có phiếu).
