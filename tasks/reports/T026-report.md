# Báo cáo T026 - Màn "Hàng hoàn về" (Bảng Hàng Hoàn Về, 4 chặng)

## Tóm tắt thực hiện
1. **Dịch vụ & Mô hình dữ liệu (`App/Services/IReturnWatchService.cs`)**:
   - Thêm interface `IReturnWatchService` với `LoadAsync`, `RemoveAsync`, `RestoreAsync`.
   - `SqliteReturnWatchService`: bọc `QuanLyHangHoan.Data.ReturnWatchService.Load/Remove/Restore` qua `Task.Run`.
   - `FakeReturnWatchService`: dữ liệu giả trong bộ nhớ đủ 4 chặng (Shopee & TikTok, gồm 2 kiện A/B cùng mã đơn, cảnh báo Vàng/Đỏ chặng 3), không đọc/ghi đĩa cho `--demo` và test.
2. **ViewModel (`App/ViewModels/ReturnWatchViewModel.cs`)**:
   - `ReturnWatchRowViewModel`: hiển thị STT, Cảnh báo 12h/24h (chỉ tính ở chặng 3 qua `AlertThresholds.MainPolicy`), Mã đơn, Sàn, Mã vận đơn hoàn (nếu trống lấy mã vận đơn giao), Sản phẩm, Giờ vào bảng (`Since`), Tiến trình 4 chặng ("Đã đồng ý › Đang về › Đã về shop › Đã nhận kho" + dòng nhỏ "Sàn: <trạng thái sàn>"), Giờ hàng về shop, Ngày nhận kho.
   - Bộ lọc kèm số đếm: Đang theo dõi (1-3, mặc định), Đã đồng ý, Đang về, Đã về shop, Đã nhận kho, Tất cả.
   - Tìm kiếm không phân biệt dấu/hoa thường (mã đơn, mã vận đơn, mã hoàn, tên sản phẩm).
   - Sắp xếp chặng tăng dần rồi giờ vào bảng sớm trước; giới hạn hiển thị tối đa 1.500 dòng kèm thông báo "còn N dòng, hãy lọc/tìm".
   - Thao tác: Xem chi tiết (`OrderDetailWindow`), Xem ở bảng chính (điều hướng sang bảng Shopee/TikTok và chọn dòng), Bỏ khỏi bảng (xác nhận đơn lẻ/hàng loạt, phím Delete, Hoàn tác ~8 giây qua toast gọi `RestoreAsync`; nhiều dòng chạy tuần tự và báo "n thành công, m lỗi").
3. **Giao diện (`App/Views/ReturnWatchView.xaml`, `ReturnWatchView.xaml.cs`, `MainWindow.xaml`, `MainViewModel.cs`, `HomeView.xaml`, `HomeViewModel.cs`, `App.xaml.cs`)**:
   - Mục sidebar **"Hàng hoàn về"** trong nhóm `CHUNG`, ngay trên **"Bảng đơn mới"**; badge là số đơn chặng 1-3 (`TrackingCount`).
   - Tự động tải lại khi mở màn, khi `ReloadAllDataAsync` chạy (sau quét nhận kiện, nạp đơn, dời lưu trữ, đồng bộ điện thoại gộp ~500 ms, và sau thao tác của chính màn này). Giữ dòng đang chọn và vị trí cuộn nếu còn.
   - Trang chủ thêm dòng nhỏ `Hàng đang về: N` (chặng 2) ở phần đầu.
   - Hỗ trợ theme Sáng/Tối (`DynamicResource`).
4. **Phạm vi tệp**:
   - Chỉ sửa/thêm trong `v2/src/QuanLyHangHoan.App/` và `v2/tests/QuanLyHangHoan.Tests/UiTests/`. Không sửa `Core/`, `Data/` hay test ngoài `UiTests/`.
   - Không thiếu API ở `Data/` (`ReturnWatchService.Load/Remove/Restore` đáp ứng đủ).

## Kiểm chứng
- `dotnet build v2/QuanLyHangHoan.sln --nologo`: **0 lỗi, 0 cảnh báo**.
- `dotnet test v2/QuanLyHangHoan.sln --no-build --nologo`: **437/437 test xanh 10/10 lần liên tiếp** (gồm 8 test mới trong `UiTests/T026ReturnWatchUiTests.cs` và cập nhật 2 test danh sách sidebar trong `T006PlatformSplitTests.cs`, `ReturnOrdersViewModelTests.cs`).
- Ảnh chụp màn hình tại `tasks/reports/T026-screens/`:
  - `01-all-4-stages.png`
  - `02-filtered-stage3.png`
  - `03-confirm-remove-dialog.png`
  - `03-undo-toast.png`
  - `04-empty-state.png`
- Lưu ý: **chưa thử với dữ liệu thật** (chỉ kiểm chứng bằng DB tạm + dữ liệu giả).

TRẠNG THÁI: XONG
