---
description: Tiếp tục dự án quản lý hàng hoàn: đọc trạng thái, kiểm tra việc của Antigravity, làm việc tiếp theo
---

Tiếp tục dự án mà KHÔNG hỏi lại những gì đã quyết. Làm theo thứ tự:

1. Đọc `AGENTS.md`, `TASKS.md`, `docs/legacy-data-schema.md` (mục "Quyết định của chủ dự án") và các phiếu `tasks/T*.md` đang mở.
2. Kiểm tra việc giao cho Antigravity: tiến trình `agy` còn chạy không, `tasks/reports/T*-agy-output.log` có lỗi (hết quota, bị từ chối quyền) không, đã có `T*-report.md` chưa.
   - Có báo cáo mới chưa kiểm: kiểm tra thật (đọc tệp, `dotnet build`, `dotnet test`, so thư mục ngoài phạm vi `App/` + `UiTests/` không bị sửa), ghi `tasks/reports/T*-review.md` (ĐẠT / CẦN SỬA), cập nhật `TASKS.md`.
   - Có phiếu sẵn sàng và agy rảnh: chạy bằng `agy -p "Đọc AGENTS.md rồi làm theo tasks/Txxx.md"` ở nền (`run_in_background`), ghi log vào `tasks/reports/Txxx-agy-output.log`. Không chạy hai phiếu cùng sửa `App/`.
3. Trong lúc agy chạy, tự làm việc tiếp theo chưa có người nhận ở `TASKS.md` thuộc phần của Claude (lõi `Core`, `Data`, importer), test trước (đỏ -> xanh), `dotnet test` xanh trước khi báo xong.
4. Dừng và hỏi CHỈ khi cần: dữ liệu mẫu từ user, quyết định nghiệp vụ chưa có, hoặc quyền bị hệ thống từ chối (không lách).
5. Cuối cùng báo ngắn: đã làm gì, trạng thái từng phiếu, việc cần user.

Giữ nguyên quy tắc bất biến trong AGENTS.md. Không dùng dữ liệu đơn thật trong test. Không tuyên bố PASS cho thứ chưa chạy thật.
