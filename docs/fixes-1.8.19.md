# 1.8.19 – giảm vòng đi-về thừa và thêm công cụ đo từng kênh

- Các lệnh POST tới ntfy.sh dùng header `Title` tùy chỉnh nên trình duyệt phải gửi thêm một yêu cầu "preflight" (OPTIONS) trước mỗi lần POST. Đổi sang tham số `?title=` giữ yêu cầu ở dạng "đơn giản", bớt một vòng đi-về ở cả điện thoại → ntfy và PC → ntfy.
- Nút "⏱ Đo tốc độ kênh" (trên điện thoại): gửi ping tới PC, PC trả lời trên mọi kênh; điện thoại hiện thời gian đi-về của từng kênh (cùng kênh cả đi lẫn về). Kênh không trả lời trong 8 giây được ghi "không phản hồi" (có thể mạng/nhà mạng chặn kênh đó).
