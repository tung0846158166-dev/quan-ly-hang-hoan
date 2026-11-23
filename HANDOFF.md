# HANDOFF - trạng thái dự án v2 (cập nhật 2026-11-23)

App cũ (Electron, thư mục gốc) vẫn là nguồn dữ liệu chính cho tới khi chủ dự án xác nhận v2 khớp số liệu. v2 = `v2/` (C# .NET 8 + WPF + SQLite). Chi tiết việc: `TASKS.md`. Quy tắc nghiệp vụ bất biến: `AGENTS.md`.

## Cấu trúc v2
- `src/QuanLyHangHoan.Core`: nghiệp vụ thuần (không WPF): `TrackingResolver` (A không nhận B/C, mã chung = Ambiguous), `AlertPolicy` (Vàng 12h/Đỏ 24h), `ArchivePolicy` (nhận >= 24h thì dời lưu trữ), `ParcelUpsert` (nhập lại không cộng lặp).
- `src/QuanLyHangHoan.Data`: SQLite (Microsoft.Data.Sqlite) + đồng bộ. `AppDatabase` (schema v2, `user_version`), `Legacy/*` (nhập dữ liệu cũ an toàn: sao chép -> kiểm hash -> dry-run -> nhập -> đối chiếu), `OrderRepository`/`PhotoRepository` (chỉ đọc), `ScanReceiver` (nhận kiện, ACK sau commit), `OrderEditor` (sửa/xóa mềm/lưu trữ + lịch sử), `ImportApplier` (áp lô đơn nhập, đơn đã xóa không nạp lại), `ArchiveSweep`, `AppSettings`, `Sync/*` (giao thức tương thích `mobile_scanner.html` cũ; `MqttTransport` MQTTnet 4.3.7.1207, `NtfyTransport`, `CompositeTransport`, `SyncReceiver` chống lặp + tuần tự theo DB, `SyncService` có `pc_hello`/`pc_heartbeat`).
- `src/QuanLyHangHoan.App`: WPF MVVM (Antigravity làm). Provider thật nằm ở `Services/Sqlite*`; dữ liệu giả chỉ khi chạy `--demo`.
- `tests/QuanLyHangHoan.Tests`: xUnit, ~220 test (`UiTests/` là của giao diện). Không dùng dữ liệu đơn thật.

## Phân vai và quy trình
- Claude: `Core`, `Data`, importer, kiểm tra. Antigravity (`agy`): CHỈ `App/` và `tests/.../UiTests/`. Mỗi phiếu: `tasks/Txxx-*.md` -> agy làm -> `tasks/reports/Txxx-report.md` -> Claude kiểm tra -> `tasks/reports/Txxx-review.md`.
- Chạy agy ở nền: `agy.exe -p "Đọc AGENTS.md rồi làm theo tasks/Txxx-*.md" --dangerously-skip-permissions` (chế độ không tương tác từ chối lệnh nếu không cấp quyền; các rule `command(...)` hẹp đã thử và KHÔNG hoạt động; chủ dự án đã cho phép cờ này). Nhớ chụp trạng thái mã ngoài phạm vi TRƯỚC khi chạy để so lại sau.
- KHÔNG build/test cả solution trong lúc agy đang chạy (hai bên đạp `obj/` của `App/`, sinh lỗi thiếu `*.g.cs`). Khi agy chạy: test `Core`+`Data` trong dự án riêng (copy hai thư mục + các test không-UI vào thư mục tạm).
- Quota agy có thể hết giữa chừng (đã xảy ra với T003): xem `tasks/reports/Txxx-agy-output.log`.

## Đã xong (đều có test; chưa chạy với điện thoại/ntfy.sh thật và chưa chạy giao diện thật trên dữ liệu thật)
Nhập dữ liệu cũ (đã chạy thử trên BẢN SAO dữ liệu thật: 4.668/4.668 khớp, 7 giây) · giao diện tách sàn + Trang chủ · quét nhận hàng (máy quét USB) · tự dời lưu trữ · xem ảnh · đồng bộ điện thoại MQTT (thử tay thật 3/3 broker) + ntfy (chỉ thử máy chủ giả) · ảnh từ điện thoại · giao diện tối/sidebar thu gọn/nhớ cửa sổ · `OrderEditor` · `ImportApplier`.

## Chưa làm / chờ chủ dự án
1. Chạy thử app thật và so số liệu với app cũ (`v2/CHAY-APP.md`).
2. Bộ ĐỌC Excel Shopee/TikTok/Salework -> `Order` (đưa vào `ImportApplier`): cần file mẫu ẩn danh của chủ dự án.
3. `new_order_scan`, `smart_label`, `sync_item`, `inbox_push`, màn "Bảng đơn mới" có dữ liệu thật: cần biết chủ dự án có dùng không.
4. Installer thật (MSIX/Inno), tự cập nhật, Salework tự động.
5. Mã hóa bản tin đồng bộ / relay riêng. `manifest_sync` cố ý chưa làm (lộ mã vận đơn qua broker công cộng).

## Tự nhập đơn từ sàn (đang làm; chủ dự án chốt dùng: theo dõi thư mục, Google Sheet, Salework tự động)
Kế hoạch và đặc tả: `docs/auto-import-plan.md`. Kỹ thuật chuyển mã: "đáp án chuẩn" sinh từ MÃ JS CŨ (`v2/tools/legacy-oracle/oracle.cjs`, cần Node) rồi test C# phải khớp (`tests/.../Golden/*.json`). Đã xong và khớp: `SpreadsheetReader`, `LegacyText`, `TikTokMapper`, `ParcelMatching`, `ItemMerge`, `ParcelMerge`, `TikTokMerger`, `TikTokImportService` (lưu SQLite). Còn: dịch vụ theo dõi thư mục + màn hình, Google Sheet, Shopee, Salework tự động (WebView2).

## Rủi ro đã biết
- **Nhập lại dữ liệu cũ sau khi đã dùng v2 ghi đè thay đổi của v2** và có thể tạo bản trùng cho đơn vừa dời sang kho lưu trữ (xem `TASKS.md`, đang xử lý).
- Đồng bộ đi qua broker công cộng (emqx, hivemq, mosquitto) và ntfy.sh, KHÔNG mã hóa; bảo vệ duy nhất là mã phòng (v2 tự sinh ngẫu nhiên 24 ký tự, cảnh báo nếu yếu). Xem `docs/sync-design.md`.
- ntfy.sh không truy cập được từ mạng máy chủ dự án (timeout), nên đường ntfy chưa kiểm chứng thật; MQTT đã kiểm chứng.
- Màn Quét và đồng bộ có thể ghi DB cùng lúc: nếu gặp "database is locked" thì trả `error` (không bao giờ ACK sai), điện thoại gửi lại.
- 1.342 đơn chỉ còn trong `sao_luu_tu_dong/` (không nhập, chờ chủ dự án xác nhận đã chủ động dọn); 3 nhóm trùng khóa trong dữ liệu cũ được giữ riêng, chờ chủ dự án xem.
