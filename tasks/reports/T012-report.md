# Báo cáo T012 - Hoàn thiện giao diện: sidebar thu gọn, giao diện tối, nhớ cửa sổ

- **Người thực hiện**: antigravity
- **Ngày**: 2026-11-23
- **Phạm vi đã sửa**: Chỉ trong `v2/src/QuanLyHangHoan.App/` và `v2/tests/QuanLyHangHoan.Tests/UiTests/` (không sửa `Core/`, `Data/`, `ImporterTests/`, test gốc).

## Những gì đã làm
1. **Sidebar thu gọn (`NavigationItem.cs`, `MainViewModel.cs`, `MainWindow.xaml`, `AppStyles.xaml`)**:
   - Ba nhóm `SHOPEE`, `TIKTOK`, `CHUNG` bấm vào tiêu đề để thu gọn/mở rộng (`ToggleNavGroupCommand`), trạng thái lưu vào `AppSettings.CollapsedNavGroups` và nạp lại lúc mở app.
   - Mục đang chọn luôn hiển thị (`IsVisible = !collapsed || item.IsSelected`), kể cả khi nhóm chứa nó đang thu gọn.
   - Nhóm thu gọn hiển thị huy hiệu tóm tắt trên tiêu đề (`⚠ X` khi có cảnh báo Đỏ/Vàng ở nhóm sàn, hoặc tổng số lượng ở nhóm `CHUNG`).
   - `ScrollViewer` dùng `VerticalScrollBarVisibility="Auto"` nên thanh cuộn chỉ hiện khi thiếu chiều cao.

2. **Giao diện tối (`Themes/LightTheme.xaml`, `Themes/DarkTheme.xaml`, `Themes/AppStyles.xaml`, `Services/ThemeService.cs`, `Converters/UiConverters.cs`, các View)**:
   - Tách bảng màu dùng chung thành `LightTheme.xaml` và `DarkTheme.xaml`; toàn bộ style và 10 màn hình nội dung dùng `{DynamicResource ...}` (cùng `PhotoViewerWindow.xaml` nền tối sẵn có).
   - Công tắc đổi Sáng/Tối nằm ở khu vực dưới sidebar (`🌙 Giao diện tối` / `☀️ Giao diện sáng`), đổi ngay lập tức không cần khởi động lại và lưu vào `AppSettings.Theme` (giá trị lạ tự chuẩn hóa về `"light"`).
   - Giữ nguyên ý nghĩa màu cảnh báo (Đỏ > 24h, Vàng > 12h, Xanh đã nhận) với độ tương phản đọc được ở cả hai chế độ (đã kiểm tra trực tiếp trên ảnh chụp, bao gồm `DatePicker` và nút lọc đang chọn).

3. **Nhớ cửa sổ & màn hình cuối (`Services/WindowPlacementService.cs`, `MainWindow.xaml.cs`, `MainViewModel.cs`)**:
   - Lưu `LastPage` khi chuyển màn hình; khi mở lại nếu `LastPage` không tồn tại thì tự về Trang chủ (`home`).
   - Lưu `Window` (`Left`, `Top`, `Width`, `Height`, `Maximized`) khi thay đổi (giãn nhịp 400ms) và khi đóng cửa sổ; bỏ qua nếu cửa sổ đang `Minimized`.
   - Trước khi áp dụng vị trí lúc mở, kiểm tra vùng giao nhau với `ScreenWorkArea` hiện có qua `EnumDisplayMonitors`/`GetMonitorInfo`; nếu nằm ngoài màn hình hoặc thanh tiêu đề bị che thì bỏ qua và dùng kích thước mặc định.

4. **An toàn cấu hình & `--demo` (`Services/UiSettingsStore.cs`, `Services/ISyncController.cs`, `App.xaml.cs`)**:
   - `UiSettingsStore` và `RealSyncController` dùng chung khóa `SettingsFileSync.Gate`, luôn đọc bản mới nhất từ `settings.json` trước khi sửa đúng trường của mình rồi ghi lại — không ghi đè `Room`, `SyncEnabled`, `NtfyEnabled`, `Brokers`.
   - Chế độ `--demo` dùng `UiSettingsStore(null, isDemoMode: true)` chỉ lưu trong bộ nhớ, không đọc/ghi tệp `settings.json` thật.

## Kiểm tra
- `dotnet build v2/QuanLyHangHoan.sln --no-restore --no-incremental`: **0 lỗi** (0 cảnh báo ở `App`/`Core`/`Data`; 2 cảnh báo xUnit2013 có sẵn từ trước ở `ImportApplierTests.cs` ngoài phạm vi T012).
- `dotnet test v2/QuanLyHangHoan.sln --no-build`: **219/219 xanh** (gồm toàn bộ test cũ + 8 test mới trong `v2/tests/QuanLyHangHoan.Tests/UiTests/T012UiPolishTests.cs`).
- Ảnh chụp kiểm chứng tại `tasks/reports/T012-screens/`:
  - `01-home-light.png` (Trang chủ sáng)
  - `02-home-dark-collapsed-sidebar.png` (Trang chủ tối + sidebar thu gọn `TIKTOK` và `CHUNG`)
  - `03-shopee-return-dark.png` (Bảng `Shopee — Hoàn hàng` chế độ tối)
  - `04-scan-receive-dark.png` (Màn `Quét nhận hàng` chế độ tối)

TRẠNG THÁI: XONG
