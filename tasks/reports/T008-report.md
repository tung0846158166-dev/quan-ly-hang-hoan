# Báo cáo T008 — Tự dời lưu trữ + hiển thị ảnh

- Người thực hiện: antigravity
- Người kiểm tra: claude
- Ngày: 2026-11-23

## Phạm vi & tuân thủ
- Chỉ thay đổi trong `v2/src/QuanLyHangHoan.App/` và `v2/tests/QuanLyHangHoan.Tests/UiTests/`.
- Không sửa `Core/`, `Data/`, `ImporterTests/`, hoặc các test gốc.
- Không dùng dữ liệu thật; toàn bộ test & ảnh chụp dùng DB tạm tạo từ dữ liệu giả qua `LegacyImporter.Import`.

## Các thay đổi đã thực hiện
1. **Tự dời lưu trữ (`IArchiveSweeper`)**:
   - Thêm `v2/src/QuanLyHangHoan.App/Services/IArchiveSweeper.cs` (`SqliteArchiveSweeper` gọi `ArchiveSweep.Run` qua `Task.Run`; `NoOpArchiveSweeper` cho `--demo`).
   - `MainViewModel`:
     - `InitializeAsync`: chạy `RunArchiveSweepAsync` lúc khởi động trước khi nạp dữ liệu vào các màn hình (không chặn UI, lỗi chỉ ghi nhận vào `LastSweepError`), sau đó nạp dữ liệu và khởi động vòng lặp định kỳ mỗi 10 phút (`PeriodicTimer`).
     - `OnLegacyImportSucceededAsync`: chạy `RunArchiveSweepAsync` sau khi nhập dữ liệu cũ thành công, rồi tải lại toàn bộ màn hình (`HomeVm`, `ShopeeReturnVm`, `ShopeeCancelledVm`, `TikTokReturnVm`, `ArchivedOrdersVm`, `StatisticsVm`) và đồng bộ badge sidebar.
     - Chế độ `--demo`: bỏ qua hoàn toàn việc gọi `IArchiveSweeper`.
2. **Hiển thị ảnh theo yêu cầu (`IPhotoProvider` + `PhotoViewerWindow`)**:
   - Thêm `v2/src/QuanLyHangHoan.App/Services/IPhotoProvider.cs` (`SqlitePhotoProvider` đọc `PhotoRepository.Get` / `PhotoRepository.MissingIds` qua `Task.Run`; `NoOpPhotoProvider` cho `--demo`).
   - Thêm `v2/src/QuanLyHangHoan.App/Services/IPhotoViewerService.cs`, `ViewModels/PhotoViewerViewModel.cs`, `Views/PhotoViewerWindow.xaml(.cs)`:
     - Tải ảnh theo yêu cầu lúc bấm nút `📷 Xem` trên bảng đơn (`ReturnOrdersView`) và Kho lưu trữ (`ArchivedOrdersView`), giải mã `byte[]` nền thành `BitmapImage` đóng băng (`Freeze()`).
     - Hỗ trợ phóng to/thu nhỏ/đặt lại `100%`, đóng bằng phím `Esc`.
     - Ảnh `null` (mất) hiển thị rõ `"Ảnh bị mất khỏi dữ liệu cũ"`.
     - `InlineDiffers == true` hiển thị ghi chú `"Ảnh nhúng khác tệp ảnh, đang hiện tệp ảnh"`.
3. **Thống kê ảnh mất trên Trang chủ & Thống kê**:
   - `HomeViewModel` / `HomeView.xaml` và `StatisticsViewModel` / `StatisticsView.xaml` hiển thị dòng nhỏ `"Ảnh mất: N"` khi `N > 0`.

## Kết quả kiểm tra
- `dotnet build v2 --no-incremental`: 0 lỗi, 0 cảnh báo.
- `dotnet test v2 --no-build`: 94/94 xanh (bao gồm 6 test mới trong `UiTests/T008ArchiveSweepPhotosTests.cs`).
- Ảnh chụp kiểm chứng lưu tại `tasks/reports/T008-screens/`:
  - `01-bang-don-bieu-tuong-anh.png`
  - `02-xem-anh-don-hang.png`
  - `03-anh-bi-mat.png`
  - `04-trang-chu-anh-mat.png`

TRẠNG THÁI: XONG
