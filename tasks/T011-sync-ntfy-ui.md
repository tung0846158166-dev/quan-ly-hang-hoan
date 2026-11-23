# T011 - Nối đường ntfy vào đồng bộ + hiển thị trạng thái từng đường (giao cho: antigravity, người kiểm tra: claude)

Phạm vi: CHỈ `v2/src/QuanLyHangHoan.App/` và `v2/tests/QuanLyHangHoan.Tests/UiTests/`. KHÔNG sửa `Core/`, `Data/`, `ImporterTests/`, test gốc. Thiếu API ở Data thì ghi vào báo cáo.
Đọc trước: `AGENTS.md`, `docs/sync-design.md`, `tasks/reports/T010-review.md`, `v2/src/QuanLyHangHoan.App/Services/ISyncController.cs`. Không dùng dữ liệu thật trong test. TEST UI KHÔNG ĐƯỢC KẾT NỐI MẠNG THẬT (không ntfy.sh, không broker): dùng `InMemoryTransport`/bản giả.

## API có sẵn (claude đã viết, có test; `QuanLyHangHoan.Data.Sync`)
- `NtfyTransport(HttpClient? http = null, string baseUrl = "https://ntfy.sh")`: `ISyncTransport` + `IDisposable`; `Start()`, `Connected` (bool), `LastError`, sự kiện `StatusChanged`. Chỉ mở kết nối cho phòng đã `Subscribe`.
- `CompositeTransport(params ISyncTransport[])`: gộp nhiều đường (gửi tới tất cả, nghe từ tất cả, một đường lỗi không chặn đường khác), `Dispose()` đóng tất cả. KHÔNG có `Start()` riêng: gọi `Start()` của từng đường con.
- `AppSettings.NtfyEnabled` (mặc định true; tệp cài đặt cũ không có trường này vẫn nạp được).
- Bản tin trùng (cùng bản tin đến cả MQTT lẫn ntfy) đã được `SyncReceiver` chống lặp theo requestId/captureId: KHÔNG cần lọc trùng ở giao diện, nhưng nhật ký màn hình đồng bộ chỉ nên hiện mỗi `requestId`+`Stage` MỘT lần (bỏ dòng trùng) nếu `Handled` bắn lặp.

## Việc cần làm
1. `RealSyncController` (`ISyncController.cs`): khi `SyncEnabled`, tạo `MqttTransport(settings.Brokers)` và, nếu `settings.NtfyEnabled`, thêm `NtfyTransport`; truyền `CompositeTransport` (hoặc chính MqttTransport nếu tắt ntfy) vào `SyncService`. Giữ NGUYÊN thứ tự khởi động đã đúng: `service.Start()` rồi `Start()` từng đường. Dừng/khởi động lại khi đổi phòng, tắt/bật, tắt/bật ntfy: dispose đủ mọi đường, hủy đăng ký sự kiện `StatusChanged` của cả hai. Factory của test phải thay được cả hai đường (không tạo kết nối thật).
2. Trạng thái màn hình "Đồng bộ điện thoại": hiển thị riêng từng đường: "MQTT: n/3 broker" và "ntfy: Đã kết nối / Mất kết nối / Đã tắt" (xanh/đỏ/xám), kèm `LastError` của từng đường (dòng nhỏ). Tổng hợp: xanh nếu >= 1 đường đang kết nối, đỏ nếu không đường nào. Thêm công tắc "Dùng ntfy (dự phòng)" lưu vào `AppSettings.NtfyEnabled`, áp dụng ngay. Ghi chú nhỏ: "Một số mạng chặn ntfy.sh; khi đó chỉ MQTT hoạt động" (máy của chủ dự án đã gặp trường hợp này).
3. `--demo` (`FakeSyncController`): không tạo đường truyền thật, hiển thị trạng thái giả đủ các tổ hợp để chụp ảnh.

## Tiêu chí hoàn thành
- `dotnet build` 0 lỗi 0 cảnh báo; `dotnet test` xanh TOÀN BỘ.
- Test trong `UiTests/`: bật ntfy tạo đủ hai đường, tắt ntfy chỉ còn MQTT, đổi phòng dispose cả hai, bản tin nhận qua đường nào cũng được xử lý đúng một lần (kiểm bằng trạng thái đơn + nhật ký không trùng), trạng thái tổng hợp đúng các tổ hợp (MQTT up/ntfy down, ngược lại, cả hai down), cài đặt `NtfyEnabled` được lưu.
- Ảnh chụp: MQTT up + ntfy down, cả hai up, ntfy đã tắt -> `tasks/reports/T011-screens/`.
- Báo cáo `tasks/reports/T011-report.md`, dòng cuối `TRẠNG THÁI: XONG` hoặc `CHƯA XONG (lý do)`. Cập nhật `TASKS.md`.
