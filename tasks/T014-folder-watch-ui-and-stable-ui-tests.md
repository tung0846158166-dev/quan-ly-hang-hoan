# T014 - Ổn định test giao diện + màn hình "Theo dõi thư mục" (giao cho: antigravity, người kiểm tra: claude)

Phạm vi: CHỈ `v2/src/QuanLyHangHoan.App/` và `v2/tests/QuanLyHangHoan.Tests/UiTests/`. KHÔNG sửa `Core/`, `Data/`, `ImporterTests/` và các test ngoài `UiTests/` (claude đã thêm `v2/tests/QuanLyHangHoan.Tests/AssemblyInfo.Tests.cs` tắt chạy song song giữa các lớp test: đừng xóa). Thiếu API ở Data thì ghi vào báo cáo.
Đọc trước: `AGENTS.md`, `docs/auto-import-plan.md` (mục 1 Theo dõi thư mục), `tasks/reports/T013-review.md`, `v2/src/QuanLyHangHoan.Data/FolderWatch/FolderWatchService.cs`. Không dùng dữ liệu thật trong test; thư mục theo dõi trong test là thư mục TẠM.

## PHẦN A - Ổn định test giao diện (làm TRƯỚC, bắt buộc)
Hiện test WPF chập chờn: chạy `dotnet test` nhiều lần thấy 4/12 lần đỏ, khi các lớp test chạy song song tranh nhau tạo `System.Windows.Application` ("Cannot create more than one System.Windows.Application instance in the same AppDomain"), và sau khi tắt song song còn lộ `T010SyncScreenTests.BackgroundThreadEvents_MarshalToUiThread_AndRenderScreenshots` lỗi `Cannot find resource named 'SidebarThemeToggleStyle'` vì dựng `MainWindow` khi chưa nạp tài nguyên giao diện (chỉ chạy được nếu một test khác đã nạp sẵn: phụ thuộc thứ tự).
Yêu cầu: tạo MỘT fixture dùng chung cho mọi test WPF (ví dụ `WpfTestHost`): một luồng STA + Dispatcher + MỘT `Application` duy nhất, nạp đủ tài nguyên của app (`App.xaml` / `Themes/AppStyles.xaml` và mọi ResourceDictionary mà `MainWindow` cần) đúng một lần; mọi test dựng cửa sổ/`ViewModel` đều đi qua nó, không test nào tự `new Application()`. Test không được phụ thuộc thứ tự chạy hay trạng thái do test khác để lại (đặt lại theme, cài đặt, cửa sổ đã mở...). Sau khi xong, chạy `dotnet test` TOÀN BỘ ít nhất **30 lần liên tiếp**, 0 lần đỏ; dán kết quả (ví dụ "30/30 xanh") vào báo cáo.

## PHẦN B - Màn hình "Theo dõi thư mục"
### API có sẵn (claude đã viết, có test; `QuanLyHangHoan.Data.FolderWatch`)
- `FolderWatchService(dbPath, statePath, Func<DateTime> clock, TimeSpan? interval = null)`: `Config` (`Dir`, `Enabled`, `SinceTicks`, `Seen`, `LastOk`, `LastNote`, `PriceMode` ("tong_gia_tri" | "sau_chiet_khau" | "gia_salework" | "doanh_thu_uoc_tinh"), `ProductMode` ("supplement" | "replace"), `OnlyReturns`, `SkipUnshippedCancel`), `Configure(dir, enabled)` (bật mới hoặc đổi thư mục thì đặt mốc "từ bây giờ"), `SaveConfig()` (sau khi sửa `PriceMode`/`ProductMode`/... trên `Config`), `RunOnce(bool includeExisting = false)` -> `FolderWatchRunResult` (`Ran`, `Ok`, `Note`, `FilesImported`, `Names`, `Skipped`, `Failed`, `Import` (`Added`, `Updated`, `EnrichedArchived`, `SkippedDeleted`, `Ambiguous`...)), `Start(TimeSpan? firstDelay)` (kiểm tra định kỳ 15 giây), `Completed` (sự kiện bắn từ luồng NỀN sau mỗi lượt có chạy), `Dispose()`.
- Đường dẫn trạng thái: thêm `AppPaths.FolderWatchStatePath` = `%LocalAppData%\QuanLyHangHoan\v2\folder_watch.json`.
- Quy tắc đã đảm bảo ở Data (đừng làm lại ở UI): không bao giờ xóa/sửa/di chuyển file trong thư mục; chỉ nạp file TikTok mới đã tải xong; bỏ cột cá nhân; file lạ bị bỏ qua không báo lỗi.

### Việc cần làm
1. Interface mới ở `App/Services` (ví dụ `IFolderWatchController`) bọc `FolderWatchService` để test được, có bản giả cho `--demo` và test (bản giả KHÔNG đọc ổ đĩa thật).
2. **Khởi động (chế độ thật, sau `AppDatabase.Initialize`)**: tạo `FolderWatchService(AppPaths.DatabasePath, AppPaths.FolderWatchStatePath, () => DateTime.Now)`; nếu `Config.Enabled` thì `Start()`. `Dispose` khi thoát. Khi `Completed` bắn với `Result.Import` có `Added`/`Updated`/`EnrichedArchived` > 0: marshal về UI thread, tải lại các màn hình/badge/Trang chủ (như các phiếu trước) và hiện thông báo nhỏ "Đã tự nạp N file TikTok: tên file". Lỗi (`Ok == false`) hiện đỏ ở màn hình, không bật hộp thoại chặn. Không làm chậm khởi động.
3. **Sidebar**: thêm nhóm mới **"NẠP ĐƠN TỰ ĐỘNG"** (thu gọn được như các nhóm khác, nhớ trạng thái qua `AppSettings.CollapsedNavGroups`) chứa mục **"Theo dõi thư mục"** (sau này sẽ thêm Google Sheet và Salework vào nhóm này).
4. **Màn hình "Theo dõi thư mục"**:
   - Ô đường dẫn (chỉ đọc) + nút "Chọn thư mục..." (dùng `IFolderPickerService` đã có) + công tắc "Tự nạp file TikTok mới trong thư mục này" (gọi `Configure`).
   - Trạng thái lượt gần nhất (`LastNote`): xanh khi `LastOk`, đỏ khi không; "Chưa nạp file nào." khi trống.
   - Nút "Nạp ngay" (`RunOnce(false)`) và nút "Nạp các file đang có trong thư mục" (`RunOnce(true)`, có hộp xác nhận: "Sẽ nạp cả các file TikTok đã có sẵn trước khi bật theo dõi"). Chạy nền, không chặn UI, hiện "Đang nạp..." và khóa nút trong lúc chạy.
   - Tùy chọn (lưu qua `Config` + `SaveConfig`): cột giá để lấy (4 giá trị `PriceMode`, tên tiếng Việt: Tổng giá trị / Sau chiết khấu / Giá Salework / Doanh thu ước tính), cách cập nhật sản phẩm (`ProductMode`: "Nhập bổ sung - giữ sản phẩm không có trong file mới" mặc định / "Thay toàn bộ sản phẩm của từng kiện có trong file"), "Chỉ nạp đơn hoàn/hủy/thất bại" (`OnlyReturns`, mặc định bật), "Bỏ đơn hủy chưa gửi" (`SkipUnshippedCancel`).
   - Nhật ký các lượt chạy (mới nhất trên cùng, tối đa 100 dòng trong bộ nhớ): giờ, kết quả, tên file, "thêm X, cập nhật Y"; dòng đỏ cho lỗi; có dòng cảnh báo vàng nếu `Import.Ambiguous.Count > 0`: "N dòng khớp nhiều kiện nên chưa nạp, cần kiểm tra thủ công".
   - Ghi chú cố định: "App chỉ ĐỌC file, không xóa/sửa/di chuyển file nào. Cột thông tin khách hàng (tên, SĐT, địa chỉ) bị bỏ ngay khi đọc. Chỉ đọc .xlsx và .csv (file .xls cũ hãy lưu lại thành .xlsx)."
   - Trạng thái trống/chưa chọn thư mục đẹp, có hướng dẫn 1 dòng.
5. `--demo`: dùng bản giả, không tạo `FolderWatchService` thật, không đọc/ghi `folder_watch.json` thật.

## Tiêu chí hoàn thành
- `dotnet build` 0 lỗi 0 cảnh báo; `dotnet test` xanh TOÀN BỘ; Phần A: 30/30 lần chạy liên tiếp xanh (ghi rõ trong báo cáo).
- Test mới trong `UiTests/` (dùng fixture của Phần A, thư mục TẠM và `FolderWatchService` thật trỏ vào DB tạm tạo bằng `AppDatabase.Initialize`, hoặc bản giả khi chỉ kiểm giao diện): chọn thư mục + bật lưu đúng `Config`; "Nạp ngay" nạp file TikTok giả (CSV dựng bằng mã) và các màn hình tải lại; sự kiện `Completed` từ luồng nền được marshal về UI thread không ném lỗi; lỗi hiện đỏ; tùy chọn được lưu; nhóm "NẠP ĐƠN TỰ ĐỘNG" thu gọn/mở và nhớ trạng thái; `--demo` không chạm đĩa; không test nào ghi vào `%LocalAppData%` thật.
- Ảnh chụp: màn hình trống, đã bật với lượt nạp thành công, lượt lỗi, cảnh báo nhập nhằng, sidebar có nhóm mới -> `tasks/reports/T014-screens/`.
- Báo cáo `tasks/reports/T014-report.md`, dòng cuối `TRẠNG THÁI: XONG` hoặc `CHƯA XONG (lý do)`. Cập nhật `TASKS.md`.
