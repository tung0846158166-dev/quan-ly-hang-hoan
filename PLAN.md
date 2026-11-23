# PLAN - Xây dựng lại thành phần mềm cài đặt (v2)

## Quyết định đã chốt với chủ dự án
- Làm phần mềm cài đặt đúng nghĩa (không còn "1 file HTML bọc Electron").
- Đồng bộ điện thoại: PHẢI chạy khi PC và điện thoại ở 2 mạng khác nhau. Trước mắt GIỮ phương án A (MQTT/ntfy công cộng + mã phòng như app cũ). Phương án B (relay riêng, mã hóa đầu cuối) tính sau khi bản mới ổn định. => Thiết kế lớp `SyncTransport` trừu tượng để đổi A -> B không sửa nghiệp vụ.
- BẮT BUỘC: nhập được dữ liệu cũ đang chạy từ đầu tháng. Không được mất, ghi đè hay đoán lại dữ liệu cũ.

## Stack đề xuất
**CẬP NHẬT: app Windows NATIVE, không dùng HTML/Electron cho desktop.** C# + .NET 8 + WPF (giao diện gốc Windows), SQLite (Microsoft.Data.Sqlite 8.x), ClosedXML/ExcelDataReader (Excel), MQTTnet + ntfy (đồng bộ, phương án A), WebView2 chỉ cho tự động hóa Salework nền, xUnit (test), bộ cài Inno Setup/MSIX + tự cập nhật. Trang quét điện thoại vẫn là web (PWA) vì cần chạy cả iPhone/Android.
Solution: `v2/QuanLyHangHoan.sln` gồm `src/QuanLyHangHoan.Core` (nghiệp vụ, không phụ thuộc UI), `src/QuanLyHangHoan.Data` (SQLite + importer dữ liệu cũ), `src/QuanLyHangHoan.App` (WPF), `tests/QuanLyHangHoan.Tests` (xUnit). Build: `dotnet build v2/QuanLyHangHoan.sln`, test: `dotnet test v2/QuanLyHangHoan.sln`.
(Kế hoạch cũ Electron+React đã bị bỏ.)

## Dữ liệu cũ cần nhập (đã khảo sát, chỉ xem cấu trúc)
Thư mục: `%USERPROFILE%\OneDrive\Documents\QuanLyHangHoan\` (có thể là `Documents\QuanLyHangHoan` trên máy khác)
- `du_lieu_hang_hoan.json`: mảng đơn (~2.3k). Trường: shipDate, returnDate, recvDate, platform, orderType, platformStatus, preOrder, orderId, returnOrderId, track, altTrack, carrier, price, status, photo, note, buyerNote, userNote, items, importWeek, importedAt, importedAtFormatted, _notificationOrderUid
- `phu_shopee_archived_orders_v1.json` (~70MB, dạng {v:...}): kho lưu trữ
- `phu_shopee_cancelled_orders_v1.json`, `phu_shopee_deleted_orders_v1.json`
- `phu_shopee_excel_audit_log_v1.json`, `phu_ShopeeReturnCellEditHistory_v1.json`, `phu_deadline_notified_v1.json`, `phu_shopee_app_notifications_v1.json`
- `phu_shopee_excel_cell_locks_v1.json`, `..._custom_week_tabs_v2.json`, `..._active_import_week_v2.json`, `..._accountant_stock_v1.json`, `phu_shopee_new_orders_inbox_v1.json`, `phu_shopee_preferred_platform_workspace.json`, `salework_auto.json`
- `khoi_phuc_don_da_nhan.json`, thư mục `khoi_phuc/`, `sao_luu_tu_dong/`
- `photos/` (~1.9k file .json, tên là hash): ảnh

### Yêu cầu bộ nhập (importer)
1. CHỈ ĐỌC thư mục cũ. Bước đầu tự sao chép nguyên thư mục sang vùng an toàn rồi mới đọc từ bản sao.
2. Chế độ "chạy thử" (dry-run): báo số đơn/ảnh/bản ghi sẽ nhập, trùng, lỗi - chưa ghi gì.
3. Nhập trong 1 transaction cho từng nhóm; lỗi thì rollback, báo rõ dòng nào.
4. Idempotent: chạy lại nhiều lần không tạo trùng (khóa theo orderId + track + returnOrderId, giữ mã lịch sử/altTrack).
5. Giữ nguyên quy tắc kiện A/B/C: KHÔNG gộp/tách dữ liệu lịch sử bằng phỏng đoán.
6. Nhập cả: đơn chính, kho lưu trữ, đơn hủy, đơn đã xóa, nhật ký, khóa ô, ảnh. Giữ lại bản JSON gốc kèm theo để đối chiếu.
7. Báo cáo đối chiếu sau nhập: tổng cũ = tổng mới, danh sách bản ghi bỏ qua + lý do.
8. Có test với dữ liệu GIẢ mô phỏng cấu trúc (không dùng đơn thật).

## Giai đoạn
0. Chuẩn bị: cài git, Node, `npm install`, chạy thử app cũ để đối chiếu hành vi.
1. Lõi nghiệp vụ + schema SQLite + resolver mã vận đơn + nhập Excel Shopee/TikTok/Salework (test đỏ->xanh, bám `tests/` cũ).
2. IMPORTER dữ liệu cũ (xem trên) - làm ngay sau khi có schema, trước khi làm giao diện lớn, để thử trên dữ liệu thật của chủ dự án.
3. Giao diện desktop WPF (MVVM): bảng hoàn về, đơn mới, đơn hủy, kho lưu trữ, thống kê, cảnh báo Vàng 12h/Đỏ 24h.
4. Đồng bộ điện thoại qua `SyncTransport` (bản A: MQTT/ntfy), PWA quét, hàng đợi bền, persistence-before-ACK.
5. Tính năng phụ: Salework tự động, Google Sheet, theo dõi thư mục, tray, autostart, backup.
6. Đóng gói: NSIS installer, tự cập nhật, hướng dẫn chuyển từ app cũ sang mới.
7. (Sau) Phương án B: relay riêng + mã hóa đầu cuối.

## Chia việc (xem TASKS.md)
Claude: lõi nghiệp vụ, SQLite, importer, SyncTransport. Antigravity: giao diện React, PWA. Chung hợp đồng: kiểu TypeScript trong `src/shared/` và IPC.
