# T025 - Màn "Trợ giúp & chẩn đoán" (hướng dẫn bắt đầu + khối thông tin để gửi hỗ trợ) (giao cho: antigravity, người kiểm tra: claude)

Phạm vi: CHỈ `v2/src/QuanLyHangHoan.App/` và `v2/tests/QuanLyHangHoan.Tests/UiTests/`. KHÔNG sửa `Core/`, `Data/` và test ngoài `UiTests/`. Thiếu API ở Data thì ghi vào báo cáo.
Đọc trước: `AGENTS.md`, `v2/src/QuanLyHangHoan.Data/DiagnosticsReport.cs`, `v2/CHAY-APP.md` (nội dung hướng dẫn thật), `App/Services/IClipboardService.cs`. Test dùng `WpfTestHost`, DB tạm; không chạm `%LocalAppData%` thật; `dotnet test` toàn bộ 10 lần liên tiếp 0 lần đỏ.

## Vấn đề
Chủ dự án tự chạy app thật và phải chép lại từng câu trạng thái/lỗi để báo cho người hỗ trợ; lần đầu dùng không biết bắt đầu từ đâu (nhập dữ liệu cũ, mã phòng, các đường nạp đơn).

## API có sẵn (claude đã viết, có test)
`DiagnosticsReport.Build(dbPath, AppSettings? settings, string appVersion, DateTime now)` -> chuỗi nhiều dòng: phiên bản, hệ điều hành, thư mục dữ liệu, kích thước DB, schema, số đơn theo nhóm/sàn, ảnh, nhật ký, hoạt động gần nhất, sao lưu gần nhất, cài đặt (mã phòng chỉ hiện 4 ký tự đầu), ổ đĩa. KHÔNG chứa mã vận đơn/tên sản phẩm/địa chỉ Apps Script/token. Gọi qua `Task.Run`, bọc interface có bản giả cho test/`--demo`. Phiên bản app lấy từ assembly của App.

## Việc cần làm
1. Mục sidebar **"Trợ giúp"** (nhóm CHUNG, cuối danh sách, sau "Cài đặt" nếu có) gồm 3 khối:
   - **Bắt đầu nhanh** (danh sách 5 bước có dấu ✓/○ tự tính từ trạng thái thật, mỗi bước có nút "Đi tới" sang màn hình tương ứng): (1) Nhập dữ liệu cũ (✓ khi DB có đơn); (2) Mã phòng đồng bộ đã sẵn sàng và nhập vào app iPhone (✓ nếu đồng bộ đang bật; không biết điện thoại đã nhập chưa thì ghi "kiểm tra trên điện thoại"); (3) Thử nạp một file Excel; (4) Bật theo dõi thư mục/Google Sheet/Salework (✓ khi có ít nhất một đang bật); (5) Bật tự sao lưu. Chỉ gợi ý, không làm thay.
   - **Chẩn đoán**: hộp chữ chỉ-đọc hiển thị `DiagnosticsReport.Build(...)` cộng các câu trạng thái hiện tại của: đồng bộ điện thoại (kết nối MQTT/ntfy), theo dõi thư mục, Google Sheet, Salework (đúng câu đang hiển thị ở các màn đó; KHÔNG BAO GIỜ kèm địa chỉ Apps Script/token/tên tệp dữ liệu khách). Nút **"Sao chép tất cả"** (qua `IClipboardService`, thông báo "Đã sao chép, dán vào tin nhắn gửi người hỗ trợ") và **"Làm mới"**. Dòng nhắc: "Báo cáo không chứa mã vận đơn, tên sản phẩm hay thông tin khách."
   - **Mở thư mục**: nút "Mở thư mục dữ liệu" và "Mở thư mục sao lưu" (bọc interface, bản giả cho test).
2. Phím tắt: liệt kê các phím đã có (Delete xóa dòng, Enter xem chi tiết, Ctrl+K tìm kiếm nếu T020 xong, Esc đóng) trong khối nhỏ "Phím tắt" (chỉ hiển thị những phím có thật trong app).
3. Hiển thị phiên bản app và ngày build ở cuối màn.
4. `--demo`: dữ liệu mẫu, không đọc đĩa; báo cáo chẩn đoán mẫu ghi rõ "chế độ demo".

## Tiêu chí hoàn thành
- `dotnet build` 0 lỗi 0 cảnh báo; `dotnet test` xanh 10/10.
- Test mới trong `UiTests/`: các bước ✓/○ đúng với DB rỗng và DB có đơn (DB tạm); "Sao chép tất cả" gọi `IClipboardService` với nội dung chứa phiên bản và số đơn nhưng KHÔNG chứa mã vận đơn/địa chỉ Apps Script/token giả đã đặt trong cấu hình thử; "Làm mới" cập nhật; các nút mở thư mục gọi bản giả; DB không tồn tại vẫn hiện báo cáo ("CHƯA CÓ") không sập; `--demo` không chạm đĩa.
- Ảnh chụp `tasks/reports/T025-screens/`: màn Trợ giúp (DB rỗng và có dữ liệu).
- Báo cáo `tasks/reports/T025-report.md`, dòng cuối `TRẠNG THÁI: XONG` hoặc `CHƯA XONG (lý do)`. Cập nhật `TASKS.md`.
