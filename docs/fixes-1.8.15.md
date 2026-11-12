# 1.8.15 — Đọc mã vạch trên điện thoại nhanh hơn

Vấn đề: nhận diện mã vạch/QR trên điện thoại chậm. Trên iPhone (Safari không có `BarcodeDetector`) mã vạch do Quagga (JavaScript) đọc, mỗi lần thử mất hàng trăm ms.

## Thay đổi

- Thêm bộ đọc **Code 128** thuần JavaScript (`fastDecodeCode128Canvas`) chạy trước Quagga/jsQR trong vòng quét và khi bấm "Chụp đọc mã". Mã vận đơn Shopee/TikTok (SPX, SOC, GHN, J&T, …) chủ yếu là Code 128.
- Mỗi khung hình quét 7 dòng ngang; không thấy thì quét thêm dòng dọc (nhãn xoay 90°, chỉ khung chẵn). Đọc được cả mã bị lộn ngược.
- Chỉ nhận mã khi: đúng mẫu Code 128, **checksum đúng**, độ dài ≥ 6 và **hai lần đọc khớp nhau** (hai dòng trong cùng khung hình, hoặc hai khung liền nhau trong 0,8 giây).
- Không đọc được (Code 39, EAN, QR, mã quá nhỏ/mờ…) thì vòng quét cũ chạy như trước; QR vẫn do jsQR/native đọc.

## Kiểm thử

- Bảng mẫu Code 128 và dữ liệu thử được tạo từ thư viện `reportlab` (độc lập với code điện thoại): 240 dòng ảnh có mờ, nhiễu, thiếu sáng, sáng lệch, lộn ngược. Đọc đúng 100% khi mỗi vạch nhỏ nhất ≥ 2 điểm ảnh; trung bình ~0,3 ms mỗi dòng (Node).
- 6.000 dòng nhiễu ngẫu nhiên và các mã bị phá hỏng một ký hiệu: không có lần đọc sai nào.
- Thử thêm (không đưa vào repo) ảnh 2 chiều xoay nghiêng đến 10°, nén JPEG, sáng lệch: không có lần đọc sai; khoảng 2/3–5/6 khung được đọc ngay, phần còn lại rơi về bộ đọc cũ (thường là mã rất nhỏ/mờ, khoảng 2 điểm ảnh/vạch).

## Chưa kiểm thử

- Camera và điện thoại thật (tốc độ và tỉ lệ đọc thực tế trên iPhone/Android), nhãn Shopee/TikTok thật. Chi phí mỗi khung đo trên Node (~5–8 ms); trên điện thoại có thể gấp vài lần.
- QR: không đổi, vẫn do jsQR/native đọc.
