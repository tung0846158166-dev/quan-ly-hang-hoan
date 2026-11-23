# T030 - Hoàn một phần: giữ đủ sản phẩm, sản phẩm đã hoàn khác màu

**Người làm: claude (không phải agy).** Chủ dự án yêu cầu dừng agy (máy chủ mô hình của agy báo quá tải 503 liên tục) và để claude tự làm. Vì vậy phiếu này KHÔNG có người thứ hai kiểm tra độc lập; kiểm chứng dựa vào test tự động và ảnh chụp do claude xem.

## Đã làm
- Dữ liệu (đã có từ trước, xem TASKS.md): `OrderItem.ReturnedQty`, schema v6, mapper TikTok/Shopee, xuất Excel, trường `rn` gửi iPhone.
- Giao diện WPF:
  - `ViewModels/ItemDisplay.cs`: quy tắc hiển thị (chỉ phân biệt khi hoàn MỘT PHẦN = có cả sản phẩm hoàn lẫn không hoàn); chip "Hoàn K/N SP" hoặc "Hoàn một phần" (khi chỉ có cờ của sàn); tooltip; chuỗi phẳng.
  - `Controls/ItemLinesTextBlock.cs`: ô hiển thị có màu theo theme; đơn hoàn một phần xếp MỖI SẢN PHẨM MỘT DÒNG, sản phẩm hoàn đậm + màu nhấn + nhãn "[Hoàn N]", sản phẩm không hoàn xám + nhãn "[Không hoàn]" (luôn có chữ). Đơn thường vẫn một dòng gọn.
  - Màu `ItemReturnedBrush`/`ItemReturnedBgBrush`/`ItemNotReturnedBrush` ở cả `LightTheme.xaml` và `DarkTheme.xaml`.
  - Gắn vào các bảng: Hoàn hàng 3 sàn (`ReturnOrdersView`), Kho lưu trữ, Đang theo dõi, Hàng hoàn về. Dòng bảng đổi `RowHeight` cố định thành `MinRowHeight` để dòng hoàn một phần cao ra.
  - Chi tiết đơn: cột "Hoàn về", dòng hoàn nền nhạt + chữ nhấn, dòng không hoàn xám (đặt ở kiểu ô vì kiểu ô mặc định ghi đè màu chữ của dòng), dòng tóm tắt "Hoàn 1/3 sản phẩm: ...".
  - Dữ liệu demo: hai đơn hoàn một phần (3 sản phẩm hoàn 1; 2 sản phẩm hoàn 1).
- Chưa làm (phạm vi phiếu ghi "không bắt buộc"): ô lọc "Chỉ đơn hoàn một phần"; Deleted/NewOrders/kết quả tìm kiếm vẫn hiển thị tóm tắt cũ (một dòng, không màu).

## Kiểm chứng
- `dotnet build` toàn solution: 0 lỗi, 0 cảnh báo.
- `dotnet test` toàn bộ: 476/476 xanh, chạy 10 lần liên tiếp, 0 lần đỏ (11 test mới trong `UiTests/T030PartialReturnUiTests.cs`: quy tắc hoàn một phần/hoàn đủ/chưa biết, cờ sàn không rõ sản phẩm, ô hiển thị có màu khác nhau + nhãn chữ, Chi tiết đơn, view model dòng, hai kiện cùng mã đơn hiển thị độc lập, ảnh chụp).
- Ảnh chụp: `tasks/reports/T030-screens/` (bảng sáng/tối, chi tiết sáng/tối). Claude đã xem trực tiếp: bảng hiện chip + đủ 3 sản phẩm, chi tiết tối đúng màu.
- Không có thao tác ghi dữ liệu ở màn hiển thị.

## Chưa kiểm chứng
- Chưa thử với dữ liệu thật (đơn nạp từ app cũ có ReturnedQty = 0 nên chưa được đánh dấu cho tới khi nạp lại file hoàn).
- Chưa chạy trên màn hình có DPI/phông khác.

TRẠNG THÁI: XONG
