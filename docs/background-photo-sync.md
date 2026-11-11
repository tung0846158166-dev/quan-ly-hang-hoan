# Gửi ảnh ngầm sau xác nhận mã — 2026-11-12

Điện thoại hiển thị kết quả khớp/không tìm thấy/trùng ngay khi nhận ACK mã hợp lệ từ PC. ACK mã vẫn được PC gửi sau bước lưu hiện có; không xác nhận trước khi lưu. Ảnh có trạng thái riêng trong chi tiết lịch sử và tiếp tục gửi bất đồng bộ bằng cơ chế hiện có.

Hàng đợi, requestId, captureId và kiểm tra ACK khớp được giữ nguyên. Chỉ xóa hàng đợi khi đủ ACK mã và ảnh. ACK ảnh muộn không ghi đè banner của lượt quét tiếp theo. Timeout của lượt cũ không thay banner mới. Không tìm thấy đơn vẫn báo ngay và giữ ảnh chưa gắn trên điện thoại, không gắn sang đơn khác.

Phạm vi chỉ mobile_scanner.html; không cần thay ứng dụng PC 1.8.7 để nhận hành vi mới. Sau khi merge vào main, cần Pages triển khai thành công rồi tải lại trang scanner GitHub trên điện thoại. Brewpage không được tự cập nhật bởi thay đổi này.

Kiểm tra: bổ sung hai test (khớp trước ảnh, ACK ảnh cũ sau lượt mới; không tìm thấy/trùng trước ảnh), đã xác nhận thất bại trước sửa và đạt sau sửa. Toàn bộ npm test PASS 58/58, cú pháp JavaScript và git diff --check PASS. Windows/iPhone/4G thực tế và mức giảm độ trễ: NOT TESTED. Không cam kết phản hồi tức thời khi mạng hoặc bước lưu PC chậm.
