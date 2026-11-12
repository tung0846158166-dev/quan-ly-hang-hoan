# 1.8.21 – bớt treo PC, bớt yêu cầu thừa

Số đo bản 1.8.20: phản hồi ~1 s, PC xử lý ~0,1 s, nhưng "PC bị treo" 370–760 ms ở nhiều lần quét.
- Sau mỗi loạt quét, `highlightAndFlashOrderRow` tính lại toàn bộ bảng tổng hợp (`recalcTotalsAndSummary`) rồi `renderAll(false)` lại tính tiếp lần nữa: bỏ lần đầu (cùng kết quả cuối).
- Lần vẽ lại do ảnh (`schedulePhoneRender`) bị bỏ qua nếu bảng đã được vẽ lại sau khi ảnh tới.
- Điện thoại bỏ yêu cầu ntfy cho mỗi lần quét khi MQTT đang kết nối và ntfy chưa từng gửi tin nào tới trang này.
Chưa làm: chỉ cập nhật đúng dòng vừa quét thay vì lọc lại 20.000 đơn (thay đổi lớn hơn, chỉ làm nếu vẫn còn treo).
