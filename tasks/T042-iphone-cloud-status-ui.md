# T042 - App iPhone: giao diện TRẠNG THÁI CLOUD, khóa ghi khi mất mạng, ghép thiết bị (giao cho: antigravity, người kiểm tra: claude)

PHẠM VI: CHỈ `ios-app/App/` (SwiftUI) và `ios-app/ScannerCore/Tests/` + `tasks/reports/T042-report.md`. KHÔNG sửa `ios-app/ScannerCore/Sources/` (trừ khi thật cần thêm tệp mới, ghi rõ trong báo cáo), `v2/`, `cloud/`. KHÔNG `git commit`/`git push`. Máy này KHÔNG có Swift: không thể biên dịch, hãy viết thật cẩn thận; Claude sẽ đẩy lên GitHub Actions để biên dịch và kiểm.

## Bối cảnh
Dự án chuyển nguồn dữ liệu gốc lên cloud: đọc `docs/cloud-design.md` và `cloud/API.md` (mục 8, 9, 10). Hợp đồng giao diện đã có sẵn trong `ios-app/ScannerCore/Sources/ScannerCore/CloudLink.swift` (`CloudState`, `CloudQuota`, `protocol CloudLink`, `FakeCloudLink`). Bạn chỉ làm GIAO DIỆN + khóa nút theo `CloudLink`; KHÔNG viết mạng thật (Claude làm sau). Điện thoại là ứng dụng ĐỘC LẬP ĐẦY ĐỦ (không chỉ để quét).

## Quyết định của chủ dự án (đã chốt)
- **Mất mạng = KHÔNG làm việc.** Trạng thái MẤT KẾT NỐI thì CHẶN quét và mọi thao tác ghi (sửa ghi chú, đổi trạng thái, theo dõi, lưu trữ...), có thông báo rõ + rung/âm báo lỗi khi cố quét; vẫn xem được dữ liệu đã tải (nhãn "Dữ liệu cũ lúc HH:mm, chỉ xem"). KHÔNG hàng đợi quét ngoại tuyến cho bản cloud (hàng đợi hiện có chỉ dùng khi chạy kiểu cũ MQTT).
- Ảnh: chỉ điện thoại chụp; ảnh của máy khác cũng xem được (làm ở phiếu sau, đừng làm ở đây).

## Việc cần làm
1. **Dải trạng thái** trên cùng mọi tab (nhỏ gọn, theo phong cách hiện có): `Đã kết nối` (xanh), `Đang đồng bộ` (xanh dương), `Đang kết nối...` (vàng), **`MẤT KẾT NỐI`** (đỏ, nổi bật, chữ lớn hơn), `Chưa ghép thiết bị` (xám), `Thiết bị đã bị thu hồi` (đỏ). Bấm vào mở chi tiết: lần đồng bộ cuối, lỗi gần nhất, nút "Đồng bộ ngay".
2. **Khóa ghi** tập trung: màn Quét hiện lớp phủ "Mất kết nối cloud — không quét được" và vô hiệu nút chụp/quét khi `!canWrite`; các nút ghi ở chi tiết đơn/Đơn mờ + giải thích. Xem/lọc/tìm/chi tiết/thống kê/AI vẫn dùng bình thường. Chỉ áp dụng khi cloud được bật (`CloudLink` có mặt); chạy kiểu cũ (không có `CloudLink`) thì mọi thứ như hiện nay.
3. **Banner "chỉ xem"** đầu tab Tổng quan/Đơn/Lịch sử khi khóa, tự biến mất khi kết nối lại.
4. **Cài đặt > Cloud**: địa chỉ Worker, mã ghép 6 số (nhập tay hoặc quét mã QR chứa `qlhh://pair?u=<url>&c=<code>` bằng camera, dùng bộ quét có sẵn), tên thiết bị, nút "Ghép thiết bị" (gọi `pair`, hiện lỗi tiếng Việt nếu có), thông tin thiết bị/vai trò khi đã ghép, "Quên ghép trên máy này" có xác nhận.
5. **Hạn mức hôm nay** (chỉ hiện khi vai trò `owner`): thanh % yêu cầu và % hàng ghi; vàng khi `level == "warn"`, đỏ khi `"stop"` kèm câu "Hết hạn mức hôm nay: thao tác ghi tạm dừng tới 07:00 sáng mai (giờ Việt Nam)".
6. Đưa `CloudLink` vào môi trường SwiftUI (EnvironmentObject/Observable bọc `CloudLink` qua `onChange`, đưa về luồng chính). Mặc định dùng `FakeCloudLink` cho Preview/Test; bản thật Claude gắn sau. Không làm vỡ XCTest và màn hình hiện có.

## Tiêu chí hoàn thành
- XCTest thuần (không UI) cho phần logic: ánh xạ `CloudState` -> chữ/màu/`canWrite`; quy tắc khóa (mất kết nối thì quét bị từ chối, xem thì không); định dạng hạn mức/câu cảnh báo; phân tích mã QR ghép `qlhh://pair?...` (đúng, thiếu tham số, sai scheme). Đặt logic thuần này trong tệp mới ở `ScannerCore/Sources` được phép (ghi rõ trong báo cáo) để test được.
- Báo cáo `tasks/reports/T042-report.md`: danh sách tệp, CHƯA BIÊN DỊCH, việc cần kiểm trên iPhone thật, dòng cuối `TRẠNG THÁI: XONG` hoặc `CHƯA XONG (lý do)`. Cập nhật `TASKS.md`.
