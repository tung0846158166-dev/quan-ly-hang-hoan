# T026 - Kiểm tra của claude (2026-11-23)

Kết luận: **ĐẠT** (kèm hai việc chưa tự chạy).

Đã tự kiểm tra:
- Phạm vi: ngoài `App/` + `UiTests/`, chỉ có tệp của claude đổi (`ReturnWatchService`, `AppDatabase` v5...). Agy dùng `ReturnWatchService.Load/Remove/Restore` đúng API, không tự thêm dữ liệu.
- Bộ chạy hàng đợi build + `dotnet test` toàn solution sau T026 trước khi sang T021: qua. Test không-UI của claude (275, gồm 9 test `ReturnWatchTests`) xanh.
- Đã xem ảnh `01-all-4-stages.png`: nút lọc có số đếm (Đang theo dõi 7, Đã đồng ý 2, Đang về 3, Đã về shop 2, Đã nhận kho 1, Tất cả 8); kiện A và B cùng mã đơn là hai dòng riêng; tiến trình 4 chặng; cảnh báo vàng/đỏ chỉ ở chặng 3; nhãn "DỮ LIỆU GIẢ" ở chế độ demo.

## Chưa kiểm chứng
1. claude CHƯA tự chạy `dotnet test` toàn solution 10 lần cho phiếu này (agy build phiếu kế cùng lúc).
2. Chưa xem ảnh 02-04; CHƯA thử với dữ liệu thật: cần chủ dự án so bảng này với "Hàng hoàn về" của app cũ sau khi nhập dữ liệu cũ.
