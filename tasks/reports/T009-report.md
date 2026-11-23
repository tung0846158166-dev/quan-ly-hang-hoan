# Báo cáo T009 - Màn hình "Quét nhận hàng" trên máy tính + khởi tạo DB lúc khởi động

- **Người thực hiện:** antigravity
- **Phạm vi:** Chỉ `v2/src/QuanLyHangHoan.App/` và `v2/tests/QuanLyHangHoan.Tests/UiTests/`. Không sửa `Core/`, `Data/`, `ImporterTests/`, test gốc.

## Những gì đã làm
1. **Khởi tạo DB lúc khởi động (`App.xaml.cs`, `Services/AppStartupInitializer.cs`):**
   - Trong chế độ thật (`--demo` không chạy), gọi `AppDatabase.Initialize(AppPaths.DatabasePath)` thông qua `AppStartupInitializer.TryInitializeDatabase` TRƯỚC khi tạo provider và nạp dữ liệu.
   - Nếu DB có schema mới hơn app (`InvalidOperationException`) hoặc lỗi khởi tạo, hiển thị hộp thoại lỗi tiếng Việt rõ ràng (`MessageBox.Show`) và thoát an toàn (`Shutdown(1)`), không sửa/hạ cấp DB.
2. **Dịch vụ quét & phụ trợ (`App/Services/`):**
   - `IScanService.cs`, `SqliteScanService.cs` (gọi `ScanReceiver.Receive` qua `Task.Run`), `FakeScanService.cs` (cho `--demo`, không ghi DB).
   - **Ghi nhận thiếu thông tin ở `Data/ScanReceiver`:** `ScanReceiver.Load` chỉ `SELECT` các cột định danh/mã vận đơn/trạng thái/`photo_id` (chưa lấy `platform`, `order_type`, `recv_date`, `items`). Để không sửa `Data/`, `SqliteScanService` bổ sung thông tin đầy đủ của `Order` từ `OrderRepository.LoadActive` ngay sau khi `ScanReceiver.Receive` trả về.
   - `IScanSoundPlayer.cs` (`SystemScanSoundPlayer` dùng `System.Media.SystemSounds.Asterisk` / `Hand`, `NoOpScanSoundPlayer` cho test).
   - `IImageFilePickerService.cs` (`WindowsImageFilePickerService` lọc `*.jpg;*.jpeg;*.png`).
3. **Màn hình "Quét nhận hàng" (`ScanReceiveViewModel.cs`, `ScanReceiveView.xaml`, `ScanReceiveView.xaml.cs`):**
   - **Vị trí sidebar:** Đặt ở **đầu nhóm CHUNG** (`scan_receive`, `📷 Quét nhận hàng`, `showSectionHeader: true`), ngay trên `Bảng đơn mới`, để tách biệt rõ luồng thao tác kho với các bảng theo dõi sàn Shopee/TikTok.
   - Ô nhập mã tự động giữ tiêu điểm khi mở màn hình, sau khi Enter và sau khi bấm các nút phụ trợ.
   - Chặn quét đúp cùng mã trong `< 1 giây` (bộ đếm thời gian `debounceClock`).
   - Thẻ kết quả lớn, rõ ràng theo từng `ScanOutcome`: xanh (`ĐÃ NHẬN`), vàng (`ĐÃ NHẬN TRƯỚC ĐÓ` kèm giờ nhận), đỏ (`NHIỀU KIỆN KHỚP - KIỂM TRA THỦ CÔNG` / `KHÔNG TÌM THẤY`), cam (`CẦN KIỂM TRA` kèm lý do).
   - Tuân thủ quy tắc 6: chỉ hiện `"đã lưu"` khi `Ack == true`; mọi trường hợp `Ack == false` đều ghi rõ `"CHƯA nhận..."`.
   - Đính kèm ảnh tùy chọn cho lần quét kế tiếp (đọc `byte[]` và xóa khỏi hàng chờ ngay sau lần quét đó).
   - Danh sách `"ĐÃ QUÉT TRONG PHIÊN"` (mới nhất trên cùng) + bộ đếm `Nhận` / `Trước đó` / `Lỗi` + nút bật/tắt `Âm báo`.
   - Sau mỗi lần `Received` (`Ack == true`), `MainViewModel` gọi `ReloadAllDataAsync()` để cập nhật Trang chủ, các bảng và badge sidebar.

## Kết quả kiểm tra
- `dotnet build v2/QuanLyHangHoan.sln --verbosity minimal`: **0 cảnh báo, 0 lỗi**.
- `dotnet test v2/QuanLyHangHoan.sln --no-build --verbosity minimal`: **114/114 test xanh** (bao gồm `UiTests/T009ScanScreenTests.cs`: đủ các `ScanOutcome`, quy tắc `Ack == false` không hiện `"đã lưu"`, quét kiện A không làm đổi kiện B, đếm phiên & thứ tự mới nhất trên cùng, quét đúp `< 1s`, lưu ảnh qua `PhotoRepository`, từ chối schema mới hơn khi khởi động, reload `MainViewModel`, và chế độ `--demo`).
- Ảnh chụp màn hình lưu tại `tasks/reports/T009-screens/`:
  - `01-scan-received.png`
  - `02-scan-already-received.png`
  - `03-scan-ambiguous.png`
  - `04-scan-not-found.png` (kèm danh sách phiên 4 lượt quét)

## Chưa kiểm chứng thực tế
- Máy quét mã vạch USB vật lý ngoài kho thật và âm thanh `SystemSounds` trên loa Windows thật.
- Chọn ảnh qua hộp thoại `OpenFileDialog` trong phiên chạy app tương tác bằng tay.

TRẠNG THÁI: XONG
