# 1.8.20 – xác nhận quét nhanh hơn

Số đo thực tế trên bản 1.8.19 (điện thoại 4G, PC wifi): đi-về mỗi kênh MQTT ~0,6–0,7 s, nhưng "mạng + chờ" lên 0,9–2,6 s và PC xử lý 0,6–2,2 s mỗi mã. Nguyên nhân:
- Bản tin quét kèm ảnh bị gửi tới cả 3 broker, rồi ảnh lại gửi thêm lần nữa qua `sendPhotoPacket` và hàng chục mảnh ntfy → tải lên nhiều lần qua 4G.
- Mỗi ảnh tới PC làm PC vẽ lại cả bảng 20.000 dòng; các mã quét liên tiếp xếp hàng sau nhau.

Thay đổi: bản tin quét nhỏ, ảnh đi riêng một lần; ntfy chỉ là dự phòng; vẽ lại bảng gộp theo loạt; ACK báo độ treo của PC (`lagMs`).
Chưa làm (nếu số đo mới vẫn cao): lưu theo từng phần thay vì ghi lại toàn bộ file 13 MB mỗi lần quét.
