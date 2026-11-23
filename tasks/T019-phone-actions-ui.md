# T019 - Giao diện cho thao tác từ app iPhone (đơn mới quét từ điện thoại vào thẳng Bảng chính) (giao cho: antigravity, người kiểm tra: claude)

Phạm vi: CHỈ `v2/src/QuanLyHangHoan.App/` và `v2/tests/QuanLyHangHoan.Tests/UiTests/`. KHÔNG sửa `Core/`, `Data/` (đặc biệt `Data/Sync/*`) và test ngoài `UiTests/` (kể cả `PhoneListTests.cs`). Thiếu API ở Data thì ghi vào báo cáo.
Đọc trước: `AGENTS.md`, `docs/sync-design.md` (mục "app iPhone gốc"), `v2/src/QuanLyHangHoan.Data/Sync/SyncService.cs` (sự kiện `Handled` và `SyncHandled`), `v2/src/QuanLyHangHoan.Data/Sync/PhoneActions.cs`, `tasks/reports/T011-review.md` (RealSyncController + nhật ký), `App/Services/ISyncController.cs`. Test dùng `WpfTestHost`, DB tạm + dữ liệu GIẢ + bản giả của controller; không chạm `%LocalAppData%` thật, không mạng thật; `dotnet test` toàn bộ 10 lần liên tiếp 0 lần đỏ.

## Bối cảnh (quyết định chủ dự án 23/11/2026)
App iPhone gốc quét mã để TẠO ĐƠN MỚI: PC (Data, claude đã làm) tạo ngay một kiện ở **Bảng chính** (Shopee hoặc TikTok tự nhận theo mã, trạng thái "Chưa trả", ghi chú "Đơn mới tạo từ máy quét điện thoại", sản phẩm "Sản phẩm mới (Quét điện thoại)"), rồi báo lại cho điện thoại. Tạo ĐƠN HỦY từ điện thoại đã BỎ (PC từ chối). Ngoài ra điện thoại còn gửi: ghi chú/theo dõi (`stage "note"`), đổi trạng thái (`"edit"`), xóa mềm (`"delete"`), đánh dấu đã nhận đơn hủy (`"edit"`), nhận kiện (`"scan"`).

## API có sẵn
`SyncService.Handled` phát `SyncHandled(RequestId, Code, Outcome, Message, At, Stage, Kind)`:
- `Outcome`: `success | duplicate | not_found | ambiguous | error | invalid | locked`.
- `Stage`: `scan | photo | note | edit | delete`.
- `Kind == "new_order"` (kèm `Outcome == "success"`): điện thoại VỪA TẠO ĐƠN MỚI ở Bảng chính (`Code` = mã vận đơn, `Message` chứa sàn, ví dụ "ĐÃ TẠO ĐƠN MỚI Ở BẢNG CHÍNH (Shopee)").
- `Outcome == "duplicate"` ở `Stage == "scan"` với Message "ĐƠN ĐÃ CÓ Ở ...": đơn đã tồn tại, không tạo lại.
Mọi thay đổi do điện thoại đã được lưu vào DB TRƯỚC khi sự kiện phát.

## Việc cần làm
1. **Tải lại dữ liệu**: khi nhận `Handled` có `Outcome == "success"` ở bất kỳ `Stage` nào trừ `photo` (hoặc `Kind == "new_order"`) thì tải lại các bảng đơn, badge sidebar, Trang chủ (cách đã dùng cho quét nhận kiện; không tải lại dồn dập: gộp trong ~500 ms). Giữ vị trí cuộn/dòng đang chọn nếu còn.
2. **Thông báo nhỏ** (toast/snackbar, không hộp thoại chặn):
   - `Kind == "new_order"`: "📥 Điện thoại vừa tạo đơn mới: <mã> (<sàn>)".
   - `duplicate` ở `scan` với Message "ĐƠN ĐÃ CÓ...": "⚠️ Điện thoại quét đơn đã có: <mã>" (vàng).
   - `Stage` `delete` thành công: "🗑 Điện thoại xóa đơn <mã> (khôi phục được ở Đã xóa)".
   - Các `Stage` `note`/`edit` thành công: một dòng gọn tương ứng ("📝 Điện thoại cập nhật ghi chú/trạng thái đơn <mã>").
   - Lỗi (`error`, `invalid`, `ambiguous`, `not_found`, `locked`): chỉ ghi vào nhật ký (mục 3), không bật toast.
3. **Nhật ký ở màn "Đồng bộ điện thoại"**: mỗi sự kiện một dòng có nhãn tiếng Việt theo `Stage`/`Kind` ("Đơn mới", "Nhận kiện", "Ghi chú", "Sửa trạng thái", "Xóa", "Ảnh") + kết quả (màu: xanh thành công, vàng trùng, đỏ lỗi) + mã + thời gian. Dòng "Đơn mới" nổi bật hơn (biểu tượng 📥).
4. **Đếm trên màn Đồng bộ điện thoại**: thêm dòng nhỏ "Đơn mới từ điện thoại hôm nay: N" (đếm từ các sự kiện `Kind == "new_order"` của phiên chạy; không cần lưu qua lần mở sau).
5. **Ghi chú trên màn hình** (một câu, cố định): "Đơn quét từ điện thoại vào thẳng Bảng chính với trạng thái 'Chưa trả', ghi chú 'Đơn mới tạo từ máy quét điện thoại'. Tạo đơn hủy từ điện thoại không còn." và một dòng cảnh báo nhỏ: "Danh sách đơn gửi sang điện thoại đi qua máy chủ công cộng không mã hóa; giữ bí mật mã phòng."
6. `--demo`: bản giả của controller phát được các sự kiện mẫu (để xem giao diện), không chạm DB/mạng.

## Tiêu chí hoàn thành
- `dotnet build` 0 lỗi 0 cảnh báo; `dotnet test` xanh TOÀN BỘ 10/10 lần.
- Test mới trong `UiTests/` (bản giả controller/phát sự kiện `SyncHandled` thủ công + DB tạm có đơn giả): `new_order` -> bảng chính tải lại và hiện đơn mới, toast đúng nội dung, đếm tăng, nhật ký có dòng "Đơn mới"; `duplicate` -> toast vàng, không tải lại thừa; lỗi chỉ vào nhật ký, không toast; `delete`/`edit`/`note` thành công tải lại; `photo` không tải lại; nhiều sự kiện dồn dập chỉ tải lại một lần (gộp); `--demo` không chạm đĩa.
- Ảnh chụp `tasks/reports/T019-screens/`: màn Đồng bộ điện thoại với nhật ký nhiều loại sự kiện, toast đơn mới, Bảng chính có dòng đơn mới từ điện thoại.
- Báo cáo `tasks/reports/T019-report.md`, dòng cuối `TRẠNG THÁI: XONG` hoặc `CHƯA XONG (lý do)`; ghi rõ "chưa thử với iPhone thật". Cập nhật `TASKS.md`.
