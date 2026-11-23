# T006 - Tách giao diện theo sàn + Trang chủ hiện đại (giao cho: antigravity, người kiểm tra: claude)

THAY THẾ và GỘP T004. Chạy SAU KHI T003 xong (hoặc user xác nhận T003 đã dừng), tránh hai agent cùng sửa `App/`. Phạm vi: chỉ `v2/src/QuanLyHangHoan.App/` và `v2/tests/QuanLyHangHoan.Tests/UiTests/`. Không sửa Core, Data, ImporterTests.
Đọc trước: `AGENTS.md`, `TASKS.md`, `docs/legacy-data-schema.md` (mục "Quyết định của chủ dự án"), `tasks/T004-ui-adjust.md` (các mục 2-4 vẫn áp dụng).

## Trạng thái T003 (claude kiểm tra 2026-11-23 09:57)
T003 đã DỪNG dở vì hết quota (log `tasks/reports/T003-agy-output.log`, không có báo cáo). Mã T003 viết dở trong `App/` (khoảng 03:04-03:11) vẫn build được và test hiện xanh (51). Hãy coi T003 là chưa xong: đọc `tasks/T003-ui-screens.md`, xem App/ đang có gì, hoàn thiện phần còn thiếu của T003 (Bảng đơn mới, Kho lưu trữ, Đã xóa, Thống kê, Nhập dữ liệu cũ) KHI làm theo T006, không làm lại từ đầu. Ghi trong báo cáo T006 phần nào của T003 đã hoàn thiện. Báo cáo T003 không cần viết riêng.

## Quyết định mới của chủ dự án (2026-11-23)
Giao diện cũ gộp 2 sàn vào một bảng. v2 phải TÁCH:
1. **Shopee: hai bảng riêng, không gộp**: "Shopee - Hoàn hàng" và "Shopee - Đơn hủy".
2. **TikTok: một bảng** "TikTok - Hoàn hàng" (không tách hủy/hoàn).
3. **Khởi động app vào Trang chủ** (dashboard hiện đại): thống kê, phân tích, báo cáo.

**ĐÍNH CHÍNH T004 mục 1:** T004 bảo bỏ "Bảng đơn hủy". Nay cần bảng "Shopee - Đơn hủy" nhưng KHÔNG phải bảng/tệp đơn hủy riêng của app cũ (`phu_shopee_cancelled_orders_v1`, vẫn bỏ). Đây chỉ là một BỘ LỌC trên bảng đơn chung: `Platform == Shopee && OrderType == "Đơn hủy"`. Đừng khôi phục `ICancelledOrderProvider` / `CancelledOrdersViewModel` theo mô hình dữ liệu riêng; dùng một nguồn đơn duy nhất rồi lọc.

## Số liệu thật (chỉ là số, không phải dữ liệu đơn) trong đơn chính 2.307
| Sàn | Loại đơn (orderType) | Số |
|---|---|---:|
| Shopee | Đơn hủy | 1.033 |
| Shopee | Trả hàng/Hoàn tiền | 523 |
| Shopee | Giao không thành công | 17 |
| TikTok | THẤT THOÁT | 433 |
| TikTok | Trả hàng/Hoàn tiền | 277 |
| TikTok | Giao không thành công | 12 |
| TikTok | Chỉ hoàn tiền | 12 |

Hệ quả:
- **TikTok KHÔNG chỉ có "Trả hàng/Hoàn tiền"**: còn "THẤT THOÁT", "Giao không thành công", "Chỉ hoàn tiền". Vì chủ dự án muốn TikTok một bảng, hiển thị TẤT CẢ loại của TikTok trong một bảng, thêm cột/bộ lọc "Loại đơn" (chip lọc), không bỏ loại nào.
- **Shopee "Hoàn hàng" = mọi đơn Shopee KHÔNG phải "Đơn hủy"** (gồm "Trả hàng/Hoàn tiền" và 17 "Giao không thành công"). Ghi rõ quy tắc này vào báo cáo để chủ dự án xác nhận.
- Tên loại đơn là chuỗi tiếng Việt gốc: gom vào MỘT lớp hằng số (vd `OrderTypeNames`), cạnh `PlatformNames` của T004; không rải chuỗi.
- Dữ liệu giả phải có đủ các loại trên để canh giao diện.

## Điều hướng (sidebar)
```
Trang chủ                (mặc định khi mở app)
SHOPEE
  Hoàn hàng
  Đơn hủy
TIKTOK
  Hoàn hàng
CHUNG
  Bảng đơn mới
  Kho lưu trữ            (= cùng đơn ở trạng thái "đã dời", xem T004 mục 3)
  Đã xóa
  Thống kê & báo cáo
  Nhập dữ liệu cũ
```
Tên màn hình theo nhãn sàn ngắn "Shopee" / "TikTok". Mỗi bảng sàn tự lọc theo sàn, KHÔNG có ô chọn sàn trong bảng (đã tách rồi). Bảng đơn mới, Kho lưu trữ, Đã xóa có thể có bộ lọc sàn (Tất cả / Shopee / TikTok).

## Trang chủ (dashboard)
Phong cách hiện đại, sạch, nhiều khoảng trắng, thẻ bo góc, bóng nhẹ, hỗ trợ sáng/tối nếu dễ (không bắt buộc), font/màu theo `Themes/AppStyles.xaml`; dùng chung style, không rải màu cứng. Gồm:
1. Hàng thẻ KPI (mỗi sàn một nhóm, hoặc hai hàng): đơn đang chờ hàng về, đã nhận hôm nay, cảnh báo Vàng (>=12h), cảnh báo Đỏ (>=24h), tổng giá trị đơn chưa về. Dùng `AlertThresholds`/`AlertPolicy` có sẵn, không viết lại ngưỡng.
2. Biểu đồ xu hướng theo ngày/tuần (đơn hoàn về mỗi ngày, tách Shopee/TikTok), biểu đồ cơ cấu loại đơn theo sàn, top lý do hoàn/hủy. Vẽ bằng thư viện nhẹ phù hợp WPF (vd LiveCharts2 hoặc ScottPlot) hoặc tự vẽ bằng Canvas; chọn MỘT, ghi lý do vào báo cáo, không kéo nhiều thư viện.
3. Danh sách "Cần xử lý ngay": các kiện Đỏ/Vàng, bấm vào mở thẳng dòng đó ở bảng đúng sàn.
4. Khối "Báo cáo": nút xuất tóm tắt (CSV là đủ cho T006; Excel/PDF để sau), chọn khoảng ngày + sàn.
5. Trạng thái trống đẹp khi chưa có dữ liệu (app mới cài, chưa nhập dữ liệu cũ) kèm nút dẫn tới "Nhập dữ liệu cũ".
Toàn bộ số liệu lấy qua interface provider hiện có/được thêm trong `App/Services` (dữ liệu giả lúc này), ViewModel không đọc DB trực tiếp (Claude sẽ nối `Data` sau). Màn "Thống kê & báo cáo" chi tiết hơn Trang chủ (lọc theo thời gian, sàn, loại đơn), tái dùng cùng provider.

## Ràng buộc
- MVVM; logic tính toán (đếm KPI, nhóm theo ngày, lọc Shopee hủy/hoàn) nằm ở lớp có thể test, KHÔNG ở code-behind. Mọi logic có test xUnit trong `UiTests/`.
- Cảnh báo Vàng 12h / Đỏ 24h kể từ lúc hàng về (quy tắc bất biến số 7), đọc từ `AlertThresholds`.
- Bảng phải chịu được 50.000 dòng (virtualization như T002); không làm chậm Trang chủ khi khởi động (tính KPI nền, hiện khung trước).
- Không dùng dữ liệu đơn thật. Số liệu bảng trên chỉ để canh tỉ lệ dữ liệu giả.

## Tiêu chí hoàn thành
- `dotnet build` 0 lỗi 0 cảnh báo; `dotnet test` xanh TOÀN BỘ (sửa test sidebar cho khớp mục mới).
- Test: bộ lọc Shopee-hoàn/Shopee-hủy/TikTok tách đúng (đủ các loại ở bảng số liệu; tổng ba bảng = tổng dữ liệu giả; không đơn nào ở hai bảng); app mở vào Trang chủ; KPI Vàng/Đỏ đúng ngưỡng.
- `grep` rỗng: không còn "TikTok Shop" trong App (T004 mục 2).
- Ảnh chụp màn hình Trang chủ + 3 bảng sàn + trạng thái trống vào `tasks/reports/T006-screens/`.
- Báo cáo `tasks/reports/T006-report.md` (nêu thư viện biểu đồ đã chọn, quy tắc Shopee-hoàn, điểm chưa làm), dòng cuối `TRẠNG THÁI: XONG` hoặc `CHƯA XONG (lý do)`. Cập nhật `TASKS.md`.
