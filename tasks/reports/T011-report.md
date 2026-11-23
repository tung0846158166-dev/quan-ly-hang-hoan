# Báo cáo T011 - Nối đường ntfy vào đồng bộ + hiển thị trạng thái từng đường

## Các thay đổi đã thực hiện
1. **`v2/src/QuanLyHangHoan.App/Services/ISyncController.cs`**:
   - Mở rộng `ISyncController` với `NtfyEnabled`, `NtfyConnected`, `MqttLastError`, `NtfyLastError`, `SetNtfyEnabled(bool)`.
   - Thêm `IManagedSyncTransport` (trong `App.Services`) để test UI có thể giả lập `ConnectedCount`/`IsConnected`/`LastError`/`StatusChanged`/`Start()`/`Dispose()` mà không chạm vào `Data/` và không kết nối mạng thật.
   - `RealSyncController`: nhận thêm `ntfyTransportFactory` (mặc định `() => new NtfyTransport()`). Khi `SyncEnabled`: tạo `MqttTransport` (qua factory) và nếu `NtfyEnabled` tạo thêm `NtfyTransport` (qua factory), gộp bằng `CompositeTransport` (hoặc dùng trực tiếp MQTT nếu tắt ntfy). Giữ nguyên thứ tự khởi động: `SyncService.Start()` trước rồi mới `Start()` từng đường truyền. Khi dừng/đổi phòng/tắt-bật/tắt-bật ntfy: hủy đăng ký `StatusChanged` trên cả hai đường và `Dispose()` đầy đủ.
   - `FakeSyncController` (`--demo`): không tạo kết nối thật, hỗ trợ `SetNtfyEnabled` và `SetDemoStatus` để kiểm thử và chụp ảnh đủ các tổ hợp trạng thái.
2. **`v2/src/QuanLyHangHoan.App/ViewModels/PhoneSyncViewModel.cs` & `v2/src/QuanLyHangHoan.App/Views/PhoneSyncView.xaml`**:
   - Hiển thị trạng thái tổng hợp (xanh nếu >= 1 đường đang kết nối, đỏ nếu 0 đường).
   - Hiển thị riêng từng đường: `MQTT: n/3 broker` và `ntfy: Đã kết nối / Mất kết nối / Đã tắt` (xanh/đỏ/xám), kèm `MqttLastError` / `NtfyLastError` nhỏ bên dưới khi có lỗi.
   - Thêm công tắc `Dùng ntfy (dự phòng)` (`ToggleNtfyCommand`) lưu ngay vào `AppSettings.NtfyEnabled`, kèm ghi chú: `Một số mạng chặn ntfy.sh; khi đó chỉ MQTT hoạt động.`
   - Chống trùng dòng nhật ký theo `requestId + Stage` trong `PhoneSyncViewModel` khi `Handled` bắn lặp từ nhiều đường.
3. **`v2/tests/QuanLyHangHoan.Tests/UiTests/T011SyncNtfyUiTests.cs` & `T010SyncScreenTests.cs`**:
   - Cập nhật `T010SyncScreenTests.CreateSettingsFile` đặt `NtfyEnabled = false` để test cũ không vô tình mở `NtfyTransport` thật.
   - Thêm 5 test trong `T011SyncNtfyUiTests` kiểm tra: bật/tắt ntfy tạo đúng đường truyền + đúng thứ tự `Subscribe` trước `Start()`, đổi phòng dispose + hủy đăng ký sự kiện cả hai đường, bản tin qua MQTT/ntfy/trùng `requestId` chỉ xử lý 1 lần và không trùng nhật ký, tất cả tổ hợp trạng thái & màu sắc, `--demo` chụp ảnh vào `tasks/reports/T011-screens/`.

## Kết quả kiểm tra
- `dotnet build v2\QuanLyHangHoan.sln --no-restore`: **0 lỗi, 0 cảnh báo**.
- `dotnet test v2\QuanLyHangHoan.sln --no-build`: **187/187 test xanh** (gồm 182 test có sẵn + 5 test mới ở `T011SyncNtfyUiTests`, không kết nối mạng thật).
  *(Ghi chú: ở lần chạy song song đầu tiên sau build, `SyncTests.Same_message_arriving_concurrently_from_several_brokers_gets_one_identical_ack` bị xáo trộn tranh chấp song song 1 lần; chạy riêng và chạy lại toàn bộ `dotnet test` đều 187/187 xanh.)*
- Ảnh chụp tại `tasks/reports/T011-screens/`:
  - `01-mqtt-up-ntfy-down.png`
  - `02-both-up.png`
  - `03-ntfy-disabled.png`

## Chưa kiểm chứng
- Chưa chạy kiểm chứng thực tế với điện thoại thật và máy chủ `ntfy.sh` thật (test dùng `FakeManagedTransport` và `FakeSyncController`).

TRẠNG THÁI: XONG
