# T016 - Salework "Lấy ngay" bằng WebView2 (giao cho: antigravity, người kiểm tra: claude)

Phạm vi: CHỈ `v2/src/QuanLyHangHoan.App/` (+ `.csproj` của App để thêm gói `Microsoft.Web.WebView2`) và `v2/tests/QuanLyHangHoan.Tests/UiTests/`. KHÔNG sửa `Core/`, `Data/` và test ngoài `UiTests/`.
Đọc trước: `AGENTS.md`, `salework_auto.js` (BẢN GỐC của app cũ, Electron: lặp lại ĐÚNG thao tác này bằng WebView2), `tasks/T015-google-sheet-ui.md` + `tasks/reports/T015-review.md` (mẫu màn hình/controller/`WpfTestHost`), `v2/src/QuanLyHangHoan.Data/SaleworkImportService.cs`.
Test dùng `WpfTestHost`; chạy `dotnet test` toàn bộ 10 lần liên tiếp, 0 lần đỏ. KHÔNG test nào mở trang Salework thật hay mạng; WebView2 chỉ được tạo trong chế độ thật, test dùng bộ điều khiển giả.

## Quy tắc an toàn (BẤT BIẾN, lấy từ app cũ)
- Chỉ lặp thao tác người dùng vẫn làm tay: mở trang Đơn hoàn đã lưu -> tab "Đang chờ hoàn" -> rê chuột "Xuất excel" -> "Theo bộ lọc" -> "Bắt Đầu Xuất" -> file tự tải về. KHÔNG đụng TikTok, TUYỆT ĐỐI không bấm phần tử có chữ "cập nhật" (`/c[aậ]p\s*nh[aậ]t/i`: bắt Salework đồng bộ lại TikTok); mọi bước tìm/bấm đều từ chối khi khớp chữ đó.
- Dùng hồ sơ WebView2 RIÊNG của app (thư mục dữ liệu riêng, ví dụ `%LocalAppData%\QuanLyHangHoan\v2\salework-webview`), KHÔNG dùng hồ sơ trình duyệt của người dùng. Không lưu mật khẩu/token/cookie ở đâu khác; chỉ lưu ĐỊA CHỈ trang Đơn hoàn (https) trong `salework_auto.json` cạnh các tệp trạng thái (địa chỉ này không bị ghi vào log/báo cáo/ảnh chụp: ảnh dùng địa chỉ giả).
- Chế độ xem (mặc định bật): hiện cửa sổ, viền đỏ quanh nút sắp bấm + biểu ngữ "App đang: ..." + chờ ~2,5 giây trước mỗi bước; đóng cửa sổ hoặc bấm "Dừng" là dừng NGAY. Khi lỗi ở chế độ xem: giữ cửa sổ 2 phút kèm biểu ngữ bước bị dừng.
- Tải về vào thư mục TẠM riêng, đọc xong xóa; không ghi file vào nơi khác. Chờ tối đa 120 giây.

## API Data có sẵn
`SaleworkImportService.Apply(dbPath, rows, new TikTokMapOptions{...}, ImportProductMode.Supplement, DateTime.Now)` -> `SaleworkImportResult` (`Ok`, `Message`, `Import` (Added/Updated/...), `SupersededMerged`, `SupersedeAmbiguous`, `FilledTracks`). Đọc file tải về bằng `QuanLyHangHoan.Data.Import.SpreadsheetReader.Read(fileName, bytes)` (đã gồm bỏ cột cá nhân khi nạp). Mọi lỗi là `Ok=false`, không đổi dữ liệu.

## Việc cần làm
1. `ISaleworkController` ở `App/Services` + bản thật (WebView2) + bản giả (`--demo`/test).
2. Bản thật: port từ `salework_auto.js` các bước: mở trang -> chờ "Xuất excel" hoặc ô mật khẩu (có ô mật khẩu mà không có "Xuất excel" = "Salework yêu cầu đăng nhập lại", gợi ý bấm "Đăng nhập Salework") -> (nếu trang trống: đi menu "Kiểm tra, đối soát" > "Đơn hoàn") -> chọn tab "Đang chờ hoàn" (khớp đầu chữ) -> kiểm tra có kênh TikTok -> rê chuột thật vào "Xuất excel" (gửi sự kiện chuột thật; dự phòng sự kiện trong trang) -> "Theo bộ lọc" trong hộp thoại -> "Bắt Đầu Xuất" -> chờ file tải về (`DownloadStarting` của WebView2). Mã chạy trong trang giữ nguyên logic `pageScript` (tìm phần tử theo chữ, bỏ chữ biểu tượng, từ chối chữ "cập nhật", kiểm tra điểm bấm không bị che).
3. Màn hình "Salework" trong nhóm sidebar "NẠP ĐƠN TỰ ĐỘNG" (dưới "Google Sheet"): nút "Đăng nhập Salework" (mở cửa sổ HIỆN, người dùng tự đăng nhập, mở trang Đơn hoàn kênh TikTok), nút "Lưu trang này" (từ chối khi địa chỉ có `login|signin|dang-nhap` hoặc không phải https), công tắc "Chế độ xem", nút "Lấy ngay" (chạy nền, "Đang lấy..."), nút "Dừng", nút "Quên đăng nhập" (xóa địa chỉ + dữ liệu hồ sơ WebView2 riêng, có xác nhận), trạng thái lượt gần nhất (xanh/đỏ, ghi bước lỗi), hướng dẫn 3 dòng, nhật ký 100 dòng.
4. Sau khi lấy file: `SaleworkImportService.Apply` (chạy nền), rồi tải lại màn hình/badge/Trang chủ + thông báo "Đã lấy N dòng từ Salework và nạp vào bảng" kèm "gộp X dòng chờ cũ" nếu `SupersededMerged > 0` và cảnh báo vàng khi `SupersedeAmbiguous > 0` ("N đơn có nhiều kiện khác nhau, giữ nguyên để bạn xem").
5. KHÔNG tự chạy theo lịch (app cũ chỉ chạy tay bằng "Lấy ngay"); không chạy lúc khởi động.

## Tiêu chí hoàn thành
- `dotnet build` 0 lỗi 0 cảnh báo (nếu gói WebView2 không khôi phục được: DỪNG và ghi rõ vào báo cáo, đừng tự bịa); `dotnet test` xanh 10/10.
- Test mới trong `UiTests/` (bản giả): lưu trang hợp lệ/không hợp lệ; "Lấy ngay" chạy nền, khóa nút, tải lại màn hình; kết quả nạp (gộp/nhập nhằng) hiện đúng; lỗi đăng nhập lại hiện gợi ý; "Dừng" hoạt động; "Quên đăng nhập" cần xác nhận; `--demo` không tạo WebView2; test trên một hàm riêng kiểm tra bộ lọc từ chối chữ "cập nhật" (đưa logic tìm phần tử thành chuỗi JS/hàm có thể kiểm tra mà không cần trình duyệt, hoặc kiểm bằng danh sách ca chuỗi).
- Ảnh chụp (`tasks/reports/T016-screens/`): chưa cài đặt, đã lưu + đang lấy, thành công có gộp, lỗi cần đăng nhập lại.
- Báo cáo `tasks/reports/T016-report.md` PHẢI ghi rõ: "Chưa chạy với Salework thật" (không thể kiểm); dòng cuối `TRẠNG THÁI: XONG` hoặc `CHƯA XONG (lý do)`. Cập nhật `TASKS.md`.
