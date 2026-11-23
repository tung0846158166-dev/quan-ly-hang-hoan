# T021 - Kiểm tra của claude (2026-11-23)

Kết luận: **ĐẠT** (kèm việc chưa tự chạy).

Đã tự kiểm tra:
- Phạm vi: ngoài `App/` + `UiTests/`, chỉ có tệp của claude đổi. Agy không đụng `Core/`/`Data/`.
- Mã chạm hệ thống: registry chỉ ở `IAutoStartService.cs`, chỉ khóa `HKCU\...\CurrentVersion\Run` của chính app (tên giá trị riêng), tắt thì chỉ `DeleteValue` đúng giá trị đó; mutex một bản chạy ở `ISingleInstanceGuard.cs`. Không thấy ghi/xóa tệp dữ liệu hay `ClearAllPools` mới. Tất cả đi qua interface có bản giả (test không đụng registry/khay thật).
- Bộ chạy hàng đợi tự build + `dotnet test` toàn solution sau T021 trước khi sang T028: qua. Agy báo 446/446 xanh 10/10 lần. Test không-UI của claude (276) xanh.
- Đã xem ảnh `01-home-background-settings-light.png`: khối "Chạy nền và thông báo" có 4 công tắc đúng; Trang chủ hiện "Hàng đang về: 3" (từ T026).

## Chưa kiểm chứng
1. claude CHƯA tự chạy `dotnet test` toàn solution 10 lần (agy build T028 cùng lúc); dựa vào bản chạy của agy và cổng hàng đợi.
2. CHƯA thử khay hệ thống, thông báo Windows, ghi/xóa khóa tự mở thật (chỉ bản giả); chưa xem ảnh nền tối.
3. Lưu ý vận hành: bật "Tự mở cùng Windows" sẽ ghi khóa Run thật của người dùng; tắt công tắc để gỡ.
