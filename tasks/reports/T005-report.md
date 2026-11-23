# Báo cáo T005 - Khảo sát `photos/`, `sao_luu_tu_dong/` và `khoi_phuc/`

- **Người thực hiện:** antigravity
- **Ngày:** 2026-11-23
- **Tài liệu kết quả:** [docs/legacy-photos-backups.md](file:///C:/Users/kimth/Projects/quan-ly-hang-hoan/docs/legacy-photos-backups.md)
- **Cập nhật tiến độ:** [TASKS.md](file:///C:/Users/kimth/Projects/quan-ly-hang-hoan/TASKS.md) (đánh dấu `[x] T005`)

## 1. Sao chép & Kiểm tra toàn vẹn thư mục gốc

- Bản sao JSON sẵn có: `C:\Users\kimth\Projects\_legacy-data-copy\2026-11-23-phien-moi` (16 tệp JSON).
- Bản sao ảnh & sao lưu mới tạo: `C:\Users\kimth\Projects\_legacy-data-copy\2026-11-23-anh-sao-luu` (`photos/`, `sao_luu_tu_dong/`, `khoi_phuc/`).
- Không sửa/xóa/di chuyển bất kỳ tệp nào ở `C:\Users\kimth\OneDrive\Documents\QuanLyHangHoan` hay trong `v2/`.
- **Hash SHA256 tổng hợp (theo `relativePath|length|sha256` từng tệp) của thư mục gốc trước / sau khi làm:**

| Thư mục (`QuanLyHangHoan\`) | Số tệp | Hash TRƯỚC | Hash BẢN SAO | Hash SAU | Kết quả |
|---|---:|---|---|---|---|
| `photos` | 1.865 | `1E0A74CE4B7498C73216A2ABB362C8905D766C1F6A210AA0DB356FAE995402BD` | `1E0A74CE4B7498C73216A2ABB362C8905D766C1F6A210AA0DB356FAE995402BD` | `1E0A74CE4B7498C73216A2ABB362C8905D766C1F6A210AA0DB356FAE995402BD` | KHỚP 100% |
| `sao_luu_tu_dong` | 94 | `B2814D5C5B5EE761E620D6F6B8B079FFB0CB5FB1C4D0D30BF8AA2C26AD7EA938` | `B2814D5C5B5EE761E620D6F6B8B079FFB0CB5FB1C4D0D30BF8AA2C26AD7EA938` | `B2814D5C5B5EE761E620D6F6B8B079FFB0CB5FB1C4D0D30BF8AA2C26AD7EA938` | KHỚP 100% |
| `khoi_phuc` | 1 | `AFC76A01B8E311F0B651543F48F093E4DB1F14066CD7C6555E2FBB345F503276` | `AFC76A01B8E311F0B651543F48F093E4DB1F14066CD7C6555E2FBB345F503276` | `AFC76A01B8E311F0B651543F48F093E4DB1F14066CD7C6555E2FBB345F503276` | KHỚP 100% |

## 2. Tóm tắt kết quả chính

1. **`photos/`:** `1.865` tệp `.json` (`64,75 MB`), đặt tên theo `SHA1(photoId).json`, bên trong chứa `{"id": "<photoId>", "data": "data:image/jpeg;base64,..."}`. Khớp 133/133 đơn chính, 1.615/2.058 đơn lưu trữ (thiếu tệp 443 đơn); còn `122` tệp mồ côi (`3,18 MB`).
2. **Base64 nhúng vs `photos/`:** Có `1.032` đơn lưu trữ có cả `photoId` lẫn base64 (`910` khớp byte 100%, `1` lệch byte, `121` thiếu tệp trong `photos/` nhưng còn base64). Có `322` đơn lưu trữ mất cả tệp lẫn base64.
3. **`sao_luu_tu_dong/`:** `94` tệp (`490,98 MB`) trong 7 thư mục ngày (`2026-11-17`..`2026-11-23`), cùng schema tệp chính/phụ. Có `1.342` khóa đơn (4 thành phần) ở các bản sao lưu chính/lưu trữ không còn trong dữ liệu hiện tại — đề xuất không tự động nhập.
4. **`khoi_phuc/`:** `1` tệp `phu_shopee_archived_orders_v1.json` (`61.672.858` bytes = `58,82 MB`), cấu trúc `{"v": "..."}` bọc mảng `1.852` đơn lưu trữ cũ hơn.

TRẠNG THÁI: XONG
