# T012 - Kiểm tra của claude (2026-11-23)

Kết luận: **ĐẠT**.

Đã tự kiểm tra:
- `dotnet build`: 0 lỗi, 0 cảnh báo (2 cảnh báo xUnit2013 agy thấy là của `ImportApplierTests.cs` do claude viết, claude đã sửa). `dotnet test`: 219/219 xanh (có `UiTests/T012UiPolishTests.cs`, 8 test).
- Phạm vi: so mã ngoài `App/` + `UiTests/` với ảnh chụp trước khi chạy: chỉ 5 tệp đổi (ImportApplier, ImportApplierTests, OrderEditor, OrderEditorTests, ParcelUpsert), TẤT CẢ do claude viết sau khi chụp. Agy không đụng Core/Data/test gốc.
- Ảnh xem trực tiếp (2/4): `02-home-dark-collapsed-sidebar.png` (sidebar thu gọn TIKTOK và CHUNG, tiêu đề nhóm có huy hiệu gộp: cảnh báo 2, số đếm 27), `03-shopee-return-dark.png` (bảng chế độ tối, nút "Giao diện sáng", màu cảnh báo Vàng/Đỏ/xanh giữ đúng ý nghĩa và đọc rõ).
- Cài đặt: `UiSettingsStore` dùng chung khóa với `RealSyncController`, luôn đọc bản mới nhất rồi chỉ sửa trường của mình (không đè Room/SyncEnabled/NtfyEnabled/Brokers); `--demo` chỉ lưu trong bộ nhớ.

## Chưa kiểm chứng / lưu ý nhỏ
1. Ở chế độ tối, ô chọn ngày của "Báo cáo nhanh" (Trang chủ) vẫn nền sáng: chưa đồng bộ theme (nhỏ, cosmetic).
2. Chưa kiểm cửa sổ nhiều màn hình/đổi độ phân giải thật (logic kiểm tra vị trí chỉ có test).
3. Chỉ xem 2/4 ảnh chụp.
