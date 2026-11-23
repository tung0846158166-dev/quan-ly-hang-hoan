> **ĐÃ BỊ THAY THẾ bởi `tasks/T006-ui-platform-split.md`** (2026-11-23): chủ dự án muốn Shopee có bảng "Đơn hủy" riêng (dạng bộ lọc trên bảng đơn chung) nên mục 1 dưới đây không còn đúng. Mục 2-4 vẫn áp dụng và đã được gộp vào T006. Đừng chạy T004 riêng.

# T004 - Chỉnh giao diện theo quyết định mới của chủ dự án (giao cho: antigravity, người kiểm tra: claude)

CHỈ chạy sau khi T003 đã xong và đã có `tasks/reports/T003-report.md`. Phạm vi như cũ: chỉ `v2/src/QuanLyHangHoan.App/` và `v2/tests/QuanLyHangHoan.Tests/UiTests/`. Không sửa Core, Data, ImporterTests.
Đọc trước: `AGENTS.md`, `TASKS.md`, `docs/legacy-data-schema.md` (mục "Quyết định của chủ dự án").

## Quyết định (2026-11-23)
1. **BỎ HẲN Bảng đơn hủy.** Chủ dự án không dùng nữa. Xóa mục sidebar "Bảng đơn hủy", `CancelledOrdersViewModel`, view, dữ liệu giả, dòng trong Thống kê/Nhập dữ liệu cũ liên quan tới đơn hủy. Lưu ý: đơn chính có loại "Đơn hủy" (cột orderType) VẪN còn, chỉ bỏ riêng cái bảng/modal đơn hủy. Bỏ luôn hằng số quá hạn 3 ngày/72h chờ-quyết trong `AlertThresholds` nếu chỉ phục vụ bảng đơn hủy.
2. **Nhãn sàn ngắn là "TikTok"** (không còn "TikTok Shop"). Sửa mọi chỗ: dữ liệu giả, bộ lọc sàn ("Cả 2 sàn / Shopee / TikTok"), converter màu, so sánh chuỗi. Gom thành MỘT hằng số (ví dụ `PlatformNames.TikTok`), không rải chuỗi.
3. **Kho lưu trữ = cùng một đơn ở trạng thái "đã dời"** (không phải loại đơn khác). Nếu ViewModel đang coi là hai loại riêng, ghi chú trong báo cáo để Claude nối với một bảng đơn có cờ lưu trữ sau này. Chưa cần đổi kiến trúc ngay.
4. Màn hình "Nhập dữ liệu cũ": báo cáo giả không còn dòng "đơn hủy", thêm dòng "Bỏ theo quyết định" (ví dụ tệp đơn hủy, tệp khôi phục) và dòng "Sàn TikTok Shop -> TikTok: N bản ghi". Số liệu thật mẫu để canh giao diện (CHỈ là số, đừng dùng dữ liệu thật): đọc 4.668 = chính 2.307 + lưu trữ 2.195 + đã xóa 166; ảnh nhúng 1.032; trùng khóa 3 nhóm.

## Tiêu chí hoàn thành
- `dotnet build` 0 lỗi 0 cảnh báo; `dotnet test` xanh TOÀN BỘ (kể cả sửa test sidebar cho khớp số mục mới).
- Không còn chuỗi "Đơn hủy" như một trang/mục điều hướng, không còn "TikTok Shop" trong App (tìm bằng grep, dán kết quả rỗng vào báo cáo).
- Báo cáo `tasks/reports/T004-report.md`, dòng cuối `TRẠNG THÁI: XONG` hoặc `CHƯA XONG (lý do)`. Cập nhật `TASKS.md`.
