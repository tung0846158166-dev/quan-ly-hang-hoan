# Dựng máy chủ MQTT riêng (Mosquitto + Caddy, TLS cổng 443)

Cần: 1 VPS Linux nhỏ (1 CPU, 512 MB là đủ), có Docker, và 1 tên miền trỏ về IP VPS (ví dụ `mq.tenmiencuaban.com`).

1. Chép thư mục này lên VPS (`docker-compose.yml`, `mosquitto.conf`, `Caddyfile`, `acl`).
2. Sửa `Caddyfile`: thay `mq.example.com` bằng tên miền của bạn.
3. Tạo tài khoản (đổi `kho` và mật khẩu):
   `docker compose run --rm mosquitto mosquitto_passwd -c -b /mosquitto/config/passwd kho MAT_KHAU_MANH`
4. Chạy: `docker compose up -d` (Caddy tự xin chứng chỉ TLS).
5. Trong app PC (Đồng bộ điện thoại > Máy chủ MQTT riêng) và app iPhone (Cài đặt > Máy chủ MQTT riêng) nhập CÙNG địa chỉ:
   `wss://kho:MAT_KHAU_MANH@mq.tenmiencuaban.com/mqtt`
   (mật khẩu có ký tự đặc biệt thì mã hoá %: `@` = `%40`). Rồi bấm lưu; hai bên dùng cùng mã phòng.

Giới hạn tin: `mosquitto.conf` cho phép tin tới 8 MB (đủ cho mảnh ảnh). Chỉ tài khoản có mật khẩu mới đọc/ghi được; `acl` chỉ cho chủ đề của app.
Địa chỉ riêng thay hoàn toàn 3 broker công cộng ở máy đó; ntfy vẫn là đường dự phòng (có thể tắt).
