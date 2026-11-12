# 1.8.13 — Kết quả PC và tốc độ nhận diện camera

Yêu cầu: điện thoại gửi mã -> PC đối chiếu -> khớp trả “Đơn đã về kho”, không có trả “Không tồn tại trong danh sách”; mã đã nhận trả “Đơn đã về kho — quét trùng”. Ảnh gửi ngầm, không phải điều kiện hiển thị kết quả mã. Người dùng xác nhận PC1.8.10/mobile1.8.12 nhưng vẫn không thấy kết quả; không được suy ra nguyên nhân mạng thiết bị từ test cục bộ.

## Kết quả đối chiếu

- Giữ ACK do PC gửi làm nguồn kết quả, không tự suy đoán bằng cache điện thoại.
- Khi thiếu realtime ACK, tra HTTP JSON receipts10phút gần nhất theo title Shopee PC Ack trước khi resend. Dữ liệu vẫn qua bộ lọc room/request/code/capture nghiêm ngặt. Không chấp nhận ACK khác yêu cầu và không biến HTTP thành công thành “nhận kho”.
- Sau lần resend cuối vẫn tra receipt thêm có giới hạn, tránh bỏ ACK chỉ xuất hiện ở lần gửi cuối. Ảnh có các lượt tra receipt riêng sau upload; queue chỉ xóa khi đủ mã/ảnh đã xác nhận.
- Dòng “PC xác nhận: mã… — kết quả…” cho biết kết quả cũ khi người dùng đã quét mã tiếp, không ghi đè banner của mã mới. Đổi phòng xóa kết quả phòng trước.
- PC gửi MQTT đúng phòng của giao dịch (không lấy phòng UI vừa đổi), QoS1. PC ghi lỗi HTTP/non2xx/MQTT vào nhật ký kết nối thay vì bỏ qua. Broker chấp nhận không có nghĩa điện thoại đã nhận.

## Nhận diện camera

Native detect trước đây được await mỗi khung hình, trong khi software mã vạch chỉ được thử mỗi ba khung; khi native chậm/rỗng có thể trì hoãn fallback. Luồng auto còn chờ nativeQR thêm lần nữa trước barcode.

Sửa native single-flight chạy nền; software fallback ngay khung đầu, rồi giữ cadence kiểm soát. Bỏ await nativeQR thừa ở luồng live; giữ guard camera generation, decode revision và chụp ảnh tĩnh, không nhận kết quả cũ vào phiên mới. Không đổi mã hay bỏ kiểm tra hợp lệ.

## Bằng chứng

- **PASS169/169** toàn suite với SheetJS tương thích0.18.5, không skip. Engine0.20.1 vẫn được Windows CI tải và kiểm tra trước build.
- **PASS đường wire thật cục bộ:** source PC1.8.10 dùng nguyên sendAckToPhone, fetch HTTP POST thật và EventSource thật tới server cục bộ; mọi pc_ack realtime cố ý bị chặn nhưng ACK được lưu. Mobile nhận mã thành công qua HTTP lookup khi ảnh vẫn đang upload3.5s; PC phoneScanCount=1; ảnh lưu sau và queue chỉ clear sau đúng capture receipt. Không mock hàm gửi/nhận ACK, không có pageerror.
- **PASS decoder thật:** Chromium sandbox với đúng Quagga1.8.4/jsQR1.4.0, ảnh Code128 ngang/dọc và QR được tạo giả, kết quả mã chính xác. Controlled native bị treo: bản cũ729–756ms, bản mới40–76ms ở lượt agent; bản tích hợp được chạy lại. Đây là benchmark giả lập, không cam kết điện thoại đọc trong40ms.
- Review độc lập tìm một Important: cần lookup sau final resend. Có regression thất bại trước sửa và đạt sau; phòng cũ còn hiển thị được xử lý kèm test. Không review lại lặp; toàn suite xanh sau fix pass.
- Cú pháp/workflow/diff check được chạy trước bàn giao.

## Giới hạn và cập nhật

NOT TESTED: camera điện thoại thực, mạng4G/Wi-Fi/publicMQTT/ntfy thực, Windows installer runtime, nguyên nhân riêng trên thiết bị của người dùng. HTTP lookup không thể tạo kết quả nếu PC không gửi được receipt qua cả hai kênh; PC1.8.13 sẽ hiện lỗi gửi trong nhật ký kết nối. Hàng đợi vẫn giữ, không báo xanh giả.

Nhánh fix/phone-result-delivery cần merge main và Pages xanh để cập nhật mobile, sau đó mở lại trang thấyv1.8.13. Core lookup tương thích PC1.8.10 đã được test ở local wire; cài PC1.8.13 để có log lỗi gửi/QoS1/topic routing mới. Artifact Windows: QuanLyHangHoan-Windows-Installer-v1.8.13. Không tự merge hoặc deploy Brewpage; không xác nhận Actions build nếu chưa đọc được kết quả.
