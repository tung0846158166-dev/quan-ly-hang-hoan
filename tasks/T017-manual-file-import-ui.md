# T017 - Màn hình "Nạp file Excel" (nạp tay TikTok / Salework / Shopee) (giao cho: antigravity, người kiểm tra: claude)

Phạm vi: CHỈ `v2/src/QuanLyHangHoan.App/` và `v2/tests/QuanLyHangHoan.Tests/UiTests/`. KHÔNG sửa `Core/`, `Data/` và test ngoài `UiTests/`. Thiếu API ở Data thì ghi vào báo cáo.
Đọc trước: `AGENTS.md`, `tasks/T015-google-sheet-ui.md` + `tasks/reports/T015-review.md` (mẫu controller/màn hình/`WpfTestHost`), `v2/src/QuanLyHangHoan.Data/FileImportService.cs`. Test dùng `WpfTestHost`; `dotnet test` toàn bộ 10 lần liên tiếp 0 lần đỏ. Test dùng file CSV dựng bằng mã trong thư mục TẠM và bản giả/DB tạm; không chạm `%LocalAppData%` thật. Không dùng dữ liệu thật.

## API có sẵn
`FileImportService.Import(dbPath, files: IReadOnlyList<(string Name, byte[] Bytes)>, new TikTokMapOptions{ OnlyReturns, SkipUnshippedCancel, PriceMode, Today }, ImportProductMode.Supplement|Replace, DateTime.Now)` -> `FileImportResult`: `Ok`, `Message`, `Files` (`Name`, `Kind` = TikTok|Shopee|Unknown, `Rows`, `Error`), `Added`, `Updated`, `Ambiguous` (tổng), `TikTok`/`Shopee` (`TikTokImportResult`: `Added`, `Updated`, `EnrichedArchived`, `SkippedDeleted`, `RowsSkipped`, `FailedDelivery`, `Ambiguous` (danh sách)). Tự nhận diện loại file, bỏ cột cá nhân ngay khi đọc, file lạ/hỏng báo riêng không làm hỏng file khác. Chỉ nhận `.xlsx` và `.csv` (`.xls` cũ báo lỗi: hướng dẫn lưu lại thành .xlsx).

## Việc cần làm
1. `IFileImportController` ở `App/Services` (bọc `FileImportService` chạy nền + bản giả cho `--demo`/test; bản giả KHÔNG đọc ổ đĩa thật). Đọc file bằng `File.ReadAllBytes` với `FileShare.ReadWrite` trong lớp thật; KHÔNG xóa/sửa/di chuyển file gốc.
2. Sidebar: thêm mục **"Nạp file Excel"** làm mục ĐẦU của nhóm "NẠP ĐƠN TỰ ĐỘNG".
3. Màn hình: nút "Chọn file..." (hộp thoại mở nhiều file, lọc `*.xlsx;*.csv`; thêm vùng kéo-thả file vào nếu làm được gọn) -> danh sách file đã chọn (tên, kích thước) có thể bỏ từng file; tùy chọn: cách cập nhật sản phẩm ("Nhập bổ sung - giữ sản phẩm không có trong file mới" mặc định / "Thay toàn bộ sản phẩm"), "Chỉ nạp đơn hoàn/hủy/thất bại" (mặc định bật), "Bỏ đơn hủy chưa gửi" (TikTok), cột giá để lấy (4 giá trị như màn hình Theo dõi thư mục); nút "Nạp" (chạy nền, khóa nút, "Đang nạp...", có hộp xác nhận "Sẽ nạp N file vào bảng đơn. Dữ liệu đã có chỉ được cập nhật, không bị xóa.").
4. Kết quả: bảng từng file (tên, loại nhận diện TikTok/Salework / Shopee / "Không nhận ra" đỏ, số dòng, lỗi) + tổng "thêm X, cập nhật Y"; cảnh báo vàng khi có nhập nhằng ("N dòng khớp nhiều kiện nên chưa nạp, cần kiểm tra thủ công"); ghi chú cố định: "App chỉ ĐỌC file, không xóa/sửa/di chuyển file. Cột thông tin khách hàng (tên, SĐT, địa chỉ) bị bỏ ngay khi đọc. Chỉ đọc .xlsx và .csv."
5. Sau khi nạp xong: tải lại các màn hình/badge/Trang chủ + thông báo nhỏ. Lỗi `Ok=false` hiện đỏ (không đổi dữ liệu), không hộp thoại chặn.
6. `--demo`: dùng bản giả (không đọc đĩa).

## Tiêu chí hoàn thành
- `dotnet build` 0 lỗi 0 cảnh báo; `dotnet test` xanh 10/10.
- Test mới trong `UiTests/`: chọn/bỏ file; nạp CSV TikTok + CSV Shopee dựng bằng mã vào DB tạm thấy đúng số dòng và các màn hình tải lại; file lạ hiện đỏ mà file khác vẫn nạp; xác nhận hủy thì không nạp; tùy chọn được truyền đúng vào `FileImportService`; mục sidebar mới ở đầu nhóm; `--demo` không chạm đĩa.
- Ảnh chụp `tasks/reports/T017-screens/`: chưa chọn file, đã chọn file, kết quả thành công, có file lạ + cảnh báo nhập nhằng.
- Báo cáo `tasks/reports/T017-report.md`, dòng cuối `TRẠNG THÁI: XONG` hoặc `CHƯA XONG (lý do)`; ghi rõ "chưa thử file Shopee/TikTok thật". Cập nhật `TASKS.md`.
