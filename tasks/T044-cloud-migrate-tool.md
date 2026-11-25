# T044 - Công cụ NHẬP DỮ LIỆU CŨ lên cloud (chế độ CHẠY THỬ trước) (giao cho: antigravity, người kiểm tra: claude)

PHẠM VI: CHỈ dự án MỚI `v2/tools/QuanLyHangHoan.CloudMigrate/` (+ thêm vào `v2/QuanLyHangHoan.sln`), test MỚI `v2/tests/QuanLyHangHoan.Tests/CloudMigrateTests.cs` và `tasks/reports/T044-report.md`. KHÔNG sửa tệp có sẵn ngoài việc thêm dòng vào `.sln` và thêm tham chiếu dự án vào `QuanLyHangHoan.Tests.csproj` nếu cần. KHÔNG `git commit`/`git push`. KHÔNG dùng dữ liệu đơn thật trong test; KHÔNG in nội dung đơn thật ra màn hình/nhật ký (chỉ đếm, tên trường, dung lượng).

## Bối cảnh
Đọc `docs/cloud-design.md` mục 3.1, 7 và `cloud/API.md` mục 1 (Parcel), 7 (`POST /v1/import`). Nguồn: SQLite v2 (`%LocalAppData%\QuanLyHangHoan\v2\data.db`, bảng `orders`, `order_items`, `photos`, `return_watch`, ...). Dùng `QuanLyHangHoan.Data.OrderRepository` để đọc (đã có sẵn), KHÔNG tự viết lại bộ đọc. Dữ liệu thật hiện có khoảng 4.962 đơn, 2.230 ảnh, DB ~180 MB, trong đó `raw_json` (~76 MB), `extra_json.photoHistory` (~37 MB) và `rawDetails` KHÔNG được đưa lên.

## Việc cần làm
1. Ứng dụng dòng lệnh `QuanLyHangHoan.CloudMigrate`:
   - `dry-run --db <đường dẫn>` (MẶC ĐỊNH, không bao giờ ghi gì lên mạng, mở CSDL ở chế độ CHỈ ĐỌC): chuyển mỗi `Order` thành Parcel theo `cloud/API.md` mục 1 (đúng danh sách trắng `extra`; KHÔNG `buyerNote`, `rawDetails`, `photoHistory`, `priceModes`...), tính `hash` ổn định, xuất `parcels.ndjson` (một Parcel mỗi dòng) vào thư mục `--out`, và in **báo cáo**: số đơn đang dùng/lưu trữ/đã xóa, tổng dung lượng ước tính trên D1, số kiện bị bỏ trường nào (chỉ tên trường + số lượng), số ảnh trong bảng `photos` và dung lượng, số ảnh nằm trong `photoHistory` mà KHÔNG có trong bảng `photos` (so theo `sha256`/nội dung băm; chỉ đếm, không in nội dung) — đây là ảnh sẽ MẤT nếu bỏ, nên phải liệt kê số lượng và xuất danh sách mã ảnh (không phải nội dung) ra `photos-unique.txt`, kiểm tra trùng khóa kiện (`id`), kiện thiếu mã vận đơn. Đầu ra báo cáo ghi cả ra `report.txt`.
   - `upload --db <đường dẫn> --url <worker> --key <khóa nhập>` (CHỈ chạy khi có cờ `--yes`): gửi các Parcel theo lô ≤ 500 tới `POST /v1/import` (`source`="file"), có thử lại hợp lý khi 429/5xx (chờ, không dồn dập), dừng ngay khi 401/403 hoặc `429 quota_exceeded`, in số `added/updated/unchanged/rejected`. Khóa chỉ lấy từ tham số hoặc biến môi trường `QLHH_IMPORT_KEY`, KHÔNG ghi ra tệp/nhật ký. Chưa có máy chủ thật để thử: test bằng máy chủ GIẢ `HttpMessageHandler`.
   - `photos --db <đường dẫn> --url <worker> --key <khóa>` (cũng cần `--yes`): chỉ LẬP KẾ HOẠCH tải ảnh lên (số ảnh, dung lượng, mã), chưa cài tải thật (ghi rõ trong báo cáo là việc của phiếu sau vì cần API ảnh).
2. Quy tắc: không bao giờ ghi/xóa gì vào SQLite nguồn; không đoán để tách/gộp dữ liệu lịch sử (AGENTS.md quy tắc 8); không cộng lặp sản phẩm.
3. Test xUnit với DB tạm do `LegacyImporter`/`OrderRepository` tạo từ dữ liệu GIẢ: Parcel không còn khóa cấm; `hash` ổn định và đổi khi nội dung đổi; báo cáo đếm đúng; `dry-run` không mở mạng (máy chủ giả báo nếu bị gọi); `upload` chia lô đúng, dừng khi 401, thử lại khi 429, không in khóa; ảnh chỉ có trong `photoHistory` được liệt kê.

## Tiêu chí hoàn thành
- `dotnet build v2/QuanLyHangHoan.sln` 0 lỗi, test mới và toàn bộ test cũ đạt.
- Báo cáo `tasks/reports/T044-report.md`: cách chạy (lệnh mẫu), phần CHƯA kiểm chứng (chưa có Worker thật), dòng cuối `TRẠNG THÁI: XONG` hoặc `CHƯA XONG (lý do)`. Cập nhật `TASKS.md`.
