# Kiểm tra T041 (Claude, 26/11/2026)

**Kết luận: ĐẠT** (sau khi agy xong báo cáo thì chủ dự án dừng agy; Claude kiểm tra và làm tiếp T042–T044).

## Đã kiểm tra thật
- **Phạm vi:** chỉ `v2/src/QuanLyHangHoan.App/`, `v2/tests/.../UiTests/`, `tasks/`, `TASKS.md`; không sửa `Core/`, `Data/`, `cloud/`, `ios-app/` (đối chiếu `git status`).
- **Build:** `dotnet build v2/QuanLyHangHoan.sln` 0 lỗi (sau khi Claude sửa lỗi của chính mình: tệp `CloudLinkService.cs` và `CloudTests.cs` thiếu `using System.Net.Http/System.IO`, do khi viết chỉ biên dịch trong dự án cách ly; lỗi này KHÔNG thuộc phiếu T041).
- **Test không-UI:** 459/459 đạt (gồm 22 test cloud của Claude).
- **Test UI:** 285/286 đạt ở lần chạy đầy đủ; mục còn lại `T020...DemoMode_DoesNotTouchDisk...` chập chờn (đạt khi chạy riêng 3/3). Nguyên nhân: test không chờ việc nạp lại dữ liệu chạy ở luồng nền (từ lần tối ưu nạp dữ liệu trước). Claude đã sửa test cho chờ tối đa 10 giây thay vì đợi cố định.
- **Test T041:** 7/7 đạt (cùng nhóm T041 khác: 15/15).
- **Ảnh chụp:** đã xem `cloud-02-disconnected-banner-locked.png`: chấm "MẤT KẾT NỐI" đỏ, banner "chỉ xem" đúng giờ, nút "Tạo đơn mới" mờ, các nút xem vẫn dùng được; đúng yêu cầu phiếu.
- **Hành vi khi chưa bật cloud:** `ICloudLink == null` thì `WriteGate.AlwaysOpen`, không hiện chấm/banner, danh sách thẻ Cài đặt giữ nguyên 5 thẻ cũ (test T017/T035 vẫn đạt).

## Việc còn lại thuộc phần Claude (ghi theo báo cáo của agy)
- Thay `cloudLink: null` ở `App.xaml.cs` bằng `CloudLinkService` thật theo công tắc "bật cloud".
- Khi `Quota.Level == "stop"`: hiện `CanWrite` chỉ phụ thuộc trạng thái kết nối; quét vẫn được phép theo thiết kế cloud (máy chủ tự từ chối thao tác ghi thường).
- Cần kiểm tay với cloud thật: rút mạng > 20 giây -> khóa; có mạng lại -> mở khóa; ghép bằng mã 6 số; thu hồi từ máy chủ.
