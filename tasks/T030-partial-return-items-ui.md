# T030 - Hiển thị đơn hoàn MỘT PHẦN: giữ đủ sản phẩm, sản phẩm đã hoàn khác màu (giao cho: antigravity, người kiểm tra: claude)

Phạm vi: CHỈ `v2/src/QuanLyHangHoan.App/` và `v2/tests/QuanLyHangHoan.Tests/UiTests/`. KHÔNG sửa `Core/`, `Data/` và test ngoài `UiTests/`. Thiếu API ở Data thì ghi vào báo cáo.
Đọc trước: `AGENTS.md` (quy tắc 1, 2), `tasks/reports/T018-review.md` (cửa sổ Chi tiết đơn), `tasks/reports/T026-review.md`, `v2/src/QuanLyHangHoan.Core/Models/Order.cs` (`OrderItem.ReturnedQty`), `v2/src/QuanLyHangHoan.Data/OrderExporter.cs` (`ReturnedItemsText`). Test dùng `WpfTestHost`, DB tạm + dữ liệu GIẢ; `dotnet test` toàn bộ 10 lần liên tiếp 0 lần đỏ.

## Bối cảnh (yêu cầu của chủ dự án 23/11/2026)
Đơn A gửi đi 3 sản phẩm, khách chỉ hoàn 1 sản phẩm. Dòng đơn vẫn phải **giữ đủ 3 sản phẩm**, và **sản phẩm nào đã hoàn về phải khác màu** để kho biết kiện này chỉ chứa sản phẩm nào. Phần dữ liệu đã làm xong (schema v6): mỗi `OrderItem` có `ReturnedQty` (số lượng đã hoàn; 0 = chưa biết/không hoàn). Khi nạp file/Sheet hoàn hàng, mặt hàng nằm trong yêu cầu hoàn có `ReturnedQty > 0`; các mặt hàng còn lại giữ `ReturnedQty = 0`.

## Quy tắc hiển thị (BẮT BUỘC)
Với MỘT đơn, xét danh sách `Items`:
- **Hoàn một phần** = có ít nhất một mặt hàng `ReturnedQty > 0` VÀ ít nhất một mặt hàng `ReturnedQty == 0`.
  - Mặt hàng đã hoàn: chữ đậm + màu nhấn (đề xuất cam/đỏ nhạt theo bảng màu hiện có, đủ tương phản ở cả theme sáng/tối) + nhãn "Hoàn N" (N = `ReturnedQty`).
  - Mặt hàng không hoàn: chữ xám nhạt + nhãn nhỏ "Không hoàn" (vẫn đọc được, KHÔNG ẩn, KHÔNG gạch ngang làm mất chữ).
- **Hoàn đủ** (mọi mặt hàng `ReturnedQty > 0`) hoặc **chưa biết** (không mặt hàng nào `ReturnedQty > 0`, ví dụ dữ liệu cũ nhập từ app cũ): KHÔNG phân biệt màu, hiển thị như hiện nay.
- Không bao giờ ẩn hay bớt dòng sản phẩm; số sản phẩm hiển thị luôn bằng `Items.Count`.

## Việc cần làm
1. **Cột "Sản phẩm & Phân loại"** ở 3 bảng sàn, Kho lưu trữ, Đã xóa, Bảng đơn mới, Đang theo dõi, Hàng hoàn về, kết quả tìm kiếm toàn cục: áp quy tắc trên (dòng sản phẩm đã hoàn khác màu, dòng không hoàn xám). Khi một ô nhiều dòng quá hẹp, hiển thị xuống dòng/tooltip liệt kê đủ.
2. **Nhãn trên dòng đơn**: với đơn hoàn một phần hiện chip nhỏ "Hoàn K/N SP" (K = số mặt hàng đã hoàn, N = tổng số mặt hàng) bên cạnh cột sản phẩm hoặc cạnh mã đơn (chọn chỗ gọn nhất, thống nhất ở mọi bảng). Có sẵn cờ `Order.IsPartialReturn` do sàn báo: nếu cờ này bật mà chưa biết mặt hàng nào đã hoàn (không có `ReturnedQty > 0`) thì chip chỉ ghi "Hoàn một phần".
3. **Chi tiết đơn (T018)**: bảng sản phẩm thêm cột "Hoàn" (hiện `ReturnedQty`, hoặc "—"), tô nền nhạt dòng đã hoàn, dòng không hoàn xám; thêm dòng tóm tắt phía trên bảng: "Hoàn 1/3 sản phẩm: Áo x1".
4. **Lọc nhanh** (nếu làm gọn được, không bắt buộc): ô "Chỉ đơn hoàn một phần" ở các bảng sàn.
5. **Theme sáng/tối** đủ tương phản (kiểm tra bằng ảnh chụp cả hai theme). Không dùng MÀU làm dấu hiệu DUY NHẤT: luôn kèm nhãn chữ ("Hoàn N"/"Không hoàn") để người khó phân biệt màu vẫn đọc được.
6. `--demo`: thêm ít nhất 2 đơn mẫu hoàn một phần (3 sản phẩm hoàn 1; 2 sản phẩm hoàn 1) vào dữ liệu giả để xem; không đọc/ghi đĩa.

## Tiêu chí hoàn thành
- `dotnet build` 0 lỗi 0 cảnh báo; `dotnet test` xanh 10/10.
- Test mới trong `UiTests/` (đơn giả dựng bằng mã): hoàn một phần (3 hàng, 1 hoàn) -> đúng 3 dòng hiển thị, đúng dòng "Hoàn 1" nhấn màu, hai dòng còn lại "Không hoàn" xám; hoàn đủ và chưa biết -> không phân biệt; chip "Hoàn 1/3 SP" đúng số; cờ `IsPartialReturn` mà không rõ mặt hàng -> chip "Hoàn một phần"; Chi tiết đơn có cột "Hoàn" và dòng tóm tắt đúng; kiện A và B cùng mã đơn hiển thị đúng riêng từng kiện; không thay đổi dữ liệu/DB khi chỉ xem; `--demo` không chạm đĩa.
- Ảnh chụp `tasks/reports/T030-screens/`: bảng có đơn hoàn một phần (sáng và tối), chi tiết đơn hoàn một phần, đơn hoàn đủ (không phân biệt).
- Báo cáo `tasks/reports/T030-report.md`, dòng cuối `TRẠNG THÁI: XONG` hoặc `CHƯA XONG (lý do)`. Cập nhật `TASKS.md`.
