# T006 - Kiểm tra của claude (2026-11-23)

Kết luận: **ĐẠT**.

Đã tự kiểm tra (không chỉ tin báo cáo):
- `dotnet build`: 0 lỗi, 0 cảnh báo. `dotnet test`: 65/65 xanh (có `UiTests/T006PlatformSplitTests.cs`).
- Grep trong `App/` (trừ bin/obj): không còn "TikTok Shop" và không còn `CancelledOrder*`.
- Phạm vi: so `Core/`, `Data/`, `ImporterTests/` với ảnh chụp trước khi chạy: agy KHÔNG sửa gì ngoài `App/` + `UiTests/` (chỉ 2 tệp mới là của claude: `Core/Import/ParcelUpsert.cs`, `Data/OrderRepository.cs`).
- Ảnh chụp xem trực tiếp (Trang chủ, Shopee - Đơn hủy): sidebar đúng nhóm Shopee/TikTok/Chung, mở vào Trang chủ, KPI Vàng/Đỏ, xu hướng, cơ cấu loại đơn, "Cần xử lý ngay", báo cáo nhanh; bảng Shopee - Đơn hủy lọc đúng loại, có cột Loại đơn và chip lọc.

## Lưu ý để nối dữ liệu thật
1. Mọi số liệu trên màn hình hiện là DỮ LIỆU GIẢ (có nhãn "Dữ liệu giả"). Việc kế tiếp của claude: nối `Data/OrderRepository` vào các provider.
2. Shopee - Hoàn hàng = mọi đơn Shopee không phải "Đơn hủy" (gồm 17 "Giao không thành công"). Chủ dự án chưa xác nhận quy tắc này.
3. Dữ liệu giả lệch tỉ lệ so với thật (hủy chiếm 32/40 ở bản giả, thật là 1.033/2.307 = khoảng 45%). Chỉ ảnh hưởng ảnh chụp, không ảnh hưởng logic.
4. Kho lưu trữ vẫn là provider riêng: sẽ chuyển sang cờ `IsArchived` khi nối `OrderRepository`.
5. Chưa chạy thử giao diện với 50.000 dòng thật hay dữ liệu thật; ảnh chụp do agy tạo, claude chỉ xem 2/5 ảnh.
