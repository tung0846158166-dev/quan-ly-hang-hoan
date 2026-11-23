# Chạy app v2 (không cần mở Visual Studio)

Bản đã đóng gói: `v2\dist\QuanLyHangHoan\QuanLyHangHoan.App.exe` (cần .NET 8 Desktop Runtime, máy này đã có).
Tạo lại bản mới sau khi code đổi (chạy trong thư mục `v2`, đóng app trước):

    dotnet publish src\QuanLyHangHoan.App -c Release -r win-x64 --self-contained false -o dist\QuanLyHangHoan

## Hai chế độ
- **Thật** (bấm đúp exe): dùng cơ sở dữ liệu `%LocalAppData%\QuanLyHangHoan\v2\data.db` và cài đặt `settings.json` cùng thư mục. Lần đầu chưa có thì app tự tạo DB rỗng + cài đặt mặc định (kèm mã phòng đồng bộ NGẪU NHIÊN); Trang chủ hiện trạng thái trống.
- **Demo** (`QuanLyHangHoan.App.exe --demo`): dữ liệu giả trong bộ nhớ, KHÔNG ghi DB/cài đặt, KHÔNG kết nối mạng. Dùng để xem giao diện.

## Có gì trong app (mục sidebar)
Trang chủ (thống kê, cảnh báo Vàng 12h/Đỏ 24h, báo cáo nhanh) · Shopee: Hoàn hàng, Đơn hủy · TikTok: Hoàn hàng · Quét nhận hàng (máy quét USB) · Đồng bộ điện thoại (mã phòng, MQTT/ntfy) · Bảng đơn mới · Kho lưu trữ · Đã xóa · Thống kê & báo cáo · Nhập dữ liệu cũ. Giao diện sáng/tối ở nút cạnh khu "Tổng quan nhanh".
Thao tác trên đơn (3 bảng sàn, Kho lưu trữ, Đã xóa): chuột phải hoặc nút `•••` ở cuối dòng: đổi trạng thái, ghi chú, theo dõi, dời lưu trữ, xóa (có xác nhận + Hoàn tác, vẫn khôi phục được ở "Đã xóa"), xem lịch sử sửa; chọn nhiều dòng để làm hàng loạt.

## Nhập dữ liệu cũ lần đầu (chạy thử với dữ liệu thật)
1. Mở app (chế độ thật) -> "Nhập dữ liệu cũ".
2. Chọn thư mục `C:\Users\kimth\OneDrive\Documents\QuanLyHangHoan`.
3. "Chạy thử (Dry-run)": app sao chép sang `%LocalAppData%\QuanLyHangHoan\v2\legacy-copies\<giờ>` (kiểm hash) và chỉ đọc từ bản sao; thư mục gốc KHÔNG bị sửa.
4. So số liệu với kết quả đã đo trên bản sao (xem `docs/legacy-data-schema.md`): 2.307 chính, 2.195 lưu trữ, 166 đã xóa (= 4.668), 3 nhóm trùng khóa, 1.032 ảnh nhúng.
5. Tick xác nhận -> "Nhập thật vào v2". Nhập lại nhiều lần an toàn (không tạo dòng trùng).
6. Kiểm tra: Shopee Đơn hủy ~1.033; TikTok ~734 (mọi loại); Shopee Hoàn hàng = Shopee còn lại; ảnh mất 322.

> **Nhập lại dữ liệu cũ lần thứ hai (đã được bảo vệ từ bản build 13:45 ngày 23/11/2026 trở đi).** Mọi thay đổi bạn làm trong v2 từ bản này (quét, sửa tay, dời lưu trữ, nạp file sàn) được đánh dấu và KHÔNG bị app cũ ghi đè khi nhập lại; với các dòng đó, dữ liệu cũ chỉ bổ sung chỗ trống. Đơn app cũ đã dời sang kho lưu trữ được đổi cờ chứ không tạo bản trùng. Dòng bạn chưa đụng tới trong v2 vẫn lấy dữ liệu mới nhất của app cũ. Các thay đổi v2 làm TRƯỚC bản này chưa được đánh dấu: nếu đã quét/sửa nhiều trước đó, hãy sao lưu `data.db` trước khi nhập lại.

## Thử đồng bộ điện thoại
1. Mở "Đồng bộ điện thoại": ghi lại MÃ PHÒNG (khác `SHP-2026` của app cũ).
2. Trên điện thoại mở trang quét cũ (`mobile_scanner`), nhập đúng mã phòng này.
3. Quét một mã vận đơn: màn hình đồng bộ có dòng nhật ký, kiện chuyển sang "Hàng đã trả".
Lưu ý: bản tin đi qua broker công cộng KHÔNG mã hóa, hãy giữ bí mật mã phòng. ntfy.sh có thể bị mạng chặn (đã gặp trên máy này); khi đó chỉ MQTT chạy: tắt ntfy ở màn đồng bộ cho gọn.

## Làm lại từ đầu
Đóng app, xóa `%LocalAppData%\QuanLyHangHoan\v2\data.db` (và `settings.json` nếu muốn mã phòng mới).
Lưu ý: app cũ (Electron) vẫn là nguồn chính cho tới khi bạn xác nhận số liệu khớp; v2 chưa ghi ngược về app cũ.
