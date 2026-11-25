# Google Sheet tự nạp lên cloud (Apps Script)

Sheet tự gửi đơn lên cloud, KHÔNG cần máy tính bật. Máy tính ở chế độ cloud sẽ không nạp Sheet nữa (tránh hai nơi cùng ghi).

## Cách chạy
- Apps Script đọc các tab ngay trong bảng tính. Nó gộp tab và chuyển dòng thành kiện, cùng quy tắc với app máy tính (kiểm bằng 450 ca chung với C#).
- Chỉ gửi kiện **có thay đổi** so với lần trước. Dấu vân tay lưu ở tab ẩn `_qlhh_state` (đừng xóa; nếu lỡ xóa thì lần sau gửi lại tất cả, cloud tự bỏ qua kiện không đổi).
- Cloud đối chiếu với dữ liệu đã có theo đúng quy tắc A/B/C. Ca nhập nhằng thì không ghi, mà đưa vào danh sách "cần kiểm tra".
- **Không gửi**: tên, SĐT, địa chỉ người mua/người nhận, ghi chú người mua.
- Lịch: 5 phút/lần (0h–7h: 30 phút/lần), đối soát đầy đủ lúc 2h sáng.
- Hết hạn mức ngày (lỗi 429) hoặc sai khóa (401): dừng ngay, lần sau gửi tiếp phần còn lại.

## Cài đặt (làm một lần)
1. Trên máy tính đã cài dự án, chạy `node cloud/sheet-script/build.js`. Lệnh này tạo `cloud/sheet-script/dist/QLHH.gs`.
2. Mở Google Sheet → **Tiện ích mở rộng → Apps Script**. Tạo tệp mới tên `QLHH` và dán toàn bộ nội dung `QLHH.gs`. Bấm **Lưu**.
   (Nếu bảng tính đã có Apps Script cũ để máy tính tải CSV thì cứ để nguyên, hai phần không đụng nhau.)
3. Vào **Cài đặt dự án (bánh răng) → Thuộc tính tập lệnh** và thêm:
   - `QLHH_CLOUD_URL` = `https://qlhh-cloud.qlhh-cloud-worker.workers.dev`
   - `QLHH_IMPORT_KEY` = khóa nhập. Đây là IMPORT_KEY đã đặt cho Worker, **chỉ chủ shop giữ**: không dán vào chat, không gửi ai.
   - Tùy chọn:
     - `QLHH_TABS` = tên các tab cần đọc, cách nhau dấu phẩy. Bỏ trống = mọi tab không bắt đầu bằng `_`.
     - `QLHH_MAX_DAYS` = số ngày hoặc ngày `dd/mm/yyyy`.
     - `QLHH_SKIP_TYPES` = loại bỏ qua, ví dụ `huy sau;bom`.
     - `QLHH_ONLY_RETURNS` = `false` để nạp cả đơn giao thành công.
4. Chọn hàm `qlhhTestNow` → **Chạy** → cấp quyền. Xem **Nhật ký thực thi**: dòng `QLHH: ... kiện trên Sheet, gửi .../...`.
5. Chọn hàm `qlhhInstallTriggers` → **Chạy** một lần để tạo lịch tự động.

## Tài khoản chỉ có quyền XEM Sheet (dự án riêng)
Không cần quyền sửa Sheet. Không ghi gì vào Sheet gốc.
1. Đăng nhập tài khoản đó, vào https://script.google.com → **Dự án mới**.
2. Dán `QLHH.gs` vào thay nội dung mẫu, rồi **Lưu**.
3. Đặt thuộc tính như bước 3 ở trên, thêm `QLHH_SHEET_ID` = đường link Sheet (dán nguyên link cũng được).
4. Chạy `qlhhTestNow` (cấp quyền), rồi chạy `qlhhInstallTriggers`.

Trạng thái lưu ở bảng tính "QLHH - trạng thái đồng bộ (đừng xóa)", tự tạo trong Drive của tài khoản này.
Nếu tài khoản bị thu quyền xem Sheet thì đồng bộ dừng; nhật ký sẽ báo lỗi.

## Cập nhật bản mới
Chạy lại `build.js`, rồi dán đè tệp `QLHH` trong Apps Script. Lịch và thuộc tính giữ nguyên.

## Kiểm thử (cho người phát triển)
- `node cloud/sheet-script/test/run.js`: so với 450 ca do C# sinh (`cloud/golden/sheet-vectors.json`) và đo 5.000 dòng.
- `node cloud/sheet-script/test/sync.test.js`: chạy đúng tệp `dist/QLHH.gs` với Apps Script giả lập. Kiểm các điểm:
  - bỏ cột cá nhân;
  - chỉ gửi kiện đổi;
  - lỗi 429 thì dừng rồi lần sau gửi tiếp;
  - không lộ khóa.

Chưa chạy thật trên Google Apps Script. Lần đầu chủ shop cài, hãy xem nhật ký của `qlhhTestNow`.
