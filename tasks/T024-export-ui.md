# T024 - Nút "Xuất Excel / CSV" cho các bảng đơn (giao cho: antigravity, người kiểm tra: claude)

Phạm vi: CHỈ `v2/src/QuanLyHangHoan.App/` và `v2/tests/QuanLyHangHoan.Tests/UiTests/`. KHÔNG sửa `Core/`, `Data/` và test ngoài `UiTests/`. Thiếu API ở Data thì ghi vào báo cáo.
Đọc trước: `AGENTS.md`, `v2/src/QuanLyHangHoan.Data/OrderExporter.cs`, `App/Services/IFileSaveService.cs` (hộp thoại lưu có sẵn), `tasks/reports/T013-review.md`. Test dùng `WpfTestHost`, thư mục TẠM, bản giả `IFileSaveService`; không chạm `%LocalAppData%` hay Documents thật; `dotnet test` toàn bộ 10 lần liên tiếp 0 lần đỏ.

## API có sẵn (claude đã viết, có test)
`OrderExporter.ToXlsx(IEnumerable<StoredOrder>, sheetName)` và `ToCsv(IEnumerable<StoredOrder>)` -> `byte[]` (mỗi KIỆN một dòng, không cột thông tin khách; CSV UTF-8 có BOM và chặn công thức; XLSX ghi chuỗi thuần). `OrderExporter.Headers`, `LocationOf`. Chỉ trả byte: giao diện tự hỏi nơi lưu và ghi tệp. Nguồn `StoredOrder`: lấy từ `OrderRepository.Load(dbPath)` (qua provider/service hiện có, bọc interface có bản giả).

## Việc cần làm
1. Nút **"Xuất file..."** (menu nhỏ: "Excel (.xlsx)", "CSV (.csv)") trên thanh công cụ của: 3 bảng sàn (Shopee Hoàn hàng, Shopee Đơn hủy, TikTok Hoàn hàng), Kho lưu trữ, Đã xóa, Bảng đơn mới, màn Đang theo dõi (nếu T020 đã có).
2. Phạm vi xuất: mặc định **các dòng đang hiển thị theo bộ lọc/tìm kiếm hiện tại** (đúng thứ tự đang sắp xếp); nếu có dòng được chọn thì thêm lựa chọn "Chỉ N dòng đã chọn". Hộp xác nhận nhỏ nêu số dòng: "Xuất N dòng ra file Excel?". Không có dòng nào: báo "Không có dòng nào để xuất", không tạo tệp.
3. Hộp thoại lưu (qua `IFileSaveService`): tên gợi ý `QuanLyHangHoan-<tên bảng>-yyyyMMdd-HHmm.xlsx`; ghi bằng ghi tạm rồi đổi tên (không để lại tệp dở), KHÔNG ghi đè tệp có sẵn mà không hỏi (hộp thoại lưu của Windows đã hỏi; nếu bản giả trả đường dẫn đã tồn tại thì vẫn không âm thầm ghi đè trong test mà phải theo luồng xác nhận). Xuất chạy nền, nút khóa + "Đang xuất...", thành công: thông báo nhỏ kèm nút "Mở thư mục chứa tệp" (bọc interface); lỗi ghi (đĩa đầy/tệp đang mở bằng Excel): thông báo đỏ rõ ràng, không sập, không đổi dữ liệu đơn.
4. Ghi chú cố định trong menu: "File xuất không chứa tên/SĐT/địa chỉ khách."
5. `--demo`: dùng bản giả (không ghi đĩa thật).

## Tiêu chí hoàn thành
- `dotnet build` 0 lỗi 0 cảnh báo; `dotnet test` xanh 10/10.
- Test mới trong `UiTests/`: xuất xlsx và csv từ DB tạm cho đúng số dòng đang hiển thị (có lọc), đúng thứ tự; "chỉ dòng đã chọn"; không dòng -> không tạo tệp; hủy hộp thoại lưu -> không tạo tệp; lỗi ghi -> thông báo lỗi, không tệp dở; đọc lại tệp xlsx bằng `SpreadsheetReader` thấy đủ dữ liệu; không ghi vào DB (băm DB trước/sau); `--demo` không ghi đĩa.
- Ảnh chụp `tasks/reports/T024-screens/`: menu Xuất file, hộp xác nhận, thông báo thành công, thông báo lỗi.
- Báo cáo `tasks/reports/T024-report.md`, dòng cuối `TRẠNG THÁI: XONG` hoặc `CHƯA XONG (lý do)`; ghi rõ "chưa mở tệp xuất bằng Excel thật". Cập nhật `TASKS.md`.
