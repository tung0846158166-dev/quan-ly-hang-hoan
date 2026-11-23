# T021 - Khay hệ thống, tự mở cùng Windows, thông báo cảnh báo vàng/đỏ (giao cho: antigravity, người kiểm tra: claude)

Phạm vi: CHỈ `v2/src/QuanLyHangHoan.App/` và `v2/tests/QuanLyHangHoan.Tests/UiTests/`. KHÔNG sửa `Core/`, `Data/` và test ngoài `UiTests/`. Thiếu API ở Data thì ghi vào báo cáo.
Đọc trước: `AGENTS.md` (quy tắc 7: vàng sau 12h, đỏ sau 24h kể từ lúc hàng về, chỉ đơn CHƯA nhận), `v2/src/QuanLyHangHoan.Data/AppSettings.cs` (các trường mới `AutoStart`, `MinimizeToTray`, `NotifyAlerts`, `Sounds`), `v2/src/QuanLyHangHoan.App/Services/AlertThresholds.cs`, `UiSettingsStore.cs`. Test dùng `WpfTestHost`; `dotnet test` toàn bộ 10 lần liên tiếp 0 lần đỏ. Test KHÔNG được ghi registry thật, không tạo biểu tượng khay thật, không chạm `%LocalAppData%` thật: bọc qua interface có bản giả.

## Vấn đề
Đồng bộ điện thoại, theo dõi thư mục, Google Sheet chỉ chạy khi app đang mở; bấm X là tắt hết. App cũ có khay hệ thống, tự mở cùng Windows và thông báo khi đơn quá 12h/24h. v2 chưa có.

## Việc cần làm
1. **Khay hệ thống**: biểu tượng ở khay (menu: "Mở app", "Tắt/Bật thông báo", "Thoát hẳn"). Khi `MinimizeToTray` bật: bấm X ẩn cửa sổ xuống khay (không thoát), lần đầu hiện thông báo nhỏ "App vẫn chạy ở khay để nhận đơn từ điện thoại"; nhấp đúp biểu tượng hoặc "Mở app" khôi phục. "Thoát hẳn" mới đóng tiến trình, dừng sạch các dịch vụ nền (đồng bộ, thư mục, Sheet) như cách thoát hiện tại. Mở app lần hai khi đã có bản đang chạy: đưa bản cũ lên trước, không chạy hai bản (mutex đặt tên theo người dùng). Biểu tượng khay đổi (hoặc có chấm) khi có đơn đỏ.
2. **Tự mở cùng Windows**: công tắc ghi/xóa khóa `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` (tên khóa `QuanLyHangHoan`, giá trị là đường dẫn exe hiện tại kèm `--tray` để khởi động thu nhỏ ở khay). Chỉ ghi khi người dùng bật; tắt thì xóa đúng khóa của mình, không đụng khóa khác. Bọc qua `IAutoStartService` (bản giả cho test/`--demo`). `--tray` mở thẳng xuống khay, không hiện cửa sổ.
3. **Thông báo cảnh báo** (Windows balloon/toast qua khay, bọc `INotificationService` có bản giả): kiểm tra mỗi 5 phút (và khi app khởi động sau 30 giây) các đơn CHƯA nhận đã chuyển sang VÀNG (>= 12h kể từ giờ hàng về) hoặc ĐỎ (>= 24h): gửi MỘT thông báo gộp ("N đơn vừa quá 12h, M đơn vừa quá 24h", bấm vào mở app tới màn Trang chủ). Mỗi đơn chỉ báo MỘT lần cho mỗi mức (nhớ trong tệp trạng thái nhỏ cạnh `settings.json`, không ghi vào DB đơn; tối đa 5.000 khóa). Không báo đơn đã nhận/không cần nhận/bị mất. Đơn chưa có giờ hàng về không báo. Tôn trọng `NotifyAlerts`.
4. **Âm thanh**: khi `Sounds` bật, phát tiếng khi điện thoại tạo/quét đơn thành công và tiếng khác khi lỗi/trùng (dùng `IScanSoundPlayer` đã có; không thêm gói mới nếu không cần).
5. **Cài đặt**: thêm khối "Chạy nền và thông báo" (4 công tắc: Tự mở cùng Windows, Thu nhỏ xuống khay khi bấm X, Báo vàng/đỏ bằng thông báo, Âm thanh) vào màn phù hợp đã có (nếu chưa có màn Cài đặt: đặt ở Trang chủ phần dưới hoặc màn Đồng bộ điện thoại; claude/agy sẽ gom vào màn Cài đặt ở phiếu sau). Lưu vào `AppSettings` (ghi nguyên tử bằng `Save`).
6. `--demo`: không tạo khay thật, không ghi registry, không phát thông báo thật (bản giả ghi vào danh sách để xem).

## Tiêu chí hoàn thành
- `dotnet build` 0 lỗi 0 cảnh báo; `dotnet test` xanh 10/10.
- Test mới trong `UiTests/` (bản giả khay/registry/thông báo): X ẩn xuống khay khi bật và thoát thật khi tắt; "Thoát hẳn" dừng dịch vụ; bật tự mở ghi đúng khóa/giá trị `--tray`, tắt xóa đúng khóa; thông báo gộp đúng số vàng/đỏ, mỗi đơn mỗi mức chỉ một lần (chạy lại không báo lại, kể cả sau "khởi động lại" giả), đơn đã nhận không báo, `NotifyAlerts` tắt thì im lặng; mở bản thứ hai không tạo cửa sổ thứ hai; `--tray` không hiện cửa sổ; `--demo` không chạm hệ thống.
- Ảnh chụp `tasks/reports/T021-screens/`: khối cài đặt chạy nền/thông báo, menu khay (nếu chụp được; không thì mô tả trong báo cáo).
- Báo cáo `tasks/reports/T021-report.md`, dòng cuối `TRẠNG THÁI: XONG` hoặc `CHƯA XONG (lý do)`; ghi rõ "chưa thử khay/thông báo/registry thật". Cập nhật `TASKS.md`.
