# T025 - Màn "Trợ giúp" (bắt đầu nhanh + chẩn đoán)

**Người làm: claude (agy đã dừng).** Không có người thứ hai kiểm tra độc lập.

## Đã làm
- Mục sidebar "Trợ giúp" (nhóm CHUNG, cuối), `HelpViewModel` + `HelpView` + `IDiagnosticsProvider`.
- **Bắt đầu nhanh**: 5 bước có ✓/○ tự tính từ trạng thái thật (có đơn; đồng bộ bật; đã có lần nạp trong nhật ký; bật theo dõi thư mục/Google Sheet; bật tự sao lưu), mỗi bước có nút "Đi tới". Chỉ gợi ý, không làm thay.
- **Chẩn đoán**: hộp chữ chỉ đọc = `DiagnosticsReport` (phiên bản, hệ điều hành, thư mục dữ liệu, schema, số đơn theo nhóm, sao lưu, mã phòng chỉ 4 ký tự đầu, ổ đĩa) + câu trạng thái hiện tại của đồng bộ điện thoại, theo dõi thư mục, Google Sheet, Salework (câu nào có địa chỉ Apps Script bị thay bằng "đã ẩn"). Nút "Sao chép tất cả" (qua `IClipboardService`), "Làm mới", "Mở thư mục dữ liệu", "Mở thư mục sao lưu".
- Khối phím tắt (chỉ phím CÓ THẬT: Ctrl+K, Enter, Delete, Esc) và dòng phiên bản.
- Lệnh làm mới chồng nhau (vừa mở trang vừa bấm) trả lại chính lượt đang chạy thay vì bỏ qua (sửa một lỗi thật tìm ra khi chạy test cả nhóm).

## Kiểm chứng
- Build 0 lỗi 0 cảnh báo; 10 test mới (`UiTests/T025HelpAndDiagnosticsUiTests.cs`): vị trí sidebar, 5 bước đúng ✓/○ và đổi theo cài đặt, "Đi tới" điều hướng, báo cáo sao chép KHÔNG chứa địa chỉ Apps Script giả/mã định danh giả, làm mới dựng lại báo cáo, các nút mở thư mục qua bản giả, bản thật trên DB không tồn tại (không tạo tệp, mã phòng bị che), bản thật trên DB tạm (có số đơn nhưng KHÔNG chứa mã đơn/mã vận đơn/tên sản phẩm), mở thư mục gọi bộ mở đúng một lần.
- Toàn bộ test: 516/516 xanh (một lần chạy đầy đủ cuối). Ảnh chụp `tasks/reports/T025-screens/` (sáng/tối); claude đã xem ảnh sáng.

## Chưa kiểm chứng
- Chưa chạy với DB thật của chủ dự án; bước 2 chỉ biết "đồng bộ đang bật" chứ không biết điện thoại đã nhập mã phòng chưa (ghi "kiểm tra trên điện thoại").

TRẠNG THÁI: XONG
