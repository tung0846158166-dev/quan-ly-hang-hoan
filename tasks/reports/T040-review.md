# T040 - Kiểm tra của Claude: ĐẠT (có điều kiện)

Phiếu: chỉ giữ ảnh khi quét xác nhận đúng một kiện; sai/không tìm thấy/mơ hồ/lỗi/trùng thì xóa ảnh ngay; chưa có kết quả thì giữ ảnh tạm.

**Đã kiểm tra thật**
- Phạm vi: chỉ `ios-app/` + báo cáo + `TASKS.md`; không đụng `v2/`.
- Biên dịch + test lõi (`ScanPhotoLifecycleTests`, `ScanItemV2AckTests`, `OrderPhotoStoreTests`) xanh trên GitHub Actions (bản dựng 22-28).
- Quy tắc nghiệp vụ giữ nguyên: ảnh gắn theo từng KIỆN (`OrderRow.id`), mã mơ hồ không tự gắn (quy tắc 1, 2, 3); không tự xóa lượt quét hay ảnh đã gắn (quy tắc 8).

**Chưa kiểm tra được (cần iPhone thật)**
- Chế độ máy bay -> quét -> xóa lượt quét -> ảnh tạm bị xóa; cảnh báo treo quá 7 ngày và nút "Bỏ ảnh tạm"; dọn ảnh tạm mồ côi khi mở app.
