# T017 - Kiểm tra của claude (2026-11-23)

Kết luận: **ĐẠT**.

Đã tự kiểm tra:
- `dotnet build`: 0 lỗi, 0 cảnh báo. `dotnet test`: 358/358 xanh; claude tự chạy 10 lần liên tiếp: 0 lần đỏ.
- Phạm vi: Core/Data chỉ có tệp của claude đổi (SpreadsheetReader, FileImportService); agy chỉ sửa `App/` + `UiTests/`.
- Quét mã App phần nạp file: không có `File.Delete/Move/WriteAll`, `Directory.Delete` (app chỉ đọc file gốc).

## Chưa kiểm chứng
1. Chưa thử file Shopee/TikTok thật (chỉ CSV dựng bằng mã).
2. Chưa xem trực tiếp ảnh chụp.
