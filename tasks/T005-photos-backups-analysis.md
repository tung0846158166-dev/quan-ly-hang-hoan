# T005 - Khảo sát `photos/` và `sao_luu_tu_dong/` (giao cho: antigravity, người kiểm tra: claude)

Việc NHẸ, CHỈ ĐỌC, không viết mã app. Chạy song song được với T003 (không đụng `v2/`).
Đọc trước: `AGENTS.md`, `docs/legacy-data-schema.md` (mục "Rủi ro" số 3).

## Phạm vi
- Chỉ đọc trên BẢN SAO: `C:\Users\kimth\Projects\_legacy-data-copy\2026-11-23-phien-moi`. Nếu bản sao chưa có `photos/` và `sao_luu_tu_dong/`, sao chép từ `C:\Users\kimth\OneDrive\Documents\QuanLyHangHoan` sang một thư mục MỚI cạnh đó (ví dụ `2026-11-23-anh-sao-luu`), so hash sau khi chép. TUYỆT ĐỐI không sửa/xóa/di chuyển gì ở thư mục gốc.
- Chỉ được tạo/sửa: `docs/legacy-photos-backups.md`, `tasks/reports/T005-report.md`, dòng T005 trong `TASKS.md`.
- Không đưa dữ liệu đơn thật (mã đơn, mã vận đơn, tên, SĐT, URL) vào báo cáo. Chỉ số liệu và dạng tên tệp (che phần định danh).

## Cần trả lời (ghi vào `docs/legacy-photos-backups.md`)
1. `photos/`: số tệp, định dạng/đuôi, dung lượng tổng, quy ước đặt tên (khớp `photoId` ra sao), tệp mồ côi (không có đơn trỏ tới) và `photoId` thiếu tệp. Đối chiếu bằng cách đọc `photoId` từ `du_lieu_hang_hoan.json` và `phu_shopee_archived_orders_v1.json` (nhớ parse 2 lớp `{"v":...}` cho tệp phụ).
2. Ảnh nhúng base64 (data-URI) vs tệp trong `photos/`: có bản ghi nào có cả hai không; hai nguồn có cùng nội dung không (so hash byte sau khi giải mã). Chỉ đếm, không xóa nguồn nào.
3. `sao_luu_tu_dong/`: 94 tệp gồm những loại gì, khoảng thời gian, kích thước, cấu trúc mỗi bản (cùng schema với tệp chính?), có chứa đơn không còn ở dữ liệu hiện tại không (đếm theo khóa sàn+mã đơn+mã vận đơn+mã yêu cầu hoàn). Đề xuất: có nên nhập gì từ đây không.
4. `khoi_phuc/` (1 tệp): chỉ mô tả loại và kích thước.
5. Danh sách rủi ro/điều chủ dự án cần quyết.

## Tiêu chí hoàn thành
- Hash thư mục gốc trước/sau khi làm không đổi (dán vào báo cáo).
- Báo cáo `tasks/reports/T005-report.md`, dòng cuối `TRẠNG THÁI: XONG` hoặc `CHƯA XONG (lý do)`. Đánh dấu `[x] T005` trong `TASKS.md`.
