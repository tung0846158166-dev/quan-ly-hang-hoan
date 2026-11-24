# T039 - App iPhone: BỎ truyền ảnh sang PC, lưu ảnh NGAY TRÊN ĐIỆN THOẠI trong chi tiết đơn (giao cho: antigravity, người kiểm tra: claude)

PHẠM VI: CHỈ `ios-app/` + `tasks/reports/T039-report.md`. KHÔNG `git commit`/`git push`. Giới hạn như T036-T038 (không có Swift/Xcode, iOS 16 tối thiểu, tự đọc lại từng tệp, mọi thay đổi ScannerCore kèm XCTest thuần).

## Quyết định của chủ dự án (đã chốt, không bàn lại)
Máy tính KHÔNG cần xem ảnh nữa. Ảnh chụp lúc quét của đơn nào sẽ lưu thẳng trong CHI TIẾT ĐƠN đó trên điện thoại. Không gửi ảnh đi đâu (tiết kiệm băng thông broker MQTT, bỏ độ trễ). Nén ảnh xuống mức thấp, miễn sao zoom lên vẫn đọc được chữ số trên đơn (mã vận đơn, mã đơn), để tiết kiệm dung lượng máy.

## Việc cần làm
1. **Bỏ hẳn luồng gửi ảnh sang PC** trong `SyncEngine.swift`: không còn gửi mảnh ảnh (`Shopee Photo Chunk`/`photoChunks`) qua MQTT hay ntfy, không còn ngân sách gửi ảnh trong `tick()`, không còn gửi lại ảnh (`photoSentAt`, `photoAttempts`) khi nối lại, không chờ ACK ảnh. Giữ nguyên gửi LỆNH QUÉT và chờ ACK quét (quy tắc 6 áp dụng cho quét: chỉ coi đã quét khi PC ACK).
2. **Lượt quét chỉ cần `scanConfirmed` là xong** (`ScanItem.isSettled`): không còn trạng thái "ảnh đang gửi ngầm", "chờ ảnh", huy hiệu ảnh chờ ở Lịch sử/Cài đặt/Trang chủ. Sửa mọi chuỗi chữ liên quan. Giữ các trường cũ trong `ScanItem` Codable để đọc được dữ liệu đã lưu (không làm hỏng hàng đợi hiện có); chỉ không dùng nữa.
3. **Kho ảnh cục bộ** (lớp mới thuần trong ScannerCore nếu được, phần tệp ở App): thư mục `Documents/order-photos/`, mỗi ảnh một tệp JPEG + một tệp chỉ mục JSON ghi nguyên tử (ghi tạm rồi đổi tên). Khoá theo **mã dòng kiện** của đơn trong snapshot (`OrderRow.id`), dự phòng theo mã vận đơn chuẩn hoá. KHÔNG khoá theo mã đơn gốc (quy tắc 1, 2: một mã đơn có nhiều kiện A/B/C, ảnh kiện A không hiện ở kiện B/C).
4. **Gắn ảnh vào đơn (ĐÃ SỬA THEO T040, T040 ghi đè)**: chỉ khi lượt quét được PC ACK thành công và khớp đúng MỘT kiện thì giữ ảnh và gắn vào kiện đó; mã không tìm thấy, mơ hồ, lỗi hoặc trùng thì XOÁ ảnh ngay (không giữ chờ gắn tay). Chưa có kết quả (mất mạng) thì giữ ảnh tạm trong hàng đợi cho đến khi có kết quả.
5. **Chi tiết đơn** (`OrderDetailView.swift`): mục "Ảnh" hiển thị ảnh nhỏ; chạm mở xem toàn màn hình có **phóng to bằng hai ngón và chạm đúp**, kéo để di chuyển, vuốt xuống đóng. Có nút xoá một ảnh (có hỏi xác nhận). Cho chụp/chọn thêm ảnh thủ công cho kiện (tối đa 5 ảnh mỗi kiện; hết chỗ thì báo rõ, không tự xoá ảnh cũ).
6. **Nén ảnh**: thay `snapshotJPEG(maxDim: 560, quality: 0.55)` (vốn cho đường truyền) bằng cấu hình mới cho LƯU TRỮ: mục tiêu ~60-120 KB mỗi ảnh nhưng chữ số mã vận đơn vẫn đọc được khi zoom (gợi ý: cạnh dài khoảng 1280 px, JPEG ~0,45-0,5; thêm vòng giảm chất lượng nếu vượt ~150 KB, KHÔNG giảm cạnh dưới 960 px). Tự kiểm bằng cách mô tả phép tính, ghi vào báo cáo; không có thiết bị thật nên đánh dấu "cần kiểm trên iPhone thật: đọc được chữ khi zoom".
7. **Dung lượng**: Cài đặt hiển thị "Ảnh đã lưu: N ảnh, X MB". Có nút "Xoá ảnh của các đơn đã hoàn tất cũ hơn N ngày" do NGƯỜI DÙNG bấm (hỏi xác nhận, hiện trước số ảnh/MB sẽ xoá). KHÔNG tự xoá ảnh (quy tắc 8). Dọn tệp mồ côi (ảnh không có trong chỉ mục) chỉ khi chắc chắn và ghi nhật ký.
8. **Ảnh đã chụp nhưng chưa gửi (hàng đợi hiện có)**: giữ làm ảnh tạm, KHÔNG xoá tệp nào ngay; khi lượt quét có kết quả thì áp dụng quy tắc ở mục 4 (đúng thì gắn vào kho, không đúng thì xoá). Sau đó ngừng gửi.
9. **Giữ tương thích PC**: không sửa phía PC; PC v2 vẫn nhận được ảnh của bản điện thoại cũ nhưng bản mới không gửi nữa. Cập nhật các mô tả/chuỗi chữ nhắc "gửi ảnh sang PC".
10. Snapshot mới/đồng bộ lại KHÔNG được làm mất liên kết ảnh-đơn (khoá theo `OrderRow.id` ổn định; nếu PC đổi id thì dùng mã vận đơn chuẩn hoá làm dự phòng).

Quy tắc nghiệp vụ bất biến trong AGENTS.md (đặc biệt 1, 2, 3, 6, 8) KHÔNG được phá.

## Tiêu chí hoàn thành
- XCTest cho: khoá ảnh theo kiện (A không lẫn B/C), mã mơ hồ không tự gắn, chỉ mục ghi/đọc nguyên tử và chịu được tệp hỏng, tính tổng dung lượng, đọc ScanItem cũ vẫn được, `isSettled` chỉ cần `scanConfirmed`.
- Báo cáo `tasks/reports/T039-report.md`: danh sách tệp sửa/thêm/xoá, mục "CHƯA BIÊN DỊCH", mục "việc cần kiểm trên iPhone thật" (chất lượng ảnh khi zoom, dung lượng thật, thao tác zoom), dòng cuối `TRẠNG THÁI: XONG` hoặc `CHƯA XONG (lý do)`. Cập nhật `TASKS.md`.
