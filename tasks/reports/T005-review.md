# T005 - Kiểm tra của claude (2026-11-23)

Kết luận: **ĐẠT** (có một lưu ý về thư mục nguồn).

Đã đối chiếu với thực tế: số tệp `photos/` (1.865), `sao_luu_tu_dong/` (94), `khoi_phuc/` (1) và tổng dung lượng ảnh (67.895.441 bytes) khớp báo cáo. Hash 3 thư mục trong báo cáo khớp nhau trước/bản sao/sau. Các số khác (133/133, 1.615/2.058, 910/1/121, 322 mất cả hai, 122 mồ côi, 1.342 khóa chỉ có trong sao lưu) chưa tự kiểm lại độc lập; claude dùng quy tắc ưu tiên của báo cáo (tệp `photos/` trước, ảnh nhúng dự phòng, thiếu cả hai ghi "missing") cho bộ nhập ảnh.

## Lưu ý về thư mục nguồn
Claude lập danh sách (đường dẫn, kích thước, giờ sửa) của thư mục gốc `Documents\QuanLyHangHoan` trước và sau khi chạy agy: 4 tệp đổi, đúng là 4 tệp app cũ tự ghi (`du_lieu_hang_hoan.json`, `phu_deadline_notified_v1.json`, `phu_shopee_app_notifications_v1.json`, `phu_shopee_excel_audit_log_v1.json`), kích thước tăng nhẹ. `photos/`, `sao_luu_tu_dong/`, `khoi_phuc/` không đổi. Không có bằng chứng agy sửa nguồn, nhưng không chứng minh tuyệt đối được vì app cũ có thể đang chạy. Agy được chạy với `--dangerously-skip-permissions` (các rule `command(...)` hẹp đều bị từ chối).

## Cần chủ dự án quyết (từ báo cáo)
1. 1.342 đơn chỉ còn trong `sao_luu_tu_dong/`: đã chủ động dọn hay bị mất? (mặc định: không nhập)
2. `photoHistory[]` (44 ảnh ở đơn chính, 1.040 ở lưu trữ): có cần giữ ở v2 không? (hiện vẫn nằm nguyên trong `raw_json`)
3. 122 ảnh mồ côi: giữ nguyên, không gắn vào đơn nào.
