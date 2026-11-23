# T020 - Tìm kiếm toàn cục + màn "Đang theo dõi" (giao cho: antigravity, người kiểm tra: claude)

Phạm vi: CHỈ `v2/src/QuanLyHangHoan.App/` và `v2/tests/QuanLyHangHoan.Tests/UiTests/`. KHÔNG sửa `Core/`, `Data/` và test ngoài `UiTests/`. Thiếu API ở Data thì ghi vào báo cáo.
Đọc trước: `AGENTS.md` (quy tắc 1, 2, 3, 8), `tasks/reports/T018-review.md` (cửa sổ chi tiết đơn), `tasks/reports/T013-review.md` (thao tác trên dòng), `v2/src/QuanLyHangHoan.Data/OrderRepository.cs`, `Core/Tracking/TrackingResolver.cs`. Test dùng `WpfTestHost`, DB tạm + dữ liệu GIẢ; `dotnet test` toàn bộ 10 lần liên tiếp 0 lần đỏ.

## Vấn đề
Người dùng không tìm được một đơn khi chỉ có một mã (mã vận đơn hoàn, mã đơn, mã yêu cầu hoàn, mã phụ) mà không biết nó nằm ở bảng nào (Shopee, TikTok, đơn hủy, kho lưu trữ, đã xóa). App cũ có ô tìm và bảng "Đang theo dõi"; v2 chưa có.

## Việc cần làm
1. **Ô tìm toàn cục** trên thanh trên cùng (phím tắt Ctrl+K hoặc Ctrl+F khi không ở ô nhập khác; Esc xóa/đóng). Gõ mã hoặc một phần tên sản phẩm/ghi chú; không phân biệt hoa thường và dấu tiếng Việt. Kết quả hiện trong khung thả xuống/trang kết quả: mỗi dòng gồm sàn, mã đơn, mã vận đơn, sản phẩm tóm tắt, trạng thái nhận kho, và **vị trí** ("Shopee - Hoàn hàng", "Shopee - Đơn hủy", "TikTok - Hoàn hàng", "Kho lưu trữ", "Đã xóa"). Mỗi kết quả là MỘT KIỆN (kiện A/B/C cùng mã đơn hiện thành các dòng riêng, không bao giờ gộp). Tối đa 50 kết quả + dòng "còn N kết quả, hãy gõ thêm".
   - Khớp chính xác một mã vận đơn/mã phụ/mã hoàn của kiện được xếp lên đầu và có nhãn "Khớp mã". Mã giao đi dùng chung không được coi là khớp chính xác (quy tắc 3).
   - Nhấp đúp/Enter trên kết quả: mở "Chi tiết đơn" (T018). Có thêm nút/mục "Đi tới bảng" chuyển sang màn hình tương ứng và chọn đúng dòng (nếu làm gọn được; không thì chỉ mở chi tiết).
   - Tìm nhanh (không treo giao diện): chạy nền, chờ ~250 ms sau lần gõ cuối.
2. **Màn "Đang theo dõi"** (mục mới trong sidebar nhóm CHUNG, đặt sau "Bảng đơn mới"; badge là số đơn đang theo dõi): bảng các kiện `IsWatchlist` đang ở bảng chính/đơn hủy (không gồm lưu trữ/đã xóa; có ô chọn "Gồm cả kho lưu trữ"), cột: sàn, mã đơn, mã vận đơn, sản phẩm, trạng thái nhận kho, cảnh báo, **lý do theo dõi**. Thao tác (dùng lại các thao tác của T013, không viết lại logic): đổi trạng thái, sửa lý do theo dõi, bỏ theo dõi, xem chi tiết, xem lịch sử. Có ô lọc nhanh và lọc theo sàn. Bảng trống: "Chưa theo dõi đơn nào".
3. Theme sáng/tối, bàn phím (Tab, mũi tên, Enter, Esc), không sập khi dữ liệu rỗng hoặc trường thiếu.
4. `--demo`: dùng dữ liệu giả, không đọc/ghi đĩa.

## Tiêu chí hoàn thành
- `dotnet build` 0 lỗi 0 cảnh báo; `dotnet test` xanh 10/10.
- Test mới trong `UiTests/`: tìm theo mã vận đơn, mã đơn, mã hoàn, mã phụ, một phần tên sản phẩm, có/không dấu; kiện A và B cùng mã đơn ra hai kết quả; khớp chính xác xếp đầu; mã giao đi dùng chung KHÔNG được nhãn "Khớp mã"; đơn ở kho lưu trữ/đã xóa/đơn hủy có nhãn vị trí đúng; giới hạn 50 kết quả; mở chi tiết từ kết quả; màn Theo dõi liệt kê đúng, bỏ theo dõi thì biến mất và DB đổi, sửa lý do lưu đúng, xem lịch sử; `--demo` không chạm đĩa.
- Ảnh chụp `tasks/reports/T020-screens/`: ô tìm có kết quả, không có kết quả, màn Đang theo dõi (có và trống).
- Báo cáo `tasks/reports/T020-report.md`, dòng cuối `TRẠNG THÁI: XONG` hoặc `CHƯA XONG (lý do)`. Cập nhật `TASKS.md`.
