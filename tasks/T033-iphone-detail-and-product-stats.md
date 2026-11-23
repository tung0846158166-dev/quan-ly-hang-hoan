# T033 - App iPhone: làm lại màn CHI TIẾT ĐƠN + thêm THỐNG KÊ SẢN PHẨM (giao cho: antigravity, người kiểm tra: claude)

PHẠM VI: CHỈ `ios-app/`. Đọc trước: `AGENTS.md`, `tasks/T032-iphone-redesign-pass2.md`, `tasks/reports/T032-report.md`, `ios-app/App/Sources/OrdersView.swift` (màn `DetailView` hiện tại), `Theme.swift`, `ios-app/ScannerCore/Sources/ScannerCore/Snapshot.swift` (các trường của `OrderRow`: `n` tên sản phẩm cách nhau " | ", `co` màu, `sz` size, `q` số lượng, `rn` tên sản phẩm đã hoàn, `lv` cảnh báo, `due`, `st`, `ty`, `x` mã phụ, `rw/rws/rv` hàng hoàn về...), `v2/src/QuanLyHangHoan.Core/Stats/ProductStatistics.cs` (cách PC tính thống kê sản phẩm, PHẢI cho kết quả tương đương).
GIỚI HẠN THẬT: không có Swift/Xcode trên máy này. iOS 16 là mức tối thiểu (API iOS 17+ phải bọc `#available`; ví dụ `.buttonBorderShape(.circle)` KHÔNG dùng được, dùng `.capsule`). Lớp/hàm `@MainActor` không được gọi từ chỗ không phải main actor. Tự đọc lại từng tệp tìm lỗi kiểu/nhãn tham số trước khi báo xong. KHÔNG `git commit`/`git push`. Claude đẩy lên GitHub build thật và trả lỗi cho bạn.

## Phản hồi của chủ dự án (ảnh thật trên iPhone, bản T032)
"Cả trên máy tính và điện thoại đều chưa có thống kê sản phẩm. Trên điện thoại phần chi tiết đơn giao diện vẫn quá cùi, sửa lại đi." Màn chi tiết đơn hiện vẫn là danh sách chữ trái/phải dày đặc như bản cũ.

## Việc 1: Màn CHI TIẾT ĐƠN thiết kế lại hoàn toàn (bấm một đơn từ Đơn / Tổng quan / Tìm kiếm)
Bố cục gợi ý (kéo/cuộn dọc, thẻ bo góc lớn, ít chữ nhỏ):
1. **Hero** trên cùng: huy hiệu sàn (Shopee cam / TikTok đen / Lazada xanh), mã vận đơn RẤT to (chữ đậm, đơn cách, có nút "Sao chép" tròn bên cạnh), mã đơn gốc nhỏ hơn, một viên thuốc trạng thái lớn ("Chưa nhận" / "Đã nhận kho" / "Mất"... theo `st`) và viên thuốc cảnh báo (đỏ/vàng/xanh) kèm "Quá 5 giờ"/"Còn 3 giờ" lấy từ `lv`/`due`. Nền hero gradient nhạt theo mức cảnh báo.
2. **Hàng nút hành động nhanh** (biểu tượng tròn có nhãn): Đã nhận / Ghi chú / Theo dõi (sao) / Sao chép mã / Xóa. Giữ đúng các thao tác và hộp xác nhận đã có (đổi trạng thái, `order_note`, `order_delete`, chờ `pc_ack`, hiện "đang chờ máy tính", báo lỗi rõ). KHÔNG thêm chức năng mới.
3. **Thẻ "Sản phẩm"**: mỗi sản phẩm một dòng riêng (tách `n`, `co`, `sz` theo " | "): tên, chip màu, chip size, số lượng "×2". Đơn hoàn một phần (`rn` không rỗng): sản phẩm đã hoàn tô màu nổi + nhãn "Hoàn", còn lại xám nhạt + nhãn "Không hoàn" (như `ProductLines`).
4. **Thẻ "Hành trình hàng hoàn"** nếu đơn nằm trong hàng hoàn về (`rw > 0`): thanh tiến trình 4 chặng (Đã đồng ý -> Đang về -> Đã về shop -> Đã nhận kho) với chặng hiện tại sáng, kèm giờ nếu có (`ar`).
5. **Thẻ "Mã vận đơn & liên quan"**: mã giao đi, mã hoàn, mã yêu cầu hoàn, mã phụ (`x`), mỗi mã một dòng có nút sao chép nhỏ; chỉ hiện dòng có dữ liệu.
6. **Thẻ "Thời gian"**: ngày giao, ngày hoàn/hủy, giờ hàng về shop, ngày nhận kho, hạn xử lý, dạng mốc thời gian dọc (timeline) có chấm.
7. **Thẻ "Ghi chú"** (ghi chú của shop và của khách) có nút sửa gọn; trống thì hiện gợi ý nhẹ.
8. Thanh trên: tiêu đề nhỏ gọn (loại đơn), nút đóng; kéo xuống để đóng khi mở dạng sheet nếu hiện đang là sheet. Không để chữ bị cắt, không chồng nội dung lên thanh trên. Tối/sáng đều đẹp, Dynamic Type không vỡ, vùng bấm >= 44pt, rung nhẹ (`Haptics`) khi bấm nút.
9. Chế độ DỮ LIỆU MẪU (T032) phải xem được màn này đầy đủ với dữ liệu mẫu (thêm vài đơn mẫu có đủ mã/ghi chú/hành trình/hoàn một phần nếu thiếu).

## Việc 2: THỐNG KÊ SẢN PHẨM trên điện thoại
- Logic thuần ở `ScannerCore` (`ProductMetrics.swift`) + test XCTest (dữ liệu GIẢ): từ danh sách `OrderRow` tính đúng như PC: nhóm theo tên sản phẩm (bỏ khác biệt hoa/thường, dấu cách thừa, dấu tiếng Việt khi so khóa nhóm; hiển thị cách viết gặp nhiều nhất), số cái = tổng `q` (qty <= 0 tính 1); ĐƠN HOÀN MỘT PHẦN (`rn` không rỗng) chỉ tính các sản phẩm có tên nằm trong `rn`; bỏ sản phẩm rỗng và "Chưa rõ sản phẩm..." (đếm riêng thành "chưa rõ"); mỗi sản phẩm: số cái, số đơn, tỷ trọng, số cái theo sàn, lý do hay gặp nếu có trường lý do (nếu dòng không có trường lý do thì bỏ cột này, KHÔNG bịa), danh sách màu/size theo số cái. Sắp xếp giảm dần theo số cái. Tách màu/size từ `co`/`sz` song song với `n` (nếu số phần tử khác nhau thì bỏ qua màu/size của đơn đó, không đoán).
- UI: mục **"Sản phẩm"** nằm trong màn Tổng quan (một thẻ "Sản phẩm hoàn nhiều nhất" top 5 có thanh tỷ trọng, nút "Xem tất cả") và một màn đầy đủ (từ thẻ đó hoặc từ tab Đơn): danh sách xếp hạng (số thứ tự, tên, số cái, thanh tỷ trọng, số đơn), bấm một sản phẩm mở màn con: màu/size nào bị hoàn nhiều (thanh ngang), tách theo sàn, và danh sách đơn có sản phẩm đó (bấm mở Chi tiết đơn). Bộ lọc nhanh theo sàn (Tất cả / Shopee / TikTok / Lazada) và khoảng thời gian (7 ngày / 30 ngày / Tất cả) nếu dữ liệu có ngày; không có thì chỉ lọc sàn.
- Chế độ DỮ LIỆU MẪU xem được đầy đủ mục này.

## Tiêu chí hoàn thành
- Chỉ sửa trong `ios-app/`. Giữ NGUYÊN mọi chức năng/hợp đồng. Không còn `Inbox`/`inbox_push`/`cancelOrder`/`SHP-2026`.
- `ProductMetrics` có test XCTest ở `ios-app/ScannerCore/Tests/ScannerCoreTests/` (gộp tên khác hoa/thường/dấu cách, hoàn một phần chỉ tính sản phẩm đã hoàn, bỏ "Chưa rõ", rỗng không chia cho 0, màu/size lệch số phần tử thì bỏ qua) chạy được bằng `swift test`.
- Báo cáo `tasks/reports/T033-report.md`: tệp mới/sửa, trước/sau của từng khối chi tiết đơn, mục "CHƯA BIÊN DỊCH", mục "việc cần kiểm trên iPhone thật", dòng cuối `TRẠNG THÁI: XONG` hoặc `CHƯA XONG (lý do)`. Cập nhật `TASKS.md`.
