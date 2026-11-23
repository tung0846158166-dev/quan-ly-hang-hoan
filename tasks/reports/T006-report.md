# Báo cáo T006 - Tách giao diện theo sàn + Trang chủ hiện đại

- **Người thực hiện:** antigravity
- **Người kiểm tra:** claude
- **Phạm vi chỉnh sửa:** `v2/src/QuanLyHangHoan.App/` và `v2/tests/QuanLyHangHoan.Tests/UiTests/` (không sửa Core, Data, ImporterTests).

## 1. Quy tắc & Chuẩn hóa đã áp dụng
- **Chuẩn hóa hằng số (`Services/OrderConstants.cs`):**
  - `PlatformNames`: `Shopee = "Shopee"`, `TikTok = "TikTok"` (đã bỏ hoàn toàn `"TikTok Shop"` trong `App/`).
  - `OrderTypeNames`: `Cancelled = "Đơn hủy"`, `ReturnRefund = "Trả hàng/Hoàn tiền"`, `FailedDelivery = "Giao không thành công"`, `Lost = "THẤT THOÁT"`, `RefundOnly = "Chỉ hoàn tiền"`.
- **Quy tắc Shopee — Hoàn hàng (đề nghị chủ dự án xác nhận):**
  - `Shopee - Hoàn hàng` = mọi đơn `Platform == "Shopee"` KHÔNG phải `"Đơn hủy"` (gồm `"Trả hàng/Hoàn tiền"` và `"Giao không thành công"`).
  - `Shopee - Đơn hủy` = bộ lọc trên cùng nguồn đơn `IReturnOrderProvider` với `Platform == "Shopee" && OrderType == "Đơn hủy"`. Đã xóa `ICancelledOrderProvider`, `CancelledOrdersViewModel`, `CancelledOrdersView.xaml(.cs)`, và ngưỡng tạm `CancelledYellowDays/RedDays/CancelledPolicy` trong `AlertThresholds`.
  - `TikTok - Hoàn hàng` = toàn bộ đơn `Platform == "TikTok"` trong một bảng duy nhất, có cột `"Loại đơn"` và hàng chip lọc đủ 4 loại (`THẤT THOÁT`, `Trả hàng/Hoàn tiền`, `Giao không thành công`, `Chỉ hoàn tiền`).

## 2. Trang chủ (Dashboard) & Thư viện biểu đồ
- **Lựa chọn biểu đồ:** Tự vẽ bằng WPF vector (`ItemsControl` + `Border`/`UniformGrid` gắn với `Themes/AppStyles.xaml`) thay vì kéo thêm LiveCharts2/ScottPlot. Lý do: 0 phụ thuộc NuGet mới, khởi động tức thì, render sắc nét ở mọi DPI, dữ liệu biểu đồ tính thuần trong `HomeViewModel` nên test xUnit được 100%.
- **Các khối trên Trang chủ (`HomeViewModel` / `HomeView.xaml`):**
  - Hàng KPI toàn shop + tách riêng Shopee / TikTok: Đang chờ hàng về, Đã nhận hôm nay, Cảnh báo Vàng (>12h), Cảnh báo Đỏ (>24h), Giá trị chưa về (dùng `AlertThresholds.MainPolicy`).
  - Biểu đồ xu hướng 7 ngày (Shopee vs TikTok), cơ cấu loại đơn theo sàn, top 5 lý do hoàn/hủy.
  - Danh sách **"Cần xử lý ngay"** (Đỏ trước, Vàng sau): bấm vào dòng mở thẳng bảng đúng sàn (`shopee_return` / `shopee_cancelled` / `tiktok_return`) và chọn đúng dòng đó.
  - Khối **"Báo cáo nhanh"**: chọn khoảng ngày + sàn, xuất tóm tắt **CSV** (`IFileSaveService` / `WindowsFileSaveService`).
  - **Trạng thái trống (`IsEmpty`):** hiển thị hướng dẫn + nút CTA **"Nhập dữ liệu cũ ngay"** dẫn thẳng tới `legacy_import`.

## 3. Phần T003 & T004 đã hoàn thiện trong T006
- **Bảng đơn mới (`NewOrdersViewModel`/`View`):** chuẩn hóa nhãn `TikTok`.
- **Kho lưu trữ (`ArchivedOrdersViewModel`/`View`):** chuẩn hóa nhãn `TikTok` (chờ Claude nối cờ `IsArchived` trên bảng đơn chung).
- **Đã xóa (`DeletedOrdersViewModel`/`View`):** bỏ bộ lọc `"Bảng đơn hủy"`, thêm bộ lọc sàn (`Tất cả / Shopee / TikTok`), chuẩn hóa nhãn `TikTok`.
- **Thống kê & báo cáo (`StatisticsViewModel`/`View`):** bỏ `ICancelledOrderProvider`, tính thống nhất trên bảng đơn chung + kho lưu trữ, bổ sung lọc khoảng ngày, loại đơn, bảng cơ cấu loại đơn theo sàn và xuất CSV.
- **Nhập dữ liệu cũ (`ILegacyImporter`, `FakeLegacyImporter`, `LegacyImportViewModel`/`View`):** cập nhật báo cáo dry-run bỏ dòng Đơn hủy cũ, thêm khối `"Bỏ theo quyết định"`, số liệu chuẩn hóa sàn về `TikTok`, nhóm trùng khóa, ảnh nhúng base64; giữ tỉ lệ số liệu mẫu (2.307 chính + 2.195 lưu trữ + 166 đã xóa; 1.032 ảnh nhúng; 3 nhóm trùng khóa).

## 4. Điều hướng Sidebar (khởi động vào Trang chủ)
`Trang chủ` (mặc định) → `SHOPEE` (`Hoàn hàng`, `Đơn hủy`) → `TIKTOK` (`Hoàn hàng`) → `CHUNG` (`Bảng đơn mới`, `Kho lưu trữ`, `Đã xóa`, `Thống kê & báo cáo`, `Nhập dữ liệu cũ`).

## 5. Kết quả kiểm chứng
- `dotnet build v2\QuanLyHangHoan.sln`: **0 lỗi, 0 cảnh báo**.
- `dotnet test v2\QuanLyHangHoan.sln`: **65/65 test PASS** (0 failed, 0 skipped), gồm bộ test mới `UiTests/T006PlatformSplitTests.cs`.
- `Select-String -Pattern "TikTok Shop"` trên `v2\src\QuanLyHangHoan.App` (trừ `bin/`/`obj/`): **rỗng (0 kết quả)**.
- Ảnh chụp màn hình (1360x820) tại `tasks/reports/T006-screens/`:
  - `01-trang-chu.png`
  - `02-shopee-hoan-hang.png`
  - `03-shopee-don-huy.png`
  - `04-tiktok-hoan-hang.png`
  - `05-trang-chu-trong.png`

## 6. Điểm chưa làm / Chờ nối tiếp
- Giao diện hiện dùng dữ liệu giả qua `IReturnOrderProvider`, `IArchivedOrderProvider`, `INewOrderProvider`, `IDeletedOrderProvider`, `ILegacyImporter` (Claude sẽ nối `Data` / SQLite ở bước tích hợp).
- Hỗ trợ theme Sáng/Tối (không bắt buộc trong T006) chưa bật; hiện dùng giao diện Sáng + Sidebar tối thống nhất theo `Themes/AppStyles.xaml`.

TRẠNG THÁI: XONG
