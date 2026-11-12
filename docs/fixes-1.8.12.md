# 1.8.12 — Khôi phục phản hồi PC trên điện thoại

User báo PC đã trả nhưng phone không nhận phản hồi. PR#5 đã merge main6a961a5. Phiên bản PC/URL/triệu chứng trên thiết bị thực đã được hỏi, chưa có câu trả lời ở thời điểm triển khai; không khẳng định nguyên nhân thiết bị duy nhất.

Lỗi có thể tái hiện trong source: queue có thể gửi trước khi listener nghe; SSE bị trì hoãn đến lúc tải xong các thư viện camera tùy chọn, onopen không replay. Nếu ACK đầu bị lỡ thì chỉ chờ/thông báo10giây, không tự hỏi lại.

Sửa:
- Mở SSE ngay khi khởi tạo; thư viện tải xong chỉ nối MQTT, không đóng/mở SSE đang nhận.
- SSE mở/reconnect hoặc MQTT subscribe xong sẽ thử lại yêu cầu chưa xác nhận; dùng guard gửi ảnh/khôi phục queue hiện có.
- Khi chưa có ACK mã, thử lại tối đa3 lần sau1.5/3/6giây, giữ nguyên requestId/captureId/mục đích, không tái tạo đơn. Hết các lần thử vẫn giữ queue và cho đồng bộ lại.
- Nhận ACK scan hoặc đổi phòng thì dừng retry; ảnh đã tách vẫn giữ chờ ACK riêng. Thời gian phản hồi tính từ lần gửi đầu.
- Mobile hiện phiên bản1.8.12; QR GitHub từ PC1.8.12 có queryversion để giảm dùng cache cũ.

PASS152 tests với compat SheetJS0.18.5; engine sản xuất0.20.1 vẫn được Windows CI kiểm tra trước build. Native Chromium hai trang PC/mobile và bridge cục bộ: cố ý bỏ ACK scan đầu -> same-request retry nhận lại success; PC phoneScanCount=1; lần quét tiếp trả duplicate; không tạo inbox mới, ambiguous code/reloadIDB vẫn đúng, không có pageerror. Review độc lập không có Important/Critical mới. Kiểm tra hydration là xác nhận guard hiện có, không coi là lỗi mới.

NOT TESTED: PC/điện thoại thật, mất ACK thật trên ntfy/MQTT qua4G, phiên bản app đang dùng, gửi quá giới hạn dịch vụ. Test mô phỏng mất ACK không chứng minh mạng thực đã được sửa. Nếu PC không thể gửi qua cả hai dịch vụ, retry không thể tạo ACK thành công giả; queue vẫn giữ.

Cần merge nhánh fix/phone-ack-recovery, chờ Pages xanh, mở lại mobile và thấyv1.8.12. PC1.8.11 tương thích replay cùngID; cài PC1.8.12 nếu cần QR chống cache phiên bản. Không triển khai Brewpage tự động, không tự merge main.
