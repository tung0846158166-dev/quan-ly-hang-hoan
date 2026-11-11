# Báo cáo sửa lỗi 1.8.7 — 2026-11-11

Phạm vi: 17 lỗi đã xác nhận trong audit; giữ kiến trúc HTML/Electron, JSON/IndexedDB và giao diện hiện tại. Không sửa dữ liệu đơn hàng thật. Đây là bản sửa mã nguồn, chưa phải nghiệm thu trên thiết bị Windows/điện thoại.

| ID | Sửa lỗi | Bằng chứng kiểm tra |
|---|---|---|
| A01 | Chỉ bỏ đơn khỏi danh sách chính sau khi lưu kho lưu trữ thành công; phục hồi khi lưu chính lỗi | html-storage |
| A02 | Lỗi/quota localStorage không chặn thử IndexedDB và đĩa | html-storage |
| A03 | IPC chờ ghi, fsync và đổi tên tệp; trả lỗi thật, xếp hàng ghi, chờ các ghi đã nhận trước khi thoát | main-storage |
| A04 | Giữ hàng đợi điện thoại khi upload thất bại hoặc thiếu ACK | phone-regression |
| A05 | Đối chiếu ACK theo request, mã và capture; ACK cũ không xóa tác vụ mới | phone-regression |
| A06 | OCR theo UID ổn định; kiểm tra lại đích và khóa sau await | ui-safety |
| A07 | FileReader/Image theo UID, không theo chỉ số dòng cũ | ui-safety + Chromium |
| A08 | Escape chi tiết thông báo, toast dùng textContent | ui-safety + Chromium |
| A09 | Loại bỏ biến không tồn tại khi trạng thái TikTok trống | excel-regression |
| A10 | Tính tiền nhiều SKU; không cộng lặp tổng đơn, giữ giá trị 0 | excel-regression |
| A11 | Khôi phục cả mảng JSON cũ và envelope; ảnh inline ưu tiên hơn cache cũ | restore-review + Chromium |
| A12 | Phát lại kết quả request trong phiên PC, không ghi lần hai; kiểm tra đích còn hợp lệ | phone-regression |
| A13 | Ảnh tới muộn được giữ lịch sử, không ghi đè ảnh mới; chỉ ghép khi mã khớp duy nhất | phone-regression |
| A14 | Không báo thành công trước ACK lưu; ảnh có ACK lưu riêng | phone-regression |
| A15 | Phân biệt biến thể chỉ có size như XL | excel-regression |
| A16 | Nhận diện Cancelled/Canceled | excel-regression |
| A17 | Không xóa ảnh khi khóa; xác định đúng đơn/ảnh đang xem, chờ lưu trước khi xóa vật lý | ui-safety + Chromium |

Tên trong cột bằng chứng tương ứng các tệp `tests/*.test.cjs`. Toàn bộ `npm test`: **PASS 56/56**. Các kiểm thử dùng dữ liệu giả, có mô phỏng lỗi lưu, ACK trễ, đảo thứ tự và thay đổi danh sách.

Review độc lập phát hiện thêm năm tình huống: phát lại success cho đích đã xóa, smart-label xác nhận sớm/nhầm kiểu ảnh cache, ảnh giữ trong lịch sử không được ACK, khôi phục làm thay đổi dữ liệu dù lưu archive lỗi, ảnh inline bị cache cũ ghi đè. Đã sửa và bổ sung kiểm thử. Các đường gửi ảnh riêng, tạo đơn mới và gửi lại sau reload cũng có kiểm thử.

## Kiểm tra trình duyệt

PASS trên Chromium Linux, với mã hàm ứng dụng và dữ liệu giả, chặn dịch vụ ngoài:

1. Nội dung HTML trong thông báo/toast không được thực thi.
2. Nút định vị thông báo tới đúng đơn.
3. Nút xóa ảnh không xóa khi bảng khóa.
4. FileReader/Image/canvas thực lưu ảnh vào đơn gốc sau khi chèn dòng khác.
5. FileReader khôi phục backup mảng cũ; ảnh inline thắng cache cũ.

Không có lỗi JavaScript trang trong lượt kiểm tra này. Harness cục bộ không thay thế thử ứng dụng Electron đóng gói. Kiểm tra cú pháp Main/Preload và ba script inline: PASS. actionlint workflow và `git diff --check`: PASS.

## Giới hạn và triển khai

- Windows installer, thoát ứng dụng Windows, iPhone/camera, 4G/Wi-Fi qua MQTT/ntfy và nhập workbook XLSX thật: **NOT TESTED**. Kiểm thử Excel hiện dùng hàng dữ liệu đã parse.
- Cache chống request trùng nằm trong phiên PC; chưa bảo đảm chống lặp xuyên lần khởi động/crash. Giao thức dịch vụ công cộng hiện tại chưa được thay bằng cơ chế xác thực mới.
- Main chờ các lần ghi IPC đã nhận; chưa có handshake đảm bảo mọi thay đổi renderer còn debounce được gửi trước khi thoát. Không cam kết chống mọi tình huống mất điện.
- Khôi phục/di chuyển kho có rollback tốt nhất; khi không thể rollback có thể giữ bản trùng thay vì xóa dữ liệu chưa lưu an toàn.
- Cần cập nhật **cả PC và mobile_scanner.html** trên trang HTTPS điện thoại đang dùng. Chưa triển khai lên Brewpage.
- Workflow Windows chạy trên nhánh sửa, tải installer và mobile_scanner.html trong Artifact `QuanLyHangHoan-Windows-Installer-v1.8.7`, lưu 30 ngày. Mã nguồn/lint PASS không chứng minh build GitHub đã thành công.
- Không tự merge main. Release chỉ được workflow hiện có tạo khi thay đổi vào main. Trạng thái push/build thực tế được báo riêng khi bàn giao.
