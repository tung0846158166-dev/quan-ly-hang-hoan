# Báo cáo T007 — Nối dữ liệu thật (SQLite) vào giao diện

## 1. Tóm tắt thực hiện
- **Đường dẫn mặc định (`v2/src/QuanLyHangHoan.App/Services/AppPaths.cs`)**:
  - DB SQLite: `%LocalAppData%\QuanLyHangHoan\v2\data.db`
  - Thư mục bản sao kiểm hash: `%LocalAppData%\QuanLyHangHoan\v2\legacy-copies`
- **Provider thật (`v2/src/QuanLyHangHoan.App/Services/SqliteOrderProvider.cs`)**:
  - Cài đặt `IReturnOrderProvider` (`OrderRepository.LoadActive`, bỏ qua tham số `count` để trả toàn bộ đơn chính, chạy qua `Task.Run`), `IArchivedOrderProvider` (`IsArchived && !IsDeleted`), `IDeletedOrderProvider` (`IsDeleted` ánh xạ sang `DeletedOrderRecord`), và `INewOrderProvider` (trả danh sách rỗng vì chưa có nguồn đồng bộ điện thoại).
  - Giữ `GetPlaceholderScreenData` và `GetCurrentTime` để tương thích giao diện.
  - DB chưa tồn tại trả danh sách rỗng, không lỗi và không tự tạo tệp.
- **Importer thật (`v2/src/QuanLyHangHoan.App/Services/SqliteLegacyImporter.cs`)**:
  - Bọc `LegacyImportService.Run(..., dryRunOnly: true/false)` qua `Task.Run` để không chặn UI.
  - Dry-run ánh xạ đầy đủ số liệu từ `DryRunReport`; nhóm trùng khóa được đưa vào danh sách cảnh báo để báo rõ cho người dùng, không tự gộp.
  - Khi `Ok = false` (kể cả lỗi đường dẫn, dry-run có lỗi hoặc đối chiếu "số liệu KHÔNG khớp"), trả/báo nguyên `Message` rõ ràng.
- **Khởi động & chế độ `--demo` (`App.xaml.cs`, `MainViewModel.cs`, các ViewModel & View)**:
  - Mặc định khởi động dùng `SqliteOrderProvider` + `SqliteLegacyImporter`. Nếu chưa có DB hoặc DB rỗng, Trang chủ hiện trạng thái trống kèm nút dẫn tới "Nhập dữ liệu cũ", không tự đổ dữ liệu giả.
  - Giữ `FakeDataService` + `FakeLegacyImporter` sau cờ dòng lệnh `--demo`. Nhãn `DỮ LIỆU GIẢ` / `GIẢ LẬP` và các nút benchmark 50.000 dòng giả chỉ hiển thị khi `IsDemoMode = true`.
  - Sau khi nhập thành công (`LegacyImportViewModel.ImportSucceeded`), `MainViewModel.ReloadAllDataAsync()` tự động tải lại toàn bộ màn hình (Trang chủ, 3 bảng sàn, Bảng đơn mới, Kho lưu trữ, Đã xóa, Thống kê) và đồng bộ KPI/badge sidebar.

## 2. Kiểm thử & Ảnh chụp
- Test mới: `v2/tests/QuanLyHangHoan.Tests/UiTests/T007WireRealDataTests.cs` (tạo DB tạm bằng `LegacyImporter.Import` từ dữ liệu giả, kiểm provider đọc đúng đơn chính/lưu trữ/đã xóa, 3 bảng sàn tách đúng và tổng khớp, DB không tồn tại trả rỗng không lỗi, dry-run không tạo DB, nhập xong tự tải lại toàn bộ màn hình).
- Kết quả build & test: `dotnet build` 0 lỗi, 0 cảnh báo; `dotnet test` xanh toàn bộ **74/74** test.
- Ảnh chụp màn hình tại `tasks/reports/T007-screens/`:
  - `01-trang-chu-trong.png`: Trang chủ trống khi chưa có DB (không nhãn dữ liệu giả).
  - `02-nhap-du-lieu-cu-ket-qua.png`: Màn "Nhập dữ liệu cũ" hiện kết quả dry-run, cảnh báo nhóm trùng khóa, nhập thành công & đối chiếu khớp.
  - `03-trang-chu-sau-nhap.png`: Trang chủ tự tải lại sau khi nhập dữ liệu giả vào SQLite tạm.

TRẠNG THÁI: XONG
