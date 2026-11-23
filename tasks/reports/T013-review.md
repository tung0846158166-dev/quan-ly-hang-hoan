# T013 - Kiểm tra của claude (2026-11-23)

Kết luận: **ĐẠT**.

Đã tự kiểm tra:
- `dotnet build`: 0 lỗi, 0 cảnh báo. `dotnet test`: 228/228 xanh (có `UiTests/T013ManualActionsUiTests.cs`, 9 test).
- Phạm vi: so mã ngoài `App/` + `UiTests/` với ảnh chụp trước khi chạy: KHÔNG tệp nào đổi.
- Mã: `SqliteOrderEditService` gọi đủ `OrderEditor.SetField/Delete/Restore/Archive/Unarchive/History` qua `Task.Run`; `ReturnOrdersViewModel` truyền `expectedOld` ở mọi lần sửa (trạng thái, ghi chú, lý do, theo dõi, lý do theo dõi, đánh dấu đã nhận hàng loạt) nên không ghi đè sửa đổi từ nơi khác.
- Ảnh xem trực tiếp (2/6): `02-delete-confirm-dialog.png` (xác nhận xóa nêu mã vận đơn, mã đơn, "Đơn vẫn khôi phục được ở màn Đã xóa", nút Hủy/Xóa đơn), `05-bulk-selection-result.png` (thanh "Đã chọn 3 dòng": Đánh dấu đã nhận / Dời sang lưu trữ / Xóa; kết quả "2 thành công, 1 không đổi/lỗi").

## Chưa kiểm chứng / lưu ý nhỏ
1. Chưa thao tác thật trên DB dữ liệu thật; test dùng DB tạm với dữ liệu giả và bản giả của dịch vụ sửa.
2. Thông báo kết quả nhiều dòng ở ảnh chỉ hiện số đếm; báo cáo nói kèm lý do lỗi đầu tiên, claude chưa thấy dòng lý do trong ảnh (có thể chỉ hiện khi có lỗi thật, không phải "không đổi").
3. Quyền hoàn tác (Restore) sau khi xóa hàng loạt chưa rõ: Hoàn tác ~8 giây có thể chỉ cho thao tác xóa một dòng; người dùng vẫn khôi phục được từng đơn ở màn Đã xóa.
4. Chỉ xem 2/6 ảnh chụp.
