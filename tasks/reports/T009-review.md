# T009 - Kiểm tra của claude (2026-11-23)

Kết luận: **ĐẠT**.

Đã tự kiểm tra:
- `dotnet build`: 0 lỗi, 0 cảnh báo. `dotnet test`: 114/114 xanh (có `UiTests/T009ScanScreenTests.cs`).
- Phạm vi: mã ngoài `App/` + `UiTests/` so với ảnh chụp trước khi chạy: KHÔNG đổi.
- Khởi tạo DB: `AppStartupInitializer.TryInitializeDatabase` gọi `AppDatabase.Initialize`, bắt `InvalidOperationException` (schema mới hơn) và hiện hộp thoại; `App.xaml.cs` gọi nó ở chế độ thật, trước khi nạp dữ liệu.
- Xem ảnh `03-scan-ambiguous.png`: thẻ đỏ lớn "NHIỀU KIỆN KHỚP - KIỂM TRA THỦ CÔNG", ghi rõ "CHƯA nhận — không thay đổi dữ liệu (NO-ACK)"; danh sách phiên có màu theo kết quả; bộ đếm Nhận/Trước đó/Lỗi; nút Chọn ảnh, bật/tắt âm báo.

## Chưa kiểm chứng
1. Máy quét USB thật, âm thanh trên loa thật, hộp thoại chọn ảnh khi chạy tay (agy cũng ghi như vậy).
2. Quét đúp < 1 giây chỉ có test, chưa thử với máy quét thật (tốc độ gõ của từng máy khác nhau).
3. Chỉ xem 1/4 ảnh chụp.
