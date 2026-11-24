# App iPhone v2 - Quét Hàng Hoàn (SwiftUI)

Dùng với **app Windows v2** qua **mã phòng**. Bản này được tách từ `ios-native/` (bản cũ, giữ nguyên để đối chiếu).

## Khác bản cũ (quyết định của chủ dự án)
- Không còn mã phòng mặc định `SHP-2026`: phải dán mã phòng của app PC v2 (màn "Đồng bộ điện thoại"), tối thiểu 12 ký tự.
- Bỏ **tạo đơn hủy** từ điện thoại và bỏ **Hộp Đơn Mới** (PC v2 không có). Đơn thường quét từ điện thoại vào thẳng Bảng chính của PC.
- Đổi trạng thái chỉ gồm: Chưa nhận, Hàng đã trả, Không cần nhận, Bị mất.
- Ảnh chụp lúc quét chỉ được lưu cục bộ vào Chi tiết đơn (`Documents/order-photos/`, tối đa 5 ảnh/kiện) khi PC xác nhận khớp đúng 1 kiện; lượt quét không tìm thấy, mơ hồ, lỗi, bị từ chối hoặc trùng sẽ tự xóa ảnh tạm ngay; KHÔNG gửi ảnh sang máy tính.
- Cài song song được với bản cũ (khác tên và mã định danh `com.quanlyhanghoan.scannerv2`).

## Build (không có máy Mac)
Đẩy lên nhánh `v2-rewrite` trên GitHub: workflow **Build iPhone App v2** tự chạy (kiểm thử lõi `ScannerCore` + build IPA chưa ký). Tải IPA ở trang chạy, mục **Artifacts**. Cài bằng Sideloadly hoặc AltStore (Apple ID miễn phí: hết hạn sau 7 ngày, cài lại).

## Lần đầu mở
Cho phép camera và thông báo, vào Cài đặt, dán mã phòng rồi "Lưu mã phòng".

## Trạng thái
Mã Swift ở đây chưa từng được biên dịch trên máy của người phát triển (Windows): kết quả biên dịch thật là kết quả của workflow trên GitHub.
