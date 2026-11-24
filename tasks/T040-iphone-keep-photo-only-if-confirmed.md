# T040 - App iPhone: CHỈ GIỮ ẢNH KHI QUÉT ĐƯỢC XÁC NHẬN ĐÚNG; không đúng thì XOÁ ẢNH NGAY (giao cho: antigravity, người kiểm tra: claude)

PHẠM VI: CHỈ `ios-app/` + `tasks/reports/T040-report.md`. KHÔNG `git commit`/`git push`. Chạy SAU T039 và SỬA ĐÈ các mục 4 và 8 của T039 (ghi đè mọi chỗ trái với phiếu này).

## Quyết định của chủ dự án (đã chốt)
Quét xác nhận đúng đơn thì ảnh đi kèm được lưu vào chi tiết đơn đó. Quét không đúng thì BỎ ảnh đi cho khỏi tốn dung lượng.

## Quy tắc ảnh theo kết quả quét
1. **Đúng** = PC ACK quét thành công (đã lưu bền, quy tắc 6) và khớp đúng MỘT kiện: giữ ảnh, gắn vào kho ảnh của kiện đó (khoá theo mã dòng kiện, quy tắc 1 và 2).
2. **Không tìm thấy mã, mơ hồ (nhiều ứng viên, quy tắc 3), lỗi, bị từ chối, quét trùng kiện đã nhận trước đó**: XOÁ ảnh của lượt quét đó (tệp và mọi tham chiếu). Không còn "giữ ảnh ở Lịch sử chờ gắn tay"; bỏ giao diện gắn tay đó nếu T039 đã làm.
3. **Chưa có kết quả** (đang mất mạng/chờ PC ACK): giữ ảnh TẠM trong hàng đợi, KHÔNG xoá. Khi có kết quả thì áp dụng mục 1 hoặc 2. Ảnh tạm không phình vô hạn: lượt quét treo quá 7 ngày chưa có kết quả thì hiện cảnh báo cho người dùng quyết định (KHÔNG tự xoá lượt quét, quy tắc 8; chỉ xoá ẢNH tạm khi người dùng chọn bỏ).
4. Người dùng huỷ/xoá một lượt quét trong Lịch sử thì xoá luôn ảnh tạm của lượt đó.
5. Ảnh đã gắn vào đơn đúng thì chỉ người dùng xoá được (như T039), không tự xoá.
6. Dọn: khi mở app, quét thư mục ảnh tạm, xoá tệp không còn lượt quét nào tham chiếu VÀ không nằm trong chỉ mục kho (chắc chắn mới xoá, ghi nhật ký).

## Tiêu chí hoàn thành
- XCTest thuần: đúng giữ, không tìm thấy/mơ hồ/lỗi/trùng thì xoá, chờ thì giữ rồi xử lý khi có kết quả, huỷ lượt quét xoá ảnh tạm, ảnh tạm mồ côi bị dọn, ảnh đã gắn không bị dọn nhầm.
- Báo cáo `tasks/reports/T040-report.md` (danh sách tệp, CHƯA BIÊN DỊCH, việc cần kiểm trên iPhone thật, dòng cuối `TRẠNG THÁI: XONG` hoặc `CHƯA XONG (lý do)`). Cập nhật `TASKS.md`.
