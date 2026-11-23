# T008 - Kiểm tra của claude (2026-11-23)

Kết luận: **ĐẠT**.

Đã tự kiểm tra:
- `dotnet build`: 0 lỗi, 0 cảnh báo. `dotnet test`: 94/94 xanh (có `UiTests/T008ArchiveSweepPhotosTests.cs`).
- Phạm vi: mã ngoài `App/` + `UiTests/` (Core, Data, test gốc) so với ảnh chụp trước khi chạy: KHÔNG đổi.
- Mã: `IArchiveSweeper`, `IPhotoProvider` có thật; sweep được nối trong `App.xaml.cs`/`MainViewModel`/`LegacyImportViewModel`; ảnh mất hiện ở Trang chủ và Thống kê.
- Ảnh chụp xem trực tiếp `02-xem-anh-don-hang.png`: khung xem ảnh có phóng to/thu nhỏ, đóng bằng Esc, ghi chú "Ảnh nhúng khác tệp ảnh, đang hiện tệp ảnh".

## Chưa kiểm chứng
1. Chạy nền định kỳ 10 phút và sweep lúc khởi động: có test, nhưng chưa chạy thật trong app mở lâu.
2. Xem ảnh với ảnh JPEG thật (ảnh chụp dùng ảnh giả hình chữ nhật cam); chưa thử ảnh thật 10-150 KB, cuộn/phóng to ảnh lớn.
3. Chỉ xem 1/4 ảnh chụp.
