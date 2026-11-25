# T041 - App máy tính: giao diện TRẠNG THÁI CLOUD, khóa ghi khi mất mạng, hạn mức, ghép thiết bị (giao cho: antigravity, người kiểm tra: claude)

PHẠM VI: CHỈ `v2/src/QuanLyHangHoan.App/` (Views, ViewModels, Themes, Services CHỈ để thêm tệp mới/đăng ký) và `v2/tests/QuanLyHangHoan.Tests/UiTests/` + `tasks/reports/T041-report.md`. KHÔNG sửa `Core/`, `Data/`, `cloud/`, `ios-app/`. KHÔNG `git commit`/`git push`. Claude đang sửa Data/ và cloud/.

## Bối cảnh
Dự án chuyển nguồn dữ liệu gốc lên cloud (Cloudflare): đọc `docs/cloud-design.md` và `cloud/API.md` (mục 8, 9, 10). Hợp đồng giao diện đã có sẵn: `Services/ICloudLink.cs` (CloudState, CloudQuota, ICloudLink) và bản giả `Services/FakeCloudLink.cs`. Bạn chỉ làm GIAO DIỆN + khóa nút trên `ICloudLink`; KHÔNG viết mạng thật (Claude làm bản thật sau). Dùng `FakeCloudLink` để chạy và test.

## Quyết định của chủ dự án (đã chốt)
- **Mất mạng = KHÔNG làm việc.** Trạng thái MẤT KẾT NỐI thì khóa mọi thao tác ghi (quét nhận, sửa, đổi trạng thái, ghi chú, theo dõi, lưu trữ, xóa/khôi phục, nạp file/thư mục/Sheet/Salework); vẫn xem được dữ liệu đã tải kèm nhãn "Dữ liệu cũ lúc HH:mm, chỉ xem". KHÔNG làm hàng đợi ngoại tuyến.
- Máy tính chỉ XEM ảnh, không chụp/đưa ảnh lên.
- Phần mềm nội bộ cho nhân viên: vai trò `owner` thấy thêm quản lý ghép thiết bị.

## Việc cần làm
1. **Chấm trạng thái** ở thanh dưới/đầu cửa sổ chính: `Đã kết nối` (xanh), `Đang đồng bộ` (xanh dương), `Đang kết nối...` (vàng), `MẤT KẾT NỐI` (đỏ, nổi bật), `Chưa ghép thiết bị` (xám), `Thiết bị đã bị thu hồi` (đỏ). Bấm vào mở chi tiết (lần đồng bộ cuối, lỗi gần nhất, nút "Đồng bộ ngay"). Cập nhật theo `ICloudLink.Changed` (có thể gọi từ luồng nền: đưa về luồng giao diện).
2. **Khóa ghi**: lệnh/nút ghi chỉ chạy khi `ICloudLink.CanWrite`; khi khóa thì nút mờ + tooltip lý do ("Mất kết nối cloud — chỉ xem được"). Làm tập trung (một điểm điều kiện dùng chung, ví dụ thuộc tính `CanWrite` ở MainViewModel mà các VM bảng/quét/chi tiết đều dùng) chứ không rải từng nơi; liệt kê trong báo cáo mọi lệnh đã khóa. Bảng đơn vẫn cuộn/lọc/tìm/xem chi tiết/xuất file bình thường.
3. **Banner "chỉ xem"** ở đầu các màn có dữ liệu khi khóa: "Mất kết nối cloud. Đang xem dữ liệu cũ lúc HH:mm. Mọi thao tác ghi bị tạm khóa."; tự biến mất khi kết nối lại.
4. **Cài đặt > Cloud** (thẻ mới trong màn Cài đặt, theo kiểu các thẻ hiện có): địa chỉ Worker, mã ghép 6 số + tên thiết bị + nút "Ghép thiết bị" (gọi `PairAsync`, hiện lỗi tiếng Việt nếu có), thông tin thiết bị/vai trò khi đã ghép, nút "Quên ghép trên máy này" có xác nhận; nếu `Role == "owner"` có nút "Tạo mã ghép cho máy khác" (gọi `StartPairingAsync`, hiện mã + đếm ngược 10 phút).
5. **Hạn mức hôm nay**: thanh nhỏ trong thẻ Cloud và trên chấm trạng thái (tooltip): % yêu cầu và % hàng ghi trong ngày; màu vàng khi `Level == "warn"`, đỏ khi `"stop"` kèm câu "Hết hạn mức hôm nay: thao tác ghi tạm dừng tới 07:00 sáng mai (giờ Việt Nam)". Làm tròn dễ đọc (ví dụ "8.200 / 100.000 yêu cầu (8%)").
6. Đăng ký `ICloudLink` vào `MainViewModel`/App: dùng `FakeCloudLink` khi chạy demo (`isDemoMode`) và khi chưa có bản thật (Claude sẽ thay bằng bản thật). Không làm vỡ các màn/test hiện có: khi `ICloudLink` là null hoặc ở chế độ cũ (chưa bật cloud) thì mọi thứ hoạt động như hiện nay (`CanWrite` coi như true, không hiện chấm/banner).

## Tiêu chí hoàn thành
- Test UI (WpfTestHost) với `FakeCloudLink`: mỗi trạng thái hiện đúng chữ/màu; khi `Disconnected` các lệnh ghi bị khóa và lệnh xem vẫn chạy; kết nối lại thì mở khóa và banner biến mất; ghép sai mã hiện lỗi, ghép đúng chuyển `Connected`; hạn mức `warn`/`stop` hiện đúng màu và câu chữ; `owner` có nút tạo mã, `staff` không; chưa bật cloud (null) thì giao diện cũ không đổi.
- `dotnet build v2/QuanLyHangHoan.sln` 0 lỗi và toàn bộ test cũ vẫn đạt. Ảnh chụp màn hình các trạng thái ở `tasks/reports/T041-screens/`.
- Báo cáo `tasks/reports/T041-report.md`: danh sách tệp, các lệnh đã khóa, việc cần kiểm tay, dòng cuối `TRẠNG THÁI: XONG` hoặc `CHƯA XONG (lý do)`. Cập nhật `TASKS.md`.
