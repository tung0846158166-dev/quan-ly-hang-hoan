# T014 - Kiểm tra của claude (2026-11-23)

Kết luận: **ĐẠT**.

Đã tự kiểm tra:
- `dotnet build`: 0 lỗi, 0 cảnh báo. `dotnet test`: 298/298 xanh (có `UiTests/T014FolderWatchUiTests.cs`, 9 test).
- **Phần A (ổn định test WPF)**: claude chạy toàn bộ `dotnet test` **30 lần liên tiếp: 0 lần đỏ** (trước phiếu: 4/12 đỏ). Có `WpfTestHost` dùng chung cho các test WPF.
- Phạm vi: so mã ngoài `App/` + `UiTests/` (Core, Data, test gốc) với ảnh chụp trước khi chạy: KHÔNG tệp nào đổi.
- Mã: `IFolderWatchController` bọc `FolderWatchService` thật (đường dẫn trạng thái `AppPaths.FolderWatchStatePath`), `FakeFolderWatchController` cho `--demo`.
- Ảnh xem trực tiếp (1/4) `02-folder-watch-success.png`: nhóm sidebar "NẠP ĐƠN TỰ ĐỘNG" > "Theo dõi thư mục"; ô thư mục + "Chọn thư mục..." + công tắc; trạng thái lượt gần nhất xanh; tùy chọn cột giá / cách cập nhật sản phẩm / chỉ nạp đơn hoàn / bỏ đơn hủy chưa gửi; nhật ký; ghi chú "chỉ ĐỌC file, không xóa/sửa/di chuyển, bỏ cột thông tin khách, chỉ .xlsx/.csv"; thông báo nổi "Đã tự nạp 1 file TikTok"; nhãn "GIẢ LẬP (--demo)".

## Chưa kiểm chứng
1. Chưa chạy với thư mục thật và file TikTok thật; test dùng thư mục tạm + CSV dựng bằng mã.
2. Chưa thấy lượt tự nạp chạy nền trong app mở lâu (chỉ test + ảnh chụp).
3. Chỉ xem 1/4 ảnh chụp.
