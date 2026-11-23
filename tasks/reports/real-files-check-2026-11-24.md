# Kiểm tra bằng 2 file thật của chủ dự án (24/11/2026)

File: `tiktok.csv` (35 dòng) và `salework.xlsx` (338 dòng), đặt ở `anh-tu-nguoi-dung/` (đã nằm trong .gitignore, KHÔNG đẩy lên GitHub). Chỉ ghi SỐ LIỆU TỔNG ở đây, không có mã đơn/mã vận đơn/tên khách. Chạy trên cơ sở dữ liệu TẠM, không đụng `data.db` thật.

## Kết quả
| Việc | Kết quả |
|---|---|
| TikTok qua "Nạp file Excel" | 35 đơn, 34 "Giao không thành công" + 1 "Trả hàng/Hoàn tiền", không nhập nhằng, không trùng mã |
| Nạp lại CÙNG file lần 2 | thêm 0, cập nhật 0, số dòng không đổi (đúng quy tắc 5) |
| Salework qua đường Salework (tự lấy) | 321 đơn, sàn TikTok, loại THẤT THOÁT: 109 chờ khách gửi, 186 đang về, 26 đã về |
| Salework qua "Nạp file Excel" | 325 đơn, nhận là Shopee, "Trả hàng/Hoàn tiền", "Chờ bạn xác nhận hàng trả về" |
| Đối chiếu với app CŨ (oracle chạy mã v1.9.77) | KHỚP từng số ở cả 3 lần chạy (số đơn, loại, trạng thái, phân bố 109/186/26) |
| Dữ liệu khách (tên, SĐT, địa chỉ) lọt vào DB? | KHÔNG (thử 31 tên, 34 SĐT, 28 địa chỉ, 34 tên đăng nhập: 0 lần xuất hiện) |

## Phát hiện cần chủ dự án biết
1. **Cùng một file Salework cho hai kết quả khác nhau tùy đường nạp** (321 đơn TikTok-THẤT THOÁT hay 325 đơn Shopee). App cũ cũng như vậy. Nếu dùng cả hai đường cho cùng dữ liệu, cùng một đơn có thể xuất hiện hai lần ở hai sàn. Nên chọn MỘT đường cho dữ liệu Salework.
2. **4 đơn "Trả hàng hoàn tiền - Một phần" (trạng thái THÀNH CÔNG) bị đường Salework bỏ qua** (321 thay vì 325), app cũ cũng vậy. Đường "Nạp file Excel" có nhận 4 đơn này (2 đến 5 sản phẩm mỗi đơn).
3. **File Salework không cho biết sản phẩm nào đã hoàn**: mọi dòng sản phẩm đều số lượng 1 và không có cột "đã hoàn". Vì vậy v2 (và app cũ) coi mọi sản phẩm là đã hoàn, cờ "hoàn một phần" không bật và không tô màu phân biệt được cho dữ liệu Salework. File TikTok có cột "Sku Quantity of return" nên phân biệt được (file mẫu lần này không có đơn hoàn một phần để thử).
4. Danh sách cột cá nhân thiếu tên tiếng Anh của TikTok (Recipient, Phone #, địa chỉ, Buyer Message). Importer không đọc và không lưu các cột này nên không lộ, nhưng đã bổ sung vào danh sách bỏ ngay khi đọc cho chắc (có test).

## Chưa kiểm chứng
- File TikTok mẫu không có đơn hoàn một phần, nhiều kiện cùng mã đơn, hay mã trùng nên các tình huống đó vẫn chỉ được test bằng dữ liệu giả.
- Chưa thử trên `data.db` thật có sẵn dữ liệu (nạp vào DB trống). Chưa thử đường Google Sheet và Salework tự động thật.
- Một test cũ (T020 demo không chạm đĩa) từng không đạt đúng một lần khi chạy cả bộ lúc máy bận, chạy lại 2 lần sau đều đạt (539/539); chưa bắt được nguyên văn lỗi nên chưa rõ nguyên nhân.
