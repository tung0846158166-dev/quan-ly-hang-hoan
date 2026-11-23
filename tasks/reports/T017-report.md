# Báo cáo T017 - Màn hình "Nạp file Excel"

## Tóm tắt thực hiện
1. **Dịch vụ & chọn file (`App/Services/`)**:
   - Tạo `IImportFilePickerService` / `WindowsImportFilePickerService` trong `v2/src/QuanLyHangHoan.App/Services/IImportFilePickerService.cs` (mở hộp thoại chọn nhiều file, lọc `*.xlsx;*.csv`).
   - Tạo `IFileImportController`, `RealFileImportController` (đọc byte qua `FileStream` với `FileShare.ReadWrite` ở luồng nền rồi gọi `FileImportService.Import`; không xóa/sửa/di chuyển file gốc) và `FakeFileImportController` (cho `--demo` và test, không đọc đĩa thật) trong `v2/src/QuanLyHangHoan.App/Services/IFileImportController.cs`.
2. **Nối khởi động & Sidebar (`App.xaml.cs`, `MainWindow.xaml`, `MainViewModel.cs`)**:
   - Chế độ `--demo`: dùng `FakeFileImportController` (không chạm đĩa). Chế độ thật: dùng `RealFileImportController` với `AppPaths.DatabasePath`.
   - Thêm mục **"Nạp file Excel"** (`file_import`) làm mục ĐẦU của nhóm sidebar **"NẠP ĐƠN TỰ ĐỘNG"** (chuyển `showSectionHeader: true` từ `folder_watch` sang `file_import`).
   - Khi `Completed` thành công có thay đổi (`Added > 0 || Updated > 0 || EnrichedArchived > 0`): marshal về UI thread, tải lại các màn hình/badge/Trang chủ và hiện thông báo nhỏ `"Đã nạp file Excel: thêm X, cập nhật Y"`. Khi `Ok = false`: hiện thông báo đỏ, không bật hộp thoại chặn.
3. **Màn hình "Nạp file Excel" (`FileImportViewModel.cs`, `FileImportView.xaml`, `FileImportView.xaml.cs`)**:
   - Nút `"Chọn file..."` + vùng kéo-thả file `.xlsx` / `.csv` + danh sách file đã chọn (tên, kích thước, nút bỏ từng file `✕`, nút `"Xóa danh sách"`).
   - Tùy chọn: cách cập nhật sản phẩm (`Supplement` mặc định / `Replace`), `"Chỉ nạp đơn hoàn/hủy/thất bại"` (mặc định bật), `"Bỏ đơn hủy chưa gửi (TikTok)"`, cột giá để lấy (4 chế độ giống màn hình Theo dõi thư mục).
   - Nút `"Nạp"` (chạy nền, khóa nút, `"Đang nạp..."`, hộp xác nhận `"Sẽ nạp N file vào bảng đơn. Dữ liệu đã có chỉ được cập nhật, không bị xóa."`).
   - Kết quả: danh sách từng file (tên, loại nhận diện `TikTok / Salework` / `Shopee` / `"Không nhận ra"` màu đỏ, số dòng, lỗi), tổng kết `"Tổng kết: thêm X, cập nhật Y"`, cảnh báo vàng khi có nhập nhằng (`"N dòng khớp nhiều kiện nên chưa nạp, cần kiểm tra thủ công"`), lỗi đỏ khi `Ok = false`, và ghi chú cố định về chỉ đọc file / bỏ cột cá nhân / chỉ nhận `.xlsx` và `.csv`.
4. **Kiểm thử & ảnh chụp (`UiTests/T017FileImportUiTests.cs`, `tasks/reports/T017-screens/`)**:
   - Thêm 9 test mới trong `T017FileImportUiTests.cs` (chọn/bỏ file, nạp CSV TikTok + Shopee dựng bằng mã vào DB tạm và tải lại màn hình, file lạ hiện đỏ mà file khác vẫn nạp, hủy xác nhận không nạp, truyền đúng tùy chọn, lỗi `Ok=false` hiện đỏ, mục sidebar ở đầu nhóm, `--demo` không chạm đĩa, chụp ảnh màn hình).
   - Cập nhật số lượng/thứ tự mục sidebar trong `T006PlatformSplitTests.cs`, `ReturnOrdersViewModelTests.cs` và `T015GoogleSheetUiTests.cs`.
   - Ảnh chụp tại `tasks/reports/T017-screens/`: `01-file-import-empty.png`, `02-file-import-selected.png`, `03-file-import-success.png`, `04-file-import-unknown-ambiguous.png`.

## Kết quả build & test
- `dotnet build v2/QuanLyHangHoan.sln --nologo`: **0 lỗi, 0 cảnh báo**.
- `dotnet test v2/QuanLyHangHoan.sln --no-build --nologo`: **358/358 xanh**, chạy liên tiếp **10/10 lần đạt (0 lần đỏ)**.

## Chưa kiểm chứng thực tế
- Chưa thử file Shopee/TikTok thật.

TRẠNG THÁI: XONG
