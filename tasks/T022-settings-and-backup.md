# T022 - Màn "Cài đặt" và sao lưu tự động (giao cho: antigravity, người kiểm tra: claude)

Phạm vi: CHỈ `v2/src/QuanLyHangHoan.App/` và `v2/tests/QuanLyHangHoan.Tests/UiTests/`. KHÔNG sửa `Core/`, `Data/` và test ngoài `UiTests/`. Thiếu API ở Data thì ghi vào báo cáo.
Chạy SAU T021 (T021 đặt các công tắc chạy nền tạm ở nơi khác; phiếu này gom chúng về màn Cài đặt). Đọc trước: `AGENTS.md`, `tasks/reports/T021-review.md` (nếu có) hoặc `T021-report.md`, `v2/src/QuanLyHangHoan.Data/AppSettings.cs`, `v2/src/QuanLyHangHoan.Data/BackupService.cs`. Test dùng `WpfTestHost`, DB tạm + thư mục tạm; không chạm `%LocalAppData%` thật; `dotnet test` toàn bộ 10 lần liên tiếp 0 lần đỏ.

## API có sẵn (claude đã viết, có test)
- `BackupService.DefaultDir(dbPath)` (thư mục `backups` cạnh DB), `Backup(dbPath, dir, now)` -> `BackupResult(Ok, Path, Bytes, Message)`, `List(dir)` -> `BackupEntry(Path, Name, Time, Bytes)` mới nhất trước, `Prune(dir, keepDays, now)` (giữ tối thiểu 3 bản, chỉ xóa tệp đúng tên `data-yyyyMMdd-HHmmss.db`), `RunIfDue(dbPath, dir, everyHours, keepDays, now)` -> `BackupResult?` (null = chưa đến hạn). An toàn khi app đang ghi.
- `AppSettings`: `Theme`, `AutoStart`, `MinimizeToTray`, `NotifyAlerts`, `Sounds`, `NtfyEnabled`, `BackupEnabled`, `BackupEveryHours` (1-24), `BackupKeepDays` (1-90); `Save(path)` ghi nguyên tử.

## Việc cần làm
1. Mục sidebar mới **"Cài đặt"** (nhóm CHUNG, cuối danh sách) với các khối:
   - **Giao diện**: sáng/tối (dùng lại công tắc theme hiện có, vẫn giữ nút ở chỗ cũ).
   - **Chạy nền và thông báo**: 4 công tắc của T021 (Tự mở cùng Windows, Thu nhỏ xuống khay khi bấm X, Báo vàng/đỏ, Âm thanh) - chuyển/ghép từ chỗ T021 đặt tạm, KHÔNG nhân đôi logic (dùng cùng dịch vụ). 
   - **Đồng bộ điện thoại**: hiển thị mã phòng (nút sao chép) và công tắc ntfy (liên kết sang màn Đồng bộ điện thoại, không làm lại).
   - **Sao lưu**: công tắc "Tự sao lưu" + "mỗi N giờ" (1-24) + "giữ N ngày" (1-90); dòng "Bản gần nhất: <giờ> (<dung lượng>)" hoặc "Chưa có"; nút **"Sao lưu ngay"** (chạy nền, khóa nút, hiện kết quả/lỗi); bảng danh sách bản sao lưu (tên, giờ, dung lượng) mới nhất trước; nút "Mở thư mục sao lưu" (mở Explorer; bọc interface để test); hướng dẫn khôi phục một câu: "Muốn khôi phục: đóng app, chép một bản sao lưu đè lên data.db (nên giữ lại bản hiện tại trước)". KHÔNG làm nút khôi phục tự động (rủi ro ghi đè dữ liệu).
2. **Tự sao lưu chạy nền**: dịch vụ gọi `BackupService.RunIfDue` ~1 phút sau khi app mở rồi mỗi 10 phút (chỉ khi `BackupEnabled`); lỗi thì ghi vào dòng trạng thái màn Cài đặt (không bật hộp thoại chặn). Dừng sạch khi thoát app.
3. Mọi công tắc lưu ngay vào `settings.json` bằng `AppSettings.Save` (không mất các trường khác như mã phòng, vị trí cửa sổ; đọc lại bản mới nhất trước khi ghi để không ghi đè thay đổi của màn khác).
4. `--demo`: màn Cài đặt hiển thị bản giả (danh sách mẫu), nút "Sao lưu ngay" chỉ giả lập, không ghi đĩa.

## Tiêu chí hoàn thành
- `dotnet build` 0 lỗi 0 cảnh báo; `dotnet test` xanh 10/10.
- Test mới trong `UiTests/`: mở màn Cài đặt; đổi công tắc lưu đúng vào tệp và giữ nguyên mã phòng; "Sao lưu ngay" tạo bản mới thấy trong danh sách (DB tạm), lỗi (DB không có) hiện thông báo đỏ; tự sao lưu chỉ chạy khi bật và khi đến hạn (đồng hồ giả), không chạy khi tắt; giữ ít nhất 3 bản dù quá hạn; nút mở thư mục gọi bản giả; `--demo` không chạm đĩa.
- Ảnh chụp `tasks/reports/T022-screens/`: màn Cài đặt (sáng và tối), có danh sách sao lưu, có lỗi sao lưu.
- Báo cáo `tasks/reports/T022-report.md`, dòng cuối `TRẠNG THÁI: XONG` hoặc `CHƯA XONG (lý do)`; ghi rõ "chưa thử sao lưu trên DB thật". Cập nhật `TASKS.md`.
