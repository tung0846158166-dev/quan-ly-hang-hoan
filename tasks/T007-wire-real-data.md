# T007 - Nối dữ liệu thật (SQLite) vào giao diện (giao cho: antigravity, người kiểm tra: claude)

Phạm vi: CHỈ `v2/src/QuanLyHangHoan.App/` và `v2/tests/QuanLyHangHoan.Tests/UiTests/`. KHÔNG sửa `Core/`, `Data/`, `ImporterTests/` và test ngoài `UiTests/`. Nếu API ở `Data` thiếu thứ cần, ghi vào báo cáo, đừng tự sửa Data.
Đọc trước: `AGENTS.md`, `TASKS.md`, `tasks/reports/T006-review.md`, `docs/legacy-data-schema.md` (mục "Quyết định của chủ dự án").
Không dùng dữ liệu đơn thật trong test. Không tự chạy nhập trên dữ liệu thật của user.

## API có sẵn ở `Data` (claude đã viết, có test)
- `QuanLyHangHoan.Data.OrderRepository.Load(dbPath)` -> `IReadOnlyList<StoredOrder>`; `StoredOrder(Order, IsArchived, ArchivedAt, IsDeleted, DeletedAt, DeletedFrom)`. `LoadActive(dbPath)` -> `IReadOnlyList<Order>` (không lưu trữ, không đã xóa). DB chưa có thì trả danh sách rỗng, KHÔNG lỗi, KHÔNG tạo tệp. Chỉ đọc.
- `QuanLyHangHoan.Data.Legacy.LegacyImportService.Run(sourceFolder, workRoot, dbPath, dryRunOnly)` -> `LegacyImportRun(Ok, Message, CopyFolder, DryRun, Import, Reconcile)`. Tự sao chép nguồn sang thư mục mới + kiểm hash, dry-run, (nếu không `dryRunOnly`) nhập + đối chiếu. `DryRun` là `DryRunReport` (Main/Archived/Deleted.Mapped, InlinePhotos, PhotoIdRefs, PlatformRenamed, Duplicates, Warnings, SkippedByDecision, Errors...). `Import` có `Inserted/Updated`. Chạy đồng bộ, có thể vài giây: gọi qua `Task.Run`.
- Đường dẫn mặc định (tự định nghĩa trong App, một chỗ duy nhất, ví dụ `AppPaths`): DB `%LocalAppData%\QuanLyHangHoan\v2\data.db`; thư mục bản sao `%LocalAppData%\QuanLyHangHoan\v2\legacy-copies`.

## Việc cần làm
1. **Provider thật** trong `App/Services/` (ví dụ `SqliteOrderProvider`) cài `IReturnOrderProvider` (đơn đang ở bảng chính = `LoadActive`), `IArchivedOrderProvider` (`IsArchived`), `IDeletedOrderProvider` (`IsDeleted`, ánh xạ sang `DeletedOrderRecord`). `INewOrderProvider`: chưa có nguồn thật (đến từ điện thoại sau này) -> trả danh sách rỗng. Tham số `count` của `GetReturnOrders` bỏ qua (trả hết); đọc DB qua `Task.Run`, không chặn UI. Giữ `GetPlaceholderScreenData`/`GetCurrentTime` cho tương thích.
2. **ILegacyImporter thật** bọc `LegacyImportService` (dry-run -> `LegacyImportDryRunReport`, nhập -> `LegacyImportResult`). Ánh xạ số liệu từ `DryRunReport`/`ImportResult`/`Reconcile`; nếu `Ok=false` hiện `Message` rõ ràng (kể cả "số liệu KHÔNG khớp"). Nhóm trùng khóa chỉ BÁO, không tự gộp.
3. **Khởi động** (`App.xaml.cs`): dùng provider thật. Nếu DB chưa có hoặc rỗng -> các màn hình hiện trạng thái TRỐNG đẹp (Trang chủ trống đã có) kèm nút dẫn tới "Nhập dữ liệu cũ", KHÔNG tự đổ dữ liệu giả. Giữ dữ liệu giả sau cờ `--demo` (dòng lệnh) để chụp ảnh/test, và test UI hiện có vẫn dùng bản giả. Nhãn "Dữ liệu giả" chỉ hiện ở chế độ `--demo`.
4. Sau khi nhập thành công, mọi màn hình tự tải lại (Trang chủ, 3 bảng sàn, Kho lưu trữ, Đã xóa, Thống kê).
5. Quy tắc bất biến: Shopee/TikTok/Đơn hủy tách đúng như T006; Vàng 12h / Đỏ 24h đọc từ `AlertThresholds`; không đổi nhãn sàn đã chuẩn hóa "TikTok".

## Tiêu chí hoàn thành
- `dotnet build` 0 lỗi 0 cảnh báo; `dotnet test` xanh TOÀN BỘ.
- Test mới trong `UiTests/`: tạo DB tạm bằng `LegacyImporter.Import` từ dữ liệu GIẢ rồi kiểm provider trả đúng số đơn chính/lưu trữ/đã xóa, ba bảng sàn tách đúng và tổng khớp, DB không tồn tại -> rỗng không lỗi, importer thật: dry-run không tạo DB, nhập xong tải lại có dữ liệu.
- Ảnh chụp: Trang chủ trống (chưa có DB), Trang chủ sau khi nhập dữ liệu giả, màn "Nhập dữ liệu cũ" hiện kết quả -> `tasks/reports/T007-screens/`.
- Báo cáo `tasks/reports/T007-report.md`, dòng cuối `TRẠNG THÁI: XONG` hoặc `CHƯA XONG (lý do)`. Cập nhật `TASKS.md`.
