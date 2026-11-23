# T018 - Màn hình "Chi tiết đơn" (giao cho: antigravity, người kiểm tra: claude)

Phạm vi: CHỈ `v2/src/QuanLyHangHoan.App/` và `v2/tests/QuanLyHangHoan.Tests/UiTests/`. KHÔNG sửa `Core/`, `Data/` và test ngoài `UiTests/`. Thiếu API ở Data thì ghi vào báo cáo (đừng tự thêm).
Đọc trước: `AGENTS.md`, `tasks/reports/T013-review.md` (menu thao tác/lịch sử sửa), `v2/src/QuanLyHangHoan.Core/Models/Order.cs` (mọi trường cần hiển thị đã có ở đây), `Services/IPhotoProvider.cs`, `IPhotoViewerService.cs`, `IOrderEditService.cs`. Test dùng `WpfTestHost`, DB tạm + dữ liệu GIẢ, không chạm `%LocalAppData%` thật; `dotnet test` toàn bộ 10 lần liên tiếp 0 lần đỏ.

## Vấn đề
Bảng đơn chỉ có các cột; người dùng không xem được toàn bộ thông tin một đơn (từng sản phẩm, mã vận đơn cũ/thay thế, dữ liệu thô từ file sàn, ảnh lớn...).

## Việc cần làm
1. Cách mở (CHỈ XEM, không sửa dữ liệu ở màn này): nhấp đúp dòng, phím Enter trên dòng đang chọn, và mục "🔍 Xem chi tiết" ĐẦU TIÊN trong menu chuột phải/nút `•••`. Áp dụng cho 3 bảng sàn, Kho lưu trữ, Đã xóa, Bảng đơn mới (nếu dòng có đủ dữ liệu `Order`). Phím Delete và các thao tác hiện có không đổi hành vi.
2. Hiển thị: cửa sổ phụ (hoặc khung trượt bên phải; chọn cách gọn, dùng được bằng bàn phím, Esc để đóng), có thể mở nhiều lần cho các dòng khác nhau. Các nhóm (ẩn nhóm nào hoàn toàn trống):
   - **Tổng quan**: sàn, loại đơn, trạng thái kiện (sàn), trạng thái nhận kho, cảnh báo vàng/đỏ + đã bao lâu kể từ lúc hàng về, giá trị, hãng vận chuyển (chung + giao đi + hoàn về), theo dõi (có/không + lý do).
   - **Mã**: mã đơn, mã yêu cầu hoàn, mã vận đơn hoàn, mã giao đi, mã phụ (AltTrack), "Mã lịch sử/thay thế" (TrackingCodes, ScanAliases). Mỗi mã có nút sao chép (dùng `IClipboardService`). Ghi chú nhỏ: "Mã giao đi dùng chung không đủ để nhận kiện".
   - **Mốc thời gian**: ngày giao, ngày hoàn/hủy, giờ hàng về shop, ngày nhận kho, hạn khiếu nại, tuần nhập, giờ nhập.
   - **Sản phẩm**: bảng từng dòng (tên, màu/phân loại, size, SL, SKU, SKU ID, giá từng ô nếu có `ImportPrices`) + tổng SL. Nếu là hoàn một phần thì hiện nhãn.
   - **Ghi chú**: lý do hoàn/hủy, ghi chú người mua, ghi chú của tôi (nhiều dòng, xuống dòng đúng).
   - **Ảnh**: ảnh thu nhỏ; bấm mở bằng `IPhotoViewerService` (đã có). Không có ảnh thì ghi "Chưa có ảnh".
   - **Dữ liệu từ file sàn** (thu gọn mặc định): cơ sở giá, bảng `RawDetails` (cột -> giá trị, có ô lọc nhanh), `PriceModes`.
   - **Lịch sử sửa**: dùng lại cách hiển thị của khung lịch sử T013 (tải qua `IOrderEditService.History`), trống thì "Chưa có sửa đổi nào".
3. Màn hình KHÔNG được hiện tên/SĐT/địa chỉ khách (dữ liệu đã bị bỏ từ lúc nạp; đừng thêm trường nào như vậy).
4. Dòng thiếu dữ liệu (rỗng) hiện "—", không để trống lạ, không sập khi `Items`/`RawDetails` rỗng hay ảnh lỗi.
5. Lấy dữ liệu qua provider/service hiện có (hoặc interface mới ở `App/Services` có bản giả); `--demo` dùng dữ liệu giả, không đọc/ghi đĩa.

## Tiêu chí hoàn thành
- `dotnet build` 0 lỗi 0 cảnh báo; `dotnet test` xanh 10/10.
- Test mới trong `UiTests/`: mở bằng nhấp đúp, Enter và menu; đúng nội dung các nhóm với đơn giả có nhiều sản phẩm, đủ mã lịch sử, rawDetails; đơn rỗng không sập; kiện A mở ra đúng kiện A (không lẫn B/C cùng mã đơn); đơn đã xóa vẫn xem được; Esc đóng; nút sao chép gọi `IClipboardService`; xem chi tiết KHÔNG đổi dữ liệu/DB; `--demo` không chạm đĩa.
- Ảnh chụp `tasks/reports/T018-screens/`: đơn Shopee nhiều sản phẩm, đơn TikTok, đơn không ảnh/ít dữ liệu, phần "Dữ liệu từ file sàn" mở rộng.
- Báo cáo `tasks/reports/T018-report.md`, dòng cuối `TRẠNG THÁI: XONG` hoặc `CHƯA XONG (lý do)`. Cập nhật `TASKS.md`.
