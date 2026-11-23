# Kế hoạch viết lại app iPhone (23/11/2026)

## Quyết định của chủ dự án
- Viết lại **app gốc SwiftUI** (IPA), **giữ đủ chức năng bản cũ**, chạy với PC v2 qua mã phòng. App cũ trong `ios-native/` GIỮ NGUYÊN làm bản tham chiếu/đối chiếu cho tới khi chủ dự án xác nhận bản mới; bản mới ở `ios-app/` (khởi đầu là bản sao có kiểm thử của `ios-native/`, rồi sửa dần).
- Quét tạo ĐƠN HỦY từ điện thoại: BỎ. Đơn thường quét từ điện thoại: vào thẳng Bảng chính của PC. (xem `docs/sync-design.md`)

## Giới hạn quan trọng (nói thật)
Máy Windows này KHÔNG biên dịch/chạy được Swift/SwiftUI/iOS. Mã Swift viết ở đây chỉ được kiểm chứng khi chạy trên **GitHub Actions (macOS)**: biên dịch + `swift test` cho lõi `ScannerCore` + build IPA chưa ký. Vì vậy:
- Chưa thể tuyên bố "chạy được" cho tới khi CI xanh; sau đó cần chủ dự án cài IPA lên iPhone và thử thật.
- Cần chủ dự án: cài git (`winget install Git.Git`), đẩy thư mục dự án lên GitHub repo của mình (đăng nhập tài khoản là việc của chủ dự án, không giao cho trợ lý), bấm chạy workflow `Build iPhone App v2`.

## Hợp đồng với PC v2 (đã cài và có test ở C#: `Data/Sync/*`)
- Điện thoại -> PC: `phone_connected`, `snapshot_request`, `barcode` (quét nhận), `new_order_scan` (đơn thường; `cancelled:true` bị từ chối), `photo`/`photo_chunk`, `order_note`, `order_edit` (chỉ trạng thái `st`), `order_delete` (xóa mềm), `cancel_receive`.
- PC -> điện thoại: `pc_hello`/`pc_heartbeat`, `pc_ack` (`ackStage` scan|photo|note|edit|delete; `outcome` success|duplicate|not_found|ambiguous|error|invalid|locked), `snapshot_chunk`/`snapshot_delta` (trường dòng k m/c/a, c o p n co sz q rd due st ty pre pd rv wl wr nt rcd lv ci ps x rw rws ar), `watch_sync`.
- `inbox_push(_many)` và Hộp Đơn Mới: v2 KHÔNG có; app mới bỏ phần Hộp Đơn Mới.
- Trạng thái nhận kho của v2: "Hàng đã trả", "Chưa trả", "Không cần nhận", "Bị mất", "Chưa về" (app gửi "Chưa nhận" cho "Chưa trả"; "Hư hỏng" không có ở v2 nên bỏ khỏi danh sách chọn).

## Chia việc
- Phiếu **T028** (agy): dựng `ios-app/` thành app mới sạch (bỏ Hộp Đơn Mới + tạo đơn hủy; đổi tên/biểu tượng/phiên bản; đồng bộ với hợp đồng v2; thanh tab + giao diện thống nhất với v2 desktop), CI mới `.github/workflows/build-iphone-v2.yml`.
- Phiếu **T029** (agy): các màn Quét, Danh sách đơn/Bảng, Chi tiết đơn, Hàng hoàn về (4 chặng), Thống kê, Thông báo hạn (vàng 12h/đỏ 24h), Lịch sử quét, Cài đặt (mã phòng, đường MQTT/ntfy) hoàn thiện theo hợp đồng.
- Claude: kiểm tra từng phiếu bằng đọc mã (không có trình biên dịch), kiểm tra phạm vi, giữ hợp đồng khớp C#; khi chủ dự án đã đẩy lên GitHub thì đọc kết quả CI.
- Test lõi `ScannerCore` (Swift) chạy ở CI: giữ và mở rộng bộ test hiện có (`ios-app/ScannerCore/Tests`).
