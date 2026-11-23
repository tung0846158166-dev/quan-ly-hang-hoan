# Quy trình giao việc

1. Claude viết phiếu `tasks/Txxx-ten.md`.
2. Người dùng bảo Antigravity: "Đọc AGENTS.md rồi làm theo tasks/Txxx-ten.md".
3. Antigravity làm xong, ghi báo cáo `tasks/reports/Txxx-report.md`, cập nhật TASKS.md.
4. Người dùng nhắn Claude "kiểm tra Txxx". Claude đối chiếu báo cáo với kết quả thật (đọc file, chạy test, so với code gốc) rồi ghi `tasks/reports/Txxx-review.md` với kết luận ĐẠT / CẦN SỬA + danh sách lỗi.
5. Nếu CẦN SỬA, Antigravity sửa theo review, báo cáo lại.

Quy tắc: không bên nào sửa file thuộc phần việc của bên kia. Không dùng dữ liệu đơn thật.
