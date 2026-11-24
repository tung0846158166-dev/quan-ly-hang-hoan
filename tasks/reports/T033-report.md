# Báo cáo T033 - App iPhone: làm lại màn Chi tiết đơn + thêm Thống kê sản phẩm

## 1. Phạm vi & tệp mới / sửa
Chỉ sửa trong `ios-app/` (cùng báo cáo này và `TASKS.md`). Không đụng `v2/src/QuanLyHangHoan.Data/`, `v2/src/QuanLyHangHoan.Core/`, hay bất kỳ tệp test nào ngoài `ios-app/`. Không thiếu API nào ở `v2/src/QuanLyHangHoan.Data/`.

### Tệp mới
- `ios-app/ScannerCore/Sources/ScannerCore/ProductMetrics.swift`: Logic thuần tính thống kê sản phẩm bị hoàn tương đương `ProductStatistics.cs` trên PC v2 (`ProductMetrics.compute`, `filter`, `hasAnyDate`, `orders(forProductKey:in:)`, `productLines(for:)`), kèm các kiểu `ProductVariantStat`, `ProductStatItem`, `ProductStatsSummary`, `ProductPlatformFilter`, `ProductDateRange`, `OrderProductLine`.
  - Nhóm theo khóa chuẩn hóa `groupKey` (bỏ hoa/thường, dấu cách thừa, dấu tiếng Việt, `đ/Đ -> d`, chỉ giữ `a-z0-9`); hiển thị cách viết gặp nhiều nhất.
  - Tổng số cái tính theo `q` (`q <= 0` tính 1); đơn hoàn một phần (`rn` không rỗng) chỉ tính các sản phẩm nằm trong `rn`.
  - Bỏ sản phẩm rỗng và `"Chưa rõ sản phẩm..."`, đếm riêng vào `unknownUnits`.
  - Tách màu/size song song từ `co`/`sz` theo `" | "`; nếu số phần tử lệch với `n` thì bỏ qua màu/size của đơn đó, không đoán.
  - Tính đủ: số cái, số đơn, tỷ trọng (`share`, không chia cho 0), tách theo sàn (Shopee/TikTok/Lazada/Khác), lý do hay gặp từ `nt` (bỏ qua nếu rỗng, không bịa), biến thể màu/size sắp giảm dần theo số cái.
- `ios-app/ScannerCore/Tests/ScannerCoreTests/ProductMetricsTests.swift`: 7 bài test XCTest dùng dữ liệu GIẢ (`DEMO-*`): gộp tên khác hoa/thường/dấu cách/dấu tiếng Việt + `qty <= 0`, đơn hoàn một phần chỉ tính sản phẩm đã hoàn, bỏ rỗng & `"Chưa rõ sản phẩm..."`, đầu vào rỗng không chia cho 0, màu/size lệch số phần tử bị bỏ qua không đoán, lọc sàn/7 ngày/30 ngày + lý do + tra danh sách đơn theo sản phẩm, và kiểm tra `DemoData` ra thống kê hợp lệ.
- `ios-app/App/Sources/OrderDetailView.swift`: Màn `DetailView` thiết kế lại hoàn toàn (tách riêng từ `OrdersView.swift`, giữ nguyên chữ ký `DetailView(row:)` để mọi nơi mở sheet hoạt động liền mạch).
- `ios-app/App/Sources/ProductStatsView.swift`: Màn `ProductStatsView` (bảng xếp hạng sản phẩm bị hoàn + lọc sàn & 7 ngày/30 ngày/Tất cả khi có ngày) và màn con `ProductStatDetailView` (biến thể màu/size thanh ngang, tách theo sàn, danh sách đơn bấm mở `DetailView`).

### Tệp sửa
- `ios-app/ScannerCore/Sources/ScannerCore/DemoData.swift`: Bổ sung `x` (mã liên quan `DEMO-RET-*` / `DEMO-REQ-*`), `nt` (ghi chú/lý do hoàn mẫu), và `co`/`sz` phân cách `" | "` khớp số sản phẩm trên các đơn hoàn một phần (giữ nguyên đúng 60 đơn và mọi bộ đếm đã kiểm trong `DemoDataTests`).
- `ios-app/App/Sources/SyncEngine.swift`: Thêm `OrderActionState` / `OrderActionFeedback` + `orderActionFeedback` và `actionFeedback(for:)` để màn `DetailView` hiển thị trực tiếp trạng thái "Đang chờ máy tính xác nhận...", "PC đã cập nhật" khi nhận `pc_ack`, hoặc báo lỗi đỏ khi PC từ chối; giữ nguyên mọi hợp đồng bản tin.
- `ios-app/App/Sources/OrdersView.swift`: Gỡ `DetailView` cũ (đã chuyển sang `OrderDetailView.swift`), thêm lối tắt **"Sản phẩm"** (kèm số mặt hàng) trên hàng lối tắt cuộn ngang dẫn thẳng tới `ProductStatsView`.
- `ios-app/App/Sources/HomeView.swift`: Tính `productStats` cùng `recompute()`, thêm thẻ **"Sản phẩm hoàn nhiều nhất"** (top 5 có thứ hạng, số cái, tỷ trọng, thanh ngang, bấm từng dòng mở `ProductStatDetailView` và nút **"Xem tất cả"** mở `ProductStatsView`, dùng `navigationDestination(isPresented:)` tương thích iOS 16).

## 2. Trước / Sau của từng khối màn Chi tiết đơn (`DetailView`)
1. **Thanh trên & bố cục tổng thể**:
   - *Trước*: `Form` danh sách chữ trái/phải dày đặc, tiêu đề cố định `"Chi tiết đơn"`.
   - *Sau*: `ScrollView` nền sâu `Theme.pageBackground`, thẻ bo góc lớn (`CardModifier`), tiêu đề inline gọn theo loại đơn (`live.ty` hoặc `"Chi tiết đơn"`), nút `"Đóng"`, hỗ trợ kéo xuống để đóng sheet; không chồng chữ, vùng bấm >= 44pt, có `Haptics`.
2. **Hero trên cùng**:
   - *Trước*: Không có Hero; mã vận đơn nằm trong dòng `LabeledContent` nhỏ.
   - *Sau*: Thẻ Hero có nền gradient nhạt theo mức cảnh báo (đỏ/vàng/xanh/cam), huy hiệu sàn (Shopee cam / TikTok đen tương thích sáng-tối / Lazada xanh), nhãn bảng nguồn + Đặt trước, mã vận đơn cỡ lớn `.title2` monospaced đậm kèm nút **"Sao chép"** tròn có phản hồi dấu tích, mã đơn gốc nhỏ hơn bên dưới, viên thuốc trạng thái lớn (chạm để đổi trạng thái) và viên thuốc cảnh báo kèm `"Quá hạn X giờ"` / `"Còn Y giờ"`.
3. **Trạng thái đồng bộ `pc_ack` & dữ liệu mẫu**:
   - *Trước*: Chỉ đổi nhãn nút `"✓ Đã lưu"`, không thấy rõ trạng thái đang chờ máy tính hay lỗi PC trả về.
   - *Sau*: Hiện thanh phản hồi ngay trên Hero: spinner + `"Đang chờ máy tính xác nhận..."`, dấu tích xanh khi PC ACK thành công, hoặc cảnh báo đỏ kèm lý do lỗi từ PC; khi ở chế độ mẫu hiện dải nhắc `"Dữ liệu mẫu: thay đổi chỉ áp dụng cục bộ"`.
4. **Hàng nút hành động nhanh**:
   - *Trước*: Nút chữ rời rạc rải trong nhiều `Section` của `Form`.
   - *Sau*: Một hàng 5 nút biểu tượng tròn có nhãn (**Đã nhận/Trạng thái**, **Ghi chú**, **Theo dõi** sao, **Sao chép**, **Xóa**), giữ nguyên toàn bộ thao tác cũ (`order_edit`, `order_note`, `order_delete`) và hộp thoại xác nhận xóa 2 bước.
5. **Thẻ "Sản phẩm"**:
   - *Trước*: `ProductLines` đặt trong một ô nhỏ, màu/size/số lượng tách rời xuống 3 dòng `LabeledContent`.
   - *Sau*: Mỗi sản phẩm một dòng thẻ riêng (tách `n`/`co`/`sz` theo `" | "`), có chip màu, chip size, số lượng `"×N"` (hoặc `"Tổng N cái"`). Đơn hoàn một phần (`rn` không rỗng) hiện nhãn `"Hoàn K/M sản phẩm"`, sản phẩm đã hoàn nổi màu cam + nhãn `"Hoàn"`, sản phẩm còn lại xám nhạt + nhãn `"Không hoàn"`.
6. **Thẻ "Hành trình hàng hoàn"**:
   - *Trước*: Không hiển thị trên màn Chi tiết đơn.
   - *Sau*: Hiện khi `live.inReturnWatch` (`rw > 0`): thanh tiến trình 4 chặng (**Đã đồng ý → Đang về → Đã về shop → Đã nhận kho**) với chặng hiện tại sáng nổi bật, kèm giờ về shop (`ar`) và giờ nhận kho (`rcd`) nếu có.
7. **Thẻ "Mã vận đơn & liên quan"**:
   - *Trước*: Chỉ hiện mã vận đơn và mã đơn hàng, bỏ qua `x`.
   - *Sau*: Hiện đầy đủ mã vận đơn, mã đơn hàng, từng mã phụ trong `x` (mỗi dòng có nút sao chép tròn nhỏ) và trạng thái sàn (`ps`); chỉ hiện dòng có dữ liệu.
8. **Thẻ "Thời gian"**:
   - *Trước*: 3 dòng `LabeledContent` chỉ hiện khi `live.pd`.
   - *Sau*: Timeline dọc có chấm màu gồm: Ngày hoàn/hủy (`rd`), Bắt đầu theo dõi hoàn về (`rw`), Hàng về shop (`ar`), Đã nhận tại kho (`rcd`), Hạn xử lý (`due` kèm viên thuốc còn lại/quá hạn); chỉ hiện mốc có dữ liệu.
9. **Thẻ "Ghi chú"**:
   - *Trước*: Ô `TextField` + `Toggle` nằm cứng giữa `Form`.
   - *Sau*: Thẻ gọn tách rõ ghi chú shop/theo dõi (`wr`) và ghi chú khách/đơn PC (`nt`), có nút **Thêm / Sửa** mở sheet nhập liệu gọn (`.presentationDetents([.medium])`); khi trống hiện gợi ý nhẹ.

## 3. Thống kê sản phẩm
- **Tổng quan (`HomeView`)**: Thẻ **"Sản phẩm hoàn nhiều nhất"** hiển thị top 5 sản phẩm (thứ hạng, tên, số cái, % tỷ trọng, thanh ngang), chạm từng dòng mở thẳng `ProductStatDetailView`, nút **"Xem tất cả"** mở `ProductStatsView`.
- **Tab Đơn (`OrdersView`)**: Lối tắt **"Sản phẩm"** trên hàng lối tắt đầu trang mở `ProductStatsView`.
- **Màn đầy đủ (`ProductStatsView`)**: Lọc nhanh theo sàn (`Tất cả / Shopee / TikTok / Lazada`) và khoảng thời gian (`7 ngày / 30 ngày / Tất cả`, tự ẩn nếu dữ liệu không có ngày); thẻ tổng quan (Tổng cái hoàn, Số đơn, Mặt hàng, ghi chú riêng `Chưa rõ sản phẩm` nếu > 0); danh sách xếp hạng đầy đủ.
- **Màn con (`ProductStatDetailView`)**: Tóm tắt sản phẩm (chỉ hiện lý do hay gặp khi có dữ liệu, không bịa), thanh ngang tách theo sàn, thanh ngang màu/size bị hoàn nhiều, và danh sách các đơn có sản phẩm đó (chạm mở `DetailView`).

## 4. CHƯA BIÊN DỊCH
- **CHƯA BIÊN DỊCH**: Máy Windows này không có Swift/Xcode nên chưa chạy được `swift test` và chưa build IPA. Cần Claude kiểm tra và đẩy lên GitHub để CI chạy `swift test` và build thật.
- Đã tự rà soát tĩnh toàn bộ 8 tệp mới/sửa:
  - Cân bằng ngoặc `{}`, `()`, `[]` đạt 100% trên cả 8 tệp.
  - 0 tham chiếu tới `Inbox`, `inbox_push`, `cancelOrder`, `SHP-2026`.
  - 0 sử dụng API iOS 17+ chưa bọc `#available` (không dùng `.buttonBorderShape(.circle)`, `navigationDestination(item:)`, `ContentUnavailableView`, `sensoryFeedback`).
  - Duy nhất 1 định nghĩa `struct DetailView` (trong `OrderDetailView.swift`), không trùng tên.
  - Không gọi hàm `@MainActor` từ ngữ cảnh ngoài main actor.

## 5. Việc cần kiểm trên iPhone thật
1. Bật **Chế độ dữ liệu mẫu** → mở một đơn quá hạn hoàn một phần (vd `DEMO-TT-1002`) và một đơn đang ở chặng 3/4 (vd `DEMO-SPX-1001`, `DEMO-SPX-2001`): kiểm tra Hero (mã to, nút Sao chép tròn, viên thuốc trạng thái & quá hạn), 5 nút hành động nhanh, thẻ Sản phẩm tô cam sản phẩm hoàn + chip màu/size, thẻ Hành trình 4 chặng có giờ `ar`/`rcd`, thẻ Mã liên quan (`DEMO-RET-*`), Timeline Thời gian dọc, thẻ Ghi chú (cả ghi chú shop lẫn ghi chú khách).
2. Thử các thao tác trên Chi tiết đơn ở chế độ mẫu và khi nối PC thật: bấm **Đã nhận** / đổi trạng thái, bấm **Theo dõi** (sao), mở sheet **Ghi chú** lưu nội dung, bấm **Xóa** kiểm tra hộp xác nhận 2 bước; khi nối PC thật kiểm tra thanh `"Đang chờ máy tính xác nhận..."` chuyển thành xanh khi PC ACK hoặc đỏ khi PC từ chối.
3. Kéo xuống để đóng sheet Chi tiết đơn; kiểm tra không cắt chữ, không chồng thanh trên, Dark Mode / Light Mode / Dynamic Type cỡ lớn và rung nhẹ (`Haptics`).
4. Ở **Tổng quan**: kiểm tra thẻ **"Sản phẩm hoàn nhiều nhất"** top 5 có thanh tỷ trọng; chạm một sản phẩm để vào `ProductStatDetailView` và chạm **"Xem tất cả"** (hoặc lối tắt **"Sản phẩm"** ở tab Đơn) để vào `ProductStatsView`.
5. Ở **Thống kê sản phẩm**: thử đổi bộ lọc sàn (`Shopee / TikTok / Lazada`) và khoảng thời gian (`7 ngày / 30 ngày / Tất cả`); vào chi tiết một sản phẩm kiểm tra thanh ngang Màu/Size, tách theo sàn, và chạm một đơn trong danh sách để mở màn Chi tiết đơn mới.

TRẠNG THÁI: XONG
