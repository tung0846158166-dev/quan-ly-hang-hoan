# T010 - Kiểm tra của claude (2026-11-23)

Kết luận: **ĐẠT**.

Đã tự kiểm tra:
- `dotnet build`: 0 lỗi, 0 cảnh báo. `dotnet test`: 164/164 xanh (có `UiTests/T010SyncScreenTests.cs`).
- Phạm vi: so mã ngoài `App/` + `UiTests/` với ảnh chụp trước khi chạy: chỉ 5 tệp đổi (ScanReceiver, SyncProtocol, SyncReceiver, SyncService, SyncPhotoTests), TẤT CẢ do claude sửa sau khi chụp (phần ảnh từ điện thoại), agy không đụng Core/Data/test gốc.
- Mã `ISyncController.cs`: `RealSyncController` chỉ tạo `MqttTransport`/`SyncService` ở chế độ thật qua factory; `FakeSyncController` (--demo) không mở kết nối (`ConnectedBrokers => 0`); đổi mã phòng / bật tắt: dừng dịch vụ cũ (hủy đăng ký sự kiện, `Dispose` cả service lẫn transport) rồi mới khởi động lại, lưu `AppSettings`; lỗi khởi động không làm app sập (ghi `LastError`).
- Xem ảnh `03-sync-weak-room.png`: cảnh báo đỏ khi mã `SHP-2026`, nút "Tạo mã mạnh", ghi chú "broker công cộng chưa mã hóa", trạng thái "Đã kết nối 0/3 broker" kèm lỗi, nhật ký màu theo kết quả.

## Chưa kiểm chứng
1. Chưa chạy màn hình đồng bộ trong app thật với điện thoại thật / broker thật (test dùng `InMemoryTransport`; riêng bộ truyền tin MQTT claude đã thử tay vòng thật bằng dữ liệu giả, kết nối 3/3 broker).
2. Sidebar giờ có 11 mục nên phải cuộn để thấy "Nhập dữ liệu cũ" ở cửa sổ thấp (ảnh chụp 720px đã thấy thanh cuộn): chấp nhận được, nên cân nhắc thu gọn nhóm sau.
3. Chỉ xem 1/3 ảnh chụp.
