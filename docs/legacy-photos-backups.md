# Khảo sát `photos/`, `sao_luu_tu_dong/` và `khoi_phuc/` (T005)

Nguồn phân tích: bản sao `C:\Users\kimth\Projects\_legacy-data-copy\2026-11-23-phien-moi` (16 tệp JSON) và `C:\Users\kimth\Projects\_legacy-data-copy\2026-11-23-anh-sao-luu` (`photos/`, `sao_luu_tu_dong/`, `khoi_phuc/`), sao chép từ `C:\Users\kimth\OneDrive\Documents\QuanLyHangHoan` ngày 2026-11-23 (đã kiểm tra khớp SHA256).

---

## 1. Thư mục `photos/`

- **Số tệp:** `1.865` tệp (0 lỗi đọc JSON).
- **Định dạng / đuôi:** 100% là `.json` (không phải tệp ảnh `.jpg`/`.png` trực tiếp). Mỗi tệp có cấu trúc JSON gồm đúng 2 khóa `{"id": "<photoId>", "data": "data:image/jpeg;base64,..."}`. Toàn bộ `1.865` tệp đều dùng MIME `image/jpeg;base64`.
- **Dung lượng tổng:** `67.895.441` bytes (`64,75 MB`). Kích thước mỗi tệp từ `10.136` bytes đến `149.936` bytes.
- **Quy ước đặt tên & khớp `photoId`:**
  - Tên tệp có dạng `<sha1_hex_40_ky_tu>.json`. Kiểm tra trên toàn bộ `1.865` tệp cho thấy `SHA1(obj.id) == tên_tệp_bỏ_đuôi` khớp **1.865 / 1.865 (100%)**.
  - `photoId` trong đơn hàng là chuỗi không có đuôi tệp, dạng `p_SPXVN<NUM>B` hoặc `p_<ID>` (khớp chính xác với trường `id` bên trong tệp `.json`).
- **Đối chiếu `photoId` từ `du_lieu_hang_hoan.json` (2.307 đơn) và `phu_shopee_archived_orders_v1.json` (2.195 đơn):**
  - **Đơn chính (`du_lieu_hang_hoan.json`):** `133` đơn có `photoId` -> **133 / 133** khớp tệp trong `photos/` (`0` thiếu tệp).
  - **Đơn lưu trữ (`phu_shopee_archived_orders_v1.json`, parse 2 lớp `{"v":...}`):** `2.058` đơn có `photoId` -> **1.615** khớp tệp trong `photos/` (trỏ tới `1.610` tệp duy nhất do có `5` `photoId` được dùng chung bởi nhiều hơn 1 đơn), **443** đơn có `photoId` nhưng **thiếu tệp** trong `photos/`.
  - **Đơn đã xóa (`phu_shopee_deleted_orders_v1.json`, 166 đơn):** `0` đơn có `photoId`, `0` ảnh base64.
  - **Tệp mồ côi (không có đơn nào ở chính + lưu trữ + đã xóa trỏ tới):** **`122` tệp** (`3.331.488` bytes = `3,18 MB`).

---

## 2. Ảnh nhúng base64 (data-URI) vs tệp trong `photos/`

- **Đơn chính (`2.307` đơn):**
  - `photo` chứa data-URI base64: `0` đơn.
  - Có cả `photoId` và `photo` base64: `0` đơn.
  - Ghi chú thêm: có `15` đơn có mảng `photoHistory[]` chứa tổng cộng `44` ảnh data-URI base64 (không có `photoId` trong `photoHistory`).
- **Đơn lưu trữ (`2.195` đơn):**
  - `photo` chứa data-URI base64: `1.032` đơn.
  - Có **cả hai** (`photoId` VÀ `photo` base64): **`1.032` đơn** (100% đơn có base64 đều có `photoId`).
  - Trong `1.032` đơn có cả hai:
    - **`911` đơn** tìm thấy tệp tương ứng trong `photos/`:
      - **`910` đơn** có nội dung byte sau khi giải mã base64 **trùng khớp 100%** (cùng SHA256).
      - **`1` đơn** có nội dung **khác byte**: bản nhúng trong JSON giải mã ra `12.931` bytes, còn bản trong `photos/*.json` giải mã ra `25.551` bytes (chênh dung lượng/độ nén hoặc ảnh chụp lại cùng `photoId`).
    - **`121` đơn** bị **thiếu tệp** trong `photos/` nhưng vẫn còn ảnh nhúng base64 trong JSON lưu trữ.
  - Ghi chú thêm: `322` đơn lưu trữ có `photoId` nhưng **mất cả hai** (không có tệp trong `photos/`, cũng không có base64 trong `photo`). Ngoài ra có `306` đơn lưu trữ có mảng `photoHistory[]` chứa tổng cộng `1.040` ảnh data-URI base64.

---

## 3. Thư mục `sao_luu_tu_dong/`

- **Tổng quan:** `94` tệp, tổng dung lượng `514.826.164` bytes (`490,98 MB`), chia vào `7` thư mục theo ngày từ `2026-11-17` đến `2026-11-23` (mỗi ngày `13–14` tệp).
- **Phân loại các tệp trong `94` tệp:**

| Loại tệp | Số tệp | Tổng (MB) | Min - Max (bytes) | Cấu trúc |
|---|---:|---:|---:|---|
| `du_lieu_hang_hoan.json` | 7 | 88,99 | 5.355.222 - 33.943.227 | Mảng JSON trực tiếp (cùng schema tệp chính) |
| `phu_shopee_archived_orders_v1.json` | 7 | 381,31 | 44.198.790 - 69.968.833 | Bọc `{"v": "..."}` (cùng schema tệp lưu trữ) |
| `phu_shopee_deleted_orders_v1.json` | 2 | 0,48 | 234.536 - 267.865 | Bọc `{"v": "..."}` |
| `phu_shopee_cancelled_orders_v1.json` | 7 | 0,27 | 39.902 - 39.902 | Bọc `{"v": "..."}` |
| `phu_shopee_new_orders_inbox_v1.json` | 7 | 2,39 | 10 - 1.299.193 | Bọc `{"v": "..."}` |
| `phu_shopee_excel_order_db_v1.json` | 1 | 15,93 | 16.700.248 | Bọc `{"v": "..."}` (Object `10.972` khóa, chỉ có ở `2026-11-17`) |
| `phu_deadline_notified_v1.json` | 7 | 0,28 | 5.124 - 63.555 | Bọc `{"v": "..."}` (Object) |
| `phu_ShopeeReturnCellEditHistory_v1.json` | 7 | 0,52 | 71.894 - 95.140 | Bọc `{"v": "..."}` (Object) |
| `phu_shopee_app_notifications_v1.json` | 7 | 0,55 | 404 - 196.438 | Bọc `{"v": "..."}` (Array) |
| `phu_shopee_excel_audit_log_v1.json` | 7 | 0,27 | 39.177 - 43.343 | Bọc `{"v": "..."}` (Array) |
| `phu_shopee_excel_accountant_stock_v1.json` | 7 | 0,00 | 10 | Bọc `{"v": "..."}` (Object) |
| `phu_shopee_excel_cell_locks_v1.json` | 7 | 0,00 | 10 | Bọc `{"v": "..."}` (Object) |
| `phu_shopee_excel_custom_week_tabs_v2.json` | 7 | 0,00 | 22 | Bọc `{"v": "..."}` (Array) |
| `phu_shopee_excel_active_import_week_v2.json` | 7 | 0,00 | 11 | Bọc `{"v": "..."}` (chuỗi thường) |
| `phu_shopee_preferred_platform_workspace.json` | 7 | 0,00 | 11 - 19 | Bọc `{"v": "..."}` (chuỗi thường) |

- **Đơn không còn ở dữ liệu hiện tại (`chính + lưu trữ + đã xóa`):**
  - Chỉ xét trong các bản sao lưu của `du_lieu_hang_hoan.json` và `phu_shopee_archived_orders_v1.json`, đối chiếu theo khóa `(sàn + mã đơn + mã vận đơn + mã yêu cầu hoàn)` (chuẩn hóa `"TikTok Shop"` -> `"TikTok"`): có **`1.342` khóa** không còn trong dữ liệu hiện tại (`1.281` khóa nếu bỏ `mã yêu cầu hoàn`).
  - Phân bố ngày xuất hiện cuối cùng của `1.342` đơn này: `2026-11-17`: 297, `2026-11-18`: 471, `2026-11-19`: 57, `2026-11-20`: 415, `2026-11-21`: 39, `2026-11-22`: 63. Đa số là đơn trạng thái `Chưa trả` / `Chưa về` (Shopee 705, TikTok 422 ở nhóm Trả hàng/Hoàn tiền).
  - (Nếu tính gộp cả `phu_shopee_cancelled_orders_v1.json` và `phu_shopee_new_orders_inbox_v1.json` thì tổng khóa khác biệt là `1.512`.)
- **Đề xuất:** **KHÔNG tự động nhập** từ `sao_luu_tu_dong/` vào v2 ở lần nhập mặc định. Lý do: phần lớn `1.342` đơn đó là đơn cũ ở bảng chính trước khi chủ shop dọn/lọc bảng hoặc thay đổi danh sách theo tuần; tự động gộp lại có thể làm sống lại đơn đã cố ý bỏ. Chỉ giữ `sao_luu_tu_dong/` làm kho tham khảo/khôi phục thủ công nếu chủ shop xác nhận thiếu đơn.

---

## 4. Thư mục `khoi_phuc/`

- **Số tệp:** `1` tệp duy nhất: `phu_shopee_archived_orders_v1.json`.
- **Loại & cấu trúc:** Tệp JSON bọc 2 lớp `{"v": "..."}`, bên trong là mảng `1.852` bản ghi đơn lưu trữ (bản chụp cũ hơn so với `2.195` đơn ở tệp hiện tại).
- **Kích thước:** `61.672.858` bytes (`58,82 MB` / `60.227,40 KB`).

---

## 5. Rủi ro & điều chủ dự án cần quyết

1. **Cách xử lý `322` đơn lưu trữ mất cả tệp `photos/` lẫn base64:** Hiện vẫn còn `photoId` nhưng không còn dữ liệu ảnh ở cả 2 nguồn. Importer cần ghi nhận `photoId` (đánh dấu thiếu ảnh) chứ không báo lỗi dừng nhập.
2. **Ưu tiên nguồn ảnh khi nhập (`910` trùng + `1` lệch byte + `121` chỉ còn base64):**
   - Đề xuất: ưu tiên tệp trong `photos/` (theo `SHA1(photoId).json`, vì ở ca lệch duy nhất thì tệp `photos/` lớn hơn: `25.551` vs `12.931` bytes); nếu thiếu tệp trong `photos/` thì lấy fallback từ data-URI base64 nhúng trong JSON (`121` đơn lưu trữ).
3. **`122` tệp ảnh mồ côi (`3,18 MB`):** Không có đơn hiện tại nào trỏ tới. Đề xuất giữ nguyên trong thư mục lưu trữ cũ hoặc chép sang thư mục riêng (không xóa), không gắn bừa vào đơn nào.
4. **Ảnh trong `photoHistory[]` (`44` ở chính, `1.040` ở lưu trữ):** Chủ dự án quyết xem v2 có cần tách và lưu lịch sử ảnh cũ không hay chỉ giữ ảnh chính hiện tại.
5. **`1.342` đơn chỉ có trong `sao_luu_tu_dong/` (`2026-11-17`..`2026-11-22`):** Chủ dự án xác nhận đó là đơn đã chủ động xóa/dọn khỏi bảng chính (nên bỏ qua khi nhập) hay có đơn nào bị mất ngoài ý muốn cần khôi phục.
6. **`5` `photoId` dùng chung bởi nhiều hơn 1 đơn:** Importer giải mã/lưu ảnh theo `photoId` (hoặc content-hash) để nhiều dòng đơn dùng chung một ảnh không bị ghi đè/lỗi khóa duy nhất.
