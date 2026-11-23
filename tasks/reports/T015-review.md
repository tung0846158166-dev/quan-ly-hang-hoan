# T015 - Kiểm tra của claude (2026-11-23)

Kết luận: **ĐẠT**.

Đã tự kiểm tra:
- `dotnet build`: 0 lỗi, 0 cảnh báo. `dotnet test`: 319/319 xanh; claude tự chạy 10 lần liên tiếp: 0 lần đỏ.
- Phạm vi: Core/Data chỉ có tệp của claude đổi trong lượt này (SheetPipeline, SheetSyncService); agy không sửa ngoài `App/` + `UiTests/`.
- Bảo mật: không có địa chỉ Apps Script thật trong báo cáo/log của T015; giao diện chỉ hiện trạng thái "Đã lưu địa chỉ" (`ShowSavedUrlState`), không có đường hiển thị lại địa chỉ; ảnh dùng địa chỉ giả `FAKE`.
- Nối khởi động ở `App.xaml.cs` chỉ khi `Enabled && HasUrl`.

## Chưa kiểm chứng
1. Chưa chạy với Google Apps Script thật trên mạng thật.
2. Chưa xem trực tiếp ảnh chụp màn hình (chỉ kiểm mã + test).
