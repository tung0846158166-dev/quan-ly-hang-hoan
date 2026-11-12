# 1.8.17 – đua nhiều kênh để giảm độ trễ mạng

Đo thực tế bản 1.8.16: PC xử lý 311 ms, mạng + chờ 2566 ms. Nên bản này giảm phần mạng:
- Thêm HiveMQ và Mosquitto cạnh EMQX trên cả điện thoại và PC; gửi/nhận song song, trùng được bỏ qua nhờ requestId.
- Điện thoại hiện "(qua ntfy/emqx/hivemq/mosquitto)" để biết kênh nào thắng.
- Sửa nhãn phiên bản trên giao diện (trước đó vẫn hiện 1.8.15).
