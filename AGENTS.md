# AGENTS.md - Quản lý hàng hoàn (hướng dẫn chung cho Claude Code & Antigravity)

## Mục tiêu
Quản lý & đối soát hàng hoàn Shopee / TikTok Shop / Lazada cho chủ shop và kho. Desktop Windows (Electron) + app quét trên điện thoại.

## HƯỚNG MỚI (v2): app Windows native C# .NET 8 + WPF + SQLite, thư mục `v2/` (xem PLAN.md). KHÔNG dựng Electron/React/HTML cho desktop. Quy ước: lõi nghiệp vụ ở `Core` không được tham chiếu WPF; UI theo MVVM; mọi logic có test xUnit. Phần dưới là mô tả APP CŨ (chỉ để tham khảo/đối chiếu).

## Kiến trúc hiện tại (v1.9.75, tải từ GitHub tung0846158166-dev/quan-ly-hang-hoan)
- `main.js` (Electron main): tray, autostart, lưu JSON nguyên tử (tmp -> fsync -> rename), hàng đợi ghi, tự backup 2h/lần giữ 7 ngày, theo dõi thư mục import, IPC.
- `preload.js`: cầu nối `window.desktopDiskAPI` (contextIsolation bật, nodeIntegration tắt).
- `Bang_Excel_...html` (~1.3MB, ~22.9k dòng, ~667 hàm): TOÀN BỘ UI + logic nghiệp vụ. Tải thư viện qua CDN: Tailwind, SheetJS, MQTT, html5-qrcode, tesseract.js, qrcode-generator.
- `mobile_scanner.html`: trang quét trên điện thoại (hàng đợi bền IndexedDB, gửi ảnh ngầm).
- `salework_*.js`: tự động hóa Salework qua cửa sổ Electron ẩn/hiện.
- `disk_aux.js`: lưu key phụ. `ios-native/`: app iOS. `tests/`: ~75 file test Node (`npm test`).
- Dữ liệu: `Documents/QuanLyHangHoan/du_lieu_hang_hoan.json` + thư mục `photos/`.
- Đồng bộ điện thoại: MQTT + ntfy theo "mã phòng". Google Sheet đồng bộ 10 phút/lần qua Apps Script.

## Quy tắc nghiệp vụ BẤT BIẾN (đừng phá)
1. Một mã đơn gốc có thể có nhiều kiện hoàn A/B/C, mỗi kiện một mã vận đơn, một dòng, trạng thái/ảnh/ngày nhận riêng.
2. Nhận kiện A KHÔNG được nhận B/C. Cùng orderId hoặc mã yêu cầu hoàn KHÔNG đủ để gộp.
3. Mã giao đi dùng chung hoặc mã đơn gốc có nhiều ứng viên => báo "ambiguous", yêu cầu kiểm tra.
4. Chỉ gộp mã thay thế khi có liên kết tường minh (cột Tracking Codes). Giữ mã lịch sử.
5. Nhập lại cùng kiện thì cập nhật, không cộng lặp sản phẩm.
6. Chỉ ACK thành công SAU khi đã lưu bền (persistence-before-ACK). Ảnh chưa ACK không coi là đã lưu.
7. Cảnh báo: Vàng sau 12h, Đỏ sau 24h kể từ lúc hàng về. Lọc đúng "phòng", không kênh GLOBAL.
8. Không xóa/ghi đè dữ liệu cũ, không đoán để tách/gộp dữ liệu lịch sử.

## Quy ước làm việc chung
- Mỗi bên (Claude / Antigravity) làm module/file khác nhau, ghi rõ trong `TASKS.md`. Cập nhật `HANDOFF.md` khi bàn giao.
- Viết test trước (đỏ -> xanh), chạy `npm test` trước khi báo xong.
- Không dùng dữ liệu đơn thật trong test.
- Không tuyên bố PASS cho thứ chưa chạy thật (Windows installer, iPhone, 4G, XLSX thật).
