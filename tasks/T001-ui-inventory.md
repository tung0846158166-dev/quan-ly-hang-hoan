# T001 - Kiểm kê giao diện app cũ (giao cho: antigravity, người kiểm tra: claude)

## Bối cảnh
Đọc trước: `AGENTS.md`, `PLAN.md`, `TASKS.md`. Chúng ta đang xây lại app "Quản lý hàng hoàn" thành phần mềm cài đặt. Giao diện cũ nằm hết trong một file:
`Bang_Excel_Quan_Ly_Hang_Hoan_Tu_Dong_Thu_Nho_Anh.html` (~22.900 dòng). Cần một bản kiểm kê đầy đủ để sau này dựng lại giao diện bằng React mà không sót tính năng.

## Việc cần làm (CHỈ ĐỌC code cũ, KHÔNG sửa bất kỳ file nào có sẵn)
Tạo file `docs/ui-inventory.md` gồm các mục:
1. **Danh sách màn hình/tab/bảng**: tên hiển thị, mục đích, nơi định nghĩa trong HTML (id hoặc số dòng).
2. **Với mỗi bảng**: danh sách cột (tên cột, trường dữ liệu tương ứng như orderId/track/status...), cột nào sửa được, sắp xếp, phân trang.
3. **Bộ lọc & tìm kiếm**: loại lọc (sàn Shopee/TikTok/cả 2, trạng thái, ngày, tìm hàng loạt...), hành vi.
4. **Nút/hành động** trên từng màn hình và việc nó làm (gọi hàm nào).
5. **Modal/hộp thoại/panel**: thông báo & lịch sử, danh mục sản phẩm, nhật ký bảo mật, dán nhanh nhiều đơn, thống kê, ca quét, camera, đổi mã phòng...
6. **Quy tắc hiển thị**: màu cảnh báo Vàng/Đỏ (ngưỡng giờ), trạng thái đơn và màu, khóa ô, đơn trùng.
7. **Phím tắt, thông báo, âm thanh** (nếu có).
8. **Các hàm dữ liệu giao diện gọi tới** (đọc/ghi/nhập Excel/xuất Excel/đồng bộ) - liệt kê tên hàm + việc nó làm, để phía lõi nghiệp vụ biết cần cung cấp API nào.
9. **Mục "Chưa chắc chắn"**: chỗ nào bạn không hiểu hoặc chỉ đoán.

## Quy tắc
- Chỉ ghi điều bạn thật sự thấy trong code. Điều gì suy đoán thì đánh dấu `(suy đoán)`.
- Ghi số dòng hoặc id để người kiểm tra đối chiếu được.
- KHÔNG đọc/sao chép dữ liệu đơn thật trong thư mục `Documents\QuanLyHangHoan` hay OneDrive. Không cần dùng tới.
- Không cài gói, không chạy app, không sửa file ngoài `docs/ui-inventory.md` và file báo cáo.

## Tiêu chí hoàn thành
- Có đủ 9 mục trên. Mỗi tab/bảng nhìn thấy trong app đều có mặt (đối chiếu với các nút: Bảng Hàng Hoàn Về, Kho lưu trữ (24h), Bảng đơn mới, Bảng đơn hủy, Phân tích, Thống kê, Tạo đơn nhanh, Ca quét).
- Số dòng/id dẫn chiếu đúng.

## Báo cáo (bắt buộc khi xong)
Tạo `tasks/reports/T001-report.md` gồm: đã làm gì, file đã tạo, những mục bỏ sót hoặc chưa chắc, thời gian ước tính, và dòng cuối `TRẠNG THÁI: XONG` hoặc `TRẠNG THÁI: CHƯA XONG (lý do)`.
Sau đó cập nhật `TASKS.md`: thêm dòng `- [x] T001 ui-inventory (antigravity) - chờ claude kiểm tra`.
