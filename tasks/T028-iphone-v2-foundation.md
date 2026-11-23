# T028 - App iPhone v2: nền tảng sạch theo hợp đồng PC v2 (giao cho: antigravity, người kiểm tra: claude)

PHẠM VI ĐẶC BIỆT (chủ dự án đã đồng ý mở rộng cho phiếu iPhone): CHỈ `ios-app/` (thư mục MỚI, đã sao từ `ios-native/`). KHÔNG sửa `ios-native/` (bản tham chiếu), `v2/` (C#), `.github/` ngoài tệp `build-iphone-v2.yml` đã có. Thiếu API/hợp đồng ở PC thì ghi vào báo cáo.
Đọc trước: `AGENTS.md`, `docs/iphone-app-v2-plan.md` (BẮT BUỘC, gồm hợp đồng bản tin), `docs/sync-design.md` (mục "app iPhone gốc"), `v2/src/QuanLyHangHoan.Data/Sync/PhoneSnapshot.cs` và `PhoneActions.cs` (cái PC gửi/nhận), mã trong `ios-app/`.

## GIỚI HẠN THẬT (đọc kỹ)
Máy này KHÔNG có Swift/Xcode: bạn KHÔNG thể biên dịch hay chạy thử. Vì vậy: viết mã thận trọng, đơn giản, chỉ dùng API SwiftUI/Foundation/Swift 5.9, iOS 16; bám sát kiểu mã đã chạy được trong `ios-native/` (copy cách dùng nguyên văn khi có thể); không thêm thư viện ngoài; giữ mỗi tệp nhỏ; sau mỗi nhóm thay đổi tự đọc lại toàn bộ để bắt lỗi cú pháp/kiểu/thiếu import. TUYỆT ĐỐI không tuyên bố "đã build"/"test xanh": ghi "CHƯA BIÊN DỊCH (không có Swift trên máy này)" vào báo cáo. Kiểm chứng thật sẽ do GitHub Actions + claude đọc.

## Việc cần làm
1. **Dọn theo hợp đồng v2** (trong `ios-app/`): 
   - BỎ "Hộp Đơn Mới" (InboxRow, `inbox_push`, `inbox_push_many`, màn/nút/đếm liên quan) và BỎ tạo ĐƠN HỦY từ điện thoại (`ScanKind.cancelOrder`, nút/lựa chọn tạo đơn hủy). Giữ: quét nhận kiện (`barcode`), tạo đơn thường (`new_order_scan` với `cancelled:false`), xem/đánh dấu đã nhận đơn hủy đã có (`cancel_receive`).
   - Danh sách trạng thái chọn khi sửa đơn: "Chưa nhận" (PC hiểu là "Chưa trả"), "Hàng đã trả", "Không cần nhận", "Bị mất" (bỏ "Hư hỏng"). Sửa sản phẩm/màu/size/số lượng/loại đơn từ điện thoại: PC từ chối (`invalid`), nên ẨN các ô sửa đó; chỉ cho đổi trạng thái, ghi chú/theo dõi, xóa.
   - Xử lý đúng các `outcome` mới của `pc_ack`: `invalid`, `locked`, `ambiguous` (hiện thông báo tiếng Việt rõ, KHÔNG coi là thành công, KHÔNG xóa khỏi hàng đợi gửi lại khi `error`; `duplicate`/`not_found`/`ambiguous`/`invalid`/`locked` là kết quả dứt khoát).
   - Mã phòng mặc định: KHÔNG dùng `SHP-2026`; ô nhập mã phòng trống lúc đầu, kiểm tra tối thiểu 12 ký tự, nhắc "lấy mã ở màn Đồng bộ điện thoại của app PC v2".
2. **Nhận diện app**: tên hiển thị "Quét Hàng Hoàn v2", bundle `com.quanlyhanghoan.scannerv2` (khác bản cũ để cài song song), phiên bản mặc định 2.0.0; cập nhật `ios-app/App/project.yml`.
3. **Khung giao diện**: thanh tab dưới (Quét, Đơn, Hàng hoàn về, Thống kê, Thêm/Cài đặt) với giao diện thống nhất tông màu app PC v2 (cam/xanh đậm, thẻ bo góc, chữ rõ), hỗ trợ chế độ tối. Các tab chưa có nội dung ở phiếu này giữ màn hiện có hoặc màn trống "Sắp có" (không để app chết).
4. **Test lõi (`ios-app/ScannerCore/Tests`)**: cập nhật/bổ sung test cho phần đã đổi (parse `pc_ack` với outcome mới, payload quét không còn `cancelOrder`, snapshot có `rw/rws/ar`, bỏ test Inbox). Test dựa theo ví dụ JSON do PC v2 thật sinh (xem `PhoneSnapshot.cs`).
5. Viết `ios-app/README.md` ngắn: cách build trên GitHub Actions (`build-iphone-v2.yml`), cách cài IPA, cách nhập mã phòng.

## Tiêu chí hoàn thành
- Mọi thay đổi chỉ trong `ios-app/`; `ios-native/` và `v2/` KHÔNG đổi (claude sẽ so băm).
- Không còn tham chiếu `Inbox`/`inbox_push`/`cancelOrder`/`SHP-2026` trong `ios-app/` (claude sẽ grep).
- Báo cáo `tasks/reports/T028-report.md`: liệt kê từng tệp đã sửa và lý do; mục "Chưa biên dịch"; dòng cuối `TRẠNG THÁI: XONG` hoặc `CHƯA XONG (lý do)` (XONG nghĩa là mã đã viết xong và tự rà soát, KHÔNG phải đã biên dịch). Cập nhật `TASKS.md`.
