# T027 - Chuông thông báo trong app

**Người làm: claude (agy đã dừng).** Không có người thứ hai kiểm tra độc lập.

## Đã làm
- Chuông 🔔 ở thanh trên (cạnh ô tìm) với số chưa đọc (ẩn khi 0, "99+" khi nhiều). Bấm hoặc Ctrl+Shift+N mở khung thả xuống, Esc đóng. Nút "Đánh dấu đã đọc", "Xóa tất cả" (chỉ xóa danh sách thông báo, không đụng dữ liệu đơn). Mỗi dòng: chấm màu theo mức (đỏ/vàng/xanh/xám), tiêu đề, chi tiết, "x phút trước". Bấm một dòng: đánh dấu đã đọc, đóng khung, đi tới đúng chỗ.
- `Services/NotificationCenter.cs`: danh sách mới nhất trước, tối đa 300, gộp trùng liên tiếp (cùng nguồn, tiêu đề, chi tiết trong 60 giây thành "×2"), trạng thái đã đọc; lưu `notifications.json` cạnh settings.json (ghi tạm rồi đổi tên; tệp hỏng thì bỏ qua và bắt đầu danh sách trống). Địa chỉ Apps Script, `token=`, mọi URL trong nội dung bị thay bằng "(đã ẩn địa chỉ)".
- Nguồn thông báo (gom từ sự kiện đã có, không đổi gì ở Core/Data):
  - Đơn chuyển VÀNG/ĐỎ: móc vào `AlertNotificationMonitor` (sự kiện `NewAlerts`), cùng bộ "mỗi đơn mỗi mức một lần" với toast cũ nên khởi động lại không báo lại; `NotifyAlerts` tắt thì không sinh. Mỗi đơn một dòng; nếu một lượt có hơn 10 đơn mới thì gộp thành MỘT dòng tóm tắt.
  - Điện thoại: tạo đơn mới, quét nhận kiện, xóa, sửa trạng thái, ghi chú, quét đơn đã có (vàng), yêu cầu lỗi/nhập nhằng (vàng). Ảnh không thông báo. Cùng một yêu cầu gửi hai lần không báo hai lần.
  - Nạp đơn xong ở 4 đường (Nạp file, Theo dõi thư mục, Google Sheet, Salework): "Thêm X, cập nhật Y", kèm dòng vàng nếu có đơn nhập nhằng; lỗi thì dòng đỏ.
  - Mất kết nối đồng bộ quá 2 phút: một dòng đỏ (không báo lặp), nối lại thì một dòng xanh; kiểm tra mỗi 30 giây ở bản thật.
- `--demo`: 7 thông báo mẫu đủ 4 mức, chỉ trong bộ nhớ, không đọc/ghi đĩa.

## Kiểm chứng
- Build 0 lỗi 0 cảnh báo; 22 test mới (`UiTests/T027NotificationCenterUiTests.cs`): đếm chưa đọc giảm khi đánh dấu, "99+", tối đa 300, gộp "×2" và hết hạn sau 60 giây, lưu và nạp lại giữ trạng thái đọc, tệp hỏng không sập, mất kết nối >2 phút một lần rồi nối lại, vàng/đỏ mỗi đơn mỗi mức một lần kể cả "khởi động lại" giả, tắt `NotifyAlerts` thì không sinh, sự kiện điện thoại đúng nội dung và đích, trùng yêu cầu không báo đôi, 4 đường nạp, lỗi đỏ và không lộ địa chỉ, nạp không đổi gì thì không báo, bấm thông báo điều hướng đúng (màn Nạp file, mở tìm kiếm theo mã), xóa tất cả không đụng số đơn, demo.
- Toàn bộ test: 538/538 xanh (một lần chạy đầy đủ cuối). Ảnh `tasks/reports/T027-screens/` (có số chưa đọc, danh sách sáng/tối, trống); claude đã xem ảnh sáng (trước khi đổi sang chấm màu) và ảnh tối.
- Sửa kèm: bản giả `FakeDataService` sinh vô hạn đơn khi được hỏi "lấy hết" (int.MaxValue), làm test treo và ăn gần 9 GB; nay giới hạn ở 300 đơn demo. Chỉ ảnh hưởng bản giả.

## Khác phiếu và chưa kiểm chứng
- Đường Salework KHÔNG có test riêng (bản giả không có hàm phát sự kiện hoàn thành); logic cùng khuôn với Google Sheet nhưng chưa được test.
- Chưa có thông báo cho "sao lưu lỗi" và "thư mục theo dõi mất" (chưa có sự kiện sẵn để móc vào).
- Chưa thử với dữ liệu thật; chưa thử Tab tới chuông bằng bàn phím thật (chỉ phím tắt Ctrl+Shift+N và Esc).

TRẠNG THÁI: XONG
