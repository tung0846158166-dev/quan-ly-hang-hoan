# T029 - App iPhone v2: hoàn thiện các màn hình (giao cho: antigravity, người kiểm tra: claude)

Chạy SAU T028. PHẠM VI: CHỈ `ios-app/` (không sửa `ios-native/`, `v2/`). Đọc trước: `AGENTS.md`, `docs/iphone-app-v2-plan.md`, `tasks/reports/T028-report.md`, `v2/src/QuanLyHangHoan.Data/Sync/PhoneSnapshot.cs` (ý nghĩa từng trường dòng), `PhoneActions.cs`, `docs/sync-design.md`.
GIỚI HẠN THẬT như T028: không có Swift trên máy này, KHÔNG biên dịch được; viết thận trọng, bám mã đã chạy ở `ios-native/`, tự đọc lại, KHÔNG tuyên bố đã build/test xanh.

## Việc cần làm (giữ đủ chức năng bản cũ, theo hợp đồng PC v2)
1. **Quét**: camera quét mã vạch/QR (mã vận đơn), có ô nhập tay, đèn pin, chụp ảnh kiện (gửi `photo`/`photo_chunk` sau khi có xác nhận quét), hàng đợi gửi bền khi mất mạng (không mất mã; gửi lại cùng `requestId`), chuyển giữa "Nhận kiện hoàn" và "Tạo đơn mới" (BỎ "Tạo đơn hủy"), rung/âm báo theo kết quả (`success` xanh, `duplicate` vàng, `not_found`/`ambiguous`/`invalid`/`locked`/`error` đỏ kèm lời giải thích tiếng Việt của PC), chống quét trùng liên tiếp cùng mã trong vài giây.
2. **Đơn** (danh sách từ `snapshot_chunk`/`snapshot_delta`): lọc Bảng chính / Chưa nhận / Đã nhận / Quá hạn (đỏ) / Sắp đến hạn (vàng) / Đang theo dõi / Đơn hủy / Kho lưu trữ; tìm theo mã (tính cả các mã khác trong `x`), không phân biệt dấu; mỗi dòng: sàn, mã đơn, mã vận đơn, sản phẩm, trạng thái, cảnh báo `lv`, hạn `due`. Bấm vào một đơn: màn **Chi tiết đơn** (mọi trường có trong dòng) với thao tác: đổi trạng thái, ghi chú + bật/tắt theo dõi (`order_note`), xóa (`order_delete`, hộp xác nhận "đơn vẫn khôi phục được trên máy tính"); chờ `pc_ack` rồi mới coi là xong, hiện trạng thái "đang chờ máy tính" khi chưa có ack, báo lỗi rõ khi `error`/`invalid`/`locked`/`ambiguous`.
3. **Đơn hủy**: bảng riêng cho `k == "c"`, chọn nhiều dòng (theo `ci`) rồi "Đánh dấu đã nhận"/"Chưa nhận" (`cancel_receive`); KHÔNG có tạo đơn hủy.
4. **Hàng hoàn về** (4 chặng): từ `rw`/`rws`/`ar`: Đã đồng ý -> Đang về -> Đã về shop -> Đã nhận kho; lọc theo chặng có số đếm; chấm tiến trình; quét nhận một kiện thì chặng chuyển 4 ngay trên máy (cập nhật cục bộ, PC xác nhận sau).
5. **Thống kê**: số đơn theo sàn/trạng thái/cảnh báo từ dữ liệu nhận được, đối chiếu với bảng `stats` PC gửi (hiện cảnh báo "lệch N đơn" nếu `dropped` > 0 hoặc số nhận được khác `sent`).
6. **Thông báo hạn**: lên lịch thông báo cục bộ "sắp đến hạn"/"đã đến hạn" từ `due` của đơn chưa nhận (như bản cũ), xin quyền thông báo lần đầu, hủy/đổi lịch khi đơn đã nhận, không quá giới hạn 64 thông báo lịch của iOS (ưu tiên đơn gần hạn nhất).
7. **Lịch sử quét** (các lần quét và kết quả, tối đa 500, lưu giữa các lần mở) và **Cài đặt** (mã phòng có nút dán/sao chép, bật/tắt MQTT/ntfy, trạng thái kết nối từng đường, "Xin lại danh sách từ máy tính" gửi `snapshot_request`, hiển thị "PC đang online/offline" từ `pc_hello`/`pc_heartbeat` quá 45 giây là offline, phiên bản app).
8. Đồng bộ trạng thái PC: nhận `watch_sync` cập nhật theo dõi; xử lý `snapshot_delta` chỉ áp khi `base` khớp bản gốc đang giữ (nếu không khớp thì gửi `snapshot_request`).
9. Giao diện thống nhất tông app PC v2, chế độ tối, chữ lớn dễ đọc ngoài kho, dùng một tay được.

## Tiêu chí hoàn thành
- Mọi thay đổi chỉ trong `ios-app/`. Không còn tham chiếu `Inbox`/`inbox_push`/`cancelOrder`/`SHP-2026`.
- Test lõi `ios-app/ScannerCore/Tests` bổ sung cho logic thuần (áp delta, lọc/tìm không dấu, tính cảnh báo, lập lịch thông báo, hàng đợi gửi lại) - viết để chạy ở GitHub Actions (`swift test`).
- Báo cáo `tasks/reports/T029-report.md`: bảng chức năng -> tệp thực hiện; mục "CHƯA BIÊN DỊCH (không có Swift trên máy này)"; mục "việc cần kiểm trên iPhone thật"; dòng cuối `TRẠNG THÁI: XONG` hoặc `CHƯA XONG (lý do)` (XONG = mã viết xong và tự rà soát, KHÔNG phải đã biên dịch). Cập nhật `TASKS.md`.
