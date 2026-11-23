# Báo cáo T015 - Màn hình "Google Sheet"

## Tóm tắt thực hiện
1. **Dịch vụ & đường dẫn (`App/Services/`)**:
   - Thêm `AppPaths.SheetSyncStatePath` (`%LocalAppData%\QuanLyHangHoan\v2\sheet_sync.json`) trong `v2/src/QuanLyHangHoan.App/Services/AppPaths.cs`.
   - Tạo `ISheetSyncController`, `RealSheetSyncController` (bọc `SheetSyncService` dùng `DpapiProtector`) và `FakeSheetSyncController` (cho `--demo` và test, không chạm mạng/đĩa) trong `v2/src/QuanLyHangHoan.App/Services/ISheetSyncController.cs`.
2. **Khởi động & điều phối (`App.xaml.cs`, `MainViewModel.cs`, `MainWindow.xaml`)**:
   - Chế độ thật: khởi tạo `RealSheetSyncController` sau `AppDatabase.Initialize`; nếu `Enabled && HasUrl` thì gọi `Start()`; gọi `Dispose()` khi thoát.
   - Chế độ `--demo`: dùng `FakeSheetSyncController`, không chạm mạng hay đĩa thật.
   - Khi `Completed` có `Import` với `Added`/`Updated`/`EnrichedArchived > 0`: marshal về UI thread, tải lại dữ liệu/badge/Trang chủ và hiện thông báo `"Đã cập nhật từ Google Sheet: thêm X, cập nhật Y"`. Lỗi không bật hộp thoại chặn.
   - Thêm mục **"Google Sheet"** vào nhóm sidebar **"NẠP ĐƠN TỰ ĐỘNG"** ngay dưới **"Theo dõi thư mục"**.
3. **Màn hình "Google Sheet" (`GoogleSheetViewModel.cs`, `GoogleSheetView.xaml`, `GoogleSheetView.xaml.cs`)**:
   - Ô dán địa chỉ Apps Script dùng `PasswordBox` (che ký tự); sau khi lưu chỉ hiện `"Đã lưu địa chỉ (đã mã hóa)"` + nút `"Đổi địa chỉ"`, không hiện lại địa chỉ đã lưu; lỗi `SetUrl` hiển thị chữ đỏ.
   - Hiển thị `Token` kèm nút `"Sao chép"` và hướng dẫn 3 bước ngắn cách dán vào Apps Script.
   - Đủ các cấu hình: công tắc `"Tự cập nhật mỗi 10 phút"`, `"Tên các tab (ngăn cách bằng ;)"`, `"Chỉ lấy đơn trong N ngày gần nhất hoặc từ ngày (vd 30 hoặc 01/11/2026)"`, `"Loại huỷ bỏ qua (ngăn cách bằng ;)"` (để trống = `null` mặc định), `"Chỉ nạp đơn hoàn/hủy/thất bại"`.
   - Trạng thái lượt gần nhất xanh/đỏ (`LastNote`, `LastAtTicks`), nút `"Cập nhật ngay"` (`RunOnceAsync(true)` chạy nền, khóa nút, `"Đang cập nhật..."`), nhật ký 100 dòng trong bộ nhớ, cảnh báo vàng khi `Import.Ambiguous.Count > 0`.
4. **Kiểm thử & ảnh chụp (`UiTests/T015GoogleSheetUiTests.cs`, `tasks/reports/T015-screens/`)**:
   - Đã cập nhật số lượng mục sidebar trong `ReturnOrdersViewModelTests.cs` và `T006PlatformSplitTests.cs`.
   - Thêm 8 test mới trong `T015GoogleSheetUiTests.cs` dùng `WpfTestHost`, thư mục tạm, bộ tải giả và `ISecretProtector` giả (không chạm mạng hay `%LocalAppData%` thật).
   - Ảnh chụp tại `tasks/reports/T015-screens/`: `01-google-sheet-empty.png`, `02-google-sheet-success.png`, `03-google-sheet-error.png`, `04-google-sheet-ambiguous.png` (dùng địa chỉ giả `https://script.google.com/macros/s/FAKE/exec`, không lộ URL/token thật).

## Kết quả build & test
- `dotnet build v2\QuanLyHangHoan.sln --nologo`: **0 lỗi, 0 cảnh báo**.
- `dotnet test v2\QuanLyHangHoan.sln --no-build --nologo`: **319/319 xanh**, chạy liên tiếp **10/10 lần đạt (0 lần đỏ)**.

## Chưa kiểm chứng thực tế
- Chưa chạy với Google Apps Script thật trên mạng thật.

TRẠNG THÁI: XONG
