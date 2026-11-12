# 1.8.10 — Shopee đơn hủy/giao thất bại có số lượng hoàn 0

Kiểm tra trực tiếp hai workbook người dùng gửi ngày 12/11/2026 cho thấy dữ liệu được đọc đúng nhưng bộ lọc 1.8.9 loại mọi dòng: cột Số lượng sản phẩm được hoàn trả có giá trị0 ở cả hai báo cáo đơn hủy/giao thất bại.

Sửa: khi là đơn hủy hoặc giao thất bại và không có ngữ cảnh yêu cầu khách trả hàng (mã hoàn/claim/trạng thái trả hàng), số lượng hoàn0 không loại đơn; dùng số lượng sản phẩm gốc để theo dõi kiện. Với yêu cầu khách trả hàng thực có ngữ cảnh hoàn, SKU hoàn0 vẫn được loại để tránh nhập hàng khách giữ lại. Không tự đánh dấu đã nhận kho.

Kết quả hàm nhập trên workbook thật: Order.cancelled có13 dòng sản phẩm/12 mã đơn ->12 kiện được nhập; Order.failed_delivery có10 dòng/9 mã đơn ->9 kiện được nhập. Không đưa workbook hoặc dữ liệu cá nhân vào repository/Artifacts.

Regression synthetic tái hiện0 đơn trước sửa, đạt sau sửa. Toàn bộ145 tests PASS với SheetJS tương thích0.18.5. Bản sản xuất0.20.1 được workflow tải và kiểm thử trước build. Windows installer runtime/engine0.20.1 locally/4G: NOT TESTED; không suy ra build thành công từ kiểm thử source.

Workflow Artifact: QuanLyHangHoan-Windows-Installer-v1.8.10. Cần xem lượt build thành công trên GitHub trước tải.

PASS Chromium toàn trang: hai workbook thật đi qua input FileReader, đọc bằng SheetJS0.18.5, lưu localStorage/IndexedDB thật, trả đúng12 và9 kiện, hiển thị thành công sau lưu, không có page error và không tự đánh dấu nhận kho. Chỉ auth được mở cho thử nghiệm; không mock business hoặc persistence.
