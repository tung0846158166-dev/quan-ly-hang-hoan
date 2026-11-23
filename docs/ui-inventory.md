# Kiểm Kê Giao Diện App Cũ (v1.9.75)

- **Nguồn phân tích**: `Bang_Excel_Quan_Ly_Hang_Hoan_Tu_Dong_Thu_Nho_Anh.html` (22.871 dòng, ~663 hàm JS) và đối chiếu cầu nối IPC tại `preload.js` (34 dòng).
- **Cấu trúc file HTML gốc**:
  - `<head>` & CSS (`L3–L421`): nạp CDN Tailwind, SheetJS (`xlsx 0.20.1`), `mqtt.min.js`, `html5-qrcode 2.3.8`, `tesseract.js 5`, `qrcode-generator 1.4.4` (có kèm bản nhúng offline `L20–L30`).
  - `<body>` phần tĩnh (`L422–L3055`): thanh tiêu đề, các thanh lọc, dải KPI, bảng đơn chính, bảng tổng kết phân loại, thanh dock đáy và nhóm modal chính.
  - `<script>` chính (`L3057–L22402`): toàn bộ logic nghiệp vụ, lưu trữ, đồng bộ, và dựng DOM động (sidebar trượt `#appSidebar`, thanh phân trang `#tableFooterPager`, các bảng con, toast, template HTML kiểm hàng xuất ra tại `L21084–L21595`).
  - Phần HTML cuối file (`L22404–L22871`): `#smartSuggestBox`, `<datalist>`, `#bulkActionBar`, và nhóm modal thêm sau (`#sheetSyncModal`, `#folderWatchModal`, `#saleworkAutoModal`, `#saleworkDiagModal`, `#statsModal`, `#cancelledOrdersModal`, `#cancelledCreateModal`, `#archivedOrdersModal`).

> **Lưu ý bố cục động lúc khởi chạy (`buildAppSidebar`, `L18054–L18161`)**:
> Khi tải trang, JS tự động tái cấu trúc DOM ban đầu:
> 1. Tạo nút menu `☰` (`#btnAppMenu`) và sidebar trượt đè `#appSidebar` + nền mờ `#appDrawerBackdrop`.
> 2. Di chuyển các nút mở bảng (`#btnOpenArchivedOrders`, `#btnOpenCancelledOrders`, `#btnNewOrdersHeader`, `#btnOpenWatchlistModal`, `#btnOpenStats`, nút Phân tích) vào mục **Bảng dữ liệu** (`#sbNav`); di chuyển các nút thao tác (`#btnAddOrder`, `#btnQuickCreateOrders`, `#btnImportShopeeRow15`, `#btnImportTikTokRow15`, `#btnPhoneScanner`, `#btnSessionScanHeader`, `#btnSafeStorageHeader`) và tạo mới 4 nút (`#btnSheetSync`, `#btnFolderWatch`, `#btnSaleworkDiag`, `#btnSaleworkAuto`) vào mục **Thao tác** (`#sbTools`); di chuyển `#kpiStrip` vào mục **Tổng quan** (`#sbKpi`).
> 3. Di chuyển ô tìm kiếm `#searchInput` lên thanh tiêu đề trên cùng; gộp Row 4 vào Row 3 thành 1 hàng cuộn ngang; tạo thanh phân trang `#tableFooterPager` ngay dưới bảng chính (`#mainTableScrollContainer`).
> 4. Mặc định khi mở app (`L7993`), gọi `switchMainView('orders')` nên **chỉ hiện Bảng Đơn Chính**, ẩn Bảng Tổng Kết Phân Loại (`#categorySummaryWrapper`) cho đến khi người dùng bấm nút ở thanh dock đáy.

---

## 1. Danh sách màn hình / tab / bảng

| STT | Tên hiển thị | Mục đích | Nơi định nghĩa (ID / Dòng) |
|---|---|---|---|
| 1 | **Màn chờ khởi động** (`Đang mở ứng dụng…`) | Che giao diện trước khi Tailwind + dữ liệu sẵn sàng (tự tắt tối đa sau 10s hoặc qua `revealAppWhenStyled`) | CSS `html.app-booting` (`L8–L13`), JS `L8068–L8085` |
| 2 | **Thanh đầu trang (Top Bar)** | Nút mở menu `☰`, tiêu đề app, badge phiên bản, ô tìm kiếm toàn cục, nút Thông Báo, Menu Nhập/Xuất, Menu Hệ Thống, nút Khóa/Mở Khóa | `L427–L865`; tái cấu trúc bởi `buildAppSidebar` (`L18107–L18117`) |
| 3 | **Menu trượt bên trái (Sidebar Drawer)** | Điều hướng tới các bảng dữ liệu, công cụ nạp/quét/tự động hóa và dải 5 KPI tổng quan | Tạo động `#appSidebar`, `#appDrawerBackdrop`, `#btnAppMenu` (`L18048–L18114`); CSS `L363–L383` |
| 4 | **Dải số liệu tổng quan (KPI Strip)** | Hiển thị 5 chỉ số nhanh: Đang chờ nhận (`#kpiPending`), Về kho hôm nay (`#kpiRecvToday`), Báo đỏ (`#kpiOverdue`), Báo vàng (`#kpiSoon`), Đơn hủy đang chờ (`#kpiCancelPending`) | `#kpiStrip` (`L1201–L1207`), chuyển vào `#sbKpi` (`L18090`), tính bởi `renderKpiStrip` (`L18924–L18951`) |
| 5 | **Bảng Đơn Hàng Chính (Main Spreadsheet Table)** | Quản lý toàn bộ đơn hoàn hiện hành (1 đơn/kiện = 1 dòng), sửa trực tiếp từng ô, chọn hàng loạt, mở chi tiết SP | `#mainTableScrollContainer`, `#orderTableBody` (`L1208–L1248`), render tại `renderAll` (`L11902–L12589`) |
| 6 | **Ngăn kéo Chi Tiết Sản Phẩm & Đối Soát (Accordion Drawer)** | Mở rộng ngay dưới 1 dòng đơn hàng: xem thời gian nạp file/file nguồn, sửa danh sách nhiều SP trong đơn, xem 4 khối đối soát gốc từ Excel, nút Khiếu nại 1 đơn & In tem Re-stock | `#drawer_row_<idx>` (`L12492–L12502`), `toggleOrderItemsDrawer` (`L8533–L8555`), `renderOrderItemsDrawer` (`L12591–L12786`) |
| 7 | **Thanh chuyển trang dưới bảng (Footer Pager)** | Hiển thị số đơn đang xem, chọn số dòng/trang, nút Trước/Sau, ô nhảy thẳng tới trang | Tạo động `#tableFooterPager` (`L18134–L18145`) từ `#tablePaginationBar` (`L1108–L1126`) và `#gotoPageInput` (`L1069–L1076`) |
| 8 | **Bảng Tổng Kết Thống Kê Chi Tiết Theo SP - Màu Sắc - Size** | Gom nhóm 300+ SKU (gồm cả đơn trong Kho lưu trữ), đối soát SL đã về kho với SL kế toán nhập kho (luôn mở khóa) | `#categorySummaryWrapper`, `#summaryTableBody` (`L1251–L1354`), render trong `recalcTotalsAndSummary` (`L12875–L13353`) |
| 9 | **Thanh Dock Tổng Kết Đáy Màn Hình** | Cố định dưới đáy: hiển thị tổng đơn, tổng SP, đã về, chưa về, không cần nhận, tổng tiền theo bộ lọc hiện hành + nút chuyển nhanh giữa Bảng Đơn Hàng & Bảng Thống Kê Phân Loại | `#stickyBottomDock` (`L1358–L1380`), cập nhật tại `L13128–L13140` |
| 10 | **🚚 Bảng Hàng Hoàn Về (Return Watch)** | Tự động gom đơn khách yêu cầu hoàn đã chuyển sang trạng thái `"Đã đồng ý"`, theo dõi 4 chặng đến khi quét nhận kho | Nút mở `#btnReturnWatchHeader` (`L922–L927`) & menu nhanh (`L510`); modal `#returnWatchModal`, `#returnWatchBody` (`L1478–L1492`), JS `L21812–L21908` |
| 11 | **📦 Kho lưu trữ (24h) (Archived Orders)** | Lưu trữ tự động các đơn đã nhận về kho >= 24 giờ (quét mỗi 10 phút) để bảng chính luôn nhẹ; cho phép lọc, khôi phục, xuất Excel, sao lưu, xóa | Nút mở `#btnOpenArchivedOrders` (`L940–L945`) & menu nhanh (`L511`); modal `#archivedOrdersModal`, `#archivedTableBody` (`L22696–L22868`), JS `L19533–L20158` |
| 12 | **📥 Bảng đơn mới (New Orders Inbox)** | Hộp chờ duyệt các đơn mới quét bằng điện thoại ở chế độ "Tạo đơn mới" trước khi đẩy ra bảng chính | Nút mở `#btnNewOrdersHeader` (`L931–L936`) & menu nhanh (`L512`); modal `#newOrdersModal`, `#newOrdersTableBody` (`L2261–L2347`), JS `L16970–L17338` |
| 13 | **🚫 Bảng đơn hủy (Cancelled Orders)** | Quản lý riêng đơn hủy (nạp Excel riêng, tạo tay, nhận quét từ điện thoại, đánh dấu đã nhận/chưa nhận hàng loạt) | Nút mở `#btnOpenCancelledOrders` (`L953–L958`) & menu nhanh (`L513`); modal `#cancelledOrdersModal`, `#cancelledTableBody` (`L22608–L22675`) + form `#cancelledCreateModal` (`L22677–L22694`), JS `L17776–L18045`, `L19165–L19408` |
| 14 | **📊 Phân tích (Analytics Dashboard)** | Đối soát tài chính (tiền hoàn, ship giao, ship hoàn, bồi thường), Top 10 mẫu SP bị hoàn nhiều nhất, Top 5 lý do hoàn | Nút mở `L911–L915` & menu nhanh (`L514`); modal `#analyticsDashboardModal`, `#analyticsDashboardBody` (`L2818–L2839`), JS `L20625–L20751` |
| 15 | **📊 Thống kê (Stats Modal)** | Thống kê đơn hoàn & đơn hủy theo Ngày/Tuần/Tháng và Theo Sàn, có dòng đối chiếu khớp tab bảng chính; bấm vào từng con số để xem danh sách chi tiết (`#statsDetail`) | Nút mở `#btnOpenStats` (`L947–L951`) & menu nhanh (`L515`); modal `#statsModal`, `#statsModalBody` (`L22586–L22606`), JS `L18953–L19163` |
| 16 | **⚡ Tạo đơn nhanh (Quick Create Orders)** | Dán danh sách mã đơn/MVĐ nhiều dòng, tự tách & kiểm tra trùng với bảng chính, gán sàn/loại đơn hàng loạt rồi đẩy ra bảng chính | Nút mở `#btnQuickCreateOrders` (`L960–L964`) & menu nhanh (`L516`); modal `#quickCreateOrdersModal`, `#quickCreateOrdersTableBody` (`L2352–L2484`), JS `L17357–L17775` |
| 17 | **📷 Ca quét (Batch Session Scanner)** | Đếm kiện quét trong phiên làm việc với shipper, liệt kê từng mã + cảnh báo trùng, sao chép biên bản giao nhận, đặt lại ca mới | Nút mở `#btnSessionScanHeader` (`L966–L970`) & menu nhanh (`L517`); modal `#batchSessionModal`, `#sessionScannedListContainer` (`L2842–L2882`), JS `L20365–L20464` |
| 18 | **⭐ Bảng Theo Dõi Đơn Hàng Đặc Biệt (Watchlist)** | Tập hợp các đơn được đánh dấu sao để theo dõi hạn xử lý, lý do theo dõi, xuất Excel riêng, dọn đơn đã về | Nút mở `#btnOpenWatchlistModal` (`L448–L454`); modal `#watchlistModal`, `#watchlistModalTableBody` (`L1495–L1590`), JS `L21723–L21810`, `L21989–L22399` |
| 19 | **🗑️ Đơn Đã Xóa (Deleted Orders)** | Danh sách đơn đã xóa (chống bị nạp lại từ sàn/Google Sheet), cho phép khôi phục về đúng bảng cũ hoặc "Cho phép nạp lại" | Menu Hệ Thống (`L747–L756`); modal `#deletedOrdersModal`, `#deletedOrdersBody` (`L1461–L1475`), JS `L4197–L4226`, `L21910–L21987` |
| 20 | **📦 Danh Mục SP & Size Của Shop (Catalog)** | Quản lý danh sách SP kèm danh sách Màu & Size riêng của từng SP để gợi ý thông minh (`#smartSuggestBox`) và hỗ trợ OCR | Menu Hệ Thống (`L734–L745`); modal `#catalogModal`, `#catalogTableBody` (`L1631–L1705`), JS `L7385–L7617` |
| 21 | **📥 Cửa sổ Nạp Đơn 2 Sàn Shopee & TikTok/Salework** | Gồm 2 tab: Cơ chế 1 (Shopee - 3 loại file) & Cơ chế 2 (TikTok/Salework - chọn cột giá, lọc hủy chưa gửi) + bảng xem trước bộ nhớ tra cứu `shopeeOrderDb` | Nút `#btnImportShopeeRow15` (`L478`), `#btnImportTikTokRow15` (`L483`), menu Nhập/Xuất (`L534–L565`); modal `#shopeeDbModal`, `#shopeeDbTableBody` (`L2486–L2722`), JS `L5316–L7265` |
| 22 | **🚨 Khiếu Nại Sàn Quá Hạn (Claim Overdue)** | Tổng hợp đơn quá 72 giờ chưa về, tự soạn mẫu tin nhắn khiếu nại, xuất Excel khiếu nại, đổi nhanh sang "Bị mất" | Menu Nhập/Xuất (`L595–L606`); modal `#claimOverdueModal`, `#claimTableBody` (`L1957–L2032`), JS `L3413–L3565` |
| 23 | **🛡️ Nhật Ký Bảo Mật (Audit Log)** | Xem lịch sử thao tác nhạy cảm (mở khóa, khóa ô, xóa đơn, đổi mật khẩu, nhập sai mật khẩu, chặn DevTools) — yêu cầu mật khẩu Chủ Shop | Menu Hệ Thống (`L769–L780`); modal `#auditLogModal`, `#auditLogTableBody` (`L1857–L1885`), JS `L8912–L8926`, `L9099–L9133` |
| 24 | **🌐 File HTML Kiểm Hàng Tương Tác (xuất rời)** | Trang HTML độc lập sinh từ đơn đã chọn (`bulkExportSelectedToHtml`) có ô tích kiểm từng SP, bảo vệ sửa ô bằng mật khẩu `9999`, lưu `localStorage` | Template nhúng trong `generateInteractiveChecklistHtml` (`L21024–L21597`) |

---

## 2. Chi tiết từng bảng (Cột, Trường dữ liệu, Sửa, Sắp xếp, Phân trang)

### 2.1. Bảng Đơn Hàng Chính (`#mainTableScrollContainer`, `#orderTableBody` — `L1208–L1248`, `L11902–L12589`)
- **Cột cố định ngang (sticky)**: Cột 1 (`48px`) và Cột 2 (`40px`) cố định bên trái khi cuộn ngang (`L211–L267`).
- **Tương tác ô văn bản (`renderCellInput`, `L11860–L11899`)**:
  - Bấm 1 lần (`handleCellClick`, `L11446–L11467`): mở popover xem đầy đủ `#cellDetailPopover` (riêng cột `orderId` bấm 1 lần sẽ **sao chép ngay** mã đơn).
  - Nhấp đúp (`handleCellDblClick`, `L11469–L11480`): bật `<input>` chỉnh sửa trực tiếp (Enter lưu & ghi lịch sử ô, Esc hủy).
  - Chuột phải (`handleCellContextMenu`, `L9274`): mở `#cellActionModal` để khóa/mở khóa riêng ô đó & xem lịch sử chỉnh sửa ô.
- **Sắp xếp**:
  - Bấm vào tiêu đề 4 cột có biểu tượng `⇅` (`toggleDateSort`, `L8638–L8648`, xoay vòng `desc → asc → default`) hoặc chọn tại `#dateSortSelect` (`L1086–L1098`): `Mặc định (STT)`, `deadline` (ưu tiên `danger > warning > safe > done > none`, sau đó theo `elapsedHours`), `recvDate`, `returnDate`, `shipDate`.
- **Phân trang**:
  - Mặc định `pageSize = 50` (`L8000`); tùy chọn `#pageSizeSelect`: `30`, `50`, `100`, `200`, `0` (Tất cả) (`L1110–L1116`). Nút `◀`/`▶` (`changeTablePage`, `L8044`), ô nhảy trang `#gotoPageInput` (`goToTablePage`, `L8031`).

| # | Tên cột (`<thead>` L1212–L1244) | Trường dữ liệu | Cho phép sửa? | Ghi chú hiển thị |
|---|---|---|---|---|
| 1 | Checkbox chọn tất cả (`#chkSelectAllOrders`) | `selectedOrdersSet` + `order.isWatchlist` | Có (checkbox chọn hàng loạt + nút sao `☆`/`⭐` bật/tắt theo dõi) | Sticky trái `0px`, ẩn khi in |
| 2 | `STT` | `displayIdx + 1` & `#${orderIdx + 1}` | Không | Sticky trái `48px`; hiển thị thêm badge cảnh báo `🚨 Quá 24h` / `🚨 Quá 3 ngày` / `⚠️ 12h` / `⚠️ 2 ngày` (`L12082–L12087`) |
| 3 | `KQ Tìm` | Kết quả `isOrderMatch(order, kw)` | Không | Hiện `🔍 KHỚP` khi khớp từ khóa tìm kiếm |
| 4 | `Ngày Gửi Đi` *(sắp xếp được)* | `order.shipDate` (qua `resolveOrderShipDate`) | Có (nhấp đúp, tự chuẩn hóa ngày `handleDateBlur`) | Chế độ xem chỉ hiện phần ngày (`split(/\s+/)[0]`), nhấp đúp/popover hiện đủ ngày giờ (`L12431`) |
| 5 | `Ngày Hoàn / Hủy` *(sắp xếp được)* | `order.returnDate` | Có (nhấp đúp, `handleDateBlur`) | Dùng để tính hạn xử lý nếu chưa có thời gian hàng về |
| 6 | `Hạn Cần Xử Lý` *(sắp xếp được)* | `order.deadline` + tính từ `getOrderReturnElapsedInfo(order)` | Có (bấm 1 lần mở `promptEditDeadline`, `L12788`) | Hiện badge `✅ Đã nhận`, `⚪ Không cần nhận`, `🚨 BÁO ĐỎ`, `⚠️ BÁO VÀNG`, `⏳ An toàn`, hoặc `+ Thêm hạn`; kèm dòng `Hạn sàn` nếu có |
| 7 | `Ngày & Giờ Về Kho` *(sắp xếp được)* | `order.recvDate` | Có (nhấp đúp, `handleDateBlur`) | Tự điền khi quét nhận hoặc đổi trạng thái sang `Hàng đã trả` |
| 8 | `Sàn` | `order.platform` (qua `getOrderPlatform`) | Có (`<select>` ẩn phủ lên chấm màu, `updateOrderPlatform`) | Chấm tròn màu: Cam (`Shopee`), Đen (`TikTok Shop`), Xanh (`Lazada`), Xám (`Khác`) |
| 9 | `Loại (theo sàn)` | `order.orderType` + `order.sheetType` + `order.isPartialReturn` | Có (`<select>` cho `orderType`: `Giao không thành công`, `Trả hàng/Hoàn tiền`, `Đơn hủy`, `THẤT THU`) | Dòng nhỏ bên dưới hiện `order.sheetType` (chữ Loại từ sàn/Google Sheet) và badge `⚡ Hoàn 1 phần` |
| 10 | `Trạng thái kiện` | `order.platformStatus` (fallback `rawDetails.returnStatus` / `returnShippingStatus`) + `order.returnedAt` | Có (nhấp đúp mở `promptEditPlatformStatus`, `L16380`) | Nếu bắt đầu bằng `"Đã về"`, hiện thêm dòng thời gian về `order.returnedAt` / `rawDetails.returnSuccessTime` (`L12209–L12211`) |
| 11 | `Mã Đơn Hàng` | `order.orderId` | Có (nhấp đúp sửa; bấm 1 lần tự copy nhanh) | Font mono đậm |
| 12 | `Mã Vận Đơn` | `order.returnTrack`, `order.forwardTrack`, `order.track` | **Có điều kiện**: chỉ hiện ô nhập sửa được (`renderCellInput`) khi **cả** `returnTrack` và `forwardTrack` đều trống (`L12350–L12369`); nếu đã có mã chỉ hiện thẻ bấm-để-copy (vẫn chuột phải khóa ô được) | Ưu tiên hiện `↩️ Hoàn` (xanh lá) trên `🚚 Giao` (xám/xanh dương) |
| 13 | `Đơn Vị Vận Chuyển` | `order.carrier` | Có (`<select>` theo `CARRIER_OPTIONS`, `L8740–L8751`) | Tự nhận diện theo mã vận đơn (`detectCarrierByTrackingCode`) |
| 14 | `Sản Phẩm & Phân Loại` | `order.items[]` (`product`, `color`, `size`, `qty`) | Sửa trong Accordion khi bấm `👁️ Chi tiết (N)` | Hiện tóm tắt SP đầu tiên `+ N món` + badge `⚡ Hoàn 1 phần` |
| 15 | `Tổng SL` | `sum(order.items[].qty)` | Không (tự cộng từ `items`) | Thêm chữ `(1 phần)` nếu `isPartialReturn` |
| 16 | `Nhận Tại Kho` | `order.status` | Có (`<select>`: `Hàng đã trả`, `Chưa trả`, `Không cần nhận`, `Bị mất`; gọi `updateStatus`) | Màu nền theo trạng thái (xem mục 6) |
| 17 | `Tổng Giá Trị` | `order.price` | Có (nhấp đúp sửa, `updateOrderField`) | Định dạng tiền Việt (`formatMoney`) |
| 18 | `Ảnh Đơn Hàng` | `order.photo` / `photoMemoryCache.get(order.photoId)` | Có: bấm 1 lần phóng to hoặc chọn file; nhấp đúp thay ảnh; focus + `Ctrl+V` dán ảnh; nút `⚡` chạy OCR | Nén tự động qua `compressAndSaveImage` (`L11062`) |
| 19 | `Lý Do Hoàn / Hủy` | `order.note` | Có (nhấp đúp sửa) | Popover khi bấm 1 lần |
| 20 | `Ghi Chú Người Mua` | `order.buyerNote` | Có (nhấp đúp sửa) | Popover khi bấm 1 lần |
| 21 | `Ghi Chú Của Tôi` | `order.myNote` | Có (nhấp đúp sửa) | Popover khi bấm 1 lần |
| 22 | `Thao Tác` | — | — | Nút `👁️` (`toggleOrderItemsDrawer`) và `✕` (`deleteOrder`) |

> **Lưu ý về cột `preOrder`**: Hàm `renderAll` có dựng sẵn biến `preOrderCellHtml` (`L12213–L12232`) nhưng **không đưa vào `<tr>` và không có `<th>`**; thông tin `order.preOrder` hiện chỉ hiển thị trong Accordion (Cột 3 Tài chính, `L12726`).

#### Bảng con Sản Phẩm trong Accordion (`renderOrderItemsDrawer`, `L12591–L12673`)
- **Không sắp xếp, không phân trang**.
- **6 cột**: `#` (`itemIdx + 1`), `Tên Sản Phẩm` (`it.product` — sửa được + `#smartSuggestBox`), `Màu Sắc` (`it.color` — sửa được + gợi ý theo đúng SP), `Size` (`it.size` — sửa được + gợi ý theo đúng SP), `SL` (`it.qty`, `type="number"` — sửa được), `Thao Tác` (nút `✕ Xóa` `removeProductFromOrder` khi `items.length > 1`).
- Kèm 4 thẻ đọc-chỉ (bấm copy mã) hiển thị `order.rawDetails`: (1) Đơn hàng & Khiếu nại (`claimId`, `buyerUsername`, `orderDate`, `claimTime`, `sellerResponseDue`), (2) Vận chuyển 2 chiều (`returnTrack`, `returnCarrier`, `returnShippingStatus`, `returnSuccessTime`, `forwardTrack`, `forwardCarrier`, `deliveryAddress`), (3) Tài chính & Phương án (`preOrder`, `returnMethod`, `returnStatus`, `refundAmount`, `totalValue`, `revenue`, `forwardShippingFee`, `returnShippingFee`, `paymentMethod`), (4) Lý do & Ghi chú (`returnReason`, `buyerReturnNote`, `buyerOrderNote`, `sellerNote`).

---

### 2.2. Bảng Tổng Kết Phân Loại SP - Màu Sắc - Size (`#categorySummaryWrapper`, `#summaryTableBody` — `L1251–L1354`, `L12893–L13353`)
- **Nguồn dữ liệu**: Gom từ **cả `orders` (bảng chính) + `archivedOrders` (kho lưu trữ)** theo bộ lọc Tuần/Sàn/Loại đơn/MVĐ/Trạng thái kiện hiện hành (nhưng **không** áp dụng lọc ngày/giờ và từ khóa tìm kiếm chính; xem `L12900–L12928`).
- **Sắp xếp / Phân trang**: Không có nút sắp xếp (hiển thị theo thứ tự xuất hiện trong `Map`), không phân trang. Có dòng chân bảng `<tfoot>` dính đáy (`L1338–L1351`).

| # | Tên cột (`L1322–L1334`) | Trường / Cách tính | Sửa được? |
|---|---|---|---|
| 1 | `STT` | `visibleCount` | Không |
| 2 | `Tên Sản Phẩm` | `st.name` (`it.product`) | Không |
| 3 | `Màu Sắc` | `st.color` (`it.color`) | Không |
| 4 | `Size` | `st.size` (`it.size`) | Không |
| 5 | `Số Đơn` | `st.orders` (số đơn chứa SKU này) | Không |
| 6 | `Tổng SL Hoàn` | `st.totalQty` | Không |
| 7 | `🟢 Đã Về Kho` | `st.returnedQty` (từ đơn `isOrderReceived`) | Không |
| 8 | `Nhập kho` | `accountantStock[skuKey]` | **Có (LUÔN MỞ KHÓA `data-always-unlocked="true"` kể cả khi bảng khóa!)** — ô `<input type="number">` + nút điền nhanh `= <returnedQty>` (`quickFillAccountantRestock`) |
| 9 | `⚖️ Chênh Lệch` | `st.returnedQty - accQty` | Không (`+N chưa nhập` đỏ / `0 (Đã khớp)` xanh) |
| 10 | `🟠 Chưa Về` | `st.unreturnedQty` | Không |
| 11 | `🔴 Bị Mất` | `st.lostQty` | Không |
| 12 | `💸 Giá Trị Mất` | `st.lostVal` (phân bổ theo tỷ lệ SL trong đơn bị mất) | Không |
| 13 | `Kết Luận Đối Soát` | Tự suy luận: `⚠️ KHIẾU NẠI ĐỀN BÙ` / `⏳ Chờ hoàn về` / `✅ Đã khớp kho` | Không |

---

### 2.3. 🚚 Bảng Hàng Hoàn Về (`#returnWatchModal`, `#returnWatchBody` — `L1478–L1492`, `L21815–L21908`)
- **Điều kiện tự động vào bảng**: `syncReturnWatchFlags()` (`L21831–L21842`) tự gắn `o.rwSince = Date.now()` khi `o.platformStatus` khớp `/(đồng|dong)\s*(ý|y)|agreed/i` và đơn đang chờ.
- **Sắp xếp**: Mặc định tăng dần theo giai đoạn (`stage` 1→4: `Đã đồng ý` → `Đang về` → `Đã về shop` → `Đã nhận kho`), cùng giai đoạn xếp theo `o.rwSince` tăng dần (`L21883`).
- **Phân trang**: Không phân trang, giới hạn hiển thị tối đa `1500` dòng (`L21886`).
- **10 cột** (không sửa trực tiếp ô): `#`, `Mã đơn` (`o.orderId`), `Sàn` (`getOrderPlatform(o)`), `Mã vận đơn` (`o.returnTrack || o.track || o.forwardTrack`), `Sản phẩm` (`items[0].product`), `Vào theo dõi` (`o.rwSince`), `Quá trình` (thanh 4 bước `RW_STAGES` + `o.platformStatus`), `Giờ về shop` (`getOrderArrivedAt(o)`), `Đã nhận kho` (`o.recvDate`), `Thao tác` (`Xem ở bảng chính` → `jumpToReturnWatchOrder`, `✕` → `removeFromReturnWatch`).

---

### 2.4. 📦 Kho Lưu Trữ (24h) (`#archivedOrdersModal`, `#archivedTableBody` — `L22696–L22868`, `L19533–L20158`)
- **Sắp xếp**: Không có sắp xếp theo cột (thứ tự trong `archivedOrders`).
- **Phân trang**: `archivedPageSize = 50` (`L19536`), nút `◀ Trang trước` / `Trang sau ▶` (`changeArchivedPage`, `L19931`).
- **11 cột** (chỉ đọc, có checkbox chọn hàng loạt):
  1. Checkbox (`#archivedTableCheckAll` / `toggleSelectArchivedItem`)
  2. `STT`
  3. `Ngày Về Kho` (`o.recvDate`)
  4. `Sàn` (`o.platform`)
  5. `Mã Đơn Hàng` (`o.orderId`, `select-all`)
  6. `Mã Vận Đơn` (`o.track`, `select-all`)
  7. `Tên Sản Phẩm / Phân Loại` (`o.items[]`: `product`, `color`, `size`, `qty`)
  8. `ĐVVC` (`o.carrier`)
  9. `Giá Trị` (`o.price`)
  10. `Ghi Chú` (`o.userNote || o.note` — *lưu ý: không đọc `o.myNote`, xem mục 9*)
  11. `Thao Tác` (nút `↩️ Khôi phục` → `restoreSingleArchivedOrder`)

---

### 2.5. 📥 Bảng Đơn Mới Từ Điện Thoại (`#newOrdersModal`, `#newOrdersTableBody` — `L2261–L2347`, `L17013–L17338`)
- **Sắp xếp / Phân trang**: Không sắp xếp, không phân trang.
- **9 cột**:
  1. `✓` (Checkbox `item.checked` — `toggleNewOrderSelect`)
  2. `STT` (`#idx+1`)
  3. `Thời Gian Quét` (`item.time`)
  4. `Sàn TMĐT` (`item.platform` — **sửa được**: bấm badge để đảo Shopee ⇄ TikTok qua `toggleNewOrderPlatform`)
  5. `Mã Vận Đơn` (`item.track` — bấm để copy)
  6. `ĐVVC` (`item.carrier`)
  7. `Ghi Chú / Sản Phẩm` (`item.note` — **sửa trực tiếp** bằng `<input>` → `updateNewOrderNote`)
  8. `Ảnh` (`item.photo` — bấm gọi `openPhotoZoomDirect`, *xem mục 9*)
  9. `Thao Tác` (`📥 Nhập ra ngoài` → `importSingleNewOrder`, `✕` → `deleteSingleNewOrder`)

---

### 2.6. 🚫 Bảng Đơn Hủy (`#cancelledOrdersModal`, `#cancelledTableBody` — `L22608–L22675`, `L19165–L19408`)
- **Sắp xếp**: Không sắp xếp theo cột.
- **Phân trang**: `cancelledPageSize = 50` (`L17778`), nút `◀` / `▶` (`changeCancelledPage`, `L19261`).
- **Chân bảng thống kê**: `Tổng đơn`, `Tổng SL`, `Tổng giá trị`, `Đã nhận` (`L22662–L22667`).
- **17 cột**:
  1. Checkbox (`#cancelledSelectAll` / `toggleCancelledSelect`)
  2. `STT`
  3. `Ngày hủy` (`o.returnDate`)
  4. `Ngày về kho` (`o.recvDate`)
  5. `Sàn TMĐT` (`o.platform`)
  6. `Loại đơn/sàn` (`o.orderType || 'Đơn hủy'`)
  7. `Mã đơn hàng` (`o.orderId`)
  8. `Mã vận đơn` (`o.track || o.returnTrack`)
  9. `ĐVVC` (`o.carrier`)
  10. `Sản phẩm` (`items[].product + ' ×' + qty`)
  11. `Màu` (`items[].color`)
  12. `Size` (`items[].size`)
  13. `Tổng SL` (`cancelledOrderTotals(o).qty`)
  14. `Tổng giá trị` (`cancelledOrderTotals(o).value`)
  15. `Trạng thái nhận` (`o.received` — **sửa được** bằng `<select>`: `Chưa nhận` / `✅ Đã nhận`, gọi `setCancelledReceived` tự điền/xóa `recvDate`)
  16. `Ảnh đơn hàng` (`photoMemoryCache.get(o.photoId)` — bấm mở lightbox `openCancelledPhoto`)
  17. `Thao tác` (nút `Xóa` → `deleteCancelledOrder`)

---

### 2.7. ⚡ Bảng Tạo Đơn Nhanh (`#quickCreateOrdersModal`, `#quickCreateOrdersTableBody` — `L2352–L2484`, `L17357–L17775`)
- **Sắp xếp / Phân trang**: Không sắp xếp, không phân trang.
- **8 cột**:
  1. Checkbox (`#chkSelectAllQuick` / `toggleQuickOrderItem` — đơn trùng tự động bỏ tích)
  2. `STT`
  3. `Mã Đơn / Mã Vận Đơn` (`item.code` + badge `⚠️ ĐÃ CÓ TRÊN BẢNG: ...` nếu trùng)
  4. `Sàn TMĐT` (`item.platform` — **sửa được** bằng `<select>`: Shopee / TikTok Shop / Lazada / Khác)
  5. `Loại Đơn / Sàn` (`item.orderType` — **sửa được** bằng `<select>`: Trả hàng/Hoàn tiền / Giao không thành công / Đơn hủy / THẤT THU)
  6. `ĐVVC` (`item.carrier` — **sửa trực tiếp** bằng `<input>`)
  7. `Trạng Thái (Chống Trùng)` (`item.existingStatusText` hoặc `⏳ Chưa nhận`)
  8. `Xóa` (nút `🗑️` → `deleteQuickOrderItem`)

---

### 2.8. ⭐ Bảng Theo Dõi Đơn Hàng Đặc Biệt (`#watchlistModal`, `#watchlistModalTableBody` — `L1495–L1590`, `L21989–L22225`)
- **Sắp xếp / Phân trang**: Không sắp xếp, không phân trang.
- **10 cột**:
  1. `STT`
  2. `Mã Đơn Hàng` (`order.orderId` — bấm để copy)
  3. `Mã Vận Đơn` (`returnTrack` / `forwardTrack` / `track` — bấm để copy)
  4. `Sản Phẩm & Phân Loại` (`items[0]` + `platform • carrier`)
  5. `Lý Do Theo Dõi` (`order.watchlistReason` — **sửa được** qua nút `✏️ Sửa` → `editSingleWatchlistReason`)
  6. `Ngày Hủy` (`order.returnDate || order.shipDate`)
  7. `Hạn Cần Xử Lý` (`getOrderReturnElapsedInfo(order)`)
  8. `Trạng Thái Sàn` (`order.platformStatus`)
  9. `Nhận Tại Kho` (`isOrderReceived` / `isOrderUnneeded` / `Bị mất` / `Chưa nhận`)
  10. `Thao Tác` (`🎯 Định Vị` → `locateOrderFromWatchlist`, `❌` → `removeOrderFromWatchlist`)

---

### 2.9. Các bảng phụ khác trong Modal
| Bảng | ID / Dòng | Các cột & Trường dữ liệu | Sửa / Sắp xếp / Phân trang |
|---|---|---|---|
| **Đơn Đã Xóa** | `#deletedOrdersBody` (`L1472`, `L21919–L21942`) | 6 cột: `#`, `Mã đơn` (`orderId`), `Mã vận đơn` (`track \|\| returnTrack`), `Sản phẩm` (`items[].product`), `Xóa từ / lúc` (`deletedFrom`, `deletedAt`), `Thao tác` (`↩ Khôi phục`, `Cho phép nạp lại`) | Không sửa ô, không sắp xếp, không phân trang |
| **Danh Mục SP & Size** | `#catalogTableBody` (`L1671–L1688`, `L7495–L7551`) | 5 cột: `STT`, `Tên Sản Phẩm` (`item.name`), `Các Màu Của SP Này` (`item.colors`, cách dấu phẩy), `Các Size Riêng Của SP Này` (`item.sizes`, cách dấu phẩy), `Xóa` (`removeCatalogRow`) | **Sửa trực tiếp** 3 cột text (`name`, `colors`, `sizes`), bấm Lưu để ghi. Không sắp xếp/phân trang |
| **Xem trước CSDL nạp Excel** | `#shopeeDbTableBody` (`L2701–L2717`, `L7163–L7225`) | 10 cột: `STT`, `Sàn` (`platform`), `Loại Đơn` (`orderType`), `Mã Vận Đơn` (`track`), `Mã Đơn Hàng` (`orderId`), `Tên Sản Phẩm` (`items[0].product`), `Màu` (`items[0].color`), `Size` (`items[0].size`), `SL` (`items[0].qty`), `Giá Trị` (`price`) | Chỉ đọc, không sắp xếp, giới hạn hiển thị tối đa `1000` dòng (`SHOW_MAX = 1000`, `L7194`) |
| **Khiếu Nại Quá Hạn** | `#claimTableBody` (`L2011–L2027`, `L3454–L3484`) | 10 cột: `STT`, `Sàn`, `Mã Vận Đơn` (`track`), `Mã Đơn Hàng` (`orderId`), `ĐVVC` (`carrier`), `Bấm Hoàn` (`returnDate \|\| shipDate`), `Đã Chờ` (`getOrderAgeText`), `Giá Trị` (`price`), `Sản Phẩm Trong Đơn` (`items[]`), `Thao Tác` (`Đổi [Bị Mất]` → `markOrderAsLostFromClaim`) | Không sửa ô, không sắp xếp, không phân trang |
| **Nhật Ký Bảo Mật** | `#auditLogTableBody` (`L1873–L1882`, `L9111–L9127`) | 3 cột: `Thời Gian` (`log.time`), `Loại Thao Tác` (`log.type`), `Chi Tiết Thay Đổi / Cảnh Báo Bảo Mật` (`log.detail`) | Chỉ đọc, mới nhất lên đầu (`unshift`, giữ tối đa `200` dòng tại `addAuditLog`, `L8915–L8920`), không phân trang |
| **Thống Kê — Bảng Theo Sàn** | `#statsModalBody` (`L19122–L19143`) | 12 cột (gồm 1 cột ngăn cách): `Sàn` \| ĐƠN HOÀN: `Số đơn`, `Đã về`, `Còn chờ`, `Khác`, `Quá hạn`, `Sắp hạn` \| ĐƠN HỦY: `Số đơn`, `Đã về`, `Còn chờ`, `Quá hạn` | Chỉ đọc; mỗi con số > 0 là nút bấm mở `#statsDetail` |
| **Thống Kê — Đơn Hoàn & Đơn Hủy theo kỳ** | `#statsModalBody` (`L19055–L19096`) | Nhóm 1 (`ĐÃ VỀ KHO theo ngày nhận`): `[Bảng chính, Lưu trữ khi scope='all']`, `Tổng về kho` \| Nhóm 2 (`PHÁT SINH theo ngày hoàn/hủy`): `Số đơn`, `Đã về`, `Còn chờ`, `Khác`, `Quá hạn`, `Sắp hạn` + dòng `Chưa có ngày` + dòng `TỔNG` | Sắp xếp thời gian giảm dần; giới hạn `31` ngày hoặc `24` tuần/tháng (`L19045`) |
| **Thống Kê — Chi tiết danh sách** | `#statsDetail` (`L19150–L19161`) | 8 cột: `#`, `Mã vận đơn`, `Mã đơn`, `Sàn`, `Ngày hoàn/hủy`, `Ngày về kho`, `Trạng thái`, `Nằm ở` (`Bảng chính` / `Kho lưu trữ` / `Bảng đơn hủy`) | Chỉ đọc, giới hạn tối đa `1000` dòng (`L19154`) |

---

## 3. Bộ lọc & tìm kiếm

### 3.1. Ô tìm kiếm chính (`#searchInput` — `L981–L995`, `L10610–L10714`, `L11922–L11925`, `L12028–L12036`)
- **Debounce**: `handleSearchInputDebounced()` (`L8008`).
- **Phạm vi khớp (`isOrderMatch`, `L10698–L10714`)**: tìm không phân biệt hoa/thường và hỗ trợ **không dấu tiếng Việt** (`removeVietnameseTones`) trên: `platform`, `orderType`, `partialText`, `preOrder`, `orderId`, `track`, `altTrack`, `returnTrack`, `forwardTrack`, `trackingCodes[]`, `claimId`, `buyerUsername`, `carrier`, `shipDate`, `returnDate`, `recvDate`, `note`, `buyerNote`, `myNote`, `watchlistReason`, `items[].product/color/size`.
- **Hành vi "Tìm kiếm toàn cục" (v1.8.6, `L11922–L11925`)**: đơn khớp từ khóa **luôn được đưa vào kết quả hiển thị** kể cả khi đang lọc ở Tuần/Tab/Sàn/Ngày khác; tự động nhảy đến đúng trang chứa đơn khớp đầu tiên, cuộn tới và nháy vàng `search-locate-flash` trong 2,5 giây (`L12028–L12036`, `L12559–L12567`).
- **Checkbox `Chỉ hiện khớp` (`#chkFilterOnly`, `L989`)**: nếu tắt (mặc định), tô nền vàng (`row-match`) các dòng khớp nhưng vẫn giữ các dòng khác theo bộ lọc; nếu bật, chỉ giữ lại các dòng khớp từ khóa.
- **Nút `✕ Xóa` (`#btnClearMainSearch`, `L992`)**: gọi `clearMainSearch()` (`L11284`).
- **Tìm hàng loạt nhiều mã (`parseBatchSearchCodes`, `L10640–L10696`)**: khi dán chuỗi có dấu cách/xuống dòng/dấu `, ; |` chứa **>= 2 mã** (chỉ tính token chữ+số dài `>= 8` ký tự và có ít nhất 1 chữ số), thanh `#searchPreviewBar` tự chuyển sang bảng đối soát từng mã trên **4 kho**: Bảng chính (`dòng # + trạng thái`), Kho lưu trữ (`về kho ...`), Bảng đơn hủy (`đã nhận/chưa nhận`), Hộp đơn mới (`chờ đẩy ra`), hoặc `❌ Không có`. Có nút `📋 Chép N mã không có` (`copyBatchMissingCodes`) và nút `Chỉ hiện đơn có ở bảng chính`.
- **Cảnh báo khớp ở bảng khác (`buildOtherTablesSearchHtml`, `L19179–L19224`)**: với tìm kiếm đơn lẻ (`>= 2` ký tự), hiển thị thêm kết quả khớp trong **Kho lưu trữ** và **Bảng đơn hủy** kèm nút `📍 Đi đến vị trí` (`gotoSearchHit`) tự mở modal, lọc và cuộn tới đúng dòng đó.
- **Cảnh báo đơn đã xóa (`#searchDeletedHint`, `L995`, `L21944–L21967`)**: nếu từ khóa (`>= 4` ký tự) khớp đơn trong `deletedOrders`, hiện khung đỏ báo `🗑 ĐÃ XÓA` kèm nút `↩ Khôi phục` ngay dưới thanh tìm kiếm.
- **Banner khi có đơn khớp ở bộ lọc khác (`L13160–L13167`)**: hiện nút `Xem trong tất cả` (`clearScopeFiltersForSearch`, `L12865`).
- **Súng quét mã vạch PC (`L14441–L14459`)**: nhấn `Enter` trong `#searchInput` tìm khớp tuyệt đối `track/altTrack/returnTrack/forwardTrack/orderId` rồi gọi `highlightAndFlashOrderRow` nháy xanh dòng đó.

### 3.2. Bộ lọc trên thanh điều hướng bảng chính
| Nhóm lọc | Phần tử HTML (ID / Dòng) | Tùy chọn | Hành vi & Hàm xử lý |
|---|---|---|---|
| **Tab Tuần Nạp** | `#weeklyImportTabsContainer`, `#dynamicWeekTabs` (`L872–L882`) | `🌐 Tất Cả Các Tuần` + các tuần phát sinh theo `order.importedAt` / `order.importWeek` + tuần tùy chọn (`savedCustomWeeks`) | `setImportWeekFilter` (`L7713`): lưu vào `localStorage` (`SAVED_ACTIVE_WEEK_KEY`), xóa selection, lọc cả bảng chính và bảng tổng kết SKU. Tuần tùy chọn rỗng có nút `✕` xóa (`removeCustomWeekTab`) |
| **Tab Sàn TMĐT** | `#platformBigTabs`: `#platBigBtn_Shopee`, `#platBigBtn_TikTok`, `#platBigBtn_all` (`L887–L906`); bản ẩn `#platformFilterSelect` (`L1062–L1068`) | `Shopee`, `TikTok Shop`, `all` (Cả 2 sàn). *(Lazada/Khác chỉ có trong `<select>` ẩn)* | `setPlatformFilter` (`L8219`): đổi không gian làm việc toàn cục (đồng bộ cả bảng chính, các tab trạng thái, bảng tổng kết SKU, thống kê) |
| **Tab Trạng Thái (Pipeline)** | `#pipelineTabs` (`L998–L1038`) | `all` (Tất cả), `waiting` (Chưa nhận), `returned` (Đã nhận), `unneeded` (K.cần nhận), `upcoming` (Báo Vàng 12h), `overdue` (Báo Đỏ 24h), `lost` (Bị mất/Hư hỏng) | `setPipelineFilter` (`L8607`): lọc bảng chính; số đếm trên từng tab tự tính theo `orderMatchesTableScope` (`L13034–L13068`) |
| **Bộ lọc nâng cao (thu gọn)** | Nút `#btnAdvancedFilters` + badge đếm `#advancedFilterBadge` (`L1077–L1083`), panel `#advancedFilterPanel` (`L1130–L1197`) | 1. **Loại đơn** (`#orderTypeFilterSelect`): Tất cả / `Giao không thành công` / `Trả hàng/Hoàn tiền` / `partial_return` (Hoàn 1 phần) / `Đơn hủy` / `THẤT THU`<br>2. **Loại theo sàn** (`#sheetTypeFilterSelect`): tự sinh danh sách từ `order.sheetType` kèm số lượng (`refreshTypeStatusFilterOptions`)<br>3. **Trạng thái kiện** (`#platStatusFilterSelect`): tự sinh từ `orderPlatStatusText(o)`<br>4. **Có/Không MVĐ** (`#trackingFilterSelect`): Tất cả / `has_track` / `no_track`<br>5. **Ngày & Giờ**: chọn mốc (`#dateFilterTypeSelect`: `any` / `returnDate` [mặc định] / `recvDate` / `shipDate`) + preset nhanh (`#recvDateFilterSelect`: Mọi lúc / Hôm nay / Hôm qua / 7 ngày qua / Ca Sáng `06-18h` / Ca Tối `18-06h`) + khoảng ngày (`#filterDateFrom` → `#filterDateTo`) + khoảng giờ 24h (`#filterTimeFrom` → `#filterTimeTo`, hỗ trợ ca qua đêm `from > to`, `L11689–L11694`) + nút `✕` xóa lọc ngày giờ (`clearDateTimeFilter`) | `toggleAdvancedFilters` (`L12829`), `setOrderTypeFilter`, `setSheetTypeFilter`, `setPlatStatusFilter`, `setTrackingFilter`, `setDateFilterType`, `setQuickDatePreset`, `applyDateTimeFilter`, `checkOrderDateTimeMatch` (`L11649–L11720`). Khi lọc giờ, đơn không có `HH:mm` (00:00:00) bị loại (`L11681–L11684`) |
| **Chỉ hiện đơn có ngày** | `#chkOnlyWithDate` (`L1101`) | Checkbox bật/tắt | Chỉ có hiệu lực khi `currentDateSort !== 'default'`: ẩn các đơn không có giá trị ngày ở cột đang sắp xếp (`L11971–L11975`) |

### 3.3. Bộ lọc trong Bảng Tổng Kết Phân Loại & Các Modal
- **Bảng Tổng Kết Phân Loại (`L1261–L1315`)**: nút đổi sàn (`#sumPlatBtn_all`, `_Shopee`, `_TikTok`), 3 ô gõ lọc không dấu: Tên SP (`#catSummarySearchProd`), Màu (`#catSummarySearchColor`), Size (`#catSummarySearchSize`), dropdown trạng thái (`#catSummaryFilterSelect`: `all`, `has_returned`, `diff_only`, `unreturned`, `lost`), nút `✕ Xóa bộ lọc` (`clearCatSummaryFilters`).
- **Bảng Hàng Hoàn Về (`L1484–L1487`, `L21871–L21882`)**: ô tìm không dấu `#returnWatchSearch` + 6 nút pill lọc chặng (`active` [Đang theo dõi], `1` [Đã đồng ý], `2` [Đang về], `3` [Đã về shop], `4` [Đã nhận kho], `all`).
- **Kho Lưu Trữ 24h (`L22731–L22763`, `L19754–L19812`)**: ô tìm `#archivedSearchInput`, lọc khoảng ngày+giờ về kho (`#archivedFilterFromDate/Time` → `#archivedFilterToDate/Time`), lọc sàn (`#archivedFilterPlatform`: Tất cả / Shopee / TikTok Shop), nút `✕ Xóa lọc` (`resetArchivedFilters`).
- **Bảng Đơn Hủy (`L22624–L22640`, `L19226–L19260`)**: ô tìm `#cancelledSearchInput`, sàn (`#cancelledPlatformFilter`), trạng thái nhận (`#cancelledStatusFilter`: Mọi / Chưa nhận / Đã nhận), mốc ngày (`#cancelledDateField`: Ngày hủy / Ngày nhận hàng) + từ/đến ngày (`#cancelledDateFrom/To`), nút `✕ Xóa lọc` (`resetCancelledFilters`).
- **Bảng Theo Dõi Đặc Biệt (`L1530–L1559`, `L21994–L22075`)**: ô tìm `#watchlistSearchInput` + 5 nút pill (`all`, `danger` [Báo Đỏ +3 ngày], `warning` [Báo Vàng 2 ngày], `pending` [Chưa Về Kho], `received` [Đã Nhận]).
- **Trung Tâm Thông Báo (`L1413–L1445`, `L16209–L16280`)**: ô tìm `#notifSearchInput` + 6 nút danh mục (`all`, `phone`, `new_order`, `excel`, `deadline`, `warning`).
- **Thống Kê (`L22590–L22600`, `L18954–L19098`)**: chọn phạm vi `#statsScopeSelect` (`table` = đúng bảng chính đang hiển thị / `all` = bảng chính + kho lưu trữ cùng bộ lọc), kỳ thống kê (`day`, `week`, `month`). *(Lưu ý: `#statsPlatformSelect` tồn tại trong HTML `L22594` nhưng bị JS ẩn `display: none` tại `L19098` vì đồng bộ theo tab sàn chính)*.
- **Đơn Đã Xóa (`#deletedOrdersSearch`, `L1470`)**, **Danh Mục SP (`#catSearchInput`, `L1652`)**, **CSDL Nạp Excel (`#shopeeDbSearchInput`, `L2693`)**: mỗi nơi có 1 ô tìm kiếm từ khóa trực tiếp.

---

## 4. Nút / hành động trên từng màn hình và hàm gọi tới

### 4.1. Thanh trên cùng (Header) & Menu Dropdown
| Vị trí | Nút / Mục | ID / Dòng | Hàm gọi tới & Tác dụng |
|---|---|---|---|
| Header trái | `☰` | `#btnAppMenu` (`L18110`) | `setAppDrawer(...)` — mở/đóng sidebar trái |
| Header trái | Badge phiên bản `🚀 Phiên bản v...` | `#appVersionBadge` (`L432`) | `showVersionInfoToast()` (`L8053`) — hiện thông tin phiên bản |
| Header phải | `🔔 Thông Báo` + badge chưa đọc | `#btnNotificationCenter` (`L457`) | `openNotificationModal()` (`L16184`) — mở lịch sử quét & hạn xử lý |
| Header phải | `📂 Nhập / Xuất ▾` | `#btnMenuImport` (`L493`) | `toggleHeaderMenu(event, 'headerMenuImport')` (`L9546`, yêu cầu mở khóa nếu bảng đang khóa) |
| ↳ Menu Nhập/Xuất (Mở nhanh) | 8 nút mở nhanh các bảng | `L510–L517` | `openReturnWatchModal()`, `openArchivedOrdersModal()`, `openNewOrdersModal()`, `openCancelledOrdersModal()`, `openAnalyticsDashboardModal()`, `openStatsModal()`, `openQuickCreateOrdersModal()`, `openBatchSessionModal()` |
| ↳ Menu Nhập/Xuất (Nhập) | `♻ Khôi phục đơn đã nhận` | `#btnRestoreReceived` (`L522`) | Kích hoạt `#restoreReceivedFile` → `restoreReceivedFromPatch(this)` (`L3977`) |
| ↳ Menu Nhập/Xuất (Nhập) | `🟠 1. Nạp Đơn Riêng Sàn SHOPEE` | `#btnShopeeDb` (`L535`) | `openShopeeDbModal('shopee')` (`L7227`) |
| ↳ Menu Nhập/Xuất (Nhập) | `⚫ 2. Nạp Đơn Riêng Sàn TIKTOK / SALEWORK` | `#btnTikTokDb` (`L552`) | `openShopeeDbModal('tiktok')` (`L7227`) |
| ↳ Menu Nhập/Xuất (Nhập) | `⚡ Dán Nhanh Nhiều Đơn` | `#btnBulkPaste` (`L568`) | `openBulkImportModal()` (`L13849`) |
| ↳ Menu Nhập/Xuất (Nhập) | `🔍 Đọc Chữ Từ Ảnh Tem (OCR)` | `#btnAutoOcrMissing` (`L582`) | `triggerPcOcrForMissingOrders()` (`L7365`) |
| ↳ Menu Nhập/Xuất (Nhập) | `⚡ Khiếu Nại Sàn (1-Click)` | `L595` | `openClaimModal()` (`L3420`) |
| ↳ Menu Nhập/Xuất (Xuất/Lưu) | `📦 Xuất File Đơn ĐÃ VỀ KHO (.xlsx)` | `L611` | `exportReturnedOrdersToExcel()` (`L13462`) — 2 sheet: Chi tiết & SKU nhập lại kho |
| ↳ Menu Nhập/Xuất (Xuất/Lưu) | `📊 Xuất Toàn Bộ Đơn Hàng (.xlsx)` | `L628` | `exportToExcel()` (`L13354`) |
| ↳ Menu Nhập/Xuất (Xuất/Lưu) | `🛡️ Lưu Trữ An Toàn & Đóng Gói Tháng` | `L641` | `openSafeStorageModal()` (`L10165`) |
| ↳ Menu Nhập/Xuất (Xuất/Lưu) | `💾 Sao Lưu Dữ Liệu (.json)` | `L657` | `exportBackupJson()` (`L3294`) |
| ↳ Menu Nhập/Xuất (Xuất/Lưu) | `📂 Phục Hồi Dữ Liệu (.json)` | `L670` | `restoreBackupJson(event)` (`L3334`) |
| ↳ Menu Nhập/Xuất (Xuất/Lưu) | `📄 Lưu Thẳng Ra File HTML (Ctrl+S)` | `#btnSaveDirectToFile` (`L684`) | `saveDirectlyToHtmlFile()` (`L14594`) |
| ↳ Menu Nhập/Xuất (Xuất/Lưu) | `🖨️ In Báo Cáo / Lưu PDF` | `L697` | `requireUnlocked(...).then(ok => window.print())` |
| Header phải | `⚙️ Hệ Thống ▾` | `#btnMenuTools` (`L717`) | `toggleHeaderMenu(event, 'headerMenuTools')` |
| ↳ Menu Hệ Thống | `📦 Danh Mục SP & Size Của Shop` | `L734` | `openCatalogModal()` (`L7484`) |
| ↳ Menu Hệ Thống | `🗑️ Đơn Đã Xóa` | `L747` | `openDeletedOrdersModal()` (`L21914`) |
| ↳ Menu Hệ Thống | `🧹 Dọn Đơn Trùng Giữa Các Bảng` | `L758` | `removeDuplicatesAcrossTables()` (`L4572`) |
| ↳ Menu Hệ Thống | `🛡️ Nhật Ký Bảo Mật` | `L769` | `openAuditLogModal()` (`L9099`) |
| ↳ Menu Hệ Thống | `🔑 Đổi Mật Khẩu Quản Trị` | `L782` | `changeAppPassword()` (`L9509`) |
| ↳ Menu Hệ Thống | `📁 Thư Mục Dữ Liệu Ổ Cứng` | `L795` | `openHardDriveDataFolder()` (`L10445`) |
| ↳ Menu Hệ Thống | `💾 Sao Lưu Ra Ổ Cứng Khác` | `L808` | `backupHardDriveData()` (`L10457`) |
| ↳ Menu Hệ Thống | `🚀 Khởi Động Cùng Windows` | `L821` | `toggleAutoStartSetting()` (`L10558`) |
| ↳ Menu Hệ Thống | `🗑️ Khôi Phục Dữ Liệu (Xóa Hết Đơn Hiện Tại)` | `#btnResetSample` (`L838`) | `resetSampleData()` (`L11014`) |
| Header phải | `🔓 Mở Khóa` / `🔒 Đang Khóa` | `#btnLockToggle` (`L861`) | `togglePasswordLock()` (`L9481`) |

### 4.2. Sidebar Trượt (`#appSidebar`, `L18054–L18105`) & Thanh Điều Hướng Hàng 2–3
- **Mục Bảng dữ liệu (`#sbNav`)**: `📦 Kho lưu trữ` (`openArchivedOrdersModal`), `🚫 Bảng đơn hủy` (`openCancelledOrdersModal`), `📥 Đơn mới từ điện thoại` (`openNewOrdersModal`), `⭐ Theo dõi` (`openWatchlistModal`), `📈 Thống kê` (`openStatsModal`), `📊 Phân Tích` (`openAnalyticsDashboardModal`). *(Nút `🚚 HÀNG HOÀN VỀ` `#btnReturnWatchHeader` vẫn ở lại Row 2 ngoài bảng chính, `L922`)*.
- **Mục Thao tác (`#sbTools`)**: `➕ Thêm Đơn` (`addOrder`), `⚡ Tạo đơn nhanh` (`openQuickCreateOrdersModal`), `🟠 Nạp Shopee` (`openShopeeDbModal('shopee')`), `⚫ Nạp TikTok` (`openShopeeDbModal('tiktok')`), `📱 Quét ĐT` (`openPhoneScannerModal`), `📦 Ca: N` (`openBatchSessionModal`), `🛡️ Lưu An Toàn` (`openSafeStorageModal`), `🔗 Google Sheet (Shopee)` (`#btnSheetSync` → `openSheetSyncModal`), `📂 Tự nạp file TikTok` (`#btnFolderWatch` → `openFolderWatchModal`), `🔎 Chẩn đoán Salework` (`#btnSaleworkDiag` → `openSaleworkDiag`), `🤖 Salework tự động (đang chờ hoàn)` (`#btnSaleworkAuto` → `openSaleworkAuto`).
- **Hàng 2 (`L868–L972`)**: nút `➕ Tuần` (`openWeekTabModal`), các nút Tuần (`setImportWeekFilter`), 3 nút Sàn (`setPlatformFilter`), nút `🚚 HÀNG HOÀN VỀ` (`openReturnWatchModal`).
- **Hàng 3 (`L998–L1044`)**: 7 tab trạng thái (`setPipelineFilter`), nút `📥 Xuất Đơn Đã Về` (`#btnExportReturnedHeader` → `exportReturnedOrdersToExcel`), nút `⚙️ Bộ lọc nâng cao` (`toggleAdvancedFilters`).
- **Thanh nổi khi chọn nhiều đơn bảng chính (`#bulkActionBar`, `L22419–L22443`)**: `⭐ Thêm Vào Theo Dõi` (`bulkAddSelectedToWatchlist`), `📊 Xuất Excel Đơn Đã Chọn` (`bulkExportSelectedToExcel`), `🌐 Xuất HTML Kiểm Hàng` (`bulkExportSelectedToHtml`), `🗑️ Xóa Các Đơn Đã Chọn` (`bulkDeleteSelectedOrders`), `✕ Bỏ chọn` (`clearAllSelectedOrders`).
- **Hành động trong Accordion chi tiết đơn (`L12648–L12778`)**: `➕ Thêm Mặt Hàng Vào Đơn` (`addProductToOrder`), `▲ Thu gọn` (`toggleOrderItemsDrawer`), `✕ Xóa` dòng SP (`removeProductFromOrder`), `🛡️ Tạo Hồ Sơ Khiếu Nại Sàn (1-Click)` (`openSingleOrderDisputeModal`), `🖨️ In Tem Kiểm Kho Re-Stock` (`printRestockLabel`).
- **Thanh dock đáy (`#stickyBottomDock`, `L1371–L1379`)**: `▲ Xem Bảng Thống Kê Phân Loại (Màu & Size)` (`switchMainView('summary'); scrollToSummaryTable()`), `📋 Bảng Đơn Hàng` (`switchMainView('orders'); scrollToMainTable()`).

### 4.3. Hành động chính bên trong các Modal bảng dữ liệu
- **Kho Lưu Trữ (`#archivedOrdersModal`, `L22717–L22805`)**: `⚡ Quét & Dời Đơn >24h Ngay` (`triggerManualArchiveScan`), `↩️ Khôi phục về Bảng chính` (`restoreSelectedArchivedOrders` / từng dòng `restoreSingleArchivedOrder`), `📊 Xuất File Excel` (`exportArchivedOrdersToExcel`), `💾 Lưu vào Ổ Cứng` (`saveArchivedToHardDrive`), `🗑️ Xóa đơn đã chọn` (`deleteSelectedArchivedOrders`).
- **Bảng Đơn Hủy (`#cancelledOrdersModal`, `L22616–L22647`)**: `➕ Tạo đơn hủy` (`openCancelledCreateForm` → `submitCancelledCreateForm`), `📥 Nhập file đơn hủy` (`handleCancelledFileUpload`), thanh chọn nhiều `#cancelledBulkBar`: `✅ Đánh dấu đã nhận` / `↩ Đánh dấu chưa nhận` (`bulkSetCancelledReceived`), `🗑 Xóa đã chọn` (`bulkDeleteCancelled`), `Bỏ chọn` (`clearCancelledSelection`).
- **Hộp Đơn Mới (`#newOrdersModal`, `L2289–L2305`)**: `📥 Nhập Đơn Đã Chọn Ra Ngoài` (`importSelectedNewOrders`), `➕ Nhập Tất Cả Ra Ngoài` (`importAllNewOrders`), `🗑️ Xóa Đã Chọn` (`deleteSelectedNewOrders`), `🧹 Xóa Tất Cả` (`clearAllNewOrders`), từng dòng: `importSingleNewOrder`, `deleteSingleNewOrder`.
- **Tạo Đơn Nhanh (`#quickCreateOrdersModal`, `L2381–L2440`)**: `⚡ TẠO DANH SÁCH ĐƠN` (`parseQuickCreateInput`), `🧹 Xóa ô dán` (`clearQuickCreateInputOnly`), gán hàng loạt Sàn (`applyBatchPlatformToQuickList`) & Loại đơn (`applyBatchOrderTypeToQuickList`), `⚡ Chỉ chọn đơn mới` (`selectOnlyNewQuickOrders`), `🛡️ Xóa đơn trùng` (`removeDuplicateQuickOrders`), `📥 Đưa Đơn Đã Chọn Ra Ngoài` (`importSelectedQuickOrders`), `➕ Đưa Tất Cả Ra Ngoài` (`importAllQuickOrders`).
- **Bảng Theo Dõi (`#watchlistModal`, `L1512–L1525`)**: `📊 Xuất Excel` (`exportWatchlistToExcel`), `✨ Dọn Đơn Đã Về Kho` (`cleanReturnedOrdersFromWatchlist`), `🧹 Bỏ Tất Cả` (`clearAllWatchlist`), từng dòng: `editSingleWatchlistReason`, `locateOrderFromWatchlist`, `removeOrderFromWatchlist`.

---

## 5. Modal / hộp thoại / panel

| STT | Tên Modal / Panel | ID & Vị trí HTML | Chức năng chính & Điều khiển nổi bật |
|---|---|---|---|
| 1 | **Trung Tâm Thông Báo & Lịch Sử** | `#notificationModal` (`L1386–L1458`) | Xem lịch sử quét ĐT, tạo đơn mới, cập nhật Excel, cảnh báo trùng và tab **⏰ Hạn Xử Lý** (tính động trực tiếp từ `orders` & `cancelledOrders`). Có nút `🎯 Định Vị Đơn` (`locateOrderFromNotification` / `locateDeadlineItem`) và `🧹 Xóa Hết` (`clearAllNotifications`). Tự mở sẵn tab `deadline` lần đầu nếu có đơn đến hạn (`L16185`). |
| 2 | **Bảng Theo Dõi Đặc Biệt** | `#watchlistModal` (`L1495–L1590`) | Quản lý danh sách đơn gắn sao, lọc theo mức cảnh báo/trạng thái, sửa lý do theo dõi, xuất Excel. |
| 3 | **Bảng Hàng Hoàn Về** | `#returnWatchModal` (`L1478–L1492`) | Theo dõi hành trình 4 chặng các đơn khách trả đã "Đã đồng ý". |
| 4 | **Đơn Đã Xóa** | `#deletedOrdersModal` (`L1461–L1475`) | Quản lý blacklist đơn đã xóa chống nạp trùng lại từ sàn; khôi phục về đúng bảng gốc hoặc xóa khỏi danh sách (`forgetDeletedOrder`, `clearDeletedOrders`). |
| 5 | **Xem Phóng To Ảnh (Lightbox)** | `#zoomModal` (`L1593–L1628`) | Phóng to ảnh đơn hàng (`#zoomModalImg`), `📥 Tải Ảnh Về` (`downloadCurrentZoomPhoto`), `🔄 Xoay` 90° (`rotateCurrentZoomPhoto`), `⚡ Đọc Mã` OCR (`triggerPcOcrForOrder`), `🗑 Xóa` (`removeCurrentZoomPhoto`). Ngoài ra bảng đơn hủy tạo overlay zoom động riêng (`openCancelledPhoto`, `L17942–L17952`). |
| 6 | **Danh Mục SP & Size Của Shop** | `#catalogModal` (`L1631–L1705`) | CRUD danh mục SP/Màu/Size, `🔄 Tự gom thêm SP từ bảng đơn hàng` (`syncCatalogFromExistingOrders`), `💾 Lưu & Áp Dụng Ngay` (`saveCatalogFromModal`). |
| 7 | **Nhập Mật Khẩu / Prompt In-App** | `#customPromptModal` (`L1708–L1761`) | Thay thế `window.prompt()` của trình duyệt; hỗ trợ hiện/ẩn mật khẩu (`togglePromptPasswordVisibility`), dùng cho mở khóa bảng/ô, đổi mật khẩu, sửa hạn, sửa trạng thái sàn, sửa lý do theo dõi (`appPrompt`, `L8928`). |
| 8 | **Hộp Thoại Thông Báo / Xác Nhận In-App** | `#customDialogModal` (`L1823–L1854`) | Thay thế `window.alert` (ghi đè toàn cục tại `L9056`) & `confirm` qua `appAlert`/`appConfirm` (`L8992–L9053`) để tránh lỗi đơ focus trên Electron Windows. |
| 9 | **Tạo Thêm & Quản Lý Tab Tuần** | `#weekTabModal` (`L1767–L1820`) | Chọn nhanh tuần kế/hiện tại/trước (`#weekTabQuickButtons`) hoặc nhập số tuần 1–53 (`#weekTabNumberInput`, `submitCustomWeekNumber`). |
| 10 | **Nhật Ký Bảo Mật** | `#auditLogModal` (`L1857–L1885`) | Bảng 3 cột lịch sử thao tác nhạy cảm; yêu cầu xác thực mật khẩu Chủ Shop khi mở. |
| 11 | **Chi Tiết Ô & Lịch Sử Chỉnh Sửa** | `#cellActionModal` (`L1888–L1928`) | Mở khi **nhấp chuột phải** vào ô trên bảng chính; cho Khóa/Mở khóa riêng ô (`actionLockCurrentModalCell` / `actionUnlockCurrentModalCell`), xem tối đa 50 lần sửa gần nhất (`cellEditHistory`), nút `🗑️ Xóa Lịch Sử Ô Này` (`clearCurrentCellHistory`). |
| 12 | **Popover Xem Chi Tiết Ô** | `#cellDetailPopover` (`L1931–L1954`) | Mở nổi tại vị trí ô khi bấm 1 lần: xem toàn bộ nội dung bị cắt ngắn, `📋 Sao chép` (`copyCellDetailContent`), `✏️ Chỉnh sửa` (`triggerEditFromPopover`). |
| 13 | **Khiếu Nại Sàn Quá Hạn (1-Click)** | `#claimOverdueModal` (`L1957–L2032`) | Liệt kê đơn quá 72h chưa về, mẫu tin nhắn tự tạo (`#claimMessageTextarea`), `📋 Copy Tin Nhắn Gửi Sàn` (`copyClaimMessage`), `📋 Chỉ Copy MVĐ` (`copyClaimTrackingList`), `⬇ Xuất Excel` (`exportClaimExcel`), đổi từng đơn sang `Bị mất`. |
| 14 | **Hồ Sơ Khiếu Nại 1 Đơn** | `#singleDisputeModal` (`L2735–L2815`) | Mở từ Accordion chi tiết đơn; chọn tình trạng thực tế (`#dispReasonSelect` 6 mẫu), nhập ghi chú (`#dispDetailNote`), đính kèm ảnh chụp đơn + tải ảnh (`downloadDisputePhoto`), tự soạn văn bản khiếu nại (`updateDisputeMessageDraft`) & copy 1-click (`copyDisputeLetter`). |
| 15 | **Dán Nhanh Nhiều Đơn** | `#bulkImportModal` (`L2035–L2075`) | Ô `<textarea>` (`#bulkImportTextarea`) nhận danh sách MVĐ hoặc bảng copy nhiều cột từ Excel (`Mã đơn \| MVĐ \| Tên SP \| Màu \| Size \| SL \| Giá trị`), xử lý bởi `processBulkImportOrders` (`L13861`). |
| 16 | **Kết Nối Điện Thoại Quét Mã (Phone Scanner Hub)** | `#phoneScannerModal` (`L2078–L2184`) | Hiện QR SVG offline (`#phoneScannerQrBox`), chọn máy chủ (`#mobileServerSelect`), mã phòng (`#phoneRoomCodeText`), link ngắn (`#phoneShortLinkTag`), `📋 Sao Chép Link` (`copyPhoneScannerLink`), `📥 Tải File Máy Quét (.html)` (`downloadMobileScannerFileForPhone`), `🔄 Đổi mã phòng kết nối mới` (`regeneratePhoneRoom`, `L20184`), trạng thái kết nối (`#phoneConnectionBadge`), `📥 Kéo Lại Mã & Ảnh Quét Lúc Tắt Máy` (`syncCloudCatchup(true)`), `🗑️ Xóa mã đã nhận` (`clearPhoneReceivedScans`), chọn hành vi quét (`smart_auto` vs `fill_active`), log trực tiếp `#phoneLiveFeedList`. |
| 17 | **Quét Camera Trực Tiếp Trên PC** | `#pcCameraScannerModal` (`L2187–L2254`) | Chọn webcam (`#pcCameraSelect`), khung soi mã vạch `html5-qrcode` (`#pcCameraReader`), `📸 Chụp & Đọc Chữ Ngay` OCR (`triggerPcManualCaptureAndOcr`), `🔄 Khởi Động Lại` (`restartPcWebcamScanner`). *(Lưu ý: không thấy nút mở trên giao diện hiện tại — xem mục 9)*. |
| 18 | **Hộp Đơn Mới Từ Điện Thoại** | `#newOrdersModal` (`L2261–L2347`) | Bảng duyệt & đẩy đơn mới quét từ điện thoại ra bảng chính. |
| 19 | **Tạo Đơn Nhanh Hàng Loạt** | `#quickCreateOrdersModal` (`L2352–L2484`) | Dán danh sách mã, tự lọc trùng bảng chính, gán Sàn/Loại đơn hàng loạt rồi đẩy ra bảng chính. |
| 20 | **Nạp Dữ Liệu Tách Riêng 2 Sàn (Shopee / TikTok)** | `#shopeeDbModal` (`L2486–L2722`) | 2 tab nền Shopee / TikTok-Salework; chọn chế độ cập nhật SP (`#importProductUpdateMode`: `supplement` / `replace`); checkbox chỉ nạp hoàn/hủy (`#chkFilterOnlyReturns`), tự đẩy ra bảng chính (`#chkPushToMainOrders`); chọn cột giá TikTok (`#tiktokPriceColumnMode`: 4 lựa chọn) & bỏ đơn hủy chưa có MVĐ (`#chkTikTokSkipUnshippedCancel`); tải file `.xlsx/.xls/.csv` hoặc dán text; nút `🗑️ Xóa Hết Đơn Hiện Tại`, `➕ Đẩy Hết Ra Bảng Chính` (`pushAllDbOrdersToMainTable`). |
| 21 | **Bảng Phân Tích & Đối Soát Tài Chính** | `#analyticsDashboardModal` (`L2818–L2839`) | 4 thẻ tài chính + Top 10 mẫu SP hoàn + Top 5 lý do hoàn. |
| 22 | **Quản Lý Ca Quét Nhận Kiện** | `#batchSessionModal` (`L2842–L2882`) | Tổng số kiện hợp lệ trong ca, `🔄 Bắt Đầu Ca Mới` (`startNewScanSession`), `📋 Sao Chép Biên Bản` (`copySessionReceipt`), danh sách mã quét trong ca (gắn nhãn `Trùng lặp` nếu quét trùng). |
| 23 | **Trung Tâm Lưu Trữ An Toàn & Đóng Gói Tháng** | `#safeStorageModal` (`L2888–L3053`) | Trạng thái 3 lớp lưu (IndexedDB, Ổ cứng, LocalStorage), tổng đơn/SP/ảnh, chọn tháng (`#archiveMonthSelect`) để `📥 Xuất File Lưu Trữ Tháng (.xlsx)` hoặc `✂️ Xuất File & Dọn Khỏi Bảng` (`exportArchiveMonth`), sao lưu/phục hồi `.json` toàn bộ. |
| 24 | **Tự Cập Nhật Google Sheet (Shopee)** | `#sheetSyncModal` (`L22451–L22504`) | Nhập link Sheet + tên các tab (ngăn cách `;`), tự sinh mã Apps Script (`#sheetSyncScript`) & `appsscript.json` chỉ đọc (`#sheetSyncManifest`) có nút chép, dán URL Web App (`#sheetSyncUrl`), nhập loại bỏ qua tab Hủy (`#sheetSyncSkip`), lọc ngày bắt đầu/số ngày gần nhất (`#sheetSyncDays`), bật tự cập nhật mỗi 10 phút (`#sheetSyncEnabled`), nút Lưu / Lưu & cập nhật ngay (`saveSheetSyncFromModal`). |
| 25 | **Tự Nạp File TikTok Từ Thư Mục** | `#folderWatchModal` (`L22506–L22531`) | Chọn thư mục theo dõi (`pickFolderWatchDir`, quét mỗi 15 giây chỉ trên bản Desktop), bật/tắt (`#folderWatchEnabled`), Lưu hoặc Lưu & nạp file đang có (`saveFolderWatchFromModal`). |
| 26 | **Salework Tự Động (Đang Chờ Hoàn)** | `#saleworkAutoModal` (`L22533–L22560`) | Tự động hóa xuất Excel Salework: nhập URL (`#swAutoUrl`), `🌐 Đăng nhập Salework`, `💾 Lưu trang này`, `▶️ Lấy ngay`, `⛔ Dừng`, `🗑 Quên đăng nhập`, checkbox `👁 Xem app thao tác` (`#swAutoWatch`). |
| 27 | **Chẩn Đoán Salework (Chỉ Quan Sát)** | `#saleworkDiagModal` (`L22562–L22584`) | Mở cửa sổ Salework tách phiên để ghi nhận cấu trúc trang/trường/cột (không lưu dữ liệu nhạy cảm): `🌐 Mở Salework`, `Tạo báo cáo`, `📋 Chép báo cáo`, `Đóng cửa sổ & xoá đăng nhập`. |
| 28 | **Thống Kê Đơn Hoàn / Đơn Hủy** | `#statsModal` (`L22586–L22606`) | Bảng đối chiếu khớp bảng chính, bảng Theo Sàn, bảng Đơn Hoàn & Đơn Hủy theo ngày/tuần/tháng + bảng con `#statsDetail`. |
| 29 | **Bảng Đơn Hủy & Form Tạo Tay** | `#cancelledOrdersModal` (`L22608–L22675`) & `#cancelledCreateModal` (`L22677–L22694`) | Quản lý danh sách đơn hủy và form tạo tay 1 đơn hủy (`#ccCode`, `#ccPlatform`, `#ccQty`, `#ccProduct`, `#ccColor`, `#ccSize`, `#ccReceived`). |
| 30 | **Kho Lưu Trữ 24h** | `#archivedOrdersModal` (`L22696–L22868`) | Quản lý đơn đã nhận quá 24h, lọc, khôi phục, xuất Excel, sao lưu ổ cứng, xóa. |
| 31 | **Popup / Banner nổi phụ trợ** | `#smartSuggestBox` (`L22405`), `#ocrLoadingBanner` (`L2725–L2731`), `#appGlobalToastContainer` (tạo động `L16413`), `#bulkActionBar` (`L22419`) | Gợi ý SP/Màu/Size bám theo ô đang gõ; tiến trình OCR; thông báo toast góc dưới phải; thanh thao tác hàng loạt dưới giữa màn hình. |

---

## 6. Quy tắc hiển thị

### 6.1. Cảnh báo Vàng / Đỏ & ngưỡng giờ (`L8381–L8565`, `L12254–L12304`)
- **Phạm vi đủ điều kiện cảnh báo (v1.9.68, `isOrderAlertEligible`, `L8391–L8400`)**:
  - Chỉ áp dụng cho đơn **đang chờ nhận** (`isOrderPending`: không phải `Hàng đã trả`/`Đã nhận`, không phải `Không cần nhận`, không phải `Bị mất`/`Hư hỏng`).
  - **Và** thỏa 1 trong 2 điều kiện:
    1. Đơn đã có **thời gian hàng về** (`getOrderArrivedAt(o)`: `o.returnedAt` hoặc `o.rawDetails.returnSuccessTime`).
    2. Hoặc đơn thuộc nhóm **Hủy sau chuẩn bị** (`isCancelAfterPrepOrder(o)`: `platformStatus` / `rawDetails.returnStatus` / `rawDetails.cancelType` / `note` khớp `/huy\s*sau\s*(khi\s*)?chuan\s*bi/`).
  - Các đơn chưa về và không phải "Hủy sau chuẩn bị" (VD: *Đang về*, *Chờ khách gửi hàng*) có `level: 'none'`, **không** lên Báo Vàng/Báo Đỏ.
- **Ngưỡng thời gian**:
  - **Trường hợp 1 — Có thời gian hàng về (`fromArrival = true`)** (`L8381`, `L8419`):
    - `< 12 giờ`: `safe` — nền xanh nhạt (`bg-sky-50 border-sky-300 text-sky-900`), nhãn `⏳ An toàn (< 12h từ lúc về)`.
    - `>= 12 giờ` và `< 24 giờ`: **Báo Vàng** (`warning` / `soon`) — nền vàng (`bg-amber-100 border-2 border-amber-500 text-amber-950`), nhãn `⚠️ BÁO VÀNG (12h từ lúc về)`, tag ở cột STT `⚠️ 12h`.
    - `>= 24 giờ`: **Báo Đỏ** (`danger` / `overdue`) — nền đỏ (`bg-rose-100 border-2 border-rose-500 text-rose-950`, chữ `animate-pulse`), nhãn `🚨 BÁO ĐỎ (+24h từ lúc về)`, tag ở cột STT `🚨 Quá 24h`.
  - **Trường hợp 2 — Chưa có thời gian hàng về & là "Hủy sau chuẩn bị" (`fromArrival = false`)** (`L8419`):
    - Tính từ `order.returnDate` (**tuyệt đối không fallback sang `shipDate`**).
    - `< 48 giờ` (< 2 ngày): `safe` (`⏳ An toàn (< 2 ngày)`).
    - `>= 48 giờ` và `< 72 giờ`: **Báo Vàng** (`⚠️ BÁO VÀNG (2 ngày)`, tag STT `⚠️ 2 ngày`).
    - `>= 72 giờ` (3 ngày): **Báo Đỏ** (`🚨 BÁO ĐỎ (+3 ngày)`, tag STT `🚨 Quá 3 ngày`).
    - Ngoài ra trong `getDeadlineStatus` (`L8519–L8526`): nếu có nhập `order.deadline` cụ thể (và chưa có `fromArrival`, là `isCancelAfterPrepOrder`), thì quá `order.deadline` → `overdue`, còn `<= 48 giờ` trước `order.deadline` → `upcoming`.
- **Ngoại lệ ở các màn hình phụ**:
  - **Bảng Đơn Hủy & Thông báo Hạn Xử Lý (`computeDeadlineInfo`, `L19409–L19448`)**: dùng `DEADLINE_DAYS = 3` (hạn = `o.deadline` hoặc `returnDate + 3 ngày`); còn `<= 24 giờ` (tức đã qua >= 2 ngày) → `upcoming`, `< 0` → `overdue`.
  - **Modal Khiếu Nại Quá Hạn (`getOverdueOrdersList`, `L3413–L3418`, `isOrderOverdue`, `L8596–L8600`)**: lấy đơn đang chờ có `elapsed.level === 'danger'` **hoặc** `rawElapsedHours >= 72` (tức kể cả đơn không đủ `alertEligible` nếu đã quá 72h từ ngày hoàn vẫn vào danh sách khiếu nại!).

### 6.2. Trạng thái đơn & màu sắc
- **Trạng thái Nhận Tại Kho (`order.status`, CSS `L84–L103`, `getStatusClass` `L10582–L10587`, `<select>` `L12247–L12250`)**:
  - `Hàng đã trả` (hoặc `Đã nhận`): `.status-returned` — nền xanh lá `#dcfce7`, chữ `#15803d`, viền `#86efac` (`🟢 Đã nhận (Về kho)`).
  - `Chưa trả` (mặc định khi đang chờ): `.status-unreturned` — nền vàng nhạt `#fef3c7`, chữ `#b45309`, viền `#fde68a` (`⏳ Chưa nhận (Chờ về)`).
  - `Không cần nhận`: `.status-unneeded` — nền xám `#f1f5f9`, chữ `#64748b`, viền `#cbd5e1` (`⚪ Không cần nhận (Hủy trước gửi)`).
  - `Bị mất`: `.status-lost` — nền đỏ nhạt `#fee2e2`, chữ `#b91c1c`, viền `#fca5a5` (`🔴 Bị mất (Khiếu nại)`).
  - *(Lưu ý: trạng thái `Hư hỏng` không có trong `<select>` nhưng được code tính chung vào tab `lost` ở `L11932`, `L13043` và loại trừ khỏi `isOrderPending` ở `L8371`.)*
- **Loại đơn (`order.orderType`, `L12149–L12154`)**:
  - `Giao không thành công`: vàng (`bg-amber-50 text-amber-900 border-amber-300`)
  - `Trả hàng/Hoàn tiền`: tím chàm (`bg-indigo-50 text-indigo-800 border-indigo-300`)
  - `Đơn hủy`: đỏ hồng (`bg-rose-50 text-rose-800 border-rose-300`)
  - `THẤT THU`: nền tối chữ vàng (`bg-slate-800 text-amber-300 border-slate-600`)
- **Trạng thái kiện từ sàn (`order.platformStatus`, `L12175–L12191`)**:
  - Chứa `chờ bạn xác nhận` / `chờ phản hồi` / `khiếu nại`: vàng đậm (`bg-amber-100 text-amber-950`)
  - Chứa `đã hoàn tiền` / `hoàn tiền thành công` / bắt đầu bằng `đã về`: xanh lá (`bg-emerald-100 text-emerald-950`)
  - Chứa `giao hàng hoàn trả thành công` / `trả hàng thành công`: xanh dương (`bg-blue-100 text-blue-950`)
  - Chứa `vận chuyển` / `chuyển hoàn`: xanh trời (`bg-sky-100 text-sky-950`)
  - Chứa `hủy`: đỏ hồng (`bg-rose-100 text-rose-950`)
  - Chứa `đóng` / `thất thu`: nền tối (`bg-slate-800 text-slate-100`)
- **Chấm màu Sàn (`L12128`)**: Shopee `#f97316` (cam), TikTok Shop `#111827` (đen), Lazada `#2563eb` (xanh dương), Khác `#94a3b8` (xám).
- **Hoàn 1 phần (`isPartialReturn`)**: badge cam `⚡ Hoàn 1 phần` (`bg-amber-500 text-white` / `bg-amber-100 text-amber-900`, `L12381`, `L12439`, `L12460`).
- **Dòng khớp tìm kiếm / quét trúng**:
  - Khớp tìm kiếm: `.row-match` nền vàng `#fef08a` (`L104–L106`).
  - Nhảy tới dòng đầu khớp tìm kiếm: `.search-locate-flash` nền `#fde047`, viền cam `3px solid #f97316` trong 2,5s (`L324–L325`, `L12564`).
  - Quét trúng từ ĐT/PC hoặc định vị thông báo: `.scan-flash-green` nhấp nháy xanh lá 2,8s (`L109–L159`, gỡ sau 3,2s ở `L16565`).
  - Dòng được tích chọn: `bg-blue-50/80 ring-1 ring-blue-300` (`L12408`).

### 6.3. Quy tắc khóa toàn bảng & khóa từng ô (`L8860–L8911`, `L9150–L9171`, `L9224–L9648`)
- **Mật khẩu mặc định**: `123456` (lưu dạng băm `h1_...` trong `PWD_HASH_STORAGE_KEY = 'shopee_excel_pwd_hash_v2'`).
- **Khóa toàn bảng (`isLocked`, `LOCK_STATE_KEY`)**:
  - Khi khóa: ẩn nút `#btnAddOrder`, đổi nút `#btnLockToggle` sang đỏ `🔒 Đang Khóa (Bấm mở)`, đổi nút Nhập/Xuất, Nạp Shopee/TikTok, Quét ĐT sang biểu tượng khóa, vô hiệu hóa toàn bộ `input`/`select` trong `#orderTableBody` (`L9645–L9647`). Các thao tác nhạy cảm gọi `requireUnlocked(actionName)` (ngoại trừ `sheetSyncRunning` và `folderWatchRunning` chạy nền, `L9459–L9460`).
  - **Ngoại lệ**: ô `Nhập kho` của kế toán trong Bảng Tổng Kết Phân Loại mang `data-always-unlocked="true"` nên **luôn cho nhập** kể cả khi bảng chính đang khóa (`L13259`, `L13269`).
- **Khóa từng ô (`cellLocks`, `CELL_LOCKS_KEY`, key dạng `${orderIdx}:${field}` hoặc `${orderIdx}:${itemIdx}:${field}`)**:
  - Ô đang khóa hiển thị nền `bg-slate-100/90` kèm biểu tượng `🔒` đỏ (`L11863–L11871`), không sửa được; bấm 1 lần mở `promptUnlockSingleCell`, chuột phải mở `#cellActionModal`.
  - Khi xóa đơn, các hàm `shiftCellLocksAfterOrderDelete` (`L10949`) và `shiftCellEditHistoryAfterOrderDelete` (`L10966`) tự dịch chuyển chỉ số dòng của các ô khóa và lịch sử ô.
  - Đơn có bất kỳ ô nào đang khóa (`hasLockedCellsInOrder`) sẽ được bảo vệ khỏi việc xóa tự động khi dọn trùng (`L4546`, `L4596`) và khi xóa dòng SP (`L10820`).

### 6.4. Quy tắc hiển thị & xử lý đơn trùng
- **Trong Bảng Tạo Đơn Nhanh (`L17396–L17520`)**: khi dán mã đã tồn tại trên bảng chính (`findExistingMainOrder`), dòng được tô nền vàng + viền trái vàng đậm (`bg-amber-50/70 border-l-4 border-amber-500`), **tự động bỏ tích checkbox** (`checked: !isDup`), hiện badge `⚠️ ĐÃ CÓ TRÊN BẢNG: Đã nhận kho (...)` hoặc `Chờ về (Chưa nhận)`.
- **Khi quét trùng từ điện thoại (`L16573–L16575`, `L16750`, `L20430`)**: phát âm thanh cảnh báo trùng (`playScanDuplicateTone`), hiện toast vàng `⚠️ [QUÉT LẠI] ... đã trả từ trước`, nháy xanh định vị dòng đó, ghi vào `#batchSessionModal` với badge vàng `Trùng lặp` (không cộng vào tổng kiện hợp lệ của ca), và ghi 1 bản ghi loại `duplicate_scan` trong Trung tâm Thông báo.
- **Dọn trùng giữa các bảng (`autoRemoveShadowDuplicates` `L4529–L4570`, `removeDuplicatesAcrossTables` `L4572–L4611`)**: chỉ gộp/gỡ các bản trùng rõ ràng (trùng Kho lưu trữ/Bảng đơn hủy mà chưa có dữ liệu kho, yêu cầu trả hàng cũ đã có yêu cầu mới trên Sheet, đơn tạm `"Chưa rõ sản phẩm"` tìm thấy đúng 1 đơn thật); **giữ nguyên** các đơn có cùng `orderId` nhưng khác kiện/MVĐ (`countMainDuplicateOrderIds`, `L4499–L4505`) và lưu bản sao dự phòng vào `localStorage` (`auto_shadow_removed_backup` / `dedupe_removed_orders_backup`).

---

## 7. Phím tắt, thông báo, âm thanh

### 7.1. Phím tắt bàn phím
| Phím tắt | Ngữ cảnh | Hành vi (vị trí code) |
|---|---|---|
| `Alt + N` | Toàn cục (khi không khóa) | Thêm 1 đơn mới và tự focus vào ô MVĐ/Mã đơn (`addOrderAndFocus`, `L14430–L14435`) |
| `Ctrl + S` / `Cmd + S` | Toàn cục | Chặn lưu mặc định trình duyệt, gọi `saveDirectlyToHtmlFile()` (`L20210–L20215`) |
| `Ctrl + V` | Toàn cục (khi đang focus 1 dòng đơn, `activePhotoOrderIndex !== null`) | Chặn dán mặc định nếu clipboard có ảnh, tự nén & lưu ảnh vào dòng đó (`paste` listener, `L11118–L11133`) |
| `Enter` | Trong `#searchInput` | Tìm khớp tuyệt đối mã vận đơn/mã đơn (hỗ trợ súng quét mã vạch PC) và nháy xanh định vị dòng (`L14441–L14459`) |
| `Enter` | Trong `#gotoPageInput` | Nhảy thẳng đến số trang đã nhập (`goToTablePage`, `L1072`) |
| `Enter` | Trong `#ccCode` (form tạo đơn hủy) | Gửi form tạo đơn hủy (`submitCancelledCreateForm`, `L22681`) |
| `Enter` | Khi `#customDialogModal` đang mở | Xác nhận OK (`closeCustomDialog(true)`, `L9076–L9079`) |
| `Enter` / `Escape` | Khi đang sửa trực tiếp 1 ô bảng chính (`handleCellEditKeyDown`, `L11553–L11571`) | `Enter`: blur để chốt & ghi lịch sử ô; `Escape`: hoàn tác về `data-initial-val` và thoát sửa |
| `ArrowUp` / `ArrowDown` / `Enter` / `Tab` / `Escape` | Trong `handleCellKeyDown` (`L13968–L14084`) | Điều khiển `#smartSuggestBox` (lên/xuống chọn gợi ý, Enter/Tab chấp nhận, Esc đóng); hoặc di chuyển kiểu Excel giữa các dòng/ô và tạo dòng mới khi Enter ở ô cuối (`note`). *(Lưu ý: `renderCellInput` hiện gắn `handleCellEditKeyDown`, chưa thấy chỗ nào gắn `handleCellKeyDown` — xem mục 9)* |
| `Escape` | Toàn cục | Đóng các lớp phủ đang mở: `#zoomModal` & `#notificationModal` (`L9061–L9070`), `#customDialogModal` (`L9073`), `#customPromptModal` (`L9083`), dropdown menu header (`L9577`), `#cellDetailPopover` (`L11583`), `#appSidebar` (`L18095`), `#archivedOrdersModal` (`L20161`) |
| `F12`, `Ctrl+Shift+I/J/C`, `Ctrl+U` | Toàn cục | **Bị chặn** (`preventDefault`), ghi nhật ký bảo mật `⚠️ CẢNH BÁO` và hiện cảnh báo chống kiểm tra mã nguồn (`L9137–L9148`) |

### 7.2. Hệ thống thông báo
1. **Floating Toast (`showAppToast` / `showToast`, `L16411–L16446`)**: hộp nổi góc dưới phải (`#appGlobalToastContainer`), 4 màu `success` (xanh lá), `warning` (vàng), `info` (xanh dương), `error` (đỏ), mặc định tự biến mất sau `3200ms`.
2. **Thông báo In-App (`addAppNotification`, `L16161–L16183`)**: lưu vào `localStorage` (`shopee_app_notifications_v1`), tăng `#unreadNotifBadge`, gồm 8 giá trị `type` (`phone_scan`, `phone_new_order`, `new_order_import`, `excel_update`, `excel_import`, `duplicate_scan`, `unknown_scan`, `deadline_warning`) chia vào 6 tab lọc (`all`, `phone`, `new_order`, `excel`, `deadline`, `warning`).
3. **Nhắc hạn định kỳ (`checkDeadlineNotifications`, `L19492–L19520`)**: chạy ở mốc `5s`, `25s`, `90s` sau khi mở app và lặp lại mỗi `30 phút`; chống báo trùng bằng `deadline_notified_v1`, bắn cả Toast (`6000ms`) lẫn mục trong Trung tâm Thông báo.
4. **Thông báo Desktop Windows (`window.desktopDiskAPI.sendNotification`, `L14833–L14840`)**: gọi qua Electron IPC (`send-desktop-notification`) mỗi khi điện thoại quét đơn thành công / quét trùng / không tìm thấy mã.
5. **Hộp thoại In-App (`appAlert` / `appConfirm` / `appPrompt`, `L8928–L9058`)**: thay thế `window.alert` (được override) nhằm tránh treo focus input trên Windows Electron.

### 7.3. Âm thanh phản hồi (Web Audio API — không cần file mp3)
| Hàm | Dòng | Đặc tính âm thanh | Nơi kích hoạt |
|---|---|---|---|
| `playBeepSound(freq, durationMs)` | `L14698–L14716` | Sóng `sine`, mặc định `1046 Hz` / `110 ms`, gain `0.18` | Nạp Excel xong (`1300Hz/150ms`, `L6276`, `L7023`), OCR xong (`1400Hz/150ms`, `L7348`), Camera PC bắt mã/OCR (`1000–1480Hz`, `L14226–L14332`), ĐT gửi ảnh/nhãn (`880–980Hz`, `L16019`, `L16961`) |
| `playScanSuccessTone()` | `L20262–L20281` | Sóng `sine` 2 nốt tăng `880 Hz → 1320 Hz` (`190 ms`) | Quét điện thoại khớp đơn hoàn/đơn hủy thành công hoặc tạo đơn mới (`L16005`, `L16778`, `L16806`, `L17993`, `L18020`) |
| `playScanDuplicateTone()` | `L20283–L20303` | Sóng `triangle` 3 tiếng bíp nhanh `587 Hz` tại `0s / 0.08s / 0.16s` | Quét trùng đơn đã nhận từ trước (`L15957`, `L16750`, `L17971`, `L17978`) |
| `playScanUnknownTone()` | `L20305–L20324` | Sóng `sawtooth` trượt giảm `320 Hz → 180 Hz` (`290 ms`) | Quét mã không khớp đơn nào trên bảng (`L16817`) |

---

## 8. Các hàm dữ liệu giao diện gọi tới (Hợp đồng cần lõi nghiệp vụ v2 cung cấp)

### 8.1. Đọc / Ghi đơn chính & Lưu trữ đa lớp
| Hàm | Dòng | Việc thực hiện |
|---|---|---|
| `installDiskMirror` (IIFE) | `L3065–L3127` | Đồng bộ 2 chiều các khóa `localStorage` phụ với ổ cứng qua `desktopDiskAPI.auxReadAllSync()` & `auxSave(key, value)` (debounce 300ms); xử lý vượt hạn mức 5MB cho `shopee_archived_orders_v1` & `shopee_excel_order_db_v1`. |
| `loadOrders` | `L9844` | Đọc `orders` ban đầu từ `localStorage` (`STORAGE_KEY = 'shopee_excel_sheet_orders_v3_3dates'`) hoặc fallback dữ liệu mẫu. |
| `checkAndSyncHardDriveOnBoot` | `L10488–L10536` | Khi khởi động bản Desktop, đọc `du_lieu_hang_hoan.json` qua `desktopDiskAPI.loadFromDisk()`, sửa mã đơn dạng khoa học (`repairScientificOrderIds`), đồng bộ ảnh vào cache/IndexedDB, làm sạch mã màu số, chuẩn hóa `shipDate`. |
| `saveOrders` / `flushPersistOrdersToDiskAndStorage` | `L10294–L10437` | Cơ chế group-commit (debounce 300ms hoặc ghi ngay `immediate=true`): lưu ảnh mới ra IndexedDB (`PhotoDB`) & file đĩa (`desktopDiskAPI.savePhotoFiles`), tách ảnh base64 khỏi JSON đơn chính rồi ghi ra đĩa (`desktopDiskAPI.saveToDisk`), cập nhật trễ IndexedDB (`SafeStore`) & `localStorage` (bỏ qua `localStorage` nếu `> 8000` đơn), đồng thời gọi `reconcileUnmatchedScans`, `syncReturnWatchFlags`, `schedulePhoneWatchSync`, `schedulePhoneSnapshotSync`. |
| `openSafeStoreDb` / `saveOrdersToSafeStore` / `loadOrdersFromSafeStore` | `L10088–L10157` | Lớp lưu trữ IndexedDB dung lượng lớn cho danh sách đơn. |
| `openPhotoDb` / `savePhotoToDb` / `deletePhotoFromDb` / `clearAllPhotosFromDb` / `loadAllPhotosIntoMemory` / `restoreMissingPhotosFromDisk` | `L3137–L3293` | Quản lý kho ảnh trong IndexedDB `ShopeeReturnPhotoDB_v1`, nạp cache RAM `photoMemoryCache` và phục hồi ảnh thiếu từ thư mục `photos/` qua `desktopDiskAPI.readPhotoFiles`. |
| `compressAndSaveImage` | `L11062–L11116` | Nén ảnh dán/tải lên rồi gắn vào đơn và lưu xuống DB/đĩa. |
| `updateOrderField` / `updateItemField` / `updateOrderPlatform` / `updateOrderType` / `updatePreOrder` / `updateStatus` / `addOrder` / `deleteOrder` / `addProductToOrder` / `removeProductFromOrder` / `resetSampleData` | `L8232`, `L8297`, `L10716–L11060` | Các thao tác CRUD đơn hàng & mặt hàng trong đơn, ghi lịch sử chỉnh sửa ô & nhật ký bảo mật, kích hoạt `saveOrders`. |

### 8.2. Đọc / Ghi các bảng phụ & cấu hình
| Nhóm dữ liệu | Hàm đọc / ghi / xử lý chính | Khóa lưu trữ (`localStorage` + disk aux) |
|---|---|---|
| **Kho lưu trữ 24h** | `loadArchivedOrders` (`L19539`), `saveArchivedOrders` (`L19563`), `checkAndAutoArchiveOrders` (`L19617`, tự chạy mỗi 10 phút), `triggerManualArchiveScan` (`L19701`), `restoreSingleArchivedOrder` (`L19962`), `restoreSelectedArchivedOrders` (`L19985`), `deleteSelectedArchivedOrders` (`L20023`) | `shopee_archived_orders_v1` |
| **Bảng đơn hủy** | `loadCancelledOrders` (`L17783`), `saveCancelledOrders` (`L17791`), `submitCancelledCreateForm` (`L19373`), `setCancelledReceived` (`L19333`), `bulkSetCancelledReceived` (`L19340`), `deleteCancelledOrder` (`L19398`), `bulkDeleteCancelled` (`L19349`), `restoreCancelledPhotosFromDisk` (`L18029`) | `shopee_cancelled_orders_v1` |
| **Đơn đã xóa** | `getDeletedOrders` (`L4200`), `saveDeletedOrders` (`L4206`), `addToDeletedOrders` (`L4210`), `restoreDeletedOrder` (`L21968`), `forgetDeletedOrder` (`L21978`), `clearDeletedOrders` (`L21983`) | `shopee_deleted_orders_v1` |
| **Hộp đơn mới (ĐT)** | `saveNewOrdersInbox` (`L16976`), `importSingleNewOrder` (`L17195`), `importSelectedNewOrders` (`L17228`), `importAllNewOrders` (`L17261`), `deleteSingleNewOrder` (`L17293`), `deleteSelectedNewOrders` (`L17304`), `clearAllNewOrders` (`L17320`) | `shopee_new_orders_inbox_v1` |
| **CSDL tra cứu nạp Excel** | `shopeeDbIdb` (`L3716`), `loadShopeeOrderDb` (`L3730`), `saveShopeeOrderDb` (`L3764`), `lookupShopeeDb` (`L4765`), `autoEnrichAllMissingOrdersFromDb` (`L5291`), `clearShopeeOrderDb` (`L7248`) | `shopee_excel_order_db_v1` (+ IndexedDB) |
| **Danh mục SP & Kế toán & Tab tuần** | `saveCatalog` (`L7385`), `syncCatalogFromExistingOrders` (`L7574`), `saveAccountantStock` (`L9179`), `updateAccountantRestock` (`L9186`), `createCustomWeekTabByKey` (`L7906`), `removeCustomWeekTab` (`L7827`) | `shopee_excel_catalog_v1`, `shopee_excel_accountant_stock_v1`, `shopee_excel_custom_week_tabs_v2`, `shopee_excel_active_import_week_v2` |
| **Bảo mật, Khóa ô, Lịch sử, Thông báo** | `saveCellLocks` (`L9224`), `saveCellEditHistory` (`L9238`), `addAuditLog` (`L8912`), `loadAppNotifications` / `saveAppNotifications` (`L16063–L16095`) | `shopee_excel_pwd_hash_v2`, `shopee_excel_locked_v1`, `shopee_excel_cell_locks_v1`, `ShopeeReturnCellEditHistory_v1`, `shopee_excel_audit_log_v1`, `shopee_app_notifications_v1`, `deadline_notified_v1` |

### 8.3. Nhập Excel / CSV / Dán dữ liệu / OCR
| Hàm | Dòng | Việc thực hiện |
|---|---|---|
| `handlePlatformExcelUpload` / `readWorkbookRowsFromBuffer` / `preparePlatformImportGroups` / `importParsedPlatformFiles` | `L5407–L5591` | Đọc file `.xlsx/.xls/.csv` bằng SheetJS, phân nhóm & chống ghi nửa chừng (`captureImportState` / `commitImportState` / `rollbackImportState`, `L4027–L4056`). |
| `ingestShopeeOnlyRecords` / `ingestShopeeRecords` | `L6307–L7039` | Bộ đọc chuẩn 3 loại file Shopee (Đơn hủy, Giao thất bại, Trả hàng/Hoàn tiền), tách kiện A/B/C bằng `getReturnParcelKey`, gộp SP theo chế độ `supplement`/`replace` (`mergeImportedItems`). |
| `ingestTikTokOnlyRecords` | `L5592–L6306` | Bộ đọc chuẩn TikTok Shop / Salework (`parseSaleworkProductAndVariation`, chọn 1 trong 4 cột giá `tiktok_price_col_mode_v1`, dịch lý do & trạng thái tiếng Anh → Việt). |
| `pushAllDbOrdersToMainTable` / `processShopeeDbPaste` | `L7040–L7162` | Đẩy toàn bộ đơn từ bộ nhớ `shopeeOrderDb` ra bảng chính hoặc nạp từ văn bản dán. |
| `handleCancelledFileUpload` / `mapCancelledRows` | `L17852–L17912` | Nhập file Excel/CSV riêng vào Bảng đơn hủy. |
| `processBulkImportOrders` | `L13861–L13965` | Nạp nhanh nhiều đơn dán từ `#bulkImportModal`. |
| `parseQuickCreateInput` / `importSelectedQuickOrders` / `importAllQuickOrders` | `L17373`, `L17629–L17775` | Tách danh sách mã tạo đơn nhanh, kiểm tra trùng, đẩy ra bảng chính. |
| `restoreReceivedFromPatch` / `applyReceivedPatch` | `L3952–L3991` | Đọc file `khoi_phuc_don_da_nhan.json` để khôi phục trạng thái đã nhận. |
| `triggerPcOcrForOrder` / `triggerPcOcrForMissingOrders` / `parseLabelTextWithCatalog` | `L7276–L7384`, `L16830–L16937` | Chạy Tesseract.js OCR đọc chữ từ ảnh tem & khớp với Danh mục SP của shop. |

### 8.4. Xuất Excel / JSON / HTML / In ấn
| Hàm | Dòng | Việc thực hiện |
|---|---|---|
| `exportToExcel` | `L13354–L13461` | Xuất toàn bộ đơn (theo bộ lọc) & bảng tổng hợp SKU ra `.xlsx`. |
| `exportReturnedOrdersToExcel` | `L13462–L13637` | Xuất 2 sheet `.xlsx`: Chi tiết đơn đã về kho & Tổng kết SKU nhập lại kho. |
| `bulkExportSelectedToExcel` | `L20901–L21023` | Xuất các đơn đang tích chọn ra `.xlsx`. |
| `bulkExportSelectedToHtml` / `generateInteractiveChecklistHtml` | `L21024–L21630` | Xuất file HTML kiểm hàng tương tác độc lập. |
| `exportArchiveMonth` | `L10219–L10292` | Đóng gói đơn theo tháng ra `.xlsx` (tùy chọn dọn khỏi bảng chính). |
| `exportArchivedOrdersToExcel` / `saveArchivedToHardDrive` | `L20053–L20150` | Xuất Kho lưu trữ ra `.xlsx` hoặc lưu JSON ra ổ cứng. |
| `exportWatchlistToExcel` / `exportClaimExcel` | `L22362`, `L3526` | Xuất danh sách Theo dõi / Khiếu nại quá hạn ra `.xlsx`. |
| `exportBackupJson` / `restoreBackupJson` / `backupHardDriveData` | `L3294–L3412`, `L10457–L10474` | Sao lưu & phục hồi toàn bộ đơn + ảnh + dữ liệu phụ ra/từ `.json`. |
| `saveDirectlyToHtmlFile` / `loadEmbeddedDataFromFile` | `L14538–L14694` | Lưu trực tiếp (hoặc tải xuống) 1 file HTML tự chứa toàn bộ dữ liệu nhúng trong `#embeddedAppData`. |
| `printRestockLabel` | `L20576–L20624` | Mở cửa sổ in tem nhiệt Re-stock cho 1 đơn hàng. |

### 8.5. Đồng bộ Điện thoại (MQTT / ntfy), Google Sheet, Folder Watcher & Salework
| Nhóm | Hàm gọi tới | Dòng | Việc thực hiện |
|---|---|---|---|
| **Điện thoại (MQTT + ntfy)** | `initPcPhoneReceiver`, `processIncomingPhonePayload`, `handleIncomingPhoneBarcode`, `applyIncomingPhoneBarcode`, `handleIncomingPhotoChunk`, `attachPhotoToOrderByCode`, `handleIncomingSmartLabel`, `handleCancelledOrderScan`, `sendAckToPhone`, `broadcastManifestToPhone`, `broadcastOrdersSnapshot`, `sendPhoneDelta`, `sendPhoneWatchSync`, `syncCloudCatchup`, `broadcastPcHello`, `reconcileUnmatchedScans`, `promoteUnmatchedScans`, `regeneratePhoneRoom` | `L14785–L16056`, `L16595–L16970`, `L17955–L18028`, `L20184`, `L20757–L20820` | Kết nối MQTT + ntfy theo `phoneRoomId` (`shopee_excel_phone_room_v1`), nhận mã quét/ảnh ghép mảnh/đơn hủy/đơn mới, đảm bảo lưu bền trước khi gửi ACK (`persistence-before-ACK`), đồng bộ ngược danh sách đơn & cảnh báo sang điện thoại (nén `deflateRawBase64`), kéo bù quét offline 12h qua. |
| **Google Sheet (Shopee)** | `runSheetSync`, `fetchSheetSyncText`, `normalizeSheetRows`, `keepLatestRequestPerOrder`, `sheetRowsToParcels`, `mergeSheetParcels`, `applyShopeeClaimDeadlines`, `saveSheetSyncFromModal` | `L18165–L18636` | Tự động chạy mỗi 10 phút (`SHEET_SYNC_INTERVAL_MS`), đọc Apps Script Web App (qua `desktopDiskAPI.fetchSheetText` hoặc `fetch`), bỏ qua nếu hash không đổi, nạp/cập nhật vào bảng chính/kho lưu trữ/đơn hủy. |
| **Theo dõi thư mục TikTok** | `runFolderWatch`, `startFolderWatchTimer`, `pickFolderWatchDir`, `applyFolderWatchDir`, `saveFolderWatchFromModal` | `L18637–L18743`, `L18879–L18923` | Quét thư mục mỗi 15 giây qua `desktopDiskAPI.listWatchFolder` & `readWatchFile`, tự nạp file TikTok mới mà không sửa/xóa file gốc. |
| **Salework Tự động & Chẩn đoán** | `saleworkAutoLogin`, `saleworkAutoSave`, `saleworkAutoRunNow`, `saleworkAutoStop`, `saleworkAutoForget`, `saleworkAutoWatchToggle`, `supersedeStaleRowsFromSalework`, `startSaleworkDiag`, `refreshSaleworkDiag`, `stopSaleworkDiag` | `L18744–L18878` | Gọi IPC `desktopDiskAPI.saleworkAuto*` và `saleworkDiag*` để tự động xuất Excel "Đang chờ hoàn" trên Salework rồi nạp vào bảng. |

### 8.6. Toàn bộ cầu nối Electron IPC (`window.desktopDiskAPI` trong `preload.js`, `L3–L34`)
- **Dữ liệu & Ảnh**: `loadFromDisk()`, `saveToDisk(data)`, `savePhotoFiles(photos)`, `readPhotoFiles(ids)`, `auxReadAllSync()`, `auxSave(key, value)`.
- **Hệ thống & Sao lưu**: `openDataFolder()`, `backupToCustomFolder(data)`, `runAutoBackup()`, `sendNotification(title, body)`, `getAutoStart()`, `setAutoStart(enabled)`, `getAppVersion()`, `getAppVersionSync()`.
- **Đồng bộ ngoài**: `fetchSheetText(url)`, `chooseWatchFolder()`, `setWatchFolder(dir)`, `listWatchFolder()`, `readWatchFile(name)`.
- **Salework**: `saleworkDiagStart(url)`, `saleworkDiagReport()`, `saleworkDiagStop(clear)`, `saleworkAutoStatus()`, `saleworkAutoLogin(url)`, `saleworkAutoSave()`, `saleworkAutoForget()`, `saleworkAutoWatch(on)`, `saleworkAutoStop()`, `saleworkAutoRun()`.

---

## 9. Mục "Chưa chắc chắn" & các điểm bất nhất tìm thấy trong code cũ

Các điểm sau cần làm rõ / xác nhận với chủ dự án hoặc nhóm lõi nghiệp vụ trước khi dựng lại giao diện React v2:

1. **Trường ghi chú riêng của shop không đồng nhất (`myNote` vs `userNote`)**:
   - Bảng chính (`L12477`), tìm kiếm (`L10629`, `L10709`), xuất Excel (`L13386`, `L20948`, `L22391`) dùng **`order.myNote`**.
   - Trong khi đó `convertInboxItemToOrder` (`L17185`), `convertQuickItemToMainOrder` (`L17619`), và toàn bộ **Kho lưu trữ 24h** (`L19797`, `L19914–L19915`, `L20088`) lại đọc/khởi tạo **`order.userNote`**. Trong v2 cần thống nhất thành 1 trường (và importer phải giữ tương thích đọc cả hai).
2. **Nút mở Camera PC (`#pcCameraScannerModal`) không thấy xuất hiện trên giao diện**:
   - Modal `#pcCameraScannerModal` (`L2187–L2254`) và hàm `openPcCameraScannerModal()` (`L14107`) tồn tại đầy đủ trong file, nhưng tìm toàn bộ file không có nút HTML nào gọi `openPcCameraScannerModal()` (tương tự `duplicateOrder` ở `L10884` không có nút gọi). *(Suy đoán)*: có thể đã bị gỡ khỏi thanh công cụ ở các bản refactor gọn giao diện sau này; cần hỏi chủ dự án v2 có cần giữ tính năng quét bằng webcam PC hay chỉ dùng điện thoại + súng quét USB.
3. **Một số lời gọi hàm / ID không còn tồn tại trong file HTML gốc**:
   - `openPhotoZoomDirect(...)` được gọi khi bấm xem ảnh trong Hộp Đơn Mới (`L17052`) nhưng không tìm thấy định nghĩa hàm này trong file.
   - `updatePlatformFilterUI()` được gọi trong `resetAllFiltersQuick()` (`L8666`) trong khi tên hàm thật là `updatePlatformSwitcherUI()` (`L8124`).
   - `exportJsonFile()` được gọi ở nhánh fallback web của `backupHardDriveData()` (`L10472`) trong khi tên hàm thật là `exportBackupJson()` (`L3294`).
   - `switchMainView()` (`L7941–L7943`) tìm `#btnViewOrdersTab`, `#btnViewSummaryTab`, `#btnViewBothTab` nhưng các nút này không còn trong HTML (chỉ còn 2 nút chuyển view ở `#stickyBottomDock`).
   - `#mobileServerSelect` (`L2105–L2109`) có các giá trị `github`, `brewpage`, `beacon`, nhưng bảng `MOBILE_SERVERS` trong JS (`L14719–L14723`) lại định nghĩa khóa `github`, `short`, `htmlpreview` (nên chọn `brewpage`/`beacon` thực tế sẽ rơi về `github`).
4. **Cột và bộ lọc `preOrder` (Hàng đặt trước)**:
   - Code vẫn còn biến `currentPreOrderFilter` (có comment *"bộ lọc đã gỡ khỏi giao diện (v1.9.53), luôn 'all'"* tại `L11591`) và biến `preOrderCellHtml` (`L12213–L12232`) chưa render ra cột bảng chính; hiện `preOrder` chỉ hiển thị trong Accordion chi tiết (`L12726`). Cần xác nhận v2 chỉ hiển thị trong chi tiết hay có cần bộ lọc riêng.
5. **Phím điều hướng Excel (`handleCellKeyDown`, `L13968–L14084`)**:
   - Hàm `handleCellKeyDown` hỗ trợ mũi tên lên/xuống giữa các hàng và Enter nhảy sang ô kế tiếp + điều khiển `#smartSuggestBox`, nhưng trong `renderCellInput` (`L11895`) ô nhập chỉ gắn `onkeydown="handleCellEditKeyDown(...)"` (chỉ xử lý `Enter` blur và `Escape` hủy). *(Suy đoán)*: có thể là hàm từ phiên bản bảng input luôn mở trước khi chuyển sang cơ chế click-xem/dblclick-sửa; cần xác nhận trong v2 có muốn khôi phục điều hướng bàn phím kiểu Excel không.
6. **Quy tắc cảnh báo quá hạn giữa Bảng chính, Đơn hủy và Khiếu nại 1-Click**:
   - Bảng chính dùng quy tắc mới v1.9.68: chỉ cảnh báo đơn đã có thời gian về (`12h` vàng / `24h` đỏ) hoặc `Hủy sau chuẩn bị` (`48h` vàng / `72h` đỏ).
   - Bảng Đơn Hủy dùng `DEADLINE_DAYS = 3` (`computeDeadlineInfo`: còn <= 24h là sắp hạn, < 0 là quá hạn) không phân biệt đã về shop hay chưa.
   - Modal Khiếu Nại Quá Hạn (`isOrderOverdue`, `L8596`) lấy cả đơn có `rawElapsedHours >= 72` bất kể `alertEligible`.
   - Cần xác nhận với chủ dự án giữ đúng 3 quy tắc khác nhau này hay chuẩn hóa chung trong v2.
7. **Sửa cột `Mã Vận Đơn` trên bảng chính**:
   - Hiện tại khi đơn đã có `returnTrack` hoặc `forwardTrack`, ô cột 12 chỉ hiển thị dạng text bấm-để-copy chứ không có nút sửa trực tiếp (`L12311–L12350`), khác với trường hợp trống (`L12358`). *(Suy đoán)*: cố ý để tránh sửa nhầm MVĐ gốc, cần xác nhận khi thiết kế v2.
8. **Hành vi `APP_VERSION_NUMBER` vs `package.json`**:
   - Hằng `APP_VERSION_NUMBER = '1.9.24'` (`L8006`) vẫn giữ số cũ làm fallback web, còn bản Desktop đọc phiên bản thật `1.9.75` từ `desktopDiskAPI.getAppVersionSync()` (`L435`, `L854`, `L8086`).
9. **Hỗ trợ sàn `Lazada` / `Khác`**:
   - Xuất hiện trong `<select>` sàn của từng dòng, bảng Tạo đơn nhanh, và `<select id="platformFilterSelect">` ẩn, nhưng không có nút tab lớn trên thanh chọn sàn (`#platformBigTabs` chỉ có Shopee, TikTok Shop, Cả 2 sàn). Cần làm rõ mức độ ưu tiên hiển thị Lazada trong v2.
