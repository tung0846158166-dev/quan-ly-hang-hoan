# T012 - Hoàn thiện giao diện: sidebar thu gọn, giao diện tối, nhớ cửa sổ (giao cho: antigravity, người kiểm tra: claude)

Phạm vi: CHỈ `v2/src/QuanLyHangHoan.App/` và `v2/tests/QuanLyHangHoan.Tests/UiTests/`. KHÔNG sửa `Core/`, `Data/`, `ImporterTests/`, test gốc. Thiếu API ở Data thì ghi vào báo cáo.
Đọc trước: `AGENTS.md`, `tasks/reports/T006-review.md`, `tasks/reports/T010-review.md`, `v2/src/QuanLyHangHoan.App/Themes/AppStyles.xaml`. Không dùng dữ liệu thật trong test.

## API có sẵn (claude vừa thêm vào `AppSettings`, có test; đường dẫn tệp qua `AppPaths`)
- `AppSettings.Theme` ("light" | "dark"; giá trị lạ tự thành "light"), `CollapsedNavGroups` (List<string> tên nhóm sidebar đang thu gọn), `LastPage` (khóa màn hình mở lần trước, "" = Trang chủ), `Window` (`WindowPlacement? { Left, Top, Width, Height, Maximized }`, null nếu chưa có).
- `AppSettings.LoadOrCreate(path)` / `.Save(path)` như T010. Tệp cài đặt cũ chưa có các trường này vẫn nạp được. KHÔNG tự ghi đè trường đồng bộ (Room, SyncEnabled, NtfyEnabled, Brokers): luôn nạp bản mới nhất, sửa đúng trường của mình, rồi lưu (hoặc dùng chung một đối tượng đang giữ trong bộ nhớ với `RealSyncController`, đừng để hai bản cài đặt ghi đè nhau).

## Việc cần làm
1. **Sidebar thu gọn**: ba nhóm SHOPEE / TIKTOK / CHUNG bấm vào tiêu đề để thu gọn/mở; trạng thái lưu vào `CollapsedNavGroups` và áp dụng lúc mở app. Mục đang chọn luôn hiển thị: nếu nhóm chứa nó đang thu gọn thì hiện riêng mục đó hoặc tự mở nhóm. Thanh cuộn chỉ xuất hiện khi thật sự cần. Huy hiệu số đếm (badge) của nhóm thu gọn: cộng/hiển thị gọn trên tiêu đề nhóm (ví dụ tổng cảnh báo), không mất thông tin.
2. **Giao diện tối**: công tắc (nút hoặc mục trong khu vực cạnh sidebar/tiêu đề, chọn chỗ hợp lý) đổi sáng/tối ngay, không cần khởi động lại, lưu `Theme`. Chỉ đổi qua tài nguyên màu dùng chung (`Themes/AppStyles.xaml` / ResourceDictionary theme), KHÔNG rải màu cứng trong từng View. Tất cả màn hình hiện có phải đọc được ở chế độ tối (Trang chủ, 3 bảng sàn, Bảng đơn mới, Kho lưu trữ, Đã xóa, Thống kê, Nhập dữ liệu cũ, Quét nhận hàng, Đồng bộ điện thoại, khung xem ảnh). Giữ NGUYÊN ý nghĩa màu cảnh báo (Vàng 12h / Đỏ 24h / xanh đã nhận) và độ tương phản đọc được ở cả hai chế độ.
3. **Nhớ cửa sổ và màn hình cuối**: lưu `Window` khi đóng (hoặc khi thay đổi, có giãn nhịp) và `LastPage` khi đổi màn hình; áp dụng lúc mở. Trước khi áp dụng `Window` phải kiểm tra còn nằm (một phần đủ lớn) trong vùng làm việc của một màn hình hiện có, nếu không (đổi màn hình, giảm độ phân giải) thì dùng kích thước mặc định. `LastPage` không còn tồn tại thì về Trang chủ. Cửa sổ thu nhỏ (minimized) không được lưu làm trạng thái mặc định.
4. `--demo`: không đọc/ghi tệp cài đặt thật của người dùng (dùng cài đặt trong bộ nhớ hoặc thư mục tạm) để không làm bẩn cấu hình thật khi chụp ảnh.

## Tiêu chí hoàn thành
- `dotnet build` 0 lỗi 0 cảnh báo; `dotnet test` xanh TOÀN BỘ.
- Test trong `UiTests/` (cài đặt ở thư mục tạm): thu gọn/mở nhóm được lưu và nạp lại; mục đang chọn không bị ẩn; đổi theme lưu đúng, giá trị lạ về sáng; cửa sổ lưu ngoài màn hình bị bỏ qua và dùng mặc định, cửa sổ hợp lệ được áp dụng; `LastPage` lạ về Trang chủ; lưu giao diện KHÔNG làm đổi Room/SyncEnabled/NtfyEnabled/Brokers; `--demo` không chạm tệp cài đặt thật.
- Ảnh chụp: Trang chủ sáng và tối, sidebar có nhóm thu gọn, một bảng sàn ở chế độ tối, màn Quét ở chế độ tối -> `tasks/reports/T012-screens/`.
- Báo cáo `tasks/reports/T012-report.md`, dòng cuối `TRẠNG THÁI: XONG` hoặc `CHƯA XONG (lý do)`. Cập nhật `TASKS.md`.
