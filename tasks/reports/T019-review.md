# T019 - Kiểm tra của claude (2026-11-23)

Kết luận: **ĐẠT** (có hai việc claude chưa tự chạy, ghi ở "Chưa kiểm chứng").

Đã tự kiểm tra:
- Phạm vi: ngoài `App/` + `UiTests/`, chỉ có tệp của claude đổi (`Data/Sync/*`, `AppSettings`, `BackupService`, `AuditLogReader`, `OrderExporter`, các test của claude). Agy không đụng `Core/`/`Data/`.
- Test không-UI của claude (dự án riêng, 264 test): xanh.
- Bộ chạy hàng đợi tự build + `dotnet test` toàn solution sau T019 trước khi sang T020: qua (không dừng, không có `queue-stopped.txt`). Agy báo 413/413 xanh 10/10 lần.
- Đã xem ảnh `01-phone-sync-logs.png`: nhật ký có đủ nhãn (Đơn mới, Nhận kiện, Ghi chú, Sửa trạng thái, Xóa, Ảnh), màu đúng (xanh/vàng/đỏ), đếm "Đơn mới từ điện thoại hôm nay", ghi chú đơn vào thẳng Bảng chính, cảnh báo máy chủ công cộng không mã hóa, toast đơn mới.
- Không thấy `ClearAllPools` mới; `File.Delete` chỉ ở `RealSaleworkController` (đã duyệt ở T016).

## Chưa kiểm chứng
1. claude CHƯA tự chạy `dotnet test` toàn solution 10 lần cho phiếu này (agy đang build T020 cùng lúc nên chạy song song sẽ tranh thư mục build); sẽ chạy lại khi hàng đợi nghỉ.
2. Chưa thử với iPhone thật; chưa xem ảnh `02-main-table-new-order-toast.png`.
