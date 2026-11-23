# Báo cáo T018 - Màn hình "Chi tiết đơn"

## 1. Tóm tắt thực hiện
Đã kiểm tra, sửa lỗi biên dịch/chạy test từ phần mã dở dang và hoàn thiện toàn bộ màn hình "Chi tiết đơn" (CHỈ XEM) theo `tasks/T018-order-detail-view.md`, chỉ chạm `v2/src/QuanLyHangHoan.App/`, `v2/tests/QuanLyHangHoan.Tests/UiTests/`, `TASKS.md` và `tasks/reports/T018-*`:

- **Dịch vụ & ViewModel (`App/Services/IOrderDetailService.cs`, `App/ViewModels/OrderDetailViewModel.cs`)**:
  - `IOrderDetailService` / `WindowsOrderDetailService`: mở `OrderDetailWindow` độc lập, hỗ trợ mở nhiều cửa sổ cùng lúc. Đã sửa logic chọn `Owner` (không gán `Owner` là một `OrderDetailWindow` khác đang active để khi nhấn `Esc` đóng cửa sổ này không kéo theo đóng các cửa sổ chi tiết khác).
  - `OrderDetailViewModel`: hiển thị đầy đủ 8 nhóm (ẩn nhóm trống trừ phần Ảnh và Lịch sử sửa luôn có trạng thái rõ ràng):
    1. **Tổng quan**: sàn, loại đơn, trạng thái kiện (sàn), trạng thái nhận kho, cảnh báo 12h/24h + thời gian đã về, giá trị, ĐVVC (chung, giao đi, hoàn về), theo dõi (có/không + lý do), đặt trước (Pre-order).
    2. **Mã**: mã đơn, mã yêu cầu hoàn, mã vận đơn, mã vận đơn hoàn, mã giao đi, mã phụ (`AltTrack`), mã lịch sử/thay thế (`TrackingCodes`, `ScanAliases`), nút sao chép qua `IClipboardService`, kèm ghi chú *"Mã giao đi dùng chung không đủ để nhận kiện"*.
    3. **Mốc thời gian**: ngày giao, ngày hoàn/hủy, giờ hàng về shop, ngày nhận kho, hạn khiếu nại, tuần nhập, giờ nhập.
    4. **Sản phẩm**: bảng từng dòng (STT, tên sản phẩm, màu/phân loại, size, SL, SKU, SKU ID, `ImportPrices`), tổng SL, nhãn *"Hoàn một phần"* khi `IsPartialReturn == true`.
    5. **Ghi chú**: lý do hoàn/hủy, ghi chú người mua, ghi chú của tôi (hỗ trợ nhiều dòng).
    6. **Ảnh**: ảnh thu nhỏ tải qua `IPhotoProvider`, bấm mở trình xem ảnh lớn qua `IPhotoViewerService`; trống hoặc lỗi hiển thị thông báo rõ ràng (*"Chưa có ảnh"*, *"Không tải được ảnh (ảnh bị mất)"*, ...).
    7. **Dữ liệu từ file sàn** (thu gọn mặc định trong `Expander`): cơ sở giá (`ImportPriceBasis`), chế độ giá đã chọn, `PriceModes`, bảng `RawDetails` có ô lọc nhanh.
    8. **Lịch sử sửa**: tải qua `IOrderEditService.GetHistoryAsync`, hiển thị theo mẫu T013, trống thì *"Chưa có sửa đổi nào"*.
  - Không hiển thị tên/SĐT/địa chỉ khách; các trường trống hiển thị `"—"`.

- **Giao diện & điểm mở (`App/Views/OrderDetailWindow.xaml`, `OrderDetailWindow.xaml.cs`, `DataGridRowHelper.cs`, các bảng đơn)**:
  - Hỗ trợ `Esc` và nút `Đóng (Esc)` để đóng cửa sổ.
  - Gắn mở chi tiết bằng nhấp đúp dòng, phím `Enter` trên dòng đang chọn, và mục `"🔍 Xem chi tiết"` đầu tiên trong menu ngữ cảnh / nút `•••` tại 3 bảng sàn (`ReturnOrdersView`), Kho lưu trữ (`ArchivedOrdersView`), Đã xóa (`DeletedOrdersView`), và Bảng đơn mới (`NewOrdersView`). Phím `Delete` và các thao tác cũ giữ nguyên hành vi.

- **Sửa lỗi trong quá trình kiểm tra**:
  - `T018OrderDetailUiTests.StubOrderProvider`: bổ sung `GetPlaceholderScreenData(string)` của `IReturnOrderProvider`.
  - `T018OrderDetailUiTests.StubPhotoProvider.Add`: sửa tham số khởi tạo `PhotoData(id, "image/png", bytes, "test", false)`.
  - `WindowsOrderDetailService.ShowOrderDetail`: tránh gán `Owner` là `OrderDetailWindow` khác để đóng bằng `Esc` không đóng dây chuyền cửa sổ đang mở.
  - `T018OrderDetailUiTests.RenderOrderDetailScreens_ForVisualVerification`: cuộn `DetailScrollViewer` xuống cuối khi chụp `04-raw-import-expanded.png` để thấy rõ phần "Dữ liệu từ file sàn" và "Lịch sử sửa".

## 2. Kết quả kiểm tra
- `dotnet build v2/QuanLyHangHoan.sln --nologo`: **0 lỗi, 0 cảnh báo**.
- `dotnet test v2/QuanLyHangHoan.sln --no-build --nologo` chạy **10/10 lần liên tiếp**: **384/384 test xanh** ở cả 10 lần (gồm 6 test trong `UiTests/T018OrderDetailUiTests.cs`).
- Ảnh chụp màn hình đã tạo và kiểm tra trực tiếp tại `tasks/reports/T018-screens/`:
  - `01-shopee-multi-items.png`
  - `02-tiktok-order.png`
  - `03-empty-no-photo.png`
  - `04-raw-import-expanded.png`

TRẠNG THÁI: XONG
