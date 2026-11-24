# T038 - App iPhone: RÀ SOÁT LỖI TRƯỚC KHI CHẠY DỮ LIỆU THẬT (giao cho: antigravity, người kiểm tra: claude)

PHẠM VI: CHỈ `ios-app/` (sửa lỗi tìm được) + `tasks/reports/T038-report.md`. KHÔNG `git commit`/`git push`. Giới hạn như T036/T037 (không có Swift/Xcode, iOS 16 tối thiểu, tự đọc lại từng tệp).

Chủ dự án sắp đưa dữ liệu thật vào chạy. Hãy RÀ SOÁT TỪNG DÒNG và sửa mọi lỗi tìm được, ưu tiên theo thứ tự:
1. **Quét mã vạch / QR** (`CameraController.swift`, `ScanView.swift`, `OrdersView.swift` phần quét, `ScannerCore/TrackingCode.swift`): chạy đua luồng, thao tác cấu hình camera ngoài luồng, rò rỉ Timer/observer, camera không dừng khi rời tab / vào nền / có cuộc gọi, quyền camera bị từ chối rồi bật lại, xoay màn hình, mã lặp, `rectOfInterest`, mã QR dạng URL/JSON/nhiều dòng, mã có khoảng trắng hoặc ký tự lạ, mã chữ thường.
2. **Hàng đợi bền và gửi ảnh** (`SyncEngine.swift`, `ScanItem` trong ScannerCore): mất mạng giữa chừng, tắt app khi đang gửi, gửi lại không sinh bản trùng (idempotencyKey), ảnh chưa ACK không được coi là đã lưu, dọn tệp ảnh khi xóa lượt quét, dung lượng ảnh không phình vô hạn, giới hạn lịch sử.
3. **Đồng bộ snapshot** (`SnapshotStore.swift`, `Snapshot.swift`): gói chia mảnh thiếu/trùng/sai thứ tự, snapshot rất lớn (5.000 đơn: thời gian dựng chỉ mục, bộ nhớ), ghép lệnh sửa cục bộ chưa được PC xác nhận.
4. **Giao diện** (`HomeView`, `OrdersView`, `OrderDetailView`, `ProductStatsView`, `HistoryView`, `SettingsView`): chỉ số ngoài phạm vi (chia cho 0, mảng rỗng, `Date` nil), danh sách 5.000 dòng có cuộn mượt không (List/LazyVStack, tránh tính lại nặng trong `body`), Dynamic Type lớn, chế độ tối, tên tiếng Việt dài.
5. Mã còn sót `Inbox`/`inbox_push`/`cancelOrder`/`SHP-2026`, mọi `try!`, `fatalError`, `!` ép kiểu, `as!`, chỉ số mảng không kiểm tra: liệt kê và sửa.
Quy tắc nghiệp vụ bất biến trong AGENTS.md (đặc biệt 1, 2, 3, 6) KHÔNG được phá. Mỗi lỗi sửa kèm test XCTest thuần (ScannerCore) khi có thể.

## Tiêu chí hoàn thành
- Báo cáo `tasks/reports/T038-report.md`: bảng lỗi tìm được (tệp:dòng, mức độ, đã sửa/chưa, vì sao), danh sách tệp sửa, mục "CHƯA BIÊN DỊCH", mục "việc cần kiểm trên iPhone thật", dòng cuối `TRẠNG THÁI: XONG` hoặc `CHƯA XONG (lý do)`. Cập nhật `TASKS.md`.
