# Triển khai Worker lên Cloudflare (làm MỘT lần, khoảng 10 phút)

Worker tên **`qlhh-cloud`** (KHÔNG đụng tới Worker `app-backend` hay D1 `app-db` bạn đã có). Kho dữ liệu chính là SQLite của Durable Object (5 GB, gói Miễn phí đủ dùng); D1 không dùng. Ảnh lưu ở R2 `app-images` (khóa ảnh bắt đầu bằng `p/` và `t/`, không đụng dữ liệu khác trong kho; muốn tách riêng thì tạo kho `qlhh-images` rồi sửa `bucket_name` trong `cloud/worker/wrangler.jsonc`).

Mọi lệnh chạy trong thư mục `cloud\worker` (PowerShell). Gõ `! lệnh` ở đầu dòng nếu chạy ngay trong phiên Claude Code.

1. **Cài gói:** `npm install`
2. **Đăng nhập Cloudflare:** `npx wrangler login` (mở trình duyệt, bấm cho phép).
3. **Đặt 2 khóa bí mật** (tự nghĩ chuỗi dài ngẫu nhiên ≥ 32 ký tự; gõ khi được hỏi, KHÔNG gửi cho ai kể cả Claude):
   - `npx wrangler secret put BOOTSTRAP_KEY` — dùng một lần để tạo máy của chủ cửa hàng đầu tiên.
   - `npx wrangler secret put IMPORT_KEY` — Apps Script dùng khi đẩy đơn từ Google Sheet.
   Gợi ý tạo chuỗi: `[Convert]::ToBase64String((1..32 | % { Get-Random -Max 256 }))`
4. **Triển khai:** `npx wrangler deploy` → in ra địa chỉ dạng `https://qlhh-cloud.<tên-bạn>.workers.dev`. Địa chỉ này là địa chỉ Worker nhập vào app (Cài đặt > Cloud).
5. **Tạo máy chủ cửa hàng đầu tiên** (chạy MỘT lần, thay địa chỉ và khóa bạn đặt ở bước 3):
   ```powershell
   $b = '{"bootstrapKey":"<BOOTSTRAP_KEY>","deviceName":"Máy chủ cửa hàng","kind":"pc"}'
   Invoke-RestMethod -Method Post -Uri "https://qlhh-cloud.<tên-bạn>.workers.dev/v1/pair/bootstrap" -ContentType "application/json; charset=utf-8" -Body ([Text.Encoding]::UTF8.GetBytes($b))
   ```
   Kết quả có `token`: **lưu lại ở nơi an toàn** (chỉ hiện một lần). Sau này giao diện ghép thiết bị sẽ làm việc này thay bạn; các máy còn lại ghép bằng mã 6 số do chủ tạo.
6. **Kiểm tra:** mở `https://qlhh-cloud.<tên-bạn>.workers.dev/v1/me` — phải thấy lỗi `unauthorized` (đúng, vì chưa có token).

Chạy thử cục bộ (không cần đăng nhập, không đụng cloud thật): tạo `cloud/worker/.dev.vars` gồm hai dòng `BOOTSTRAP_KEY=...` và `IMPORT_KEY=...` rồi `npx wrangler dev --local`. Chạy test: `npm test`.

## Hạn mức gói Miễn phí (xem docs/cloud-design.md mục 3.7)
Workers 100.000 yêu cầu/ngày (1.000/phút, 10 ms CPU/yêu cầu); Durable Object 100.000 yêu cầu/ngày, 5 GB; R2 10 GB, 1 triệu lượt ghi + 10 triệu lượt đọc mỗi tháng. Chạm trần ngày thì dừng tới 00:00 UTC (07:00 giờ Việt Nam), không bị tính tiền. API có `GET /v1/quota` đếm mức dùng trong ngày và cảnh báo ở 70%.
