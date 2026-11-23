# Báo cáo T020 - Tìm kiếm toàn cục + màn "Đang theo dõi"

## Đã hoàn thành
1. **Tìm kiếm toàn cục (`GlobalSearchService` + `MainViewModel` + `MainWindow`)**
   - Thêm thanh tìm kiếm toàn cục trên thanh trên cùng của `MainWindow` (phím tắt `Ctrl+K`, `Ctrl+F` khi không ở ô nhập khác; `Esc` xóa/đóng; mũi tên `Lên`/`Xuống` + `Enter`).
   - Chạy nền bất đồng bộ với debounce 250 ms (`_searchDebounce`) không treo giao diện.
   - Tìm theo mã vận đơn (`Track`, `ReturnTrack`, `AltTrack`, `TrackingCodes`, `ScanAliases`), mã đơn (`OrderId`), mã yêu cầu hoàn (`ReturnOrderId`), mã giao đi (`ForwardTrack`), tên sản phẩm/phân loại/SKU, ghi chú, ĐVVC; không phân biệt hoa thường và dấu tiếng Việt (`AlertThresholds.NormalizeForSearch`).
   - Mỗi kết quả là **một kiện riêng** (kiện A/B/C cùng mã đơn hiện thành các dòng riêng, không gộp).
   - Khớp chính xác mã riêng của kiện (`TrackingResolver.OwnCodes` + `ReturnOrderId`) được xếp lên đầu và gắn nhãn `"Khớp mã"`. Mã giao đi dùng chung (`ForwardTrack`) và `OrderId` KHÔNG gắn nhãn `"Khớp mã"` (quy tắc 3).
   - Hiển thị đầy đủ vị trí: `"Shopee - Hoàn hàng"`, `"Shopee - Đơn hủy"`, `"TikTok - Hoàn hàng"`, `"Kho lưu trữ"`, `"Đã xóa"`.
   - Giới hạn tối đa 50 kết quả + dòng thông báo `"Còn N kết quả, hãy gõ thêm"`.
   - Nhấp đúp / `Enter` mở cửa sổ Chi tiết đơn (`OrderDetailWindow`); nút `"Đi tới bảng"` chuyển tới màn hình tương ứng và chọn đúng dòng (đã bổ sung `SelectOrder` cho `ArchivedOrdersViewModel` và `DeletedOrdersViewModel`).

2. **Màn "Đang theo dõi" (`WatchlistViewModel` + `WatchlistView`)**
   - Thêm mục `"Đang theo dõi"` (`key = "watchlist"`) vào sidebar nhóm `CHUNG`, đặt ngay sau `"Bảng đơn mới"`, badge là số kiện đang theo dõi ở bảng chính/đơn hủy.
   - Hiển thị các kiện `IsWatchlist` từ bảng chính/đơn hủy (không gồm kho lưu trữ/đã xóa), có ô chọn `"Gồm cả kho lưu trữ"`, ô lọc nhanh và lọc theo sàn (`Tất cả`, `Shopee`, `TikTok`), trạng thái trống `"Chưa theo dõi đơn nào"`.
   - Dùng lại các thao tác của T013 qua `IOrderEditService` + `IOrderActionDialogService` + `OrderEditHelper`: đổi trạng thái, sửa lý do theo dõi, bỏ theo dõi, xem chi tiết, xem lịch sử sửa.

3. **Kiểm tra phạm vi & API Data**
   - Không sửa `Core/`, `Data/`, hoặc bất kỳ test nào ngoài `UiTests/`.
   - Không thiếu API ở `Data/`: `OrderRepository.Load`, `SqliteOrderEditService` (`EditField.IsWatchlist`, `EditField.WatchlistReason`, `EditField.Status`, `GetHistoryAsync`), và `TrackingResolver` đã đáp ứng đầy đủ.

## Kết quả build & test
- `dotnet build v2\QuanLyHangHoan.sln --nologo`: **0 Error(s), 0 Warning(s)**.
- `dotnet test v2\QuanLyHangHoan.sln --no-build`: **429/429 xanh**, chạy **10 lần liên tiếp: 10/10 xanh (0 lần đỏ)**.
- Test mới trong `v2/tests/QuanLyHangHoan.Tests/UiTests/T020SearchAndWatchlistUiTests.cs` (5 test): tìm theo mã vận đơn/mã đơn/mã hoàn/mã phụ/tên sản phẩm có & không dấu, kiện A/B tách riêng, khớp chính xác xếp đầu, mã giao đi dùng chung không gắn `"Khớp mã"`, nhãn vị trí đúng, giới hạn 50 kết quả, mở chi tiết & đi tới bảng, màn Theo dõi lọc/sửa lý do/bỏ theo dõi/xem lịch sử trên SQLite tạm, chế độ `--demo` không chạm đĩa.
- Cập nhật 2 test kiểm tra số lượng/danh sách sidebar cũ trong `UiTests/` (`ReturnOrdersViewModelTests.cs`, `T006PlatformSplitTests.cs`) cho mục `"watchlist"` mới.

## Ảnh chụp màn hình (`tasks/reports/T020-screens/`)
- `01-global-search-results.png`: Ô tìm toàn cục hiển thị kết quả (kiện A/B tách dòng, vị trí, nút Đi tới bảng).
- `02-global-search-empty.png`: Ô tìm toàn cục không có kết quả.
- `03-watchlist-populated.png`: Màn "Đang theo dõi" có dữ liệu (Dark theme).
- `04-watchlist-empty.png`: Màn "Đang theo dõi" trống (`"Chưa theo dõi đơn nào"`).

TRẠNG THÁI: XONG
