# T002 - Khung giao diện WPF với dữ liệu giả (giao cho: antigravity, người kiểm tra: claude)

## Bối cảnh
Đọc trước: `AGENTS.md`, `PLAN.md`, `TASKS.md`, `tasks/README.md`, `docs/ui-inventory.md`.
Hướng mới là app Windows NATIVE: C# + .NET 8 + WPF, MVVM. TUYỆT ĐỐI không dùng WebView/HTML/Electron/React cho giao diện chính.
Solution: `v2/QuanLyHangHoan.sln`. Bạn CHỈ làm trong `v2/src/QuanLyHangHoan.App/` (và thêm test nếu cần ở `v2/tests/QuanLyHangHoan.Tests/UiTests/`).
Claude đang viết `Core` và `Data` song song: KHÔNG sửa hai project đó, KHÔNG sửa file ngoài phạm vi.

## Việc cần làm
Dựng khung giao diện chạy được với DỮ LIỆU GIẢ (chưa nối database):
1. Cửa sổ chính `MainWindow`: thanh bên (sidebar) điều hướng + vùng nội dung. Các mục lấy từ `docs/ui-inventory.md` mục 1: Bảng Hàng Hoàn Về, Kho lưu trữ (24h), Bảng đơn mới, Bảng đơn hủy, Thống kê, Tạo đơn nhanh, Ca quét, Danh mục SP, Đơn đã xóa, Cài đặt.
2. Màn hình **Bảng Hàng Hoàn Về** làm đầy đủ nhất: DataGrid với các cột theo mục 2 của inventory (ngày giao, ngày hoàn, sàn, mã đơn, mã vận đơn, trạng thái, ghi chú, ảnh...), lọc theo sàn (Cả 2 / Shopee / TikTok), ô tìm kiếm, sắp xếp, trạng thái trống.
3. Quy tắc màu cảnh báo: Vàng khi hàng về đã quá 12 giờ chưa xử lý, Đỏ quá 24 giờ (hằng số đặt ở MỘT chỗ, dễ đổi; chủ dự án có thể đổi quy tắc sau).
4. Các màn hình còn lại: tạo trang khung (tiêu đề, bảng/ô nội dung giữ chỗ, vài dòng dữ liệu giả), chưa cần logic.
5. Kiến trúc MVVM: dùng `CommunityToolkit.Mvvm` (cho phép thêm gói NuGet này). View không có logic nghiệp vụ trong code-behind. Dữ liệu giả đặt trong `App/Services/FakeDataService.cs` sau một interface `IReturnOrderProvider` để Claude sau này thay bằng bản thật.
6. Hiệu năng: bảng phải bật virtualization (dự kiến vài chục nghìn dòng). Nạp thử 50.000 dòng giả không được treo giao diện.
7. Giao diện tiếng Việt có dấu. Giao diện gọn, đọc được tên mã vận đơn, font dễ nhìn, hỗ trợ DPI cao.

## Quy tắc
- Dữ liệu giả phải là GIẢ hoàn toàn (không copy đơn thật, không đọc `Documents\QuanLyHangHoan` hay OneDrive).
- Không thêm gói NuGet nào ngoài `CommunityToolkit.Mvvm` mà không ghi lý do trong báo cáo. Nếu cần gói bảng dữ liệu khác, nêu đề xuất, đừng tự thêm.
- Không chạy các lệnh xóa/ghi ngoài thư mục `v2/src/QuanLyHangHoan.App/`.
- Giữ `dotnet build v2/QuanLyHangHoan.sln` không lỗi, không cảnh báo mới.

## Tiêu chí hoàn thành
- `dotnet build v2/QuanLyHangHoan.sln` thành công.
- `dotnet run --project v2/src/QuanLyHangHoan.App` mở được cửa sổ, chuyển được giữa các mục sidebar.
- Bảng Hàng Hoàn Về có lọc, tìm kiếm, sắp xếp, tô màu Vàng/Đỏ đúng ngưỡng.
- Có ảnh chụp màn hình (lưu ở `tasks/reports/T002-screens/`) nếu chụp được.
- Có ít nhất 3 test xUnit cho logic màu cảnh báo / lọc / tìm kiếm của ViewModel (không cần mở cửa sổ).

## Báo cáo (bắt buộc khi xong)
Ghi `tasks/reports/T002-report.md`: đã làm gì, danh sách file tạo/sửa, cách chạy, kết quả build/test (dán đầu ra rút gọn), điểm chưa làm hoặc chưa chắc, đề xuất gói NuGet nếu có. Dòng cuối `TRẠNG THÁI: XONG` hoặc `TRẠNG THÁI: CHƯA XONG (lý do)`.
Cập nhật `TASKS.md`: `- [x] T002 wpf-shell (antigravity) - chờ claude kiểm tra`.
