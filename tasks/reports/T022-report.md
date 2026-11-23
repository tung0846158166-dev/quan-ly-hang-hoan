# T022 - Màn "Cài đặt" và sao lưu tự động

**Người làm: claude (không phải agy; agy bị dừng vì máy chủ mô hình quá tải).** Không có người thứ hai kiểm tra độc lập. Làm theo yêu cầu của chủ dự án: các công tắc chạy nền phải nằm trong mục Cài đặt, không để ở Trang chủ.

## Đã làm
- Mục sidebar **"Cài đặt"** (nhóm CHUNG, cuối danh sách), `SettingsViewModel` + `SettingsView`:
  - **Chạy nền và thông báo**: 4 công tắc (tự mở cùng Windows, thu nhỏ xuống khay, báo vàng/đỏ, âm thanh) CHUYỂN từ Trang chủ sang. Cùng một dịch vụ cũ (`MainViewModel` vẫn lưu `settings.json` và áp dụng khay/registry/âm thanh); Trang chủ không còn khối này (`HomeViewModel`/`HomeView` đã gỡ).
  - **Đồng bộ điện thoại**: hiện mã phòng (chỉ đọc, nút sao chép qua `IClipboardService`), nhắc giữ bí mật, nút mở màn Đồng bộ điện thoại.
  - **Sao lưu**: công tắc "Tự sao lưu", chọn mỗi N giờ (1-24) và giữ N ngày (3-90) (giá trị lạ vẫn hiện trong danh sách chọn), dòng "Bản gần nhất", nút "Sao lưu ngay" (chạy nền, khóa nút khi đang chạy, báo xanh/đỏ), nút "Mở thư mục sao lưu", danh sách bản sao lưu, hướng dẫn khôi phục bằng tay (KHÔNG có nút khôi phục tự động để tránh ghi đè dữ liệu).
- `IBackupController` (`SqliteBackupController` bọc `BackupService` + bộ hẹn giờ ~1 phút sau khi mở rồi mỗi 10 phút; `FakeBackupController` cho demo/test, không ghi đĩa, không mở Explorer). Lịch tự sao lưu KHÔNG bao giờ bật ở chế độ demo. `App.xaml.cs` nối bản thật.
- Cài đặt sao lưu lưu vào `settings.json` bằng cùng cơ chế ghi nguyên tử, giữ nguyên mã phòng và các trường khác.

## Kiểm chứng
- `dotnet build`: 0 lỗi, 0 cảnh báo.
- `dotnet test` toàn bộ: 487/487 xanh (một lần chạy đầy đủ sau thay đổi cuối; trước đó 10 lần liên tiếp 476/476 cho phần T030 chưa có màn này). 11 test mới trong `UiTests/T022SettingsAndBackupUiTests.cs` + cập nhật test T021 (công tắc nay qua `SettingsVm`) và hai test cấu trúc sidebar (thêm mục "settings").
- Test sao lưu thật trên DB tạm: sao lưu ngay tạo bản, danh sách, lịch chỉ chạy khi bật và khi đến hạn (đồng hồ giả), tắt thì im lặng, DB không có thì báo lỗi không tạo thư mục, mở thư mục gọi bộ mở đúng một lần.
- Ảnh chụp `tasks/reports/T022-screens/` (sáng/tối); claude đã xem.

## Chưa kiểm chứng
- Chưa chạy lịch tự sao lưu thật trên DB thật của chủ dự án (chỉ DB tạm).
- Chưa thử nút "Mở thư mục sao lưu" bằng Explorer thật.
- Nút trong màn Cài đặt dùng kiểu mặc định của Windows (chưa theo giao diện của app).

TRẠNG THÁI: XONG
