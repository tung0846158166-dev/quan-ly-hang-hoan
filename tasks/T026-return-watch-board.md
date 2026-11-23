# T026 - Màn "Hàng hoàn về" (Bảng Hàng Hoàn Về, 4 chặng) (giao cho: antigravity, người kiểm tra: claude)

Phạm vi: CHỈ `v2/src/QuanLyHangHoan.App/` và `v2/tests/QuanLyHangHoan.Tests/UiTests/`. KHÔNG sửa `Core/`, `Data/` và test ngoài `UiTests/`. Thiếu API ở Data thì ghi vào báo cáo.
Đọc trước: `AGENTS.md` (quy tắc 1, 2, 7, 8), `v2/src/QuanLyHangHoan.Data/ReturnWatchService.cs` (đọc kỹ phần tóm tắt đầu tệp), `tasks/reports/T018-review.md` (chi tiết đơn), `tasks/reports/T013-review.md` (thao tác trên dòng, xác nhận + hoàn tác). Test dùng `WpfTestHost`, DB tạm + dữ liệu GIẢ; `dotnet test` toàn bộ 10 lần liên tiếp 0 lần đỏ.

## Bối cảnh
App cũ (v1.9.72) có "Bảng Hàng Hoàn Về": khi khách yêu cầu hoàn và sàn chuyển kiện sang trạng thái **"Đã đồng ý"**, kiện TỰ vào bảng và được theo dõi qua 4 chặng: **1 Đã đồng ý -> 2 Đang về -> 3 Đã về shop -> 4 Đã nhận kho** (quét). Mục đích: biết hàng nào đang trên đường về để chuẩn bị nhận. Phần dữ liệu đã làm xong ở v2; phiếu này làm màn hình.

## API có sẵn (claude đã viết, có test; `QuanLyHangHoan.Data.ReturnWatchService`)
- `Load(dbPath, now)` -> `IReadOnlyList<ReturnWatchEntry(StoredOrder Stored, long Since, int Stage)>`: tự cập nhật tư cách thành viên rồi trả các đơn đang ở bảng, chặng thấp trước rồi giờ vào sớm trước. `Since` = giờ vào bảng (ms epoch, giờ máy). `StageNames` = {"", "Đã đồng ý", "Đang về", "Đã về shop", "Đã nhận kho"}.
- `Remove(dbPath, orderId, now)` (bỏ khỏi bảng, KHÔNG xóa đơn) và `Restore(dbPath, orderId, now)` (đưa lại, dùng làm "Hoàn tác") -> `EditResult(Kind, Message)` (`Saved`, `NoChange`, `NotFound`, `Error`). `orderId` là `Order.Id`.
- Quy tắc đã đảm bảo ở Data (đừng làm lại ở UI): vào bảng một lần rồi ở lại dù trạng thái sàn đổi; đơn "Không cần nhận"/"Bị mất"/đã lưu trữ/đã xóa không hiện; đơn đã nhận từ trước khi vào bảng thì không vào; kiện A/B/C cùng mã đơn là các dòng riêng.
- Giờ hàng về shop: `Order.ReturnedAt` (chuỗi); dùng cùng cách đọc ngày với bảng đơn hiện có (`AlertThresholds.TryParseVietnamDateTime`).
Gọi qua `Task.Run`, bọc interface mới ở `App/Services` có bản giả cho test/`--demo`.

## Việc cần làm
1. Mục sidebar **"Hàng hoàn về"** (nhóm CHUNG, ngay trên "Bảng đơn mới"; badge = số đơn chặng 1-3, tức chưa nhận kho).
2. Màn hình: hàng nút lọc kèm số đếm: **Đang theo dõi (chặng 1-3)** (mặc định), Đã đồng ý, Đang về, Đã về shop, Đã nhận kho, Tất cả. Ô tìm (mã đơn, mã vận đơn, mã hoàn, tên sản phẩm; không phân biệt dấu/hoa thường).
3. Bảng cột: STT, Mã đơn, Sàn, Mã vận đơn hoàn (nếu chưa có thì mã vận đơn), Sản phẩm, **Giờ vào bảng**, **Tiến trình** (4 chấm/nhãn "Đã đồng ý › Đang về › Đã về shop › Đã nhận kho", chặng đã qua tô màu, kèm dòng nhỏ "Sàn: <trạng thái sàn>"), Giờ hàng về shop, Ngày nhận kho. Màu/biểu tượng cảnh báo vàng/đỏ (12h/24h kể từ giờ hàng về, chỉ chặng 3) dùng lại thành phần cảnh báo của bảng đơn hiện có. Tối đa 1.500 dòng hiển thị (ghi "còn N dòng, hãy lọc/tìm").
4. Thao tác (menu chuột phải + nút `•••`, dùng được bằng bàn phím): **Xem chi tiết** (mở `OrderDetailWindow` của T018), **Xem ở bảng chính** (chuyển sang màn Shopee/TikTok tương ứng và chọn đúng dòng nếu làm gọn được; nếu không thì chỉ mở chi tiết), **Bỏ khỏi bảng** (hộp xác nhận: "Chỉ bỏ khỏi bảng theo dõi, đơn vẫn còn ở Bảng chính." + thông báo nhỏ "Hoàn tác" ~8 giây gọi `Restore`; phím Delete trên dòng mở cùng xác nhận). Chọn nhiều dòng (Ctrl/Shift) rồi "Bỏ khỏi bảng": xác nhận nêu số lượng, chạy tuần tự, báo "n thành công, m lỗi".
5. Tải lại khi: mở màn, sau khi quét nhận kiện/nạp đơn/đồng bộ điện thoại (dùng cơ chế gộp ~500 ms của T019), sau thao tác của chính màn này. Giữ vị trí cuộn/dòng đang chọn nếu còn. Bảng trống: "Chưa có đơn nào trong bảng này."
6. Trang chủ: thêm một ô nhỏ "Hàng đang về: N" (số đơn chặng 2) nếu thêm gọn được (không bắt buộc; ghi vào báo cáo nếu bỏ qua).
7. Theme sáng/tối. `--demo`: dữ liệu giả đủ 4 chặng, không đọc/ghi đĩa.

## Tiêu chí hoàn thành
- `dotnet build` 0 lỗi 0 cảnh báo; `dotnet test` xanh 10/10.
- Test mới trong `UiTests/` (DB tạm, đơn giả có trạng thái sàn "Đã đồng ý"/"Đang vận chuyển về"/có giờ hàng về/đã nhận): mỗi chặng hiện đúng và đúng số đếm; lọc từng chặng; tìm kiếm; sắp xếp chặng rồi giờ vào; quét nhận một kiện thì sau tải lại thành chặng 4; "Bỏ khỏi bảng" biến khỏi bảng nhưng đơn vẫn ở Bảng chính, "Hoàn tác" đưa lại; xác nhận Hủy thì không gọi API; nhiều dòng báo đúng số thành công/lỗi; kiện A và B cùng mã đơn là hai dòng riêng, bỏ A không đổi B; mở chi tiết đúng kiện; badge sidebar đúng; `--demo` không chạm đĩa.
- Ảnh chụp `tasks/reports/T026-screens/`: bảng đủ 4 chặng, đang lọc một chặng, hộp xác nhận bỏ khỏi bảng + thông báo Hoàn tác, bảng trống.
- Báo cáo `tasks/reports/T026-report.md`, dòng cuối `TRẠNG THÁI: XONG` hoặc `CHƯA XONG (lý do)`; ghi rõ "chưa thử với dữ liệu thật". Cập nhật `TASKS.md`.
