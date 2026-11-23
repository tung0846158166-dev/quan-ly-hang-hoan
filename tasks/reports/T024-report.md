# T024 - Nút "Xuất file" (Excel/CSV) cho các bảng đơn

**Người làm: claude (agy đã dừng).** Không có người thứ hai kiểm tra độc lập.

## Đã làm
- `IOrderExportService` / `OrderExportService` (bọc `OrderExporter` của lõi): mỗi KIỆN một dòng, không có cột thông tin khách; CSV UTF-8 có BOM, chặn công thức; XLSX tự dựng. Hỏi nơi lưu qua `IFileSaveService.PickSavePath` (tên gợi ý `QuanLyHangHoan-<bảng>-yyyyMMdd-HHmm.xlsx`), tệp chỉ được tạo SAU khi người dùng chọn nơi lưu; ghi tạm (`.tmp`) rồi đổi tên nên lỗi giữa chừng KHÔNG để lại tệp dở (`WindowsFileSaveService.WriteBytesAsync`).
- Nút "⬇ Xuất file ▾" ở 3 bảng sàn (Shopee Hoàn hàng, Shopee Đơn hủy, TikTok Hoàn hàng) và Kho lưu trữ: Excel/CSV cho "các dòng đang hiển thị" (đúng bộ lọc, đúng thứ tự); ở 3 bảng sàn có thêm "chỉ các dòng đã chọn". Không có dòng nào: báo "Không có dòng nào để xuất", không hỏi nơi lưu, không tạo tệp. Hủy hộp thoại lưu: im lặng, không tạo tệp. Lỗi ghi (tệp đang mở bằng Excel, đĩa đầy...): báo đỏ rõ ràng.
- Trạng thái hiện dưới nút và thông báo nhỏ khi thành công/lỗi. KHÔNG sửa dữ liệu đơn hay DB.
- Cột "Sản phẩm đã hoàn" trong tệp xuất (đơn hoàn một phần).

## Kiểm chứng
- Build 0 lỗi 0 cảnh báo; 9 test mới (`UiTests/T024ExportUiTests.cs`): xlsx/csv đúng số dòng và thứ tự, đọc lại xlsx bằng `SpreadsheetReader`, tên gợi ý, hủy/không dòng không tạo tệp, lỗi ghi không để tệp dở, ghi thật thay tệp đích nguyên tử và dọn `.tmp`, xuất không đổi dữ liệu đơn, cột "Vị trí" của kho lưu trữ, view model xuất đúng số dòng "hiển thị" và "đã chọn", lỗi hiện đỏ, hủy không bật thông báo.
- Toàn bộ test: 516/516 xanh (một lần chạy đầy đủ cuối).

## Khác phiếu và chưa kiểm chứng
- KHÔNG làm hộp xác nhận "Xuất N dòng?" riêng: hộp thoại lưu của Windows đã là bước xác nhận, thông báo thành công nêu số dòng. Chưa thêm nút ở màn Đã xóa, Bảng đơn mới, Đang theo dõi.
- Chưa mở tệp xuất bằng Excel thật; chưa xem ảnh chụp nút/menu (menu chuột phải hiện khi bấm, không có trong ảnh tự động).

TRẠNG THÁI: XONG
