# Báo cáo T010 - Đồng bộ điện thoại: khởi động dịch vụ + màn hình "Đồng bộ điện thoại"

## Tóm tắt thay đổi (chỉ trong `v2/src/QuanLyHangHoan.App/` và `v2/tests/QuanLyHangHoan.Tests/UiTests/`)
- `Services/AppPaths.cs`: thêm `SettingsPath` (`%LocalAppData%\QuanLyHangHoan\v2\settings.json`).
- `Services/ISyncController.cs`: thêm `ISyncController`, `RealSyncController` (nạp `AppSettings.LoadOrCreate`, khởi động `SyncService.Start()` rồi `MqttTransport.Start()`, bắt lỗi khởi động để không sập/chậm app, hỗ trợ bật/tắt đồng bộ và tạo mã phòng mới + lưu `AppSettings`, `Dispose()` khi thoát) và `FakeSyncController` (dùng cho `--demo`, không tạo `MqttTransport`, không kết nối mạng thật).
- `Services/IClipboardService.cs` & `Services/IConfirmationService.cs`: bọc `Clipboard` và `MessageBox` qua interface để test không cần tương tác UI thật.
- `ViewModels/PhoneSyncViewModel.cs` & `Views/PhoneSyncView.xaml` (+ `.xaml.cs`):
  - Hiển thị mã phòng lớn (font `Consolas`), nút `Sao chép`, nút `Tạo mã mới` (có xác nhận `"Điện thoại phải nhập lại mã mới"`).
  - Cảnh báo đỏ `"Mã phòng quá yếu, người lạ có thể đoán"` + nút `"Tạo mã mạnh"` khi `!SyncRoom.IsStrong(room)`, kèm ghi chú `"Bản tin gửi qua broker công cộng chưa mã hóa; giữ bí mật mã phòng."`.
  - Trạng thái kết nối `"Đã kết nối n/3 broker"` (xanh khi >= 1, đỏ khi 0), `LastError` ở dòng nhỏ, công tắc bật/tắt đồng bộ lưu `AppSettings` và áp dụng ngay, marshal mọi cập nhật từ luồng nền về UI thread.
  - Nhật ký nhận từ điện thoại (mới nhất trên cùng, tối đa 200 dòng, màu xanh/vàng/đỏ theo `Outcome`).
  - Ghi chú hướng dẫn nhập vào `mobile_scanner`.
- `App.xaml.cs`, `MainViewModel.cs`, `MainWindow.xaml`: gắn mục sidebar `"Đồng bộ điện thoại"` (`phone_sync`) vào nhóm `CHUNG`, khởi động `RealSyncController` ở chế độ thật sau `AppDatabase.Initialize`, dùng `FakeSyncController` ở `--demo`, tải lại dữ liệu toàn app (marshal UI thread) khi `Handled` có `Outcome == "success"`, và `Dispose` khi thoát.
- `UiTests/T010SyncScreenTests.cs`: kiểm thử bằng `InMemoryTransport`, SQLite tạm (`AppDatabase.Initialize` + `LegacyImporter.Import` dữ liệu giả), `AppSettings` tạm, `--demo`, marshal sự kiện từ luồng nền và xuất ảnh chụp.
- `UiTests/ReturnOrdersViewModelTests.cs` & `UiTests/T006PlatformSplitTests.cs`: cập nhật số lượng và danh sách khóa sidebar (10 -> 11 mục, thêm `phone_sync`).

## Kết quả kiểm tra
- `dotnet build v2/QuanLyHangHoan.sln --no-incremental --nologo`: **0 lỗi, 0 cảnh báo**.
- `dotnet test v2/QuanLyHangHoan.sln --no-build --nologo`: **164/164 xanh** (0 failed, 0 skipped).
- Ảnh chụp màn hình tại `tasks/reports/T010-screens/`:
  - `01-sync-connected.png` (kết nối 2/3 broker, có nhật ký xanh/vàng/đỏ)
  - `02-sync-disconnected.png` (0/3 broker, có `LastError`)
  - `03-sync-weak-room.png` (mã yếu `SHP-2026`, hiện cảnh báo đỏ + nút `Tạo mã mạnh`)
- Chưa kiểm chứng trên broker MQTT thật / điện thoại thật trong phiếu này (chỉ test bằng `InMemoryTransport` và stub theo yêu cầu).

TRẠNG THÁI: XONG
