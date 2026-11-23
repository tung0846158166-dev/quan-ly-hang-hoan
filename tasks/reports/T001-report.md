# Báo cáo T001 - Kiểm kê giao diện app cũ

## 1. Đã làm gì
- Đọc đầy đủ các tài liệu bối cảnh: `AGENTS.md`, `PLAN.md`, `TASKS.md`, `tasks/README.md`, `tasks/T001-ui-inventory.md`.
- Chỉ đọc mã nguồn (không cài gói, không chạy app, không đọc dữ liệu thật trong `Documents\QuanLyHangHoan` hay OneDrive, không sửa bất kỳ file code gốc nào):
  - Đọc toàn bộ phần CSS & cấu trúc HTML tĩnh (`L1–L3056` và `L22400–L22871`) của `Bang_Excel_Quan_Ly_Hang_Hoan_Tu_Dong_Thu_Nho_Anh.html` (22.871 dòng).
  - Lập chỉ mục toàn bộ 663 hàm JS và đọc đối chiếu trực tiếp các hàm dựng giao diện động (`buildAppSidebar`, `renderAll`, `renderOrderItemsDrawer`, `recalcTotalsAndSummary`, `renderReturnWatchTable`, `renderArchivedOrdersTable`, `renderNewOrdersTable`, `renderCancelledTable`, `renderQuickCreateTable`, `renderWatchlistTable`, `renderDeletedOrdersTable`, `renderCatalogTable`, `renderShopeeDbTable`, `openClaimModal`, `renderStatsModal`, `openAnalyticsDashboardModal`, `renderBatchSessionList`, `renderNotificationList`, `renderDeadlinePanel`), bộ lọc/tìm kiếm/phân trang/sắp xếp, quy tắc cảnh báo & khóa ô, phím tắt/âm thanh/thông báo, và các hàm đọc/ghi/nhập/xuất/đồng bộ.
  - Đối chiếu thêm `preload.js` để liệt kê đủ 30 phương thức `window.desktopDiskAPI`.
- Lập bản kiểm kê đủ 9 mục yêu cầu tại `docs/ui-inventory.md`, có dẫn chiếu `id` và số dòng cho từng mục và phủ đủ toàn bộ màn hình/tab/bảng (gồm Bảng chính, Bảng Tổng kết phân loại SP-Màu-Size, Bảng Hàng Hoàn Về, Kho lưu trữ 24h, Bảng đơn mới, Bảng đơn hủy, Phân tích, Thống kê, Tạo đơn nhanh, Ca quét, Theo dõi đặc biệt, Đơn đã xóa, Danh mục SP, Nạp 2 sàn, Khiếu nại quá hạn, Nhật ký bảo mật, Google Sheet, Folder Watch, Salework Auto/Diag, Camera/Điện thoại).
- Cập nhật `TASKS.md` với trạng thái hoàn thành T001 chờ Claude kiểm tra.

## 2. File đã tạo / cập nhật
- Tạo mới: `docs/ui-inventory.md`
- Tạo mới: `tasks/reports/T001-report.md`
- Cập nhật: `TASKS.md` (thêm dòng `- [x] T001 ui-inventory (antigravity) - chờ claude kiểm tra`)

## 3. Những mục bỏ sót hoặc chưa chắc
- **Không bỏ sót mục nào** trong 9 mục yêu cầu của phiếu `T001`.
- **Các điểm chưa chắc / bất nhất trong code gốc** đã ghi rõ ở Mục 9 của `docs/ui-inventory.md` (có gắn nhãn `(suy đoán)` cho các suy luận), nổi bật gồm:
  1. Bất nhất trường ghi chú `myNote` (bảng chính & xuất Excel) vs `userNote` (Kho lưu trữ 24h & converter từ Hộp đơn mới / Tạo đơn nhanh).
  2. Modal Camera PC (`#pcCameraScannerModal`, `openPcCameraScannerModal`) và `duplicateOrder` tồn tại trong code nhưng không còn nút gọi trên giao diện.
  3. Một số lời gọi hàm / cấu hình lệch hoặc không tồn tại định nghĩa trong file: `openPhotoZoomDirect` (`L17052`), `updatePlatformFilterUI` (`L8666`), `exportJsonFile` (`L10472`), `#btnViewOrdersTab`/`Summary`/`Both` (`L7941–L7943`), và giá trị `brewpage`/`beacon` của `#mobileServerSelect` (`L2105–L2109`) không khớp khóa `MOBILE_SERVERS` (`L14719–L14723`).
  4. Cột & bộ lọc `preOrder` đã được gỡ khỏi UI chính nhưng còn code dựng sẵn (`preOrderCellHtml`, `currentPreOrderFilter`).
  5. Phím điều hướng kiểu Excel trong `handleCellKeyDown` (`L13968`) hiện không được gắn vào `renderCellInput` (đang dùng `handleCellEditKeyDown`).
  6. Sự khác nhau về quy tắc tính quá hạn giữa Bảng chính (v1.9.68: chỉ đơn đã về 12h/24h hoặc Hủy sau chuẩn bị 48h/72h), Bảng Đơn Hủy (`DEADLINE_DAYS = 3`), và Modal Khiếu Nại Quá Hạn (`rawElapsedHours >= 72`).
  7. Phạm vi kiểm kê chỉ tập trung vào giao diện Desktop (`Bang_Excel_...html`); chưa kiểm kê chi tiết giao diện trang quét điện thoại `mobile_scanner.html` vì ngoài phạm vi phiếu T001.

## 4. Thời gian ước tính
- ~9 phút (khảo sát cấu trúc 22.871 dòng, đối chiếu chi tiết các hàm UI/dữ liệu, viết báo cáo kiểm kê).

TRẠNG THÁI: XONG
