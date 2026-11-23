# Báo cáo T013 - Giao diện thao tác thủ công trên đơn (antigravity)

## Đã hoàn thành
1. **Dịch vụ sửa đơn & bản giả (`App/Services/`)**:
   - `IOrderEditService` + `SqliteOrderEditService` (gọi `OrderEditor` qua `Task.Run`).
   - `FakeOrderEditService` + `FakeDataService(enableMutableDemoStore: true)`: sửa trực tiếp trong bộ nhớ ở chế độ `--demo` và test, không bao giờ ghi/tạo tệp DB.
   - `OrderEditHelper`: xử lý thống nhất kết quả `Saved` (tải lại các màn hình/badge/Trang chủ), `NoChange` (không báo lỗi), `Conflict` (thông báo đã bị thay đổi ở nơi khác, tải lại và không ghi đè), `NotEditable`/`NotFound`/`Invalid`/`Error` (thông báo tiếng Việt rõ ràng). Luôn truyền `expectedOld` bằng giá trị đang hiển thị.
   - `IOrderActionDialogService` + `WpfOrderActionDialogService`: tách toàn bộ hộp thoại để ViewModel kiểm thử được mà không chặn UI.

2. **Hộp thoại & giao diện thao tác (`App/Views/` & `App/ViewModels/`)**:
   - **3 bảng sàn (`ReturnOrdersView`, `ReturnOrdersViewModel`)**: menu chuột phải + nút `•••` cuối dòng (dùng được bằng bàn phím): Đổi trạng thái (`ChangeStatusWindow` hiển thị trạng thái hiện tại + 5 giá trị `ReceiveStatus`), Ghi chú của tôi (`EditNoteWindow` nhiều dòng), Sửa lý do, Bật/tắt theo dõi kèm lý do (`WatchlistEditWindow`), Dời sang kho lưu trữ, Xem lịch sử sửa (`OrderHistoryWindow`), Xóa. Phím `Delete` trên dòng đang chọn mở cùng hộp xác nhận.
   - **Chọn nhiều dòng (`SelectionMode="Extended"`)**: thanh thao tác hàng loạt (`Đã chọn X dòng`: Đánh dấu đã nhận, Dời sang lưu trữ, Xóa) có hộp xác nhận nêu SỐ LƯỢNG, chạy tuần tự và báo `n thành công, m không đổi/lỗi` kèm lý do lỗi đầu tiên. Thao tác trên kiện A không ảnh hưởng kiện B cùng mã đơn.
   - **Kho lưu trữ (`ArchivedOrdersView`, `ArchivedOrdersViewModel`)**: menu chuột phải + `•••` + phím `Delete`: Trả về bảng chính (`Unarchive`), Xem lịch sử sửa, Xóa.
   - **Đã xóa (`DeletedOrdersView`, `DeletedOrdersViewModel`)**: nút `↩ Khôi phục` + `•••` + menu chuột phải: Khôi phục (`Restore`), Xem lịch sử sửa (chỉ đọc); không có thao tác sửa.
   - **Xác nhận xóa & Hoàn tác (`ConfirmActionWindow`, `MainWindow.xaml`, `MainViewModel`)**: hộp xác nhận nêu mã vận đơn, mã đơn và "Lưu ý: Đơn vẫn khôi phục được ở màn Đã xóa"; thông báo nổi `"Đã xóa ... ↩ Hoàn tác"` tự ẩn sau ~8 giây.
   - **Khung Lịch sử sửa (`OrderHistoryWindow`)**: cột Giờ, Trường, Giá trị cũ → mới, Loại, mới nhất trên cùng; trống hiện `"Chưa có sửa đổi nào"`.

3. **Kiểm tra phạm vi & build/test**:
   - Chỉ sửa/thêm trong `v2/src/QuanLyHangHoan.App/` và `v2/tests/QuanLyHangHoan.Tests/UiTests/`. Không đụng `Core/`, `Data/`, `ImporterTests/`, test gốc.
   - `dotnet build v2/QuanLyHangHoan.sln`: 0 lỗi, 0 cảnh báo.
   - `dotnet test v2/QuanLyHangHoan.sln`: **228/228 xanh** (9 test mới trong `T013ManualActionsUiTests.cs`).
   - Ảnh chụp lưu tại `tasks/reports/T013-screens/` (6 ảnh).

4. **Thiếu API ở Data**: Không thiếu. `OrderEditor` đã đáp ứng đủ.

TRẠNG THÁI: XONG
