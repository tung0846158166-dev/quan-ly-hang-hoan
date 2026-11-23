# Báo cáo T021 - Khay hệ thống, tự mở cùng Windows, thông báo cảnh báo vàng/đỏ

## Phạm vi & Quy tắc
- Chỉ sửa trong `v2/src/QuanLyHangHoan.App/` và `v2/tests/QuanLyHangHoan.Tests/UiTests/`.
- Không sửa `Core/`, `Data/` và các test ngoài `UiTests/`.
- Không thiếu API ở `Data/` (`AppSettings` đã có sẵn `AutoStart`, `MinimizeToTray`, `NotifyAlerts`, `Sounds` và `Save`).

## Các thay đổi đã thực hiện
1. **Khay hệ thống & một phiên bản duy nhất (`ITrayService`, `WindowsTrayService`, `FakeTrayService`, `ISingleInstanceGuard`)**:
   - Biểu tượng khay (`NotifyIcon`) với menu: `"Mở app"`, `"Tắt thông báo"` / `"Bật thông báo"`, `"Thoát hẳn"`.
   - Khi `MinimizeToTray` bật: bấm X ẩn cửa sổ xuống khay (không thoát), lần đầu hiện thông báo `"App vẫn chạy ở khay để nhận đơn từ điện thoại"`. Nhấp đúp biểu tượng hoặc chọn `"Mở app"` để khôi phục.
   - Chọn `"Thoát hẳn"` mới đóng cửa sổ thật và gọi `Dispose()` dừng sạch các dịch vụ nền (đồng bộ điện thoại, theo dõi thư mục, Google Sheet, Salework, quét lưu trữ, theo dõi cảnh báo).
   - Khi có đơn Đỏ (> 24h), biểu tượng khay chuyển sang biểu tượng có chấm đỏ.
   - `WindowsSingleInstanceGuard` dùng `Mutex` + `EventWaitHandle` đặt tên theo người dùng Windows (`Local\QuanLyHangHoan_<hash>`) để ngăn mở bản thứ hai và đưa bản đang chạy lên trước.
   - Tham số `--tray` khởi động thẳng xuống khay, không gọi `Show()` lúc mở app.
2. **Tự mở cùng Windows (`IAutoStartService`, `WindowsAutoStartService`, `FakeAutoStartService`)**:
   - Ghi/xóa đúng giá trị `QuanLyHangHoan` trong `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` với đường dẫn exe hiện tại kèm `--tray`; tắt thì xóa đúng khóa của mình, không đụng khóa khác.
3. **Thông báo cảnh báo Vàng/Đỏ (`INotificationService`, `AlertNotificationMonitor`, `AlertNotifyStateStore`)**:
   - Kiểm tra sau khi khởi động 30 giây và định kỳ mỗi 5 phút các đơn chưa nhận (`AlertThresholds.EvaluateOrder`).
   - Gửi MỘT thông báo gộp (`"{N} đơn vừa quá 12h, {M} đơn vừa quá 24h"`), bấm vào khôi phục cửa sổ và điều hướng về Trang chủ (`home`).
   - Ghi nhớ mỗi đơn mỗi mức chỉ báo một lần trong `alert_notify_state.json` (cạnh `settings.json`, tối đa 5.000 khóa, ghi nguyên tử). Không báo đơn đã nhận / không cần nhận / bị mất / chưa có giờ hàng về; tôn trọng `NotifyAlerts`.
4. **Âm thanh (`MainViewModel.OnSyncHandled`)**:
   - Khi `Sounds` bật, phát `PlaySuccess()` khi điện thoại tạo/quét đơn thành công và `PlayError()` khi lỗi/trùng/nhập nhằng/kiểm tra thủ công; đồng bộ `ScanReceiveVm.IsSoundEnabled`.
5. **Cài đặt (`HomeViewModel`, `HomeView.xaml`, `UiSettingsStore`)**:
   - Thêm khối `"⚙️ Chạy nền và thông báo"` ở phần dưới Trang chủ với 4 công tắc (`AutoStart`, `MinimizeToTray`, `NotifyAlerts`, `Sounds`), lưu vào `AppSettings` và đồng bộ với menu khay.
   - Cập nhật `UiSettingsStore.Clone` để giữ đủ các trường mới ở chế độ `--demo`.
6. **Chế độ `--demo`**:
   - Dùng `FakeTrayService`, `FakeAutoStartService`, `FakeNotificationService`, `InMemoryAlertNotifyStateStore`, `FakeSingleInstanceGuard`, `NoOpScanSoundPlayer`: không tạo khay thật, không ghi registry, không chạm `%LocalAppData%`.

## Tệp đã thêm / sửa
- Thêm:
  - `v2/src/QuanLyHangHoan.App/Services/IAutoStartService.cs`
  - `v2/src/QuanLyHangHoan.App/Services/ITrayService.cs`
  - `v2/src/QuanLyHangHoan.App/Services/INotificationService.cs`
  - `v2/src/QuanLyHangHoan.App/Services/ISingleInstanceGuard.cs`
  - `v2/src/QuanLyHangHoan.App/Services/AlertNotifyStateStore.cs`
  - `v2/src/QuanLyHangHoan.App/Services/AlertNotificationMonitor.cs`
  - `v2/tests/QuanLyHangHoan.Tests/UiTests/T021TrayAutoStartNotifyUiTests.cs`
- Sửa:
  - `v2/src/QuanLyHangHoan.App/QuanLyHangHoan.App.csproj`
  - `v2/src/QuanLyHangHoan.App/Services/AppPaths.cs`
  - `v2/src/QuanLyHangHoan.App/Services/UiSettingsStore.cs`
  - `v2/src/QuanLyHangHoan.App/ViewModels/HomeViewModel.cs`
  - `v2/src/QuanLyHangHoan.App/ViewModels/MainViewModel.cs`
  - `v2/src/QuanLyHangHoan.App/Views/HomeView.xaml`
  - `v2/src/QuanLyHangHoan.App/MainWindow.xaml.cs`
  - `v2/src/QuanLyHangHoan.App/App.xaml.cs`

## Ảnh chụp
- `tasks/reports/T021-screens/01-home-background-settings-light.png`: Khối `"⚙️ Chạy nền và thông báo"` (4 công tắc) ở phần dưới Trang chủ (Light theme).
- `tasks/reports/T021-screens/02-home-background-settings-dark.png`: Khối `"⚙️ Chạy nền và thông báo"` ở phần dưới Trang chủ (Dark theme).
- Menu khay hệ thống (`WindowsTrayService` dùng `ContextMenuStrip` của Windows Forms): gồm `"Mở app"`, `"Tắt thông báo"` / `"Bật thông báo"`, đường kẻ ngang, `"Thoát hẳn"`; không chụp trực tiếp vì test dùng `FakeTrayService` và chưa chạy khay thật.

## Kiểm tra
- `dotnet build v2/QuanLyHangHoan.sln --no-restore`: `0 Warning(s), 0 Error(s)`.
- `dotnet test v2/QuanLyHangHoan.sln --no-build`: **446/446 passed** trong **10/10 lần chạy liên tiếp** (gồm 9 test mới trong `T021TrayAutoStartNotifyUiTests`).
- Lưu ý: **chưa thử khay/thông báo/registry thật** (toàn bộ kiểm thử chạy qua `FakeTrayService`, `FakeAutoStartService`, `FakeNotificationService`, `FakeSingleInstanceGuard`).

TRẠNG THÁI: XONG
