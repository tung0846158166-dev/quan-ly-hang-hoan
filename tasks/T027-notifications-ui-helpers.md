# T027 - Màn "Thông báo" (trung tâm thông báo trong app) (giao cho: antigravity, người kiểm tra: claude)

Phạm vi: CHỈ `v2/src/QuanLyHangHoan.App/` và `v2/tests/QuanLyHangHoan.Tests/UiTests/`. KHÔNG sửa `Core/`, `Data/` và test ngoài `UiTests/`. Thiếu API ở Data thì ghi vào báo cáo.
Chạy SAU T021 (dùng cùng dịch vụ cảnh báo vàng/đỏ và danh sách "đã báo"). Đọc trước: `AGENTS.md` (quy tắc 7), `tasks/reports/T021-report.md`, `tasks/reports/T019-review.md` (sự kiện đồng bộ), `tasks/reports/T026-review.md`.

## Bối cảnh
App cũ có trung tâm thông báo (chuông) gom: đơn sắp tới hạn/quá hạn, đơn mới từ điện thoại, nạp đơn xong, lỗi cần chú ý. v2 hiện chỉ có toast tức thời (mất sau vài giây) và nhật ký kỹ thuật; người dùng bỏ lỡ cảnh báo nếu không nhìn đúng lúc.

## Việc cần làm
1. **Biểu tượng chuông** trên thanh trên cùng (cạnh ô tìm) kèm số thông báo CHƯA ĐỌC (ẩn khi 0, "99+" khi nhiều). Bấm mở khung thả xuống/màn hình danh sách thông báo (mới nhất trước; nút "Đánh dấu tất cả đã đọc", "Xóa tất cả" chỉ xóa thông báo trong danh sách này — KHÔNG đụng dữ liệu đơn).
2. **Nguồn thông báo** (gom từ các sự kiện đã có, không sinh dữ liệu mới ở Data): 
   - đơn chuyển VÀNG (>= 12h) / ĐỎ (>= 24h) kể từ giờ hàng về, chỉ đơn CHƯA nhận (cùng quy tắc và cùng bộ "mỗi đơn mỗi mức một lần" của T021);
   - điện thoại tạo đơn mới / xóa / sửa / ghi chú (từ `SyncHandled`, T019); điện thoại quét nhận kiện thành công;
   - nạp đơn xong từ các đường (Nạp file, Theo dõi thư mục, Google Sheet, Salework): "Đã nạp: thêm X, cập nhật Y", kèm cảnh báo nhập nhằng nếu có;
   - lỗi cần chú ý: đồng bộ mất kết nối kéo dài (> 2 phút), Google Sheet/Salework lỗi, sao lưu lỗi (nếu T022 xong), thư mục theo dõi mất.
3. Mỗi thông báo: biểu tượng, tiêu đề, một dòng chi tiết, thời gian ("2 phút trước"), màu theo mức (thông tin/vàng/đỏ/xanh). Bấm vào thông báo đưa tới đúng màn hình (đơn -> chi tiết đơn; nạp đơn -> màn nạp tương ứng; đồng bộ -> màn Đồng bộ điện thoại). Không lộ địa chỉ Apps Script/token trong nội dung.
4. **Lưu giữa các lần mở**: danh sách tối đa 300 thông báo gần nhất lưu trong tệp riêng cạnh `settings.json` (ghi nguyên tử, tệp hỏng thì bỏ qua và tạo mới, không sập); trạng thái đã đọc cũng lưu. Gộp thông báo trùng liên tiếp (cùng nguồn+nội dung trong 60 giây thành "x2").
5. Tôn trọng công tắc `NotifyAlerts` cho thông báo vàng/đỏ (tắt thì không sinh); các nguồn khác vẫn hiển thị trong chuông.
6. Theme sáng/tối; bàn phím (mở bằng phím tắt Ctrl+Shift+N hoặc Tab tới chuông, Esc đóng). `--demo`: thông báo mẫu, không đọc/ghi đĩa.

## Tiêu chí hoàn thành
- `dotnet build` 0 lỗi 0 cảnh báo; `dotnet test` xanh 10/10.
- Test mới trong `UiTests/` (bản giả nguồn sự kiện + đồng hồ giả + thư mục tạm): đếm chưa đọc đúng và giảm khi đánh dấu đọc; mỗi đơn mỗi mức vàng/đỏ chỉ một thông báo (kể cả sau "khởi động lại" giả); đơn đã nhận không sinh thông báo; sự kiện đồng bộ -> thông báo đúng nội dung; nạp đơn xong -> thông báo; lỗi mất kết nối > 2 phút; bấm thông báo điều hướng đúng; lưu và nạp lại giữ trạng thái đọc; tệp hỏng không làm sập; tối đa 300; gộp trùng "x2"; `NotifyAlerts` tắt thì không sinh vàng/đỏ; không có địa chỉ Apps Script/token trong nội dung; `--demo` không chạm đĩa.
- Ảnh chụp `tasks/reports/T027-screens/`: chuông có số chưa đọc, danh sách thông báo nhiều loại, trống.
- Báo cáo `tasks/reports/T027-report.md`, dòng cuối `TRẠNG THÁI: XONG` hoặc `CHƯA XONG (lý do)`. Cập nhật `TASKS.md`.
