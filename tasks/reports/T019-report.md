# Báo cáo T019 - Giao diện cho thao tác từ app iPhone (đơn mới quét từ điện thoại vào thẳng Bảng chính)

- Người thực hiện: antigravity
- Người kiểm tra: claude
- Ngày: 2026-11-23
- Phạm vi: CHỈ `v2/src/QuanLyHangHoan.App/` và `v2/tests/QuanLyHangHoan.Tests/UiTests/` (không sửa `Core/`, `Data/`, `Data/Sync/*`, `PhoneListTests.cs` hay test ngoài `UiTests/`).

## Những gì đã làm

1. **Tải lại dữ liệu gộp (~500 ms) & giữ lựa chọn/cuộn**
   - `MainViewModel.OnSyncHandled` (`v2/src/QuanLyHangHoan.App/ViewModels/MainViewModel.cs`):
     - Chống xử lý lặp theo `(RequestId, Stage)` khi nhiều đường truyền cùng đẩy về.
     - Khi nhận `Outcome == "success"` ở mọi `Stage` trừ `"photo"` (hoặc khi `Kind == "new_order"`), gọi `ScheduleSyncReload()` gộp các sự kiện dồn dập trong `500 ms` (mặc định, có tham số `syncReloadDebounce` tùy chọn cho test) rồi mới gọi `ReloadAllDataAsync()`.
     - `Stage == "photo"` hoặc các lỗi (`error`, `invalid`, `ambiguous`, `not_found`, `locked`) hoặc `duplicate` không kích hoạt tải lại.
   - Giữ dòng đang chọn (`SelectedRow` theo `Order.Id`) qua `ReturnOrdersViewModel` / `ArchivedOrdersViewModel` / `DeletedOrdersViewModel` và giữ vị trí cuộn dọc `ScrollViewer` của `OrdersGrid` trong `v2/src/QuanLyHangHoan.App/Views/ReturnOrdersView.xaml.cs`.

2. **Toast/Snackbar không chặn**
   - `MainViewModel` + `MainWindow.xaml` (thêm `ToastIsWarning` cho toast màu vàng `#78350F` / viền `#F59E0B`):
     - `Kind == "new_order"` (`success`): `"📥 Điện thoại vừa tạo đơn mới: <mã> (<sàn>)"`.
     - `duplicate` ở `Stage == "scan"` với `Message` chứa `"ĐƠN ĐÃ CÓ"`: `"⚠️ Điện thoại quét đơn đã có: <mã>"` (nền vàng cảnh báo).
     - `Stage == "delete"` (`success`): `"🗑 Điện thoại xóa đơn <mã> (khôi phục được ở Đã xóa)"`.
     - `Stage == "note"` (`success`): `"📝 Điện thoại cập nhật ghi chú đơn <mã>"`.
     - `Stage == "edit"` (`success`): `"📝 Điện thoại cập nhật trạng thái đơn <mã>"`.
     - Các lỗi (`error`, `invalid`, `ambiguous`, `not_found`, `locked`): chỉ ghi nhật ký, không bật toast.

3. **Nhật ký màn "Đồng bộ điện thoại"** (`PhoneSyncViewModel.cs` + `PhoneSyncView.xaml`)
   - `PhoneSyncLogItem` bổ sung `StageLabel` tiếng Việt (`"Đơn mới"`, `"Nhận kiện"`, `"Ghi chú"`, `"Sửa trạng thái"`, `"Xóa"`, `"Ảnh"`) và `IsNewOrder`.
   - Dòng `"Đơn mới"` hiển thị biểu tượng `📥` và viền nổi bật (`BorderThickness="2"`).
   - Giữ màu theo kết quả: xanh (`#DCFCE7`) cho thành công, vàng (`#FEF3C7`) cho trùng, đỏ (`#FEE2E2`) cho lỗi.

4. **Đếm đơn mới & ghi chú cố định** (`PhoneSyncViewModel.cs` + `PhoneSyncView.xaml`)
   - Thêm `NewOrdersTodayCount` / `NewOrdersTodayText`: `"Đơn mới từ điện thoại hôm nay: N"` (đếm các sự kiện `Kind == "new_order"` thành công trong phiên chạy).
   - Thêm câu ghi chú cố định: `"Đơn quét từ điện thoại vào thẳng Bảng chính với trạng thái 'Chưa trả', ghi chú 'Đơn mới tạo từ máy quét điện thoại'. Tạo đơn hủy từ điện thoại không còn."`
   - Cập nhật dòng cảnh báo nhỏ: `"🔒 Danh sách đơn gửi sang điện thoại đi qua máy chủ công cộng không mã hóa; giữ bí mật mã phòng."`

5. **Chế độ `--demo`** (`FakeSyncController` trong `ISyncController.cs` + nút `"⚡ Phát sự kiện mẫu"` trên `PhoneSyncView.xaml`)
   - `FakeSyncController.EmitDemoSampleEvents()` phát đủ các sự kiện mẫu (`new_order`, `scan` thành công, `duplicate`, `note`, `edit`, `delete`, `photo`, `error`) hoàn toàn trong bộ nhớ, không chạm DB/mạng.

## Tệp đã sửa / thêm
- `v2/src/QuanLyHangHoan.App/Services/ISyncController.cs`
- `v2/src/QuanLyHangHoan.App/ViewModels/MainViewModel.cs`
- `v2/src/QuanLyHangHoan.App/MainWindow.xaml`
- `v2/src/QuanLyHangHoan.App/ViewModels/PhoneSyncViewModel.cs`
- `v2/src/QuanLyHangHoan.App/Views/PhoneSyncView.xaml`
- `v2/src/QuanLyHangHoan.App/Views/ReturnOrdersView.xaml.cs`
- `v2/tests/QuanLyHangHoan.Tests/UiTests/T010SyncScreenTests.cs` (cập nhật thời gian chờ `120 ms` -> `650 ms` cho phù hợp cơ chế gộp 500 ms)
- `v2/tests/QuanLyHangHoan.Tests/UiTests/T019PhoneActionsUiTests.cs` (mới, 11 test case)

## Kiểm chứng
- `dotnet build v2\QuanLyHangHoan.sln --no-restore`: **0 lỗi, 0 cảnh báo**.
- `dotnet test v2\QuanLyHangHoan.sln --no-build` (chạy **10/10 lần liên tiếp**): **413/413 test xanh** mỗi lần (`Failed: 0, Passed: 413, Skipped: 0`).
- Ảnh chụp kiểm chứng tại `tasks/reports/T019-screens/`:
  - `01-phone-sync-logs.png`: màn Đồng bộ điện thoại với bộ đếm, ghi chú cố định, cảnh báo và nhật ký đủ loại sự kiện.
  - `02-main-table-new-order-toast.png`: Bảng chính Shopee hiện đơn mới `SPXVN069876543210` và toast đơn mới.
- **Lưu ý**: chưa thử với iPhone thật (chỉ kiểm thử bằng bản giả của `ISyncController`, DB tạm và dữ liệu giả).

TRẠNG THÁI: XONG
