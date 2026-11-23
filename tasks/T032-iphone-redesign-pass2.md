# T032 - App iPhone: làm lại giao diện lần 2 (THẬT SỰ hiện đại) + chế độ dữ liệu mẫu (giao cho: antigravity, người kiểm tra: claude)

PHẠM VI: CHỈ `ios-app/`. Đọc trước: `AGENTS.md`, `tasks/T031-iphone-modern-home.md`, `tasks/reports/T031-report.md`, `ios-app/App/Sources/*.swift`, `ios-app/ScannerCore/Sources/ScannerCore/DashboardMetrics.swift`.
GIỚI HẠN THẬT: không có Swift/Xcode trên máy này. Viết thận trọng, iOS 16 là mức tối thiểu (API iOS 17+ phải bọc `#available`), tự đọc lại từng tệp tìm lỗi. Mỗi lỗi biên dịch trước đây của bạn là do KHÔNG tự rà: lớp/hàm gắn `@MainActor` (như `SyncEngine.pref`) KHÔNG được gọi từ chỗ không phải main actor; kiểm tra kỹ kiểu và nhãn tham số. Claude đẩy lên GitHub để build thật và trả lỗi cho bạn. KHÔNG `git commit`/`git push`.

## Phản hồi của chủ dự án sau khi cài bản T031 (ảnh thật trên iPhone)
"Chưa có gì hiện đại ngoài việc thêm được tab chính, các tab còn lại vẫn giữ nguyên." Đúng: T031 chỉ đổi màu, bo góc và thanh tab; bố cục vẫn là danh sách + nút như bản cũ. Ngoài ra chủ dự án chưa thấy được số liệu Tổng quan vì app chưa nối PC (màn chỉ hiện hướng dẫn trống). Lỗi thấy trên ảnh:
1. Tổng quan: góc trái trên có nút bị cắt chữ thành "Tổ…" (xấu, vô nghĩa). Bỏ hoặc thay bằng thứ có nghĩa.
2. Quét: dòng kết quả toàn CHỮ HOA dài ("CHƯA CÓ TRÊN ĐIỆN THOẠI, CHỜ PC XÁC NHẬN") khó đọc; khung camera chiếm gần hết màn hình; hai biểu tượng gạch chéo (mất kết nối) không có nhãn nên khó hiểu.
3. Cài đặt: thẻ đầu bị tiêu đề "Cài đặt" đè chồng chữ mờ lên; vẫn là danh sách chữ trái/phải dày đặc.
4. Đơn / Lịch sử: vẫn là ô tìm + dãy nút bo tròn + danh sách trống; chưa có cảm giác "thông minh".

## Yêu cầu 1 (QUAN TRỌNG NHẤT): chế độ DỮ LIỆU MẪU
Để chủ dự án (và claude khi xem ảnh) thấy app đầy đủ mà KHÔNG cần nối PC:
- `ScannerCore/Sources/ScannerCore/DemoData.swift`: tạo ~60 dòng `OrderRow` GIẢ (không dữ liệu thật) đủ mọi trường hợp: Shopee/TikTok/Lazada, chưa nhận/đã nhận (`rcd` rải 7 ngày), quá hạn đỏ, sắp đến hạn vàng, hoàn một phần có `rn`, đang theo dõi, trong hàng hoàn về đủ 4 chặng (`rw`/`rws`/`rv`), đơn hủy (`k=="c"`), vài dòng đã lưu trữ; kèm `stats` hợp lệ và vài mục lịch sử quét giả. Có test XCTest (số lượng từng nhóm đúng, mọi dòng giải mã được, không có mã/tên thật).
- Cài đặt: công tắc "Xem thử với dữ liệu mẫu". Bật thì `SnapshotStore` hiển thị dữ liệu mẫu (cờ riêng, trong bộ nhớ), một dải nhỏ "DỮ LIỆU MẪU, không gửi gì cho máy tính" luôn hiện ở đầu mọi màn, và MỌI thao tác ghi/gửi (quét gửi PC, đổi trạng thái, ghi chú, xóa, `snapshot_request`) bị vô hiệu hóa hoặc chỉ chạy cục bộ trên dữ liệu mẫu, TUYỆT ĐỐI không phát MQTT/ntfy, không ghi đè dữ liệu thật đã lưu; tắt công tắc thì trở lại dữ liệu thật nguyên vẹn. Trạng thái rỗng của Tổng quan có nút "Xem thử với dữ liệu mẫu".

## Yêu cầu 2: giao diện thật sự mới (thiết kế lại BỐ CỤC, không chỉ đổi màu)
Định hướng: gọn, thoáng, ưu tiên con số và hành động, như ứng dụng Apple Sức khỏe/Ví, Linear, Things. Nền tối thật sâu, nhấn cam `Theme.accent`, thẻ dùng `Material` (`.ultraThinMaterial`/nền thẻ mờ) và đường viền mảnh, chữ số lớn dạng `rounded` đậm, biểu tượng SF Symbols trong ô vuông bo màu, khoảng cách dọc rộng, ít viền, KHÔNG nút bo tròn xếp hàng dài.
1. **Tổng quan** (bố cục gợi ý, có thể cải thiện):
   - Khối **hero** trên cùng: tiêu đề lớn "Hôm nay" + ngày; vòng tiến độ (ring) "Đã nhận X / Cần nhận Y hôm nay" ở giữa thẻ có gradient cam nhạt; bên phải 2 con số phụ (quá hạn, sắp hạn) có chấm màu.
   - Dải **thẻ cuộn ngang** (snap): Hàng hoàn về (đường ống 4 chặng dạng thanh liền có số), Theo sàn (3 thẻ nhỏ), Hoàn một phần.
   - **Cần xử lý ngay**: danh sách thẻ gọn, mỗi dòng có thanh màu bên trái theo mức (đỏ/vàng), huy hiệu sàn, mã, sản phẩm, "quá 5 giờ"; vuốt phải để "Ghi chú", vuốt trái "Theo dõi" nếu thao tác đã có sẵn trong app (không thêm chức năng mới).
   - **7 ngày**: cột + đường trung bình, số trên cột; ẩn khi không có dữ liệu.
   - Không còn nút trống/cắt chữ ở thanh trên; thanh trên chỉ có tiêu đề lớn thu nhỏ khi cuộn (`navigationTitle` + `.large`) và nút chuông/làm mới có nhãn truy cập.
2. **Quét**: camera chiếm khoảng 55% màn, góc bo, kết quả là **thẻ trượt từ dưới lên** (bottom card) với biểu tượng lớn, mã, một dòng giải thích bằng chữ thường (không toàn chữ hoa), màu theo kết quả và tự thu sau vài giây; chuyển "Hoàn kho / Đơn mới" bằng bộ chọn phân đoạn nhỏ ở trên; điều khiển (đèn, zoom, dừng) là một hàng biểu tượng tròn có nhãn nhỏ; trạng thái kết nối là MỘT chấm + chữ ("Đã nối PC" / "Mất kết nối PC") thay cho hai biểu tượng gạch chéo; có "Đã quét hôm nay: N" gọn.
3. **Đơn**: ô tìm lớn, bộ lọc là **một hàng ô cuộn ngang có số đếm** (Tất cả, Chưa nhận, Quá hạn, Sắp hạn, Hoàn một phần, Theo dõi...), mỗi đơn là thẻ rộng rãi: dòng trên (huy hiệu sàn + "còn 3 giờ" tô màu), mã vận đơn đậm, sản phẩm (hoàn một phần tô màu sản phẩm hoàn như đã làm), chân thẻ: chặng hàng hoàn về hoặc trạng thái. Trạng thái rỗng thân thiện kèm nút "Xem thử với dữ liệu mẫu". Bảng phụ (Đơn hủy, Hàng hoàn về...) vào từ một hàng lối tắt có biểu tượng + số, không phải nút chữ dày.
4. **Hàng hoàn về**: đường ống 4 chặng lớn ở đầu (bấm chặng để lọc), mỗi đơn có thanh tiến trình 4 điểm.
5. **Lịch sử**: nhóm theo ngày ("Hôm nay", "Hôm qua"), mỗi lượt quét là một dòng gọn có biểu tượng kết quả; 4 số liệu đầu trang thu thành một thẻ.
6. **Cài đặt**: nhóm kiểu danh sách "inset grouped" với biểu tượng màu bên trái từng mục (Kết nối, Dữ liệu, Thông báo, Quét, Giới thiệu); trạng thái kết nối hiện bằng chấm màu + chữ; mã phòng ở một thẻ riêng có nút Dán/Sao chép; sửa lỗi thẻ đầu bị tiêu đề đè (dùng `ScrollView` + `.navigationBarTitleDisplayMode(.large)`, không để nội dung chồng lên thanh trên).
7. Mọi màn: tối/sáng đều đẹp, Dynamic Type không vỡ, vùng bấm >= 44pt, chuyển động tinh tế có tôn trọng Reduce Motion, rung nhẹ có sẵn (`Haptics`).

## Tiêu chí hoàn thành
- Chỉ sửa trong `ios-app/`. Giữ NGUYÊN mọi chức năng/hợp đồng (quét, hàng đợi bền, xác nhận, hoàn một phần tô màu, đồng bộ). Không còn `Inbox`/`inbox_push`/`cancelOrder`/`SHP-2026`.
- Test XCTest cho `DemoData` và mọi hàm thuần mới ở `ScannerCore`; chạy được bằng `swift test`.
- Báo cáo `tasks/reports/T032-report.md`: tệp mới/sửa, từng màn đã đổi gì (trước/sau), cách bật dữ liệu mẫu, mục "CHƯA BIÊN DỊCH", mục "việc cần kiểm trên iPhone thật", dòng cuối `TRẠNG THÁI: XONG` hoặc `CHƯA XONG (lý do)`. Cập nhật `TASKS.md`.
