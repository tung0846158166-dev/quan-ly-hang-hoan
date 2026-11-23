# T003 - Hoàn thiện các màn hình WPF (giao cho: antigravity, người kiểm tra: claude)

## Bối cảnh
Đọc trước: `AGENTS.md`, `PLAN.md`, `TASKS.md`, `tasks/README.md`, `docs/ui-inventory.md`, `tasks/T002-wpf-shell.md`.
Chỉ làm trong `v2/src/QuanLyHangHoan.App/` và `v2/tests/QuanLyHangHoan.Tests/UiTests/`. KHÔNG sửa `Core`, `Data`. Claude đang viết Core/Data (schema SQLite, resolver mã vận đơn, importer) song song.
Vẫn dùng DỮ LIỆU GIẢ sau interface; không đọc dữ liệu thật.

## Bước 0 - Đóng T002 (bắt buộc làm trước)
Phiếu T002 còn thiếu `tasks/reports/T002-report.md` và chưa tick trong `TASKS.md`. Viết báo cáo theo mẫu T002 (có kết quả build/test thật, đã chạy `dotnet run` mở được app, thử 50.000 dòng không treo, ảnh chụp ở `tasks/reports/T002-screens/`). Nếu chưa tự chạy kiểm chứng được mục nào, ghi rõ "chưa kiểm chứng".

## Việc cần làm
1. Thay các trang placeholder bằng màn hình thật (dữ liệu giả, qua interface riêng cho từng màn, ví dụ `INewOrderProvider`, đặt cạnh `IReturnOrderProvider`):
   - **Bảng đơn mới**, **Bảng đơn hủy**, **Kho lưu trữ (24h)**, **Đơn đã xóa**: DataGrid có tìm kiếm, sắp xếp, trạng thái trống, virtualization. Cột theo `docs/ui-inventory.md`.
   - **Thống kê**: các ô số liệu (tổng đơn, theo sàn, theo trạng thái, số đơn Vàng/Đỏ) tính từ dữ liệu giả bằng logic trong ViewModel (có test).
2. **Cảnh báo**: Vàng 12h / Đỏ 24h dùng lại `AlertThresholds` (một chỗ duy nhất). Đơn hủy và Khiếu nại có quy tắc quá hạn khác (3 ngày / 72h): CHƯA quyết, để hằng số riêng `AlertThresholds` có ghi chú "chờ chủ dự án quyết", không tự suy diễn.
3. **Màn hình "Nhập dữ liệu cũ"** (chỉ giao diện): chọn thư mục nguồn, nút "Chạy thử (dry-run)", hiển thị báo cáo giả (số đơn chính/lưu trữ/hủy/đã xóa/ảnh, số cảnh báo), nút "Nhập thật" chỉ bật sau khi dry-run xong và người dùng tick xác nhận. Gọi qua interface `ILegacyImporter` (Claude sẽ cài bản thật). Bản giả trả số liệu giả và KHÔNG đọc ổ đĩa.
4. Điều hướng: sidebar thêm mục "Nhập dữ liệu cũ"; nhớ mục đang chọn khi chuyển trang.
5. Quy tắc cũ vẫn giữ: MVVM + CommunityToolkit.Mvvm, không logic nghiệp vụ trong code-behind, tiếng Việt có dấu, hỗ trợ DPI cao.

## Quy tắc
- Không thêm gói NuGet mới mà không ghi lý do trong báo cáo.
- Không ghi/xóa ngoài `v2/src/QuanLyHangHoan.App/` và thư mục UiTests.
- `dotnet build v2/QuanLyHangHoan.sln` không lỗi, không cảnh báo mới; `dotnet test` xanh toàn bộ.

## Tiêu chí hoàn thành
- Có `T002-report.md`; build + test xanh.
- 4 bảng + Thống kê + Nhập dữ liệu cũ chạy được với dữ liệu giả, chuyển trang mượt.
- Thêm ít nhất 6 test xUnit mới (lọc/tìm kiếm/thống kê/điều kiện bật nút "Nhập thật").

## Báo cáo (bắt buộc khi xong)
Ghi `tasks/reports/T003-report.md`: đã làm gì, file tạo/sửa, cách chạy, kết quả build/test (rút gọn), điểm chưa chắc. Dòng cuối `TRẠNG THÁI: XONG` hoặc `TRẠNG THÁI: CHƯA XONG (lý do)`.
Cập nhật `TASKS.md`: `- [x] T003 ui-screens (antigravity) - chờ claude kiểm tra`.
