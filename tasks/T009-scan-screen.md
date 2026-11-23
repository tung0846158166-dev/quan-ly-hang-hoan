# T009 - Màn hình "Quét nhận hàng" trên máy tính + khởi tạo DB lúc khởi động (giao cho: antigravity, người kiểm tra: claude)

Phạm vi: CHỈ `v2/src/QuanLyHangHoan.App/` và `v2/tests/QuanLyHangHoan.Tests/UiTests/`. KHÔNG sửa `Core/`, `Data/`, `ImporterTests/`, test gốc. Thiếu API ở Data thì ghi vào báo cáo.
Đọc trước: `AGENTS.md` (quy tắc bất biến 2, 3, 6), `tasks/reports/T008-review.md`. Không dùng dữ liệu thật trong test.

## API có sẵn (claude đã viết, có test)
- `QuanLyHangHoan.Data.AppDatabase.Initialize(dbPath)`: tạo thư mục/DB/bảng nếu chưa có, idempotent; ném `InvalidOperationException` nếu DB có schema mới hơn app.
- `QuanLyHangHoan.Data.ScanReceiver.Receive(dbPath, code, DateTime now, byte[]? photo = null, string photoMime = "image/jpeg")` -> `ScanResult(Outcome, Ack, Message, Order?)`.
  `ScanOutcome`: `Received` (vừa nhận), `AlreadyReceived` (đã nhận trước đó, giữ nguyên giờ nhận), `Ambiguous` (nhiều kiện khớp, KHÔNG tự nhận), `NotFound`, `NeedsManualCheck` ("Không cần nhận"/"Bị mất"), `Error`.
  `Ack` = true CHỈ khi dữ liệu đã lưu bền. Chỉ khi `Ack` mới được hiện "đã lưu"; các trường hợp khác phải hiện rõ là CHƯA nhận. Đồng bộ, gọi qua `Task.Run`.

## Việc cần làm
1. **Khởi động** (`App.xaml.cs`, chế độ thật): gọi `AppDatabase.Initialize(AppPaths.DatabasePath)` TRƯỚC khi nạp dữ liệu; nếu ném lỗi (schema mới hơn) hiện hộp thoại tiếng Việt rõ ràng và thoát an toàn, không sửa DB.
2. **Màn hình "Quét nhận hàng"** (mục mới trong sidebar nhóm CHUNG, ngay dưới "Trang chủ" hoặc đầu nhóm CHUNG; chọn chỗ hợp lý và ghi vào báo cáo):
   - Ô nhập mã luôn giữ focus (máy quét USB gõ mã + Enter). Enter -> gọi `IScanService` (interface mới ở `App/Services` bọc `ScanReceiver`, có bản giả cho test và `--demo`).
   - Thẻ kết quả LỚN, màu và chữ rõ (đọc được từ xa): xanh "ĐÃ NHẬN" (Received); vàng "ĐÃ NHẬN TRƯỚC ĐÓ" kèm giờ nhận (AlreadyReceived); đỏ "NHIỀU KIỆN KHỚP - KIỂM TRA THỦ CÔNG" (Ambiguous, không đổi gì); đỏ "KHÔNG TÌM THẤY" (NotFound); cam "CẦN KIỂM TRA" kèm lý do (NeedsManualCheck, Error). Hiện mã đơn, mã vận đơn, sàn, loại đơn, sản phẩm của kiện khi có `Order`.
   - Danh sách "Đã quét trong phiên" (mới nhất trên cùng): giờ, mã, kết quả, màu theo kết quả. Có bộ đếm Nhận / Trước đó / Lỗi.
   - Âm báo ngắn khác nhau cho thành công và lỗi (`System.Media.SystemSounds`), tắt/bật được.
   - Đính kèm ảnh tùy chọn: nút "Chọn ảnh" (hộp thoại mở tệp jpg/png) gắn vào lần quét kế tiếp; đọc tệp thành `byte[]` đưa cho `Receive`. Chưa cần camera.
   - Không bao giờ hiện "đã lưu" khi `Ack == false`. Quét trùng liên tiếp cùng mã trong < 1 giây (máy quét gõ đúp) chỉ xử lý một lần.
3. Sau mỗi lần `Received`: tải lại các màn hình/badge sidebar/Trang chủ (như đã làm cho nhập dữ liệu cũ và sweep).
4. `--demo`: dùng bản giả của `IScanService`, không ghi DB.

## Tiêu chí hoàn thành
- `dotnet build` 0 lỗi 0 cảnh báo; `dotnet test` xanh TOÀN BỘ.
- Test trong `UiTests/` (DB tạm bằng `AppDatabase.Initialize` + `LegacyImporter.Import` dữ liệu GIẢ): từng `ScanOutcome` hiện đúng thẻ/màu/chữ; không hiện "đã lưu" khi `Ack=false`; quét A không làm B đổi (kiểm bằng `OrderRepository`); đếm đúng; quét đúp < 1 giây chỉ xử lý một lần; schema mới hơn -> không crash, báo lỗi; ảnh đính kèm được lưu (kiểm bằng `PhotoRepository`).
- Ảnh chụp: màn hình quét ở các trạng thái Received, AlreadyReceived, Ambiguous, NotFound + danh sách phiên -> `tasks/reports/T009-screens/`.
- Báo cáo `tasks/reports/T009-report.md`, dòng cuối `TRẠNG THÁI: XONG` hoặc `CHƯA XONG (lý do)`. Cập nhật `TASKS.md`.
