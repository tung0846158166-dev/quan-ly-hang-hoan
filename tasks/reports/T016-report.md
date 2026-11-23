# Báo cáo T016 - Salework "Lấy ngay" bằng WebView2

## Tóm tắt thay đổi (chỉ trong `v2/src/QuanLyHangHoan.App/` và `v2/tests/QuanLyHangHoan.Tests/UiTests/`)
1. **Gói WebView2**: thêm `Microsoft.Web.WebView2` (1.0.4370.26) vào `QuanLyHangHoan.App.csproj`.
2. **Đường dẫn (`Services/AppPaths.cs`)**: thêm `SaleworkAutoStatePath` (`%LocalAppData%\QuanLyHangHoan\v2\salework_auto.json`) và `SaleworkWebViewFolder` (`%LocalAppData%\QuanLyHangHoan\v2\salework-webview`).
3. **Bộ lọc & mã chạy trong trang (`Services/SaleworkPageScript.cs`)**:
   - Port 1:1 `pageScript` từ `salework_auto.js` (tìm phần tử theo chữ, bỏ chữ biểu tượng ở đầu/cuối, từ chối mọi phần tử hoặc điểm bấm chứa `/c[aậ]p\s*nh[aậ]t/i`, kiểm tra điểm bấm không bị che `elementFromPoint`).
   - Kiểm tra hợp lệ URL mở (`https://`) và URL lưu (`https://`, từ chối `login|signin|dang-?nhap`).
4. **Controller (`Services/ISaleworkController.cs`, `Services/SaleworkBrowserWindow.cs`, `Services/RealSaleworkController.cs`)**:
   - `ISaleworkController` + `FakeSaleworkController` (`--demo`/test không tạo WebView2, không mở mạng).
   - `RealSaleworkController`: dùng hồ sơ WebView2 riêng (`SaleworkWebViewFolder`), không lưu mật khẩu/cookie ra ngoài, chỉ lưu URL trong `salework_auto.json`; port đầy đủ các bước từ `salework_auto.js` (mở trang -> kiểm tra "Xuất excel" / ô mật khẩu -> dự phòng menu "Kiểm tra, đối soát" > "Đơn hoàn" -> tab "Đang chờ hoàn" -> kiểm tra kênh TikTok -> rê chuột thật qua CDP `Input.dispatchMouseEvent` vào "Xuất excel" -> "Theo bộ lọc" -> "Bắt Đầu Xuất" -> `DownloadStarting` vào thư mục tạm riêng -> đọc bằng `SpreadsheetReader.Read` -> `SaleworkImportService.Apply` trên nền -> xóa thư mục tạm).
   - Chế độ xem (mặc định bật): hiện cửa sổ, biểu ngữ đỏ + viền đỏ quanh nút, chờ 2,5 giây; lỗi giữ cửa sổ 2 phút.
   - Không chạy theo lịch, không chạy lúc khởi động.
5. **Màn hình & Sidebar (`ViewModels/SaleworkViewModel.cs`, `Views/SaleworkView.xaml(.cs)`, `ViewModels/MainViewModel.cs`, `MainWindow.xaml`, `App.xaml.cs`)**:
   - Thêm mục **"Salework"** trong nhóm **"NẠP ĐƠN TỰ ĐỘNG"** (dưới **"Google Sheet"**).
   - Nút "Đăng nhập Salework", "Lưu trang này", "Quên đăng nhập" (có xác nhận), công tắc "Chế độ xem", nút "Lấy ngay" (chạy nền, khóa nút, "Đang lấy..."), nút "Dừng", trạng thái lượt gần nhất (xanh/đỏ, ghi bước lỗi + gợi ý đăng nhập lại), cảnh báo vàng khi `SupersedeAmbiguous > 0`, hướng dẫn 3 dòng, nhật ký 100 dòng.
   - Khi `Completed` thành công: tải lại `MainViewModel` và hiện thông báo `"Đã lấy N dòng từ Salework và nạp vào bảng"` kèm `"gộp X dòng chờ cũ"` / cảnh báo nhập nhằng.

## Kiểm tra tự động
- `dotnet build v2/QuanLyHangHoan.sln`: 0 lỗi, 0 cảnh báo.
- `dotnet test v2/QuanLyHangHoan.sln`: 345/345 xanh, chạy 10/10 lần liên tiếp không lỗi (`T016SaleworkUiTests` kiểm tra bộ lọc `"cập nhật"`, lưu trang hợp lệ/không hợp lệ, "Lấy ngay" chạy nền & tải lại màn hình, gộp/nhập nhằng, lỗi đăng nhập lại, "Dừng", "Quên đăng nhập" cần xác nhận, `--demo` không tạo WebView2).
- Ảnh chụp màn hình giả lập (`tasks/reports/T016-screens/`): `01-not-configured.png`, `02-saved-and-running.png`, `03-success-merged.png`, `04-error-relogin.png` (không dùng địa chỉ Salework thật).

## Giới hạn kiểm chứng
- **Chưa chạy với Salework thật** (không thể kiểm trên tài khoản và trang Salework thật).

TRẠNG THÁI: XONG
