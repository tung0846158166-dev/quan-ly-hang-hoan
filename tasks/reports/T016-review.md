# T016 - Kiểm tra của claude (2026-11-23)

Kết luận: **ĐẠT (phần có thể kiểm tự động)**. Phần chạy thật với Salework CHƯA kiểm được.

Đã tự kiểm tra:
- `dotnet build`: 0 lỗi, 0 cảnh báo. `dotnet test`: 345/345 xanh; claude tự chạy 10 lần liên tiếp: 0 lần đỏ.
- Phạm vi: Core/Data chỉ có tệp của claude đổi (SaleworkSupersede, SaleworkImportService, SheetPipeline, SheetSyncService, Data.csproj); agy chỉ sửa `App/` + `UiTests/`.
- An toàn: bộ lọc chữ "cập nhật" có ở cả mã .NET (`SaleworkPageScript.ForbiddenTextRegex`) lẫn mã chạy trong trang (từ chối ở bước tìm và bước chọn phần tử), đúng bản gốc; hồ sơ WebView2 riêng `AppPaths.SaleworkWebViewFolder` (`...\v2\salework-webview`), không dùng hồ sơ trình duyệt của người dùng; không có địa chỉ Salework thật trong báo cáo; `--demo` dùng bản giả không tạo WebView2.

## Chưa kiểm chứng
1. Chưa chạy với Salework thật: rê chuột mở menu, tên nút, tải file, đăng nhập lại đều chưa được thử.
2. Chưa xem trực tiếp ảnh chụp.
3. Gói WebView2 cần có "WebView2 Runtime" trên máy người dùng (Windows 11 thường có sẵn).
