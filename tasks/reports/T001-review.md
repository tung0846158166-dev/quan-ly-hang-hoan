# Review T001 (claude) - KẾT LUẬN: ĐẠT

Đã kiểm tra ngày 2026-11-23.

## Đã đối chiếu với file gốc
- 7/7 dẫn chiếu số dòng lấy mẫu đúng (L17052, L8666, L10472, L14719, L13968, L2105, L7941).
- 3 hàm báo "gọi nhưng không có định nghĩa" (`openPhotoZoomDirect`, `updatePlatformFilterUI`, `exportJsonFile`) - xác nhận ĐÚNG, không có định nghĩa trong file. Đây là phát hiện có giá trị (lỗi tiềm ẩn của app cũ, không cần port).
- `handleCellKeyDown` và `handleCellEditKeyDown` đều tồn tại; `duplicateOrder` tồn tại - khớp báo cáo.
- Đủ 9 mục theo phiếu; 490 dòng, có mục "Chưa chắc chắn" với nhãn (suy đoán).
- Phạm vi: không sửa file code gốc nào. Chỉ tạo `docs/ui-inventory.md`, `tasks/reports/T001-report.md` và cập nhật `TASKS.md`. Không đụng dữ liệu thật.

## Lưu ý / việc tiếp theo (không làm T001 bị rớt)
1. Chưa kiểm kê `mobile_scanner.html` (đã nêu rõ ngoài phạm vi). Cần phiếu riêng (T00x) khi làm PWA.
2. Báo cáo ghi "663 hàm", mình đếm ~667 theo regex khác - chênh lệch không đáng kể.
3. Mục "bất nhất" quan trọng cho nghiệp vụ, cần chủ dự án QUYẾT trước khi port: (a) `myNote` vs `userNote`; (b) 3 quy tắc quá hạn khác nhau (Bảng chính 12h/24h, Đơn Hủy 3 ngày, Khiếu nại 72h). Không tự chọn khi viết lõi.
4. T001 viết trước khi đổi sang WPF; nội dung vẫn dùng được làm đặc tả. Giao diện v2 là WPF/MVVM, KHÔNG dựng React/HTML.
