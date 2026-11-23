# Báo cáo T014 - Ổn định test giao diện + màn hình "Theo dõi thư mục"

## Phần A - Ổn định test giao diện
- Tạo `v2/tests/QuanLyHangHoan.Tests/UiTests/WpfTestHost.cs`: dùng MỘT luồng STA duy nhất + `Dispatcher` + MỘT `QuanLyHangHoan.App.App` (`ShutdownMode = OnExplicitShutdown`, gọi `InitializeComponent()` nạp đủ `Themes/LightTheme.xaml` và `Themes/AppStyles.xaml` gồm `SidebarThemeToggleStyle`).
- Tự động dọn dẹp trước và sau mỗi test (`ResetState()`: đóng mọi cửa sổ còn mở, đặt lại `ThemeService.ApplyTheme(ThemeService.LightTheme)`, pump Dispatcher).
- Cập nhật `ThemeService.ApplyTheme` (`v2/src/QuanLyHangHoan.App/Services/ThemeService.cs`) để marshal về `Application.Current.Dispatcher` nếu được gọi từ luồng nền/MTA trong test.
- Chuyển toàn bộ 7 file test WPF (`T007WireRealDataTests`, `T008ArchiveSweepPhotosTests`, `T009ScanScreenTests`, `T010SyncScreenTests`, `T011SyncNtfyUiTests`, `T012UiPolishTests`, `T013ManualActionsUiTests`) và `T014FolderWatchUiTests` sang dùng `WpfTestHost.Run(...)`, không còn test nào tự `new Application()` hay tạo luồng STA riêng.
- Kết quả chạy lặp `dotnet test v2\QuanLyHangHoan.sln --no-build` 30 lần liên tiếp: **30/30 xanh, 0 đỏ** (298/298 test mỗi lượt).

## Phần B - Màn hình "Theo dõi thư mục"
- Thêm `AppPaths.FolderWatchStatePath` (`%LocalAppData%\QuanLyHangHoan\v2\folder_watch.json`) trong `v2/src/QuanLyHangHoan.App/Services/AppPaths.cs`.
- Tạo `v2/src/QuanLyHangHoan.App/Services/IFolderWatchController.cs`:
  - `RealFolderWatchController`: bọc `FolderWatchService` của `Data`.
  - `FakeFolderWatchController`: dùng cho `--demo` và test giao diện, không đọc/ghi ổ đĩa thật.
- Khởi động (`v2/src/QuanLyHangHoan.App/App.xaml.cs` + `MainViewModel.cs`):
  - Chế độ thật (sau `AppDatabase.Initialize`): tạo `RealFolderWatchController`, nếu `Enabled` thì gọi `Start()` sau khi mở cửa sổ (chạy nền, không làm chậm khởi động), `Dispose()` khi thoát.
  - Khi `Completed` bắn với `Added`/`Updated`/`EnrichedArchived` > 0: marshal về UI thread, gọi `ReloadAllDataAsync()` cập nhật các màn hình/badge/Trang chủ và hiện thông báo nhỏ `"Đã tự nạp N file TikTok: ..."`. Lỗi (`Ok == false`) chỉ hiện đỏ trên màn hình, không bật hộp thoại chặn.
  - Chế độ `--demo`: dùng `FakeFolderWatchController`, không tạo `FolderWatchService` thật, không chạm `folder_watch.json`.
- Sidebar (`MainViewModel.cs`): thêm nhóm **"NẠP ĐƠN TỰ ĐỘNG"** (thu gọn/mở rộng và nhớ trạng thái qua `AppSettings.CollapsedNavGroups`) chứa mục **"Theo dõi thư mục"**.
- Màn hình (`FolderWatchViewModel.cs`, `FolderWatchView.xaml`, `FolderWatchView.xaml.cs`, `MainWindow.xaml`):
  - Trạng thái trống có hướng dẫn; ô đường dẫn chỉ đọc + nút `"Chọn thư mục..."` (`IFolderPickerService`) + công tắc `"Tự nạp file TikTok mới trong thư mục này"`.
  - Trạng thái lượt gần nhất (`"Chưa nạp file nào."` / xanh khi `LastOk` / đỏ khi lỗi).
  - Nút `"Nạp ngay"` (`RunOnce(false)`) và `"Nạp các file đang có trong thư mục"` (`RunOnce(true)`, có hộp xác nhận `"Sẽ nạp cả các file TikTok đã có sẵn trước khi bật theo dõi"`), chạy nền và khóa nút với nhãn `"Đang nạp..."`.
  - Lưu 4 tùy chọn (`PriceMode`, `ProductMode`, `OnlyReturns`, `SkipUnshippedCancel`) qua `Config` + `SaveConfig()`.
  - Nhật ký tối đa 100 dòng (mới nhất trên cùng), dòng đỏ cho lỗi, dòng cảnh báo vàng khi `Import.Ambiguous.Count > 0`.
  - Ghi chú cố định về quyền chỉ-đọc, bỏ cột cá nhân, hỗ trợ `.xlsx`/`.csv`.

## Kiểm chứng & ảnh chụp
- `dotnet build v2\QuanLyHangHoan.sln --no-restore`: **0 lỗi, 0 cảnh báo**.
- `dotnet test v2\QuanLyHangHoan.sln --no-build`: **298/298 xanh** (gồm `T014FolderWatchUiTests`, 9 test mới dùng thư mục tạm & DB tạm hoặc bản giả; không chạm `%LocalAppData%` thật).
- Ảnh chụp tại `tasks/reports/T014-screens/`:
  - `01-folder-watch-empty.png`
  - `02-folder-watch-success.png`
  - `03-folder-watch-error.png`
  - `04-folder-watch-ambiguous.png`
- Phạm vi: chỉ sửa `v2/src/QuanLyHangHoan.App/` và `v2/tests/QuanLyHangHoan.Tests/UiTests/`; không sửa `Core/`, `Data/`, `ImporterTests/`.

TRẠNG THÁI: XONG
