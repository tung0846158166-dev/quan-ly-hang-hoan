# Báo cáo T041 — App máy tính: giao diện Trạng thái Cloud, khóa ghi khi mất mạng, hạn mức, ghép thiết bị

- **Người thực hiện:** Antigravity
- **Người kiểm tra:** Claude
- **Phạm vi thực hiện:** Chỉ sửa/thêm trong `v2/src/QuanLyHangHoan.App/`, `v2/tests/QuanLyHangHoan.Tests/UiTests/`, `tasks/reports/T041-screens/`, `tasks/reports/T041-report.md` và `TASKS.md`. **Không** sửa `Core/`, `Data/`, `cloud/`, `ios-app/`, hay bất kỳ test nào ngoài `UiTests/`.

---

## 1. Danh sách tệp thêm mới & chỉnh sửa

### Thêm mới
- `v2/src/QuanLyHangHoan.App/Services/WriteGate.cs` — Cổng điều kiện ghi tập trung (`CanWrite`, `IsLocked`, `TooltipReason`, sự kiện `Changed`). Mặc định `WriteGate.AlwaysOpen` khi chưa bật cloud (`ICloudLink == null`) hoặc khi VM chạy độc lập trong test cũ.
- `v2/src/QuanLyHangHoan.App/ViewModels/CloudStatusViewModel.cs` — Quản lý trạng thái kết nối (6 trạng thái), màu/nhãn tiếng Việt, popup chi tiết (đồng bộ cuối, lỗi gần nhất, nút "Đồng bộ ngay"), banner "chỉ xem" kèm giờ `HH:mm`, hạn mức hôm nay (`ok`/`warn`/`stop` + định dạng `8.200 / 100.000 yêu cầu (8%)`), biểu mẫu ghép thiết bị (`PairAsync`), quên ghép có xác nhận (`Unpair`), tạo mã ghép 6 số + đếm ngược 10 phút cho `owner` (`StartPairingAsync`). Tự đưa sự kiện `ICloudLink.Changed` và timer đếm ngược về luồng giao diện qua `_dispatchToUi`.
- `v2/src/QuanLyHangHoan.App/Views/CloudSettingsView.xaml` & `CloudSettingsView.xaml.cs` — Nội dung thẻ **Cài đặt > Cloud** theo đúng phong cách thẻ hiện có (tương thích cả giao diện sáng và tối).
- `v2/tests/QuanLyHangHoan.Tests/UiTests/T041CloudStatusUiTests.cs` — 7 test UI trên `WpfTestHost` + `FakeCloudLink` (bao gồm cập nhật từ luồng nền và chụp 8 ảnh màn hình các trạng thái).
- `tasks/reports/T041-screens/cloud-01..08*.png` — 8 ảnh chụp màn hình các trạng thái Cloud.

### Chỉnh sửa
- `v2/src/QuanLyHangHoan.App/App.xaml.cs` — Đăng ký `FakeCloudLink` cho `--demo` (kèm trạng thái `Connected`, vai trò `owner`, hạn mức mẫu). Chế độ cục bộ hiện tại truyền `cloudLink: null` với ghi chú rõ điểm để Claude cắm bản `ICloudLink` thật khi bật chế độ cloud.
- `v2/src/QuanLyHangHoan.App/ViewModels/MainViewModel.cs` — Tạo `WriteGate` dùng chung và `CloudVm`, truyền `writeGate` xuống toàn bộ các VM có thao tác ghi, khóa `UndoDeleteCommand` & `RunArchiveSweepAsync`, thêm điều hướng `NavigateByKey("cloud")` mở thẳng **Cài đặt > Cloud**, đóng chéo popup Cloud và chuông thông báo, giải phóng `CloudVm` trong `Dispose()`.
- `v2/src/QuanLyHangHoan.App/ViewModels/SettingsViewModel.cs` & `Views/SettingsView.xaml` — Thêm thẻ `☁️ Cloud` (key `"cloud"`) **chỉ khi** `Cloud.IsEnabled == true` (khi `cloudLink == null`, danh sách 5 thẻ cũ giữ nguyên 100%, không làm vỡ các test T017/T035).
- `v2/src/QuanLyHangHoan.App/MainWindow.xaml` & `MainWindow.xaml.cs` — Thêm chấm trạng thái Cloud (`CloudStatusButton`) ở thanh trên bên cạnh nút chuông (ẩn hoàn toàn khi `cloudLink == null`), popup chi tiết (`CloudStatusPopup`, đóng bằng `Esc`), banner đỏ "chỉ xem" (`CloudReadOnlyBanner`) ngay đầu vùng nội dung các màn hình (tự ẩn khi kết nối lại), gắn tooltip khóa ghi cho nút `↩ Hoàn tác`.
- `v2/src/QuanLyHangHoan.App/Themes/AppStyles.xaml` — Thêm `ToolTipService.ShowOnDisabled="True"` trên `PrimaryActionButtonStyle` và `FilterPillButtonStyle`, bổ sung hiệu ứng làm mờ (`Opacity=0.45`) khi vô hiệu hóa cho `FilterPillButtonStyle`.
- Các ViewModel & View nối vào `WriteGate` tập trung (khóa lệnh ghi + mờ nút + tooltip `"Mất kết nối cloud — chỉ xem được"`):
  - `ViewModels/ReturnOrdersViewModel.cs` & `Views/ReturnOrdersView.xaml`
  - `ViewModels/ArchivedOrdersViewModel.cs` & `Views/ArchivedOrdersView.xaml`
  - `ViewModels/DeletedOrdersViewModel.cs` & `Views/DeletedOrdersView.xaml`
  - `ViewModels/WatchlistViewModel.cs` & `Views/WatchlistView.xaml`
  - `ViewModels/ReturnWatchViewModel.cs` & `Views/ReturnWatchView.xaml`
  - `ViewModels/ScanReceiveViewModel.cs` & `Views/ScanReceiveView.xaml`
  - `ViewModels/FileImportViewModel.cs` & `Views/FileImportView.xaml`
  - `ViewModels/FolderWatchViewModel.cs` & `Views/FolderWatchView.xaml`
  - `ViewModels/GoogleSheetViewModel.cs` & `Views/GoogleSheetView.xaml`
  - `ViewModels/SaleworkViewModel.cs` & `Views/SaleworkView.xaml`
  - `ViewModels/LegacyImportViewModel.cs` & `Views/LegacyImportView.xaml`

---

## 2. Danh sách đầy đủ các lệnh ghi đã khóa tập trung qua `WriteGate` (`MainViewModel.CanWrite`)

Tất cả các lệnh dưới đây dùng chung một điều kiện `CanWrite` (từ `ICloudLink.CanWrite` qua `WriteGate`), vừa chặn ở `CanExecute` (WPF tự làm mờ nút/mục menu + hiện tooltip lý do khi rê chuột qua control bị khóa), vừa chặn ở đầu hàm thực thi (kể cả phím tắt `Del`/`Enter`):

| ViewModel | Lệnh đã khóa | Mô tả |
|---|---|---|
| `MainViewModel` | `UndoDeleteCommand` (`UndoDeleteAsync`) | Hoàn tác xóa đơn / bỏ khỏi bảng Hàng hoàn về |
| `MainViewModel` | `RunArchiveSweepAsync` | Dời lưu trữ tự động (chặn chạy khi `!CanWrite`) |
| `ReturnOrdersViewModel` (áp dụng cho Shopee Hoàn hàng, Shopee Đơn hủy, TikTok Hoàn hàng, Bảng đơn mới) | `CreateNewOrderCommand` | Tạo đơn mới |
| `ReturnOrdersViewModel` | `ChangeStatusCommand` | Đổi trạng thái đơn |
| `ReturnOrdersViewModel` | `EditUserNoteCommand` | Sửa ghi chú của tôi |
| `ReturnOrdersViewModel` | `EditNoteCommand` | Sửa lý do hoàn/hủy |
| `ReturnOrdersViewModel` | `ToggleWatchlistCommand` | Bật/tắt theo dõi đơn |
| `ReturnOrdersViewModel` | `ArchiveRowCommand` | Dời sang kho lưu trữ |
| `ReturnOrdersViewModel` | `DeleteRowCommand` | Xóa mềm đơn |
| `ReturnOrdersViewModel` | `BulkMarkReceivedCommand` | Đánh dấu đã nhận hàng loạt |
| `ReturnOrdersViewModel` | `BulkArchiveCommand` | Dời lưu trữ hàng loạt |
| `ReturnOrdersViewModel` | `BulkWatchlistCommand` | Bật/tắt theo dõi hàng loạt |
| `ReturnOrdersViewModel` | `BulkDeleteCommand` | Xóa mềm hàng loạt |
| `ReturnOrdersViewModel` | `HandleDeleteKeyCommand` | Phím `Del` trên lưới |
| `ArchivedOrdersViewModel` | `UnarchiveRowCommand` | Trả về bảng chính |
| `ArchivedOrdersViewModel` | `DeleteRowCommand` | Xóa mềm từ kho lưu trữ |
| `ArchivedOrdersViewModel` | `HandleDeleteKeyCommand` | Phím `Del` trên lưới lưu trữ |
| `DeletedOrdersViewModel` | `RestoreRowCommand` | Khôi phục đơn đã xóa |
| `WatchlistViewModel` | `ChangeStatusCommand` | Đổi trạng thái trong Đang theo dõi |
| `WatchlistViewModel` | `EditWatchlistReasonCommand` | Sửa lý do theo dõi |
| `WatchlistViewModel` | `RemoveFromWatchlistCommand` | Bỏ theo dõi |
| `ReturnWatchViewModel` | `RemoveFromBoardCommand` & `HandleDeleteKeyAsync` | Bỏ đơn khỏi bảng Hàng hoàn về (đơn lẻ, nhiều dòng, phím `Del`) |
| `ScanReceiveViewModel` | `SubmitScanCommand` (+ khóa ô nhập `ScanCodeBox`), `PickPhotoCommand` | Quét nhận kiện bằng máy quét USB / Enter |
| `FileImportViewModel` | `ImportFilesCommand` (`CanImport`) | Nạp file Excel/CSV |
| `FolderWatchViewModel` | `RunNowCommand`, `RunExistingCommand` (`CanRunNow`) | Nạp ngay / nạp file đang có trong thư mục |
| `GoogleSheetViewModel` | `RunNowCommand` (`CanRunNow`) | Cập nhật ngay từ Google Sheet |
| `SaleworkViewModel` | `RunNowCommand` (`CanRunNow`) | Lấy ngay từ Salework |
| `LegacyImportViewModel` | `ExecuteImportCommand` (`CanExecuteImport`) | Nhập thật dữ liệu từ app cũ (Dry-run chỉ đọc vẫn cho chạy) |

**Các thao tác CHỈ XEM vẫn chạy bình thường khi khóa:** cuộn/chuyển trang, lọc sàn/loại đơn/trạng thái/bộ lọc nâng cao, tìm kiếm toàn cục (`Ctrl+K`) và tìm trong bảng, mở `Chi tiết đơn` (`OrderDetailViewModel` hoàn toàn chỉ đọc, không có lệnh ghi), xem ảnh, xem lịch sử sửa (`ShowHistoryCommand`), xuất Excel/CSV (`ExportVisibleCommand` / `ExportSelectedCommand`), chạy thử `Dry-run`.

---

## 3. Ghi chú về API ở `Data/` và `ICloudLink`

- Hợp đồng `Services/ICloudLink.cs` hiện có **đủ** cho toàn bộ yêu cầu T041 (trạng thái, `CanWrite`, `LastError`, `LastSyncAt`, `Quota`, `DeviceName`, `Role`, `BaseUrl`, `Changed`, `PairAsync`, `StartPairingAsync`, `SyncNowAsync`, `Unpair`). Không thiếu API ở `Data/` cho phạm vi T041.
- **Lưu ý tích hợp bản thật cho Claude:**
  - Khi thay `cloudLink: null` ở `App.xaml.cs` bằng bản `ICloudLink` thật, nếu chế độ cloud có lưu danh sách thiết bị (`GET /devices` / `DELETE /devices/{id}` cho `owner`) hoặc đổi/thu hồi từ xa ở phiếu sau thì mới cần mở rộng thêm API trên `ICloudLink`.
  - Khi `Quota.Level == "stop"`, hiện `ICloudLink.CanWrite` chỉ phụ thuộc `State` theo hợp đồng `ICloudLink.cs`; nếu phía cloud muốn khóa mềm nút ghi ở mức `stop` (trừ quét), cần thống nhất ở `ICloudLink` trong phiếu sau.
  - Lưu ý: `App.xaml.cs` hiện giữ `cloudLink: null` ở chế độ chạy thật (không `--demo`) để không khóa người dùng đang chạy SQLite cục bộ cho tới khi Claude nối bản cloud thật.

---

## 4. Kết quả biên dịch, test & ảnh chụp màn hình

- **Biên dịch:** `dotnet build v2/QuanLyHangHoan.sln --nologo` → **0 lỗi** (0 cảnh báo mới trong `App/` và `T041CloudStatusUiTests.cs`).
- **Test mới T041 (`T041CloudStatusUiTests`):** **7/7 đạt** (chạy riêng: 6 giây; chạy trong cụm `T026–T041`: 7/7 đạt).
- **Toàn bộ test không-UI (`Core` + `Data`):** **437/437 đạt** (23 giây).
- **Test UI cũ (`UiTests`):**
  - `T012UiPolishTests`: **8/8 đạt** (3 giây).
  - Cụm `T026–T041` (112 test): **111/112 đạt**; 1 test hiệu năng `T037TableFiltersTests.SortByColumn_50kRows_IsFast` bị quá ngưỡng thời gian trong lần chạy tải cao (5709 ms) và đã **đạt** khi chạy lại ở cụm kế tiếp.
  - Cụm `T003–T025` + `BackgroundBuild` + `ReturnOrdersViewModel` + `T037.SortByColumn_50kRows_IsFast` (184 test): **183/184 đạt**. 1 test cũ `T020SearchAndWatchlistUiTests.DemoMode_DoesNotTouchDisk_AndCapturesT020Screenshots` có hiện tượng chập chờn sẵn (đạt ở lần chạy trước, lỗi ở lần chạy sau tại dòng 413 do vòng `foreach` gọi `RemoveFromWatchlistCommand.Execute(row)` không chờ `ReloadAllDataAsync()` dùng `Task.Run`). Không sửa test cũ ngoài phạm vi T041.
- **Ảnh chụp màn hình tại `tasks/reports/T041-screens/`** (đã mở kiểm tra trực tiếp bằng mắt):
  1. `cloud-01-connected-table.png` — Trạng thái `Đã kết nối` (chấm xanh) trên bảng Shopee Hoàn hàng.
  2. `cloud-02-disconnected-banner-locked.png` — Trạng thái `MẤT KẾT NỐI` (nút đỏ nổi bật) + banner đỏ "chỉ xem" lúc `14:22` + nút `➕ Tạo đơn mới` bị khóa mờ.
  3. `cloud-03-status-popup.png` — Popup chi tiết trạng thái Cloud (thiết bị, vai trò, đồng bộ cuối, lỗi gần nhất, hạn mức hôm nay, nút "Đồng bộ ngay").
  4. `cloud-04-scan-locked.png` — Màn Quét nhận hàng bị khóa cả ô nhập lẫn nút nhận kiện khi mất kết nối.
  5. `cloud-05-settings-cloud-unpaired.png` — Cài đặt > Cloud ở trạng thái `Chưa ghép thiết bị` với biểu mẫu ghép và thông báo lỗi tiếng Việt khi nhập sai mã.
  6. `cloud-06-settings-cloud-owner-warn.png` — Cài đặt > Cloud vai trò `owner` + hạn mức màu vàng (`warn`, 76% / 73%) + badge `⚠ Hạn mức` trên thanh đầu.
  7. `cloud-07-settings-cloud-quota-stop.png` — Cài đặt > Cloud hạn mức màu đỏ (`stop`, 99% / 99%) + đúng câu dừng tới 07:00 sáng mai (giờ Việt Nam) + badge `⛔ Hết hạn mức`.
  8. `cloud-08-revoked-dark.png` — Trạng thái `Thiết bị đã bị thu hồi` ở giao diện tối (Dark theme).

---

## 5. Việc cần kiểm tay (chưa chạy thật với Cloudflare Worker)

- Chưa chạy với Cloudflare Worker / WebSocket / mạng thật (hiện dùng `FakeCloudLink` theo yêu cầu T041).
- Khi Claude nối `ICloudLink` thật, cần kiểm tay:
  1. Rút mạng/tắt Wi-Fi trên Windows → sau >20s chấm chuyển đỏ `MẤT KẾT NỐI`, banner hiện đúng giờ `HH:mm`, rê chuột lên nút/mục menu bị khóa hiện tooltip `"Mất kết nối cloud — chỉ xem được"`, quét USB không nhận đơn.
  2. Bật mạng lại → tự đồng bộ, banner biến mất, các nút mở khóa lại ngay.
  3. Ghép thiết bị thật bằng mã 6 số từ máy `owner` và kiểm tra đếm ngược 10 phút, quên ghép trên máy và ghép lại.
  4. Kiểm tra thu hồi thiết bị từ máy `owner` → máy bị thu hồi lập tức chuyển `Thiết bị đã bị thu hồi` và khóa ghi.

TRẠNG THÁI: XONG
