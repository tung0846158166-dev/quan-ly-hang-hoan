# T031 - App iPhone: giao diện hiện đại + màn "Tổng quan" mở đầu (giao cho: antigravity, người kiểm tra: claude)

PHẠM VI: CHỈ `ios-app/` (không sửa `ios-native/`, `v2/`). Đọc trước: `AGENTS.md`, `ios-app/README.md`, `docs/iphone-app-v2-plan.md`, `tasks/T029-iphone-v2-screens.md` (chức năng phải giữ), `v2/src/QuanLyHangHoan.Data/Sync/PhoneSnapshot.cs` (ý nghĩa từng trường dòng).
GIỚI HẠN THẬT: không có Swift/Xcode trên máy này, KHÔNG biên dịch được. Viết thận trọng, chỉ dùng API có từ iOS 16 trở xuống (kiểm tra `deployment target` trong `ios-app/App/project.yml`), tự đọc lại từng tệp để bắt lỗi cú pháp/kiểu. KHÔNG tuyên bố đã build. Claude sẽ đẩy lên GitHub để build thật rồi báo lỗi biên dịch (nếu có) cho bạn sửa.

## Vì sao
Chủ dự án đã cài bản hiện tại: "chả khác gì cái cũ". Muốn giao diện THÔNG MINH và HIỆN ĐẠI hơn. Ví dụ chủ dự án nêu: mở app lên phải thấy ngay thống kê đơn.

## Việc cần làm
1. **Màn "Tổng quan" là tab ĐẦU TIÊN và là màn mặc định khi mở app** (hiện đang mở vào tab Quét; Quét vẫn là một tab, thêm nút lớn "Quét ngay" ở Tổng quan). Dữ liệu lấy từ `SnapshotStore` (đã có `rows`, `alertCount`...), KHÔNG đổi giao thức đồng bộ.
2. Nội dung Tổng quan (cuộn dọc, thẻ bo góc lớn, khoảng cách rộng rãi):
   - Đầu trang: lời chào theo giờ ("Chào buổi sáng"), chấm trạng thái "PC đang online / offline / chưa kết nối" (từ `SyncEngine`), giờ cập nhật dữ liệu gần nhất, kéo xuống để xin lại danh sách (`snapshot_request`).
   - 4 thẻ số lớn: **Đang chờ hàng về**, **Đã nhận hôm nay**, **Quá hạn (đỏ)**, **Sắp đến hạn (vàng)**; bấm thẻ mở danh sách Đơn đã lọc đúng.
   - **Hàng hoàn về** dạng đường ống 4 chặng (Đã đồng ý -> Đang về -> Đã về shop -> Đã nhận kho) với số đếm từng chặng (từ `rw`/`rws`/`rv`), bấm mở màn Hàng hoàn về.
   - **Cần xử lý ngay**: tối đa 5 đơn đỏ rồi vàng gần hạn nhất (sàn, mã vận đơn, sản phẩm, "còn/quá bao lâu"), bấm vào mở Chi tiết đơn.
   - **Theo sàn**: Shopee / TikTok / Lazada: số đơn chờ + số quá hạn (thanh ngang nhỏ).
   - **Hoàn một phần**: số đơn có `rn` không rỗng, bấm mở danh sách đã lọc (nếu chưa có bộ lọc này thì thêm "Hoàn một phần" vào bộ lọc ở màn Đơn).
   - **7 ngày gần nhất**: cột nhỏ số đơn nhận mỗi ngày từ dữ liệu có sẵn (nếu không đủ dữ liệu thì ẩn thẻ, không bịa số). Dùng thuần SwiftUI (HStack/Capsule), KHÔNG dùng thư viện ngoài.
   - Cảnh báo "lệch N đơn" khi `stats` của PC cho thấy `dropped` > 0 hoặc số nhận được khác `sent` (như T029 mục 5).
   - Trạng thái rỗng thân thiện (chưa nhập mã phòng, chưa có dữ liệu): hướng dẫn 2 bước, nút đi tới Cài đặt.
3. **Làm mới giao diện toàn app cho hiện đại**: bộ màu/độ nổi/bo góc/chữ nhất quán trong `Theme.swift` (thẻ nổi nhẹ, bo 16-20, chữ số lớn đậm, nhấn cam thương hiệu `Theme.accent`), tối/sáng đều đẹp, `Dynamic Type` không vỡ bố cục, vùng bấm >= 44pt, dùng được một tay (nút chính ở nửa dưới). Áp cho danh sách Đơn (hàng đơn dạng thẻ, huy hiệu sàn, chấm màu cảnh báo), Hàng hoàn về, Thông báo, Lịch sử, Cài đặt (nhóm thẻ, không còn dạng `Form` mặc định trơ).
4. Chuyển động tinh tế: số đếm đổi mượt, kéo-để-làm-mới, rung nhẹ (`UIImpactFeedbackGenerator`) khi bấm thẻ/quét xong, KHÔNG làm chậm máy. Tôn trọng `reduceMotion`.
5. Giữ NGUYÊN mọi chức năng và hợp đồng đã có (quét, hàng đợi bền, xác nhận, hoàn một phần tô màu, đồng bộ). Không đổi `ScannerCore` trừ khi thêm logic thuần có test (ví dụ hàm tính số liệu Tổng quan nên nằm ở `ScannerCore` để test được bằng `swift test`: đếm theo sàn/chặng/cảnh báo, "cần xử lý ngay", dãy 7 ngày).

## Tiêu chí hoàn thành
- Mọi thay đổi chỉ trong `ios-app/`. Không còn tham chiếu `Inbox`/`inbox_push`/`cancelOrder`/`SHP-2026`.
- Logic số liệu Tổng quan nằm ở `ScannerCore` (hàm thuần, dữ liệu GIẢ) KÈM test XCTest ở `ios-app/ScannerCore/Tests/ScannerCoreTests/` (chạy bằng `swift test` ở GitHub Actions): đếm đúng theo sàn, theo chặng hàng hoàn về, quá hạn/sắp đến hạn, đơn "hoàn một phần", chọn 5 đơn cần xử lý (đỏ trước, rồi vàng, sắp xếp theo hạn), dãy 7 ngày, dữ liệu rỗng không chia cho 0.
- Báo cáo `tasks/reports/T031-report.md`: danh sách tệp mới/sửa, mô tả từng khối của Tổng quan, mục "CHƯA BIÊN DỊCH (không có Swift trên máy này)", mục "việc cần kiểm trên iPhone thật"; dòng cuối `TRẠNG THÁI: XONG` hoặc `CHƯA XONG (lý do)` (XONG = mã viết xong và tự rà soát, KHÔNG phải đã biên dịch). Cập nhật `TASKS.md`.
- KHÔNG chạy `git commit`/`git push` (Claude sẽ làm sau khi kiểm tra).
