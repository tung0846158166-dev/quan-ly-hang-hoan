# T008 - Tự dời lưu trữ + hiển thị ảnh (giao cho: antigravity, người kiểm tra: claude)

Phạm vi: CHỈ `v2/src/QuanLyHangHoan.App/` và `v2/tests/QuanLyHangHoan.Tests/UiTests/`. KHÔNG sửa `Core/`, `Data/`, `ImporterTests/`, test gốc. Thiếu API ở Data thì ghi vào báo cáo.
Đọc trước: `AGENTS.md`, `tasks/reports/T007-review.md`. Không dùng dữ liệu thật trong test, không tự nhập dữ liệu thật của user.

## API có sẵn (claude đã viết, có test)
- `QuanLyHangHoan.Data.ArchiveSweep.Run(dbPath, DateTime now)` -> số đơn vừa dời. Đặt cờ lưu trữ cho đơn `Hàng đã trả` đã nhận >= 24h; không xóa; chạy lại an toàn (idempotent); không đụng đơn đã xóa. Đồng bộ, gọi qua `Task.Run`.
- `QuanLyHangHoan.Data.PhotoRepository.Get(dbPath, photoId)` -> `PhotoData(PhotoId, Mime, Bytes, Source, InlineDiffers)` hoặc `null` (không có ảnh). `MissingIds(dbPath)` -> danh sách photoId mất ảnh. DB chưa có -> null/rỗng, không lỗi.
- `Order.PhotoId` là khóa ảnh của đơn.

## Việc cần làm
1. **Tự dời lưu trữ**: chạy `ArchiveSweep` (qua một dịch vụ/interface ở `App/Services` để test được, ví dụ `IArchiveSweeper`) (a) lúc khởi động trước khi nạp dữ liệu, (b) định kỳ mỗi 10 phút khi app đang mở, (c) sau khi nhập dữ liệu cũ thành công. Nếu số đơn dời > 0 thì tải lại các màn hình (Trang chủ, 3 bảng sàn, Kho lưu trữ, Thống kê) và cập nhật badge sidebar. Chạy nền, không chặn UI, lỗi chỉ ghi nhận (không làm app sập). KHÔNG tự đổi gì ở chế độ `--demo` (dữ liệu giả không có DB).
2. **Hiển thị ảnh**: ở bảng đơn (Hoàn hàng Shopee/Đơn hủy/TikTok) và Kho lưu trữ, dòng có `PhotoId` có biểu tượng ảnh; bấm mở khung xem ảnh (cửa sổ/popup, phóng to, đóng bằng Esc). Tải ảnh THEO YÊU CẦU lúc bấm (không nạp trước cả nghìn ảnh), nền, giải mã `byte[]` thành `BitmapImage` đóng băng. Ảnh `null` (mất) -> thông báo rõ "Ảnh bị mất khỏi dữ liệu cũ" chứ không để trống. Nếu `InlineDiffers` thì ghi chú nhỏ "Ảnh nhúng khác tệp ảnh, đang hiện tệp ảnh". Bọc đọc ảnh qua interface (`IPhotoProvider`) để test bằng bản giả.
3. Trang chủ/Thống kê: thêm một dòng nhỏ "Ảnh mất: N" lấy từ `MissingIds` (chỉ khi N > 0).

## Tiêu chí hoàn thành
- `dotnet build` 0 lỗi 0 cảnh báo; `dotnet test` xanh TOÀN BỘ.
- Test trong `UiTests/` (DB tạm tạo bằng `LegacyImporter.Import` từ dữ liệu GIẢ): sweeper dời đúng đơn đủ điều kiện và các màn hình tải lại, chạy hai lần không dời lặp, `--demo` không gọi sweeper; provider ảnh trả `null` khi mất, trả bytes khi có; ViewModel xem ảnh xử lý được cả hai.
- Ảnh chụp: bảng có biểu tượng ảnh, khung xem ảnh, thông báo ảnh mất -> `tasks/reports/T008-screens/`.
- Báo cáo `tasks/reports/T008-report.md`, dòng cuối `TRẠNG THÁI: XONG` hoặc `CHƯA XONG (lý do)`. Cập nhật `TASKS.md`.
