# 1.8.14 — Phản hồi quét nhanh hơn khi có nhiều đơn

Vấn đề: với ~20.000 đơn (đơn nào quét khớp thì có ảnh), điện thoại chờ trung bình 5-6 giây mới thấy kết quả từ PC.

## Nguyên nhân (đọc từ code)

Mỗi mã quét, PC chỉ gửi ACK **sau khi** `saveOrders(true)` ghi xong. Mỗi lần ghi đó:

- tạo JSON của toàn bộ danh sách đơn nhiều lần (localStorage, IndexedDB, IPC sang `main.js`) và ghi file + fsync;
- file ghi xuống ổ cứng chứa luôn ảnh gốc base64 của mọi đơn đã có ảnh (có thể hàng trăm MB, giới hạn 512 MB);
- tra cứu mã vận đơn chạy hàng chục regex cho từng đơn trong 20.000 đơn, nhiều lần mỗi lần quét.

## Thay đổi

- **Ảnh lưu thành từng file riêng** (`QuanLyHangHoan/photos/`, mỗi ảnh ghi một lần bằng tmp + fsync + rename). File dữ liệu chính và IndexedDB danh sách đơn không còn mang ảnh đã lưu riêng. Ảnh chỉ bị bỏ khỏi file chính sau khi file ảnh đã ghi thành công; nếu ghi ảnh lỗi, ảnh vẫn nằm trong file chính nên không mất.
- Ảnh có từ các phiên bản trước được chuyển sang file riêng dần ở nền (8 ảnh mỗi lượt), không chặn việc quét.
- Khi mở app, ảnh nào không còn trong IndexedDB sẽ được nạp lại từ file ảnh.
- **Gộp lần ghi (group commit):** các yêu cầu lưu đến khi một lần ghi đang chạy dùng chung một lần ghi kế tiếp; lần ghi đó chụp dữ liệu sau khi lần trước xong nên vẫn bao gồm thay đổi của từng yêu cầu. ACK vẫn chỉ gửi sau khi dữ liệu đã ghi xuống ổ cứng.
- localStorage (chỉ là bản dự phòng) trên bản `.exe` được làm mới sau 5 giây, không còn nằm trên đường trả kết quả.
- `getOrderTrackingCodes` có bộ nhớ đệm theo từng đơn, chỉ tính lại khi mã của đơn đổi.
- Điện thoại: thanh "Chờ PC xác nhận: N mã" (đổi đỏ khi chờ quá 10 giây); khi PC đang online nhưng có từ 5 mã chưa xác nhận thì tạm dừng nhận mã mới. Khi PC không phản hồi (tắt), hàng đợi offline vẫn hoạt động như cũ.

## Số đo (mô phỏng bằng Node, 20.000 đơn, 800 ảnh ~250 KB)

Đo phần chuẩn bị và tuần tự hóa dữ liệu trên đường lưu (chưa gồm fsync/IPC thật):

| | mỗi lần lưu | dữ liệu ghi xuống ổ cứng |
|---|---|---|
| Trước (1.8.13) | ~2.000 ms | ~210 MB |
| Sau, khi ảnh đã chuyển sang file riêng | ~70 ms | ~10 MB |

Đây là số đo mô phỏng, không phải số trên máy của bạn.

## Chưa kiểm thử

- Máy Windows/ổ cứng thật, app `.exe` thật với dữ liệu thật, thời gian đến lúc điện thoại báo kết quả.
- Tốc độ đọc QR/mã vạch trên điện thoại: vòng quét đã dùng bộ đọc có sẵn của trình duyệt khi có, vùng đọc giữa khung và Quagga/jsQR chạy nền. Trên iPhone (Safari không có `BarcodeDetector`) mã vạch vẫn do Quagga (JavaScript) đọc nên có thể chậm; chưa thay đổi phần này vì không thể đo trên điện thoại thật.
- ACK vẫn đi qua `ntfy.sh` và MQTT công cộng; độ trễ mạng nằm ngoài phạm vi sửa này.
