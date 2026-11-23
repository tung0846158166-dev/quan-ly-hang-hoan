# T020 - Kiểm tra của claude (2026-11-23)

Kết luận: **ĐẠT** (kèm hai việc chưa tự chạy).

Đã tự kiểm tra:
- Phạm vi: ngoài `App/` + `UiTests/`, chỉ có tệp của claude đổi. Agy không đụng `Core/`/`Data/`.
- Bộ chạy hàng đợi tự build + `dotnet test` toàn solution sau T020 trước khi sang phiếu kế: qua (không có `queue-stopped.txt`). Test không-UI của claude (275) xanh.
- Đã xem ảnh `01-global-search-results.png`: ô tìm toàn cục (Ctrl+K) trả hai kiện cùng mã đơn thành HAI dòng riêng, có vị trí ("Shopee - Hoàn hàng"), trạng thái, nút "Đi tới bảng".

## Chưa kiểm chứng
1. claude CHƯA tự chạy `dotnet test` toàn solution 10 lần (agy build phiếu kế cùng lúc); dựa vào bản chạy của agy (10/10) và cổng của hàng đợi.
2. Chưa xem ảnh 02-04; chưa thử tìm bằng dữ liệu thật; chưa thao tác tay Ctrl+K.
