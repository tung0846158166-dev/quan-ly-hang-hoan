# T011 - Kiểm tra của claude (2026-11-23)

Kết luận: **ĐẠT**.

Đã tự kiểm tra:
- `dotnet build`: 0 lỗi, 0 cảnh báo. `dotnet test`: 187/187 xanh (có `UiTests/T011SyncNtfyUiTests.cs`).
- Phạm vi: so mã ngoài `App/` + `UiTests/` với ảnh chụp trước khi chạy: chỉ 4 tệp đổi (SyncPresenceTests, SyncReceiver, SyncService, SyncTests), TẤT CẢ do claude sửa sau khi chụp (nhịp "PC online" + sửa lỗi nhận trùng đồng thời). Agy không đụng Core/Data/test gốc.
- Mã `ISyncController.cs`: có `NtfyEnabled`, `SetNtfyEnabled`, factory ntfy thay được (`Func<ISyncTransport>`), mặc định `NtfyTransport`.
- Ảnh `01-mqtt-up-ntfy-down.png`: hiện riêng "MQTT: 3/3 broker" (xanh) và "ntfy: Mất kết nối" (đỏ) kèm lý do "Kết nối ntfy.sh quá hạn (mạng có thể đang chặn ntfy.sh)", tổng hợp xanh khi có >= 1 đường, công tắc "Dùng ntfy (dự phòng)", ghi chú mạng chặn ntfy.sh. Nhãn "GIẢ LẬP (--demo)" đúng.

## Lưu ý
1. Agy ghi nhận test `Same_message_arriving_concurrently...` đỏ 1 lần ở lần chạy đầu: đó là lỗi thật mà claude tìm ra và đã sửa độc lập (xử lý tuần tự theo từng DB trong `SyncReceiver`); sau sửa test qua ổn định (đã chạy lặp 3 lần).
2. Chưa kiểm chứng: điện thoại thật; ntfy.sh thật (mạng máy này không tới được ntfy.sh); chỉ xem 1/3 ảnh chụp.
