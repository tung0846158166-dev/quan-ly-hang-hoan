# T015 - Màn hình "Google Sheet" (giao cho: antigravity, người kiểm tra: claude)

Phạm vi: CHỈ `v2/src/QuanLyHangHoan.App/` và `v2/tests/QuanLyHangHoan.Tests/UiTests/`. KHÔNG sửa `Core/`, `Data/` và test ngoài `UiTests/`. Thiếu API ở Data thì ghi vào báo cáo.
Đọc trước: `AGENTS.md`, `tasks/T014-folder-watch-ui-and-stable-ui-tests.md` + `tasks/reports/T014-review.md` (mẫu màn hình tương tự: `IFolderWatchController`, `FolderWatchViewModel`, `WpfTestHost`), `v2/src/QuanLyHangHoan.Data/Sheet/SheetSyncService.cs`. Test dùng `WpfTestHost` (KHÔNG tự tạo `Application`); chạy `dotnet test` toàn bộ ít nhất 10 lần liên tiếp, 0 lần đỏ. Không dùng dữ liệu thật; không test nào chạm mạng hay `%LocalAppData%` thật (dùng bộ tải giả + thư mục tạm + `ISecretProtector` giả).

## API có sẵn (`QuanLyHangHoan.Data.Sheet`)
- `SheetSyncService(dbPath, statePath, ISecretProtector, Func<DateTime> clock, fetch = null)`; chế độ thật dùng `new DpapiProtector()` và `AppPaths.SheetSyncStatePath` (THÊM: `%LocalAppData%\QuanLyHangHoan\v2\sheet_sync.json`).
- `Config` (`Enabled`, `Tabs`, `MaxDays`, `SkipTypes` (null = mặc định "Huỷ trước chuẩn bị"), `OnlyReturns`, `LastAtTicks`, `LastOk`, `LastNote`); sửa xong gọi `SaveConfig()`.
- `SetUrl(url, out error)` (kiểm tra https + host Google, lưu MÃ HÓA), `HasUrl`, `Token` (mã ngẫu nhiên để chủ shop dán vào Apps Script), `static IsAllowedUrl`.
- `RunOnce(manual)` / `RunOnceAsync` -> `SheetRunResult` (`Ran`, `Ok`, `Unchanged`, `Note`, `Import` có `Added/Updated/Ambiguous`...). `manual=true` nạp lại toàn bộ; tự động chỉ nạp đơn mới/đổi.
- `Completed` (bắn từ luồng NỀN), `Start(firstDelay, interval)` (mặc định 30 giây rồi 10 phút/lần), `Stop`, `Dispose`.

## Việc cần làm
1. Interface `ISheetSyncController` ở `App/Services` bọc dịch vụ thật + bản giả cho `--demo`/test (bản giả KHÔNG chạm mạng/đĩa).
2. Khởi động (chế độ thật, sau `AppDatabase.Initialize`): tạo dịch vụ, nếu `Config.Enabled && HasUrl` thì `Start()`; `Dispose` khi thoát. Khi `Completed` có `Import` với `Added/Updated` > 0: marshal về UI thread, tải lại màn hình/badge/Trang chủ + thông báo "Đã cập nhật từ Google Sheet: thêm X, cập nhật Y". Lỗi chỉ hiện đỏ, không hộp thoại chặn.
3. Sidebar: thêm mục **"Google Sheet"** vào nhóm "NẠP ĐƠN TỰ ĐỘNG" (dưới "Theo dõi thư mục").
4. Màn hình "Google Sheet": ô dán địa chỉ Apps Script (ô mật khẩu/che bớt, KHÔNG hiện lại địa chỉ đã lưu: chỉ hiện "Đã lưu địa chỉ (đã mã hóa)" + nút "Đổi địa chỉ"), nút "Lưu" (báo lỗi `SetUrl` bằng chữ đỏ); hiển thị `Token` kèm nút "Sao chép" và hướng dẫn 3 dòng ngắn cách dán vào Apps Script; công tắc "Tự cập nhật mỗi 10 phút"; ô "Tên các tab (ngăn cách bằng ;)", "Chỉ lấy đơn trong N ngày gần nhất hoặc từ ngày (vd 30 hoặc 01/11/2026)", "Loại huỷ bỏ qua (ngăn cách bằng ;)" (để trống = mặc định); công tắc "Chỉ nạp đơn hoàn/hủy/thất bại" (`OnlyReturns`); trạng thái lượt gần nhất xanh/đỏ (`LastNote`, `LastAtTicks`); nút "Cập nhật ngay" (`RunOnce(true)`, chạy nền, khóa nút, "Đang cập nhật..."); nhật ký 100 dòng trong bộ nhớ; cảnh báo vàng khi `Import.Ambiguous.Count > 0`.
5. Bảo mật: địa chỉ/token KHÔNG xuất hiện trong log, nhật ký màn hình, ảnh chụp, báo cáo, tên tệp. Ảnh chụp dùng địa chỉ giả `https://script.google.com/macros/s/FAKE/exec`.

## Tiêu chí hoàn thành
- `dotnet build` 0 lỗi 0 cảnh báo; `dotnet test` xanh toàn bộ, 10/10 lần liên tiếp.
- Test mới trong `UiTests/`: lưu địa chỉ hợp lệ/không hợp lệ (http, host lạ -> báo đỏ, không lưu); địa chỉ đã lưu không hiện lại trên màn hình; cấu hình được lưu; "Cập nhật ngay" chạy nền và tải lại màn hình; `Completed` từ luồng nền marshal đúng; lỗi hiện đỏ; mục sidebar mới; `--demo` không chạm mạng/đĩa.
- Ảnh chụp (`tasks/reports/T015-screens/`): chưa có địa chỉ, đã lưu + thành công, lỗi, cảnh báo nhập nhằng.
- Báo cáo `tasks/reports/T015-report.md`, dòng cuối `TRẠNG THÁI: XONG` hoặc `CHƯA XONG (lý do)`. Cập nhật `TASKS.md`.
