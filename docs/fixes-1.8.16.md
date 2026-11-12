# 1.8.16 – giảm độ trễ phản hồi sau khi quét

- Vẽ lại bảng 20.000 dòng được gộp: một loạt quét liên tiếp chỉ vẽ một lần (tối đa 600 ms sau lần quét cuối), không chặn cửa sổ ngay khi mã tiếp theo tới.
- Bản sao IndexedDB trên máy tính (bản exe) được ghi sau 4 giây thay vì nằm trong đường đi của phản hồi.
- Gửi dữ liệu lưu xuống main.js dưới dạng chuỗi JSON (bỏ structured clone 20.000 đơn qua IPC).
- Phản hồi gửi kèm `processingMs` và `saveMs`; điện thoại hiển thị "PC xử lý X ms, trong đó lưu Y ms; mạng + chờ Z ms" để biết thời gian đi đâu.
