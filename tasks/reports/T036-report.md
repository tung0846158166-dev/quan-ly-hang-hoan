# Báo cáo T036 - App iPhone: GỌN và HIỆN ĐẠI hơn nữa

## 1. Phạm vi & tệp sửa
Chỉ sửa trong `ios-app/` (cùng báo cáo này và `TASKS.md`). Không đụng `v2/src/QuanLyHangHoan.Data/`, `v2/src/QuanLyHangHoan.Core/`, hay bất kỳ tệp test nào ngoài `ios-app/`. Không thiếu API nào ở `v2/src/QuanLyHangHoan.Data/`.

### Danh sách tệp sửa
- `ios-app/App/Sources/Theme.swift`:
  - Tập trung toàn bộ hằng số thiết kế theo lưới 8pt vào `Theme`: `cardRadius = 14`, `smallRadius = 10`, `cardPadding = 12`, `cardSpacing = 8`, `sectionSpacing = 10`, `screenPadding = 14`, `minTapTarget = 44`, `demoBannerHeight = 24`.
  - Bổ sung thang cỡ chữ chuẩn `Theme.Typography` (tự co giãn theo Dynamic Type, mặc định 17 đậm tiêu đề thanh trên / 15 chữ chính / 13–12 chữ phụ / 11 nhãn nhỏ / số lớn chỉ ở hero).
  - Thu gọn `DemoModeBanner` còn đúng 1 dòng mảnh cao 24pt, chữ 11pt, nút "Tắt" nhỏ, nền `.ultraThinMaterial` phủ sạch lên vùng trạng thái (`ignoresSafeAreaEdges: .top`).
  - Tinh gọn `CardModifier`, `PlatformBadge`, `StatusPill`, `SectionTitleRow`, `FilterTile` dùng chung các hằng số mới và vẫn đảm bảo vùng bấm tối thiểu 44pt.
- `ios-app/App/Sources/ScannerApp.swift`:
  - Đưa `DemoModeBanner` lên TRÊN `TabView` trong `VStack(spacing: 0)` (bỏ `.safeAreaInset(edge: .top)` ngoài `TabView` vốn gây đè/cắt các nút thanh trên dưới dải DỮ LIỆU MẪU).
  - Dùng `TabView` chuẩn hệ thống (không thanh tab nổi tự vẽ).
  - Bỏ huy hiệu vô nghĩa trên tab **Tổng quan**; thêm huy hiệu số đơn quá hạn đỏ (`store.overdueCount`) ở tab **Đơn**; giữ huy hiệu số lượt chờ PC xác nhận (`engine.pendingCount`) ở tab **Lịch sử**.
- `ios-app/App/Sources/HomeView.swift`:
  - Chuyển sang thanh trên inline gọn (`.navigationBarTitleDisplayMode(.inline)`), bỏ tiêu đề 34pt; đưa 1 dòng chào theo khung giờ + ngày ngắn (`Chào buổi sáng · T3, 24/11`) sang góc trái thanh trên.
  - Bỏ thanh "Quét ngay" cam to ở đáy (`.safeAreaInset(edge: .bottom)`), thay bằng nút "Quét" nhỏ gọn ngay trên thanh trên bên cạnh nút làm mới & chuông thông báo → không còn lớp chồng đáy che nội dung.
  - Thu gọn Hero: vòng tiến độ giảm từ 112pt xuống 84pt kèm Quá hạn / Sắp đến hạn bên phải.
  - Thêm dải 4 số liệu ngang gọn (`metricsStrip`: Chờ về / Nhận hôm nay / Quá hạn / Sắp hạn) ngay dưới Hero, mỗi ô bấm được để nhảy lọc sang tab Đơn.
  - Bỏ băng cuộn ngang cồng kềnh (`horizontalDeck`), chuyển **"Sản phẩm hoàn nhiều nhất"**, **"Hàng hoàn về"**, **"Cần xử lý ngay"** (tối đa 3 đơn, giữ vuốt Ghi chú/Theo dõi), và **"Theo sàn"** (kèm chip Hoàn 1 phần) thành các thẻ xếp dọc gọn (~90–140pt mỗi thẻ). Giữ nguyên quy tắc T035: thẻ Sản phẩm luôn hiển thị cả khi trống.
- `ios-app/App/Sources/OrdersView.swift`:
  - Tiêu đề inline gọn (`.navigationBarTitleDisplayMode(.inline)`).
  - Thu hàng lối tắt (**Hoàn về / Sản phẩm / Đơn hủy / Sau CB**) từ 4 thẻ lớn thành 1 hàng chip nhỏ gọn (vẫn luôn hiển thị cả khi chưa có dữ liệu).
  - Thu mỗi hàng đơn từ 5–6 dòng xuống đúng **3 dòng gọn**:
    1. Huy hiệu sàn nhỏ + mã vận đơn đậm + hạn tô màu bên phải (bỏ dòng "Mã đơn", vẫn có đủ ở màn Chi tiết).
    2. Tên sản phẩm 1 dòng cắt bằng `"…"` kèm `"Đen · S"`; với đơn **hoàn một phần** chỉ hiện chip `"Hoàn K/N"` + tên sản phẩm **ĐÃ hoàn** tô màu cam (1 dòng, danh sách đủ xem ở Chi tiết).
    3. Trạng thái bằng chấm tiến trình nhỏ (4 chấm chặng nếu thuộc Hàng hoàn về, hoặc 1 chấm màu + nhãn) + ghi chú rút gọn 1 dòng có biểu tượng ghim (`pin.fill`).
- `ios-app/App/Sources/HistoryView.swift`:
  - Tiêu đề inline gọn (`.navigationBarTitleDisplayMode(.inline)`).
  - Bỏ hoàn toàn thẻ "Hôm nay" thống kê trùng lặp/lệch số với hàng chip bên dưới; dùng duy nhất hàng chip có số đếm (`Tất cả / Hợp lệ / Quét trùng / Không có / Chờ PC`) tính từ một nguồn duy nhất `scopedItems`.
  - Thu gọn dòng lịch sử theo ngày xuống khoảng ~52pt mỗi dòng.
- `ios-app/App/Sources/ScanView.swift`:
  - Bọc trong `NavigationStack` với tiêu đề inline gọn `"Quét"` + trạng thái kết nối PC ngay góc phải thanh trên.
  - Giảm chiều cao khung camera từ ~54% (tối đa 420pt) xuống ~36% (180–280pt) để thấy ngay danh sách **"Quét gần đây"** bên dưới không bị che.
  - Thu gọn hàng điều khiển (Đèn, Zoom, Dừng/Quét, Dán mã — vẫn dùng `.buttonBorderShape(.capsule)` an toàn iOS 16, không dùng `.circle`) và ô nhập tay một dòng.
- `ios-app/App/Sources/SettingsView.swift`:
  - Chuyển sang `.navigationBarTitleDisplayMode(.inline)`, áp cùng thang chữ/khoảng cách gọn từ `Theme.swift` (giữ nguyên toàn bộ chức năng).
- `ios-app/App/Sources/OrderDetailView.swift`:
  - Áp cùng thang đệm (`Theme.cardPadding = 12`), bo góc (`Theme.cardRadius = 14`), khoảng cách (`Theme.sectionSpacing = 10`, `Theme.screenPadding = 14`) cho toàn bộ thẻ và sheet (giữ nguyên toàn bộ chức năng, hành động nhanh, tô màu hoàn một phần).

## 2. So sánh từng màn trước / sau (ước lượng trên iPhone ~390×844pt)

| Màn hình | Trước (T035) | Sau (T036) |
|---|---|---|
| **Tổng quan (`HomeView`)** | Tiêu đề 34pt + Hero lớn (~230pt) + dải ngang + thanh "Quét ngay" cam dưới đáy che nội dung → chỉ thấy **~1,5–2 khối** trên 1 màn. Huy hiệu "15" vô nghĩa trên tab. | Thanh trên inline 1 dòng chào + nút Quét gọn; bỏ thanh đáy; Hero 84pt + dải 4 số liệu + Sản phẩm hoàn nhiều nhất + Hàng hoàn về → thấy ngay **~3,5–4 khối** trên 1 màn. Không còn huy hiệu vô nghĩa. |
| **Đơn hàng (`OrdersView`)** | Tiêu đề 34pt + 4 thẻ lối tắt lớn + thẻ đơn 5–6 dòng (~145pt/đơn) → chỉ thấy **2–3 đơn**/màn. | Tiêu đề inline + 1 hàng chip lối tắt nhỏ + hàng đơn 3 dòng (~78pt/đơn) → thấy **5–6 đơn**/màn. Tab Đơn có huy hiệu số đơn quá hạn đỏ. |
| **Lịch sử (`HistoryView`)** | Tiêu đề 34pt + thẻ "Hôm nay" lặp lại/lệch số với hàng chip + dòng ~74pt → thấy **~3–4 dòng**/màn. | Tiêu đề inline + duy nhất 1 hàng chip đồng nhất 1 nguồn số + dòng ~52pt → thấy **7–8 dòng**/màn. |
| **Quét (`ScanView`)** | Khung camera ~54% chiều cao màn hình + hàng nút tròn lớn → mục "Quét gần đây" bị đẩy xuống dưới/che khuất. | Khung camera ~36% (180–280pt) + hàng nút gọn + ô nhập tay 1 dòng → thấy ngay **2–3 mục "Quét gần đây"** mà không cần cuộn. |
| **Cài đặt & Chi tiết đơn** | Tiêu đề lớn (Cài đặt), đệm thẻ 16–18pt, bo góc 18–22pt. | Tiêu đề inline, đệm thẻ 12pt, bo góc 14pt, khoảng cách 8–10pt đồng nhất toàn app. |

## 3. CHƯA BIÊN DỊCH
- **CHƯA BIÊN DỊCH**: Máy Windows này không có Swift/Xcode nên chưa chạy được `swift test` và chưa build IPA. Cần Claude kiểm tra và đẩy lên GitHub để CI chạy `swift test` và build thật.
- Đã rà soát tĩnh toàn bộ 8 tệp Swift đã sửa trong `ios-app/App/Sources/`:
  - Cân bằng ngoặc `{}`, `()`, `[]` đều bằng `0` trên cả 8 tệp.
  - Không dùng API iOS 17+ chưa bọc `#available` (không có `.buttonBorderShape(.circle)`, `ContentUnavailableView`, `sensoryFeedback`, `navigationDestination(item:)`).
  - Không có `Inbox`/`inbox_push`/`cancelOrder`/`SHP-2026` trong toàn bộ `ios-app/App/Sources/`.
  - Không gọi hàm/lớp `@MainActor` từ ngữ cảnh ngoài main actor; mọi chữ ký hàm và nhãn tham số đối chiếu khớp với `ScannerCore` (`DashboardMetrics`, `ProductMetrics`, `SnapshotStore`, `SyncEngine`).

## 4. Việc cần kiểm trên iPhone thật
1. **Dải DỮ LIỆU MẪU & thanh trên**: bật dữ liệu mẫu, kiểm tra dải mảnh 24pt nằm TRÊN thanh điều hướng trên cả 5 tab, không còn mảnh nút bo tròn bị cắt lòi ra dưới dải; bấm "Tắt" hoạt động bình thường.
2. **Thanh tab & đệm đáy**: cuộn tới mục cuối cùng ở cả 5 tab (Tổng quan, Quét, Đơn, Lịch sử, Cài đặt) kiểm tra mục cuối không bị thanh tab che. Kiểm tra huy hiệu: Tổng quan không có huy hiệu; tab Đơn hiện đúng số đơn quá hạn đỏ; tab Lịch sử hiện đúng số lượt chờ PC.
3. **Tổng quan**: kiểm tra dòng chào + ngày + nút "Quét" trên thanh trên, vòng hero 84pt, dải 4 số liệu ngang, thứ tự xếp dọc gọn (Sản phẩm hoàn nhiều nhất, Hàng hoàn về, Cần xử lý ngay có vuốt Ghi chú/Theo dõi, Theo sàn). Kiểm tra cả khi chưa có dữ liệu (thẻ Sản phẩm vẫn hiện).
4. **Đơn hàng**: kiểm tra xem được 5–6 đơn/màn, đúng 3 dòng/đơn, đơn hoàn một phần chỉ hiện chip `"Hoàn K/N"` + tên món đã hoàn tô màu cam (bấm vào Chi tiết vẫn thấy đủ mọi sản phẩm), không còn dòng "Mã đơn" trong danh sách; hàng chip lối tắt nhỏ gọn.
5. **Lịch sử**: xác nhận không còn thẻ "Hôm nay", số đếm trên hàng chip khớp 100% với số dòng khi bấm lọc từng chip.
6. **Quét**: kiểm tra khung camera nhỏ gọn hơn, thấy "Quét gần đây" ngay trên màn hình, các nút Đèn / Zoom / Dừng / Dán mã và ô nhập tay hoạt động bình thường.
7. **Sáng/Tối, Dynamic Type & Reduce Motion**: thử chuyển Dark/Light mode và tăng cỡ chữ trong Trợ năng để kiểm tra hàng đơn tự co giãn không vỡ chữ.

TRẠNG THÁI: XONG
