# T039 - Kiểm tra của Claude: ĐẠT (có điều kiện)

Phiếu: bỏ truyền ảnh sang PC, lưu ảnh ngay trên điện thoại trong chi tiết đơn, nén ảnh thấp.

**Đã kiểm tra thật**
- Phạm vi: agy chỉ sửa `ios-app/` + báo cáo + `TASKS.md`; không đụng `v2/` (đối chiếu `git status` trước khi commit).
- Mã gốc biên dịch và test lõi chạy trên macOS qua GitHub Actions: các bản dựng 22-28 xanh (gồm `swift test` toàn bộ ScannerCore, trong đó `OrderPhotoStoreTests`, `ScanPhotoLifecycleTests`).
- Không còn mã gửi mảnh ảnh sang PC (theo báo cáo agy: 0 `photoChunks`, 0 `transmitPhoto`); ACK quét là điều kiện duy nhất để lượt quét xong (`isSettled = scanConfirmed`).

**Chưa kiểm tra được (cần iPhone thật)**
- Chất lượng ảnh khi zoom (nén ~60-120 KB), dung lượng thật, thao tác zoom/vuốt, giới hạn 5 ảnh mỗi kiện, nâng cấp đè lên bản có ảnh chưa gửi.

**Ghi chú:** mục "gắn tay ảnh cho mã mơ hồ" của phiếu này bị T040 thay thế (theo quyết định chủ dự án: sai thì bỏ ảnh).
