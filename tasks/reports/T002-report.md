# Báo cáo T002 - Khung giao diện WPF với dữ liệu giả

## 1. Đã làm gì
- Dựng khung giao diện Windows native hoàn toàn bằng **C# .NET 8 + WPF + MVVM (`CommunityToolkit.Mvvm` 8.4.2)** trong `v2/src/QuanLyHangHoan.App/`, không dùng HTML/WebView/Electron, không viết logic nghiệp vụ trong code-behind (`MainWindow.xaml.cs`, `ReturnOrdersView.xaml.cs`, `PlaceholderPageView.xaml.cs` chỉ gọi `InitializeComponent()`), không sửa `Core` hay `Data`, không đọc dữ liệu đơn thật.
- **Cửa sổ chính `MainWindow` + Sidebar điều hướng (10 mục theo `docs/ui-inventory.md`)**:
  1. `Bảng Hàng Hoàn Về` (màn hình chính, làm đầy đủ)
  2. `Kho lưu trữ (24h)`
  3. `Bảng đơn mới`
  4. `Bảng đơn hủy`
  5. `Thống kê`
  6. `Tạo đơn nhanh`
  7. `Ca quét`
  8. `Danh mục SP`
  9. `Đơn đã xóa`
  10. `Cài đặt`
  - Kèm dải 4 KPI nhanh cố định dưới đáy sidebar (`Đang chờ`, `Đã nhận`, `Đỏ >24h`, `Vàng >12h`), tự cập nhật đồng bộ theo dữ liệu bảng chính.
- **Màn hình Bảng Hàng Hoàn Về (`ReturnOrdersView` + `ReturnOrdersViewModel`)**:
  - `DataGrid` gồm **17 cột** theo mục 2 của `docs/ui-inventory.md`: `STT`, `Cảnh báo (12h/24h)`, `Sàn`, `Mã đơn hàng` (font `Consolas` đậm), `Mã vận đơn (Hoàn / Giao)` (font `Consolas` xanh dễ đọc), `Nhận tại kho`, `Trạng thái kiện`, `Ngày giao`, `Ngày hoàn / hủy`, `Giờ hàng về shop`, `Ngày nhận kho`, `ĐVVC`, `Sản phẩm & Phân loại`, `SL`, `Giá trị`, `Ảnh`, `Ghi chú / Lý do`. Cố định 2 cột đầu (`FrozenColumnCount="2"`).
  - **Lọc theo sàn**: `Cả 2 sàn` / `Shopee` / `TikTok Shop` (kèm số đếm trực tiếp).
  - **Lọc theo trạng thái & cảnh báo**: `Tất cả`, `Chưa nhận`, `Đã nhận`, `Báo Vàng >12h`, `Báo Đỏ >24h`, `Bị mất`, `K.cần nhận`.
  - **Tìm kiếm**: hỗ trợ tiếng Việt **không dấu** trên mã đơn, MVĐ hoàn/giao, ĐVVC, sản phẩm, ghi chú, trạng thái + nút `✕` xóa nhanh.
  - **Sắp xếp**: 6 chế độ trên ComboBox (`Mặc định`, `Cảnh báo Đỏ → Vàng → An toàn`, `Ngày hoàn Mới → Cũ / Cũ → Mới`, `Ngày giao Mới → Cũ`, `Giá trị Cao → Thấp`) + bấm tiêu đề cột `DataGrid`.
  - **Trạng thái trống**: lớp phủ hiển thị thông báo rõ ràng + nút `✕ Xóa toàn bộ bộ lọc`.
- **Quy tắc cảnh báo tập trung một chỗ (`Services/AlertThresholds.cs`)**:
  - Đặt hằng số `YellowHours = 12`, `RedHours = 24` và `MainPolicy = new AlertPolicy(...)` dùng trực tiếp `QuanLyHangHoan.Core.Alerts.AlertPolicy`.
  - Tô màu nền dòng (`#FEF3C7` Vàng / `#FEE2E2` Đỏ) và badge nổi bật (`#D97706` / `#DC2626`).
- **Tách dữ liệu giả sau interface (`IReturnOrderProvider` + `FakeDataService`)**:
  - Sinh 100% dữ liệu giả (có sẵn ví dụ kiện A/B dùng chung 1 `OrderId` tách 2 dòng riêng) và chế độ sinh nhanh **50.000 dòng giả** trên nền (`Task.Run`) qua nút `⚡ Thử 50.000 dòng giả`.
- **Hiệu năng & Virtualization & High DPI**:
  - Bật `EnableRowVirtualization="True"`, `EnableColumnVirtualization="True"`, `VirtualizingPanel.VirtualizationMode="Recycling"`, `ScrollViewer.CanContentScroll="True"`, `RowHeight="36"`, frozen brushes và `app.manifest` (`PerMonitorV2`).
  - Đã đo thực tế trên cửa sổ WPF thật với **50.000 dòng**: sinh + nạp nền ~`597 ms`, lọc/sắp xếp ~`7–35 ms`, hiển thị & chuyển tab không treo UI.
- **9 màn hình còn lại**: dùng `PlaceholderPageView` + `PlaceholderPageViewModel` hiển thị tiêu đề, mô tả, bảng dữ liệu giả minh họa và ghi chú chân trang.
- **Test xUnit (`UiTests/ReturnOrdersViewModelTests.cs`)**: 13 test cases mới (7 test methods, gồm Theory 7 ngưỡng cảnh báo + lọc sàn + lọc Vàng/Đỏ + tìm kiếm không dấu & trạng thái trống + sắp xếp + stress 50.000 dòng + điều hướng 10 mục sidebar).
- **Ảnh chụp màn hình thực tế**: đã lưu 6 ảnh tại `tasks/reports/T002-screens/`.

## 2. Danh sách file tạo / sửa

### Tạo mới trong `v2/src/QuanLyHangHoan.App/`
- `v2/src/QuanLyHangHoan.App/app.manifest`
- `v2/src/QuanLyHangHoan.App/Services/AlertThresholds.cs`
- `v2/src/QuanLyHangHoan.App/Services/IReturnOrderProvider.cs`
- `v2/src/QuanLyHangHoan.App/Services/FakeDataService.cs`
- `v2/src/QuanLyHangHoan.App/Converters/UiConverters.cs`
- `v2/src/QuanLyHangHoan.App/Themes/AppStyles.xaml`
- `v2/src/QuanLyHangHoan.App/ViewModels/NavigationItem.cs`
- `v2/src/QuanLyHangHoan.App/ViewModels/ReturnOrderRowViewModel.cs`
- `v2/src/QuanLyHangHoan.App/ViewModels/ReturnOrdersViewModel.cs`
- `v2/src/QuanLyHangHoan.App/ViewModels/PlaceholderPageViewModel.cs`
- `v2/src/QuanLyHangHoan.App/ViewModels/MainViewModel.cs`
- `v2/src/QuanLyHangHoan.App/Views/ReturnOrdersView.xaml`
- `v2/src/QuanLyHangHoan.App/Views/ReturnOrdersView.xaml.cs`
- `v2/src/QuanLyHangHoan.App/Views/PlaceholderPageView.xaml`
- `v2/src/QuanLyHangHoan.App/Views/PlaceholderPageView.xaml.cs`

### Sửa trong `v2/src/QuanLyHangHoan.App/`
- `v2/src/QuanLyHangHoan.App/QuanLyHangHoan.App.csproj` (thêm gói `CommunityToolkit.Mvvm` 8.4.2 và `<ApplicationManifest>app.manifest</ApplicationManifest>`)
- `v2/src/QuanLyHangHoan.App/App.xaml` & `App.xaml.cs` (nạp `Themes/AppStyles.xaml`, khởi tạo `FakeDataService` -> `MainViewModel` -> `MainWindow`)
- `v2/src/QuanLyHangHoan.App/MainWindow.xaml` & `MainWindow.xaml.cs` (khung Sidebar + `ContentControl` + `DataTemplate`)

### Test & Báo cáo
- Tạo mới: `v2/tests/QuanLyHangHoan.Tests/UiTests/ReturnOrdersViewModelTests.cs`
- Sửa tối thiểu: `v2/tests/QuanLyHangHoan.Tests/QuanLyHangHoan.Tests.csproj` (đổi `net8.0` -> `net8.0-windows`, bật `<UseWPF>true</UseWPF>` và thêm `ProjectReference` tới `QuanLyHangHoan.App.csproj` để `UiTests` gọi trực tiếp được ViewModel của `App` — không sửa `CoreTests.cs`)
- Tạo mới ảnh chụp màn hình:
  - `tasks/reports/T002-screens/01-bang-hang-hoan-ve.png`
  - `tasks/reports/T002-screens/02-sap-xep-bao-do-vang.png`
  - `tasks/reports/T002-screens/03-50k-dong-virtualization.png`
  - `tasks/reports/T002-screens/04-thong-ke-placeholder.png`
  - `tasks/reports/T002-screens/05-kho-luu-tru-placeholder.png`
  - `tasks/reports/T002-screens/06-trang-thai-trong.png`
- Tạo mới: `tasks/reports/T002-report.md`
- Cập nhật: `TASKS.md`

## 3. Cách chạy
```powershell
# Build toàn bộ solution
dotnet build v2/QuanLyHangHoan.sln

# Chạy toàn bộ test (Core + UiTests)
dotnet test v2/QuanLyHangHoan.sln

# Mở ứng dụng WPF
dotnet run --project v2/src/QuanLyHangHoan.App
```

## 4. Kết quả build, test & chạy thực tế

### Kết quả `dotnet build` & `dotnet test`
```text
Build succeeded.
    0 Warning(s)
    0 Error(s)
Time Elapsed 00:00:03.35

Test run for ...\QuanLyHangHoan.Tests.dll (.NETCoreApp,Version=v8.0)
Passed!  - Failed:     0, Passed:    26, Skipped:     0, Total:    26, Duration: 1 s
```
- 13 test cũ của `CoreTests` + 13 test mới trong `UiTests/ReturnOrdersViewModelTests` đều PASS (26/26), 0 lỗi, 0 cảnh báo.

### Kết quả chạy cửa sổ WPF & 50.000 dòng
- Đã chạy thật `dotnet run --project v2/src/QuanLyHangHoan.App`, cửa sổ mở bình thường (`Title: Quản Lý Hàng Hoàn v2 — Native WPF (.NET 8)`), không có lỗi binding WPF (`PresentationTraceSources.DataBindingSource` = 0 lỗi).
- Nạp 50.000 dòng giả lên `DataGrid` thực tế: **nạp nền 597 ms, lọc/sắp xếp 7–35 ms**, toàn bộ render hoàn tất trong ~1,05s không treo giao diện (xem ảnh `03-50k-dong-virtualization.png` và `06-trang-thai-trong.png`).

## 5. Điểm chưa làm / cần lưu ý cho giai đoạn nối dữ liệu thật
- Các nút hành động nghiệp vụ (sửa ô trực tiếp, khóa ô bằng mật khẩu, mở accordion chi tiết nhiều SP, xem phóng to ảnh, nhập/xuất Excel, đồng bộ điện thoại) chưa cài ở T002 vì đây là khung giao diện dữ liệu giả.
- Đã phải sửa `v2/tests/QuanLyHangHoan.Tests/QuanLyHangHoan.Tests.csproj` sang `net8.0-windows` + `<UseWPF>true</UseWPF>` + tham chiếu `QuanLyHangHoan.App.csproj` để chạy được xUnit cho ViewModel ngay trong `v2/tests/QuanLyHangHoan.Tests/UiTests/` như phiếu yêu cầu. Nếu Claude muốn giữ `QuanLyHangHoan.Tests` thuần `net8.0` (chạy đa nền tảng), có thể tách project `QuanLyHangHoan.App.Tests` riêng hoặc đẩy phần ViewModel thuần không phụ thuộc `System.Windows` sang project `Presentation` chung.
- Khi nối `QuanLyHangHoan.Data`, Claude chỉ cần tạo implementation mới cho `IReturnOrderProvider` (và thay 1 dòng trong `App.xaml.cs`).

## 6. Đề xuất gói NuGet (chỉ nêu đề xuất, KHÔNG tự thêm)
- **Hiện tại chưa cần thêm gói DataGrid bên thứ ba**: `DataGrid` native của WPF khi bật `VirtualizationMode="Recycling"` + `RowHeight` cố định đã đạt ~35 ms cho 50.000 dòng.
- **Đề xuất cân nhắc ở Giai đoạn 3 (nếu cần)**:
  - `Microsoft.Extensions.DependencyInjection`: gọn gàng hơn khi nối nhiều service (SQLite, Importer, SyncTransport, Settings) vào các ViewModel.
  - `WPF-UI` (MIT, nhẹ) chỉ nếu chủ dự án muốn bộ icon Segoe Fluent & hộp thoại chuẩn Windows 11 thay cho emoji Unicode trên thanh công cụ/sidebar.

TRẠNG THÁI: XONG
