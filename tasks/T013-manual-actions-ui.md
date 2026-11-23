# T013 - Giao diện thao tác thủ công trên đơn (giao cho: antigravity, người kiểm tra: claude)

CHỈ chạy SAU KHI T012 xong và đã có `tasks/reports/T012-review.md` (tránh hai agent cùng sửa `App/`).
Phạm vi: CHỈ `v2/src/QuanLyHangHoan.App/` và `v2/tests/QuanLyHangHoan.Tests/UiTests/`. KHÔNG sửa `Core/`, `Data/`, `ImporterTests/`, test gốc. Thiếu API ở Data thì ghi vào báo cáo.
Đọc trước: `AGENTS.md` (quy tắc 1, 2, 8), `tasks/reports/T012-review.md`, `v2/src/QuanLyHangHoan.Data/OrderEditor.cs`. Không dùng dữ liệu thật trong test.

## API có sẵn (claude đã viết, có test; `QuanLyHangHoan.Data.OrderEditor`)
- `SetField(dbPath, orderId, EditField field, string newValue, DateTime now, string? expectedOld = null)`; `EditField`: `Status, Note, UserNote, BuyerNote, Carrier, Deadline, IsWatchlist ("0"/"1"), WatchlistReason`. `Status` chỉ nhận đúng 5 giá trị của `ReceiveStatus` (Hàng đã trả, Chưa trả, Không cần nhận, Bị mất, Chưa về).
- `Delete(dbPath, orderId, string from, now)` (xóa MỀM, `from` = tên bảng nguồn, ví dụ "Shopee - Hoàn hàng"), `Restore(dbPath, orderId, now)`, `Archive(dbPath, orderId, now)`, `Unarchive(dbPath, orderId, now)`, `History(dbPath, orderId)` -> `IReadOnlyList<HistoryEntry(Time, Field, Label, OldValue, NewValue, EditType)>` mới nhất trước.
- Tất cả trả `EditResult(Kind, Message)`; `Kind`: `Saved, NoChange, Invalid, Conflict, NotEditable, NotFound, Error`; `Ok` = Saved hoặc NoChange. `orderId` là `Order.Id`.
- Quy tắc đã đảm bảo ở Data (đừng làm lại ở UI): ghi lịch sử + nhật ký cùng giao dịch, giữ giờ nhận đầu tiên, đơn đã xóa không sửa được, `Conflict` khi giá trị hiện tại khác `expectedOld`.
- Gọi qua `Task.Run`, bọc bằng interface mới ở `App/Services` (ví dụ `IOrderEditService`) có bản giả cho test và `--demo` (bản giả sửa trong bộ nhớ, KHÔNG ghi DB).

## Việc cần làm
1. **Thao tác trên dòng** (menu chuột phải VÀ nút/menu "..." ở cuối dòng, để dùng được cả bằng bàn phím) ở 3 bảng sàn: Đổi trạng thái (danh sách 5 giá trị, ghi rõ trạng thái hiện tại), Ghi chú của tôi (hộp nhập nhiều dòng), Sửa lý do, Bật/tắt theo dõi (kèm lý do), Dời sang kho lưu trữ, Xóa, Xem lịch sử sửa.
2. **Kho lưu trữ**: Trả về bảng chính (`Unarchive`), Xem lịch sử, Xóa. **Đã xóa**: Khôi phục (`Restore`), Xem lịch sử (đọc được); không có thao tác sửa trên đơn đã xóa.
3. **Xóa luôn có xác nhận** (hộp thoại nêu mã vận đơn, mã đơn, nói rõ "đơn vẫn khôi phục được ở màn Đã xóa"), kèm thông báo nhỏ "Đã xóa - Hoàn tác" trong ~8 giây (Hoàn tác = `Restore`). Phím Delete trên dòng đang chọn mở cùng hộp xác nhận.
4. **Nhiều dòng**: chọn nhiều dòng (Ctrl/Shift) rồi "Đánh dấu đã nhận" hoặc "Dời sang lưu trữ" hoặc "Xóa": hộp xác nhận nêu SỐ LƯỢNG; chạy tuần tự, cuối cùng báo "n thành công, m không đổi/lỗi" kèm lý do lỗi đầu tiên. Quy tắc bất biến: thao tác trên kiện nào chỉ tác động đúng kiện đó (kiện A/B/C cùng mã đơn là các dòng riêng, không bao giờ lan sang kiện khác).
5. **Xử lý kết quả**: `Saved` -> tải lại các màn hình/badge/Trang chủ (như các phiếu trước) và giữ vị trí cuộn/dòng đang chọn nếu còn; `NoChange` -> không báo lỗi; `Conflict` -> thông báo "đã bị thay đổi ở nơi khác (ví dụ vừa quét)" và tải lại, KHÔNG ghi đè; `NotEditable`/`NotFound`/`Invalid`/`Error` -> thông báo tiếng Việt rõ, không sập. Luôn truyền `expectedOld` = giá trị đang hiển thị khi sửa.
6. **Khung "Lịch sử sửa"**: bảng giờ, trường, giá trị cũ -> mới, loại (manual...), mới nhất trên cùng; trống thì ghi "Chưa có sửa đổi nào".
7. `--demo`: dùng bản giả, thao tác thấy ngay trên dữ liệu giả, không đụng DB.

## Tiêu chí hoàn thành
- `dotnet build` 0 lỗi 0 cảnh báo; `dotnet test` xanh TOÀN BỘ.
- Test trong `UiTests/` (DB tạm bằng `AppDatabase.Initialize` + `LegacyImporter.Import` dữ liệu GIẢ): đổi trạng thái/ghi chú/theo dõi lưu đúng và hiện lại; xóa -> biến khỏi bảng, hiện ở Đã xóa, Hoàn tác và Khôi phục đưa về; Lưu trữ/Trả về; thao tác trên kiện A không đổi kiện B cùng mã đơn; Conflict không ghi đè; nhiều dòng báo đúng số thành công/lỗi; xác nhận Hủy thì không gọi API; lịch sử hiện đúng và có dòng mới sau mỗi sửa; `--demo` không ghi DB.
- Ảnh chụp: menu thao tác trên dòng, hộp xác nhận xóa, thông báo Hoàn tác, khung lịch sử, kết quả nhiều dòng, màn Đã xóa với nút Khôi phục -> `tasks/reports/T013-screens/`.
- Báo cáo `tasks/reports/T013-report.md`, dòng cuối `TRẠNG THÁI: XONG` hoặc `CHƯA XONG (lý do)`. Cập nhật `TASKS.md`.
