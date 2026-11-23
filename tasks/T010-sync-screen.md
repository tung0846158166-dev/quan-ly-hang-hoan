# T010 - Đồng bộ điện thoại: khởi động dịch vụ + màn hình "Đồng bộ điện thoại" (giao cho: antigravity, người kiểm tra: claude)

Phạm vi: CHỈ `v2/src/QuanLyHangHoan.App/` và `v2/tests/QuanLyHangHoan.Tests/UiTests/`. KHÔNG sửa `Core/`, `Data/`, `ImporterTests/`, test gốc. Thiếu API ở Data thì ghi vào báo cáo.
Đọc trước: `AGENTS.md`, `docs/sync-design.md` (đọc kỹ mục RỦI RO BẢO MẬT), `tasks/reports/T009-review.md`. Không dùng dữ liệu thật trong test. TEST UI KHÔNG ĐƯỢC KẾT NỐI MẠNG/broker thật: dùng `InMemoryTransport` hoặc bản giả của `ISyncTransport`.

## API có sẵn (claude đã viết, có test; `QuanLyHangHoan.Data` và `QuanLyHangHoan.Data.Sync`)
- `AppSettings.LoadOrCreate(path)` / `.Save(path)`: `Room` (lần đầu tự tạo mã NGẪU NHIÊN 24 ký tự), `SyncEnabled`, `Brokers`. Tệp hỏng được giữ lại, không ghi đè. Đường dẫn mặc định: định nghĩa trong `AppPaths` (ví dụ `%LocalAppData%\QuanLyHangHoan\v2\settings.json`).
- `SyncRoom.IsStrong(room)`, `SyncRoom.Generate()`, `SyncRoom.NtfyTopic(room)`.
- `MqttTransport(IEnumerable<string> brokerUris)`: `Start()`, `ConnectedBrokers` (0..n), `LastError`, sự kiện `StatusChanged`, `Dispose()`. Kết nối thật tới broker công cộng: chỉ dùng ở chế độ thật.
- `SyncService(dbPath, room, ISyncTransport, Func<DateTime> clock)`: `Start()`, `Dispose()`, sự kiện `Handled(SyncHandled(RequestId, Code, Outcome, Message, At))` bắn SAU khi đã lưu và đã gửi xác nhận; `Outcome` = `success | duplicate | not_found | ambiguous | error`.
- `ISyncTransport` + `InMemoryTransport` để test.

## Việc cần làm
1. **Khởi động (chế độ thật, sau `AppDatabase.Initialize`)**: nạp `AppSettings`; nếu `SyncEnabled` thì tạo `MqttTransport(settings.Brokers)` + `SyncService(AppPaths.DatabasePath, settings.Room, transport, () => DateTime.Now)`, `service.Start()` rồi `transport.Start()`. Đóng (`Dispose`) khi thoát app. Lỗi mạng/broker KHÔNG được làm app sập hoặc chậm khởi động (kết nối chạy nền, MqttTransport đã tự lo). Bọc qua interface (ví dụ `ISyncController`) để test được; `--demo` dùng bản giả, KHÔNG mở kết nối thật.
2. **Khi `Handled` bắn với `Outcome == "success"`** (kiện vừa nhận qua điện thoại): tải lại các màn hình/badge sidebar/Trang chủ như đã làm ở màn Quét (nhớ marshal về UI thread, sự kiện đến từ luồng nền). Mọi `Handled` đều ghi vào nhật ký màn hình đồng bộ.
3. **Màn hình "Đồng bộ điện thoại"** (mục sidebar nhóm CHUNG, gần "Quét nhận hàng"):
   - **Mã phòng** hiển thị lớn (font đơn cách), nút "Sao chép", nút "Tạo mã mới" (có hộp xác nhận: "Điện thoại phải nhập lại mã mới"); đổi mã thì lưu `AppSettings`, dừng/khởi động lại dịch vụ với phòng mới.
   - Cảnh báo ĐỎ rõ ràng nếu `!SyncRoom.IsStrong(room)` ("Mã phòng quá yếu, người lạ có thể đoán"), kèm nút "Tạo mã mạnh". Có ghi chú ngắn: "Bản tin gửi qua broker công cộng chưa mã hóa; giữ bí mật mã phòng."
   - Trạng thái kết nối: "Đã kết nối n/3 broker" (xanh khi >= 1, đỏ khi 0), `LastError` ở dòng nhỏ; cập nhật theo `StatusChanged` (marshal UI thread). Công tắc bật/tắt đồng bộ (lưu `AppSettings`, áp dụng ngay).
   - Nhật ký các lần nhận từ điện thoại (mới nhất trên cùng): giờ, mã, kết quả (màu như màn Quét: success xanh, duplicate vàng, not_found/ambiguous/error đỏ), tối đa 200 dòng trong bộ nhớ.
   - Thông tin để nhập vào điện thoại: mã phòng; ghi chú "Trang quét điện thoại cũ vẫn dùng được: mở mobile_scanner, nhập đúng mã phòng này".
4. Ngoài phạm vi (đừng làm): ảnh từ điện thoại, ntfy, mã hóa bản tin, QR.

## Tiêu chí hoàn thành
- `dotnet build` 0 lỗi 0 cảnh báo; `dotnet test` xanh TOÀN BỘ.
- Test trong `UiTests/` (dùng `InMemoryTransport`, DB tạm bằng `AppDatabase.Initialize` + `LegacyImporter.Import` dữ liệu GIẢ, `AppSettings` ở thư mục tạm): gửi bản tin quét giả qua transport -> đơn được nhận, nhật ký có dòng đúng màu/kết quả, màn hình khác được tải lại; mã yếu hiện cảnh báo, "Tạo mã mới" đổi phòng (bản tin phòng cũ không còn được xử lý) và lưu settings; tắt đồng bộ dừng xử lý; `--demo` không tạo `MqttTransport`; sự kiện từ luồng nền không ném lỗi cross-thread.
- Ảnh chụp: màn hình đồng bộ (kết nối tốt, 0 broker, mã yếu, có nhật ký) -> `tasks/reports/T010-screens/`.
- Báo cáo `tasks/reports/T010-report.md`, dòng cuối `TRẠNG THÁI: XONG` hoặc `CHƯA XONG (lý do)`. Cập nhật `TASKS.md`.
