# T036 - App iPhone: GỌN và HIỆN ĐẠI hơn nữa (giao cho: antigravity, người kiểm tra: claude)

PHẠM VI: CHỈ `ios-app/`. Đọc trước: `tasks/T032-iphone-redesign-pass2.md`, `tasks/T033-iphone-detail-and-product-stats.md`, `tasks/T035-iphone-product-stats-visible.md`, các báo cáo `tasks/reports/T032..T035-report.md`, và các tệp `ios-app/App/Sources/*.swift` (đặc biệt `ScannerApp.swift` thanh tab, `Theme.swift`, `HomeView.swift`, `OrdersView.swift`, `ScanView.swift`, `HistoryView.swift`, `SettingsView.swift`, `OrderDetailView.swift`).
GIỚI HẠN THẬT: không có Swift/Xcode trên máy này; iOS 16 là mức tối thiểu (API iOS 17+ bọc `#available`, KHÔNG dùng `.buttonBorderShape(.circle)`); lớp/hàm `@MainActor` không gọi từ chỗ không phải main actor; tự đọc lại từng tệp tìm lỗi kiểu/nhãn trước khi báo xong (các lỗi biên dịch trước đây đều do không tự rà). KHÔNG `git commit`/`git push`. Claude đẩy lên GitHub build thật.

## Phản hồi của chủ dự án sau khi xem bản T035 (ảnh thật 24/11/2026)
"Tối ưu lại cho nó gọn hiện đại hơn nữa." Các vấn đề thấy trên ảnh iPhone:
1. **Thanh tab nổi quá to và CHE nội dung**: danh sách/thẻ bị cắt sau thanh tab; ở Tổng quan còn một thanh "Quét ngay" cam to chồng thêm một lớp nữa phía dưới. Nội dung không có khoảng đệm đáy nên mục cuối bị che.
2. **Đầu trang lỗi**: dưới dải "DỮ LIỆU MẪU" lòi ra mảnh nút bo tròn bị cắt (các nút thanh trên bị đè/cuộn lên dưới dải); tiêu đề lớn 34pt chiếm cả khối.
3. **Quá thưa, mỗi màn chỉ xem được 2 đến 3 mục**: tiêu đề quá lớn, thẻ đệm quá dày, thẻ đơn cao tới 5 dòng (nhãn sàn, mã vận đơn, mã đơn, sản phẩm, ghi chú, trạng thái).
4. **Huy hiệu "15" trên tab Tổng quan** không có nghĩa (và huy hiệu "1" trên Lịch sử không rõ là gì).
5. **Lịch sử**: thẻ "Hôm nay" (2 hợp lệ...) lặp lại đúng thông tin của hàng chip bên dưới (5 hợp lệ...) và số hai nơi khác nhau, gây hiểu nhầm.

## Việc cần làm
1. **Thanh tab**: dùng thanh tab chuẩn của hệ thống (`TabView`, nền mờ `.ultraThinMaterial`/mặc định), nhỏ gọn; BỎ thanh tab nổi tự vẽ đang che nội dung. Mọi màn cuộn phải có đệm đáy đúng (safe area) để mục cuối không bị che. Chỉ giữ huy hiệu có nghĩa rõ: tab Lịch sử = số lượt đang chờ máy tính xác nhận; tab Đơn = số đơn quá hạn (đỏ). BỎ huy hiệu ở Tổng quan.
2. **Dải DỮ LIỆU MẪU**: một dòng mảnh (cao tối đa 24pt, chữ 11pt, nút "Tắt" nhỏ) nằm TRÊN thanh trên, không đè nội dung; sửa lỗi mảnh nút bị cắt dưới dải (các nút thanh trên đặt trong `toolbar`/`navigationBar` đúng cách, không dùng lớp phủ tự vẽ chồng lên nội dung).
3. **Tiêu đề và khoảng cách**: dùng tiêu đề inline gọn (`.navigationBarTitleDisplayMode(.inline)`, chữ 17 đậm) ở Đơn, Lịch sử, Cài đặt, Quét; Tổng quan dùng một dòng chào + ngày nhỏ (không tiêu đề 34pt). Lưới 8pt: khoảng cách giữa thẻ 8 đến 10, đệm thẻ 12, bo góc 14. Chữ chính 15, phụ 12 đến 13, số lớn chỉ ở hero.
4. **Tổng quan** gọn hơn: hero nhỏ lại (vòng 84pt) kèm quá hạn/sắp hạn bên phải; 4 số liệu (chờ về, nhận hôm nay, quá hạn, sắp hạn) thành MỘT dải ngang gọn dưới hero; "Hàng hoàn về", "Sản phẩm hoàn nhiều nhất", "Cần xử lý ngay", "Theo sàn" là các thẻ gọn xếp dọc, mỗi thẻ cao tối đa khoảng 140pt; BỎ thanh "Quét ngay" cam to ở đáy (thay bằng một nút "Quét" nhỏ ở thanh trên hoặc một nút nổi tròn nhỏ 52pt góc phải dưới không che nội dung).
5. **Danh sách Đơn**: mỗi đơn là một hàng gọn 3 dòng: (1) huy hiệu sàn nhỏ + mã vận đơn đậm + "Quá 1 ngày 4 giờ" màu ở bên phải; (2) tên sản phẩm một dòng cắt bằng "…" kèm "Đen · S"; (3) trạng thái bằng chấm tiến trình nhỏ + ghi chú rút gọn (một dòng, biểu tượng ghim). Hoàn một phần: hiện chip "Hoàn 1/3" và chỉ tên sản phẩm ĐÃ hoàn tô màu, các sản phẩm còn lại xem ở màn Chi tiết (không liệt kê hết trong danh sách). Bỏ dòng "Mã đơn" (có ở Chi tiết). Hàng lối tắt (Hoàn về/Sản phẩm/Đơn hủy) thu thành một hàng chip nhỏ, không phải 3 thẻ to.
6. **Lịch sử**: BỎ thẻ "Hôm nay" thống kê trùng lặp; dùng duy nhất hàng chip có số đếm (đồng nhất một nguồn số). Nhóm theo ngày, mỗi dòng cao khoảng 52pt.
7. **Quét**: giữ khung camera nhưng cho nhỏ hơn một chút để thấy danh sách "Quét gần đây" không bị che; hàng điều khiển (đèn, zoom, tạm dừng, dán mã) gọn hơn; ô nhập tay một dòng.
8. **Cài đặt** và **Chi tiết đơn**: áp cùng thang khoảng cách/chữ gọn; không đổi chức năng.
9. Giữ NGUYÊN mọi chức năng/hợp đồng (quét, hàng đợi bền, xác nhận, hoàn một phần tô màu, đồng bộ, thống kê sản phẩm Premium/Thường, dữ liệu mẫu). Tối/sáng đều đẹp, Dynamic Type không vỡ (hàng đơn tự co giãn), vùng bấm >= 44pt, tôn trọng Reduce Motion.
10. Quy ước chung: tập trung các hằng số (khoảng cách, bo góc, cỡ chữ) vào `Theme.swift` để toàn app dùng một thang, tránh số tự do rải rác.

## Tiêu chí hoàn thành
- Chỉ sửa trong `ios-app/`. Test lõi hiện có vẫn đạt (`swift test`); logic thuần mới (nếu có) kèm test XCTest. Không còn `Inbox`/`inbox_push`/`cancelOrder`/`SHP-2026`.
- Báo cáo `tasks/reports/T036-report.md`: từng màn trước/sau (số mục xem được trên một màn hình iPhone), danh sách tệp sửa, mục "CHƯA BIÊN DỊCH", mục "việc cần kiểm trên iPhone thật", dòng cuối `TRẠNG THÁI: XONG` hoặc `CHƯA XONG (lý do)`. Cập nhật `TASKS.md`.
