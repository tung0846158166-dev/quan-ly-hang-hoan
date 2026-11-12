# 1.8.11 — Mobile báo tạo mới thay vì đối soát

Hai nguyên nhân được tái hiện: currentMobileMode đọc localStorage mobile_scan_mode=new_order từ phiên cũ trong khi HTML khởi tạo tô nút Quét hoàn kho; hàng đợi cùng mã có thể dùng lại request/kind của chế độ khác. ACK duplicate của hộp đơn mới cũng bị cưỡng bức hiển thị new_order.

Sửa: phiên mở scanner luôn mặc định return, tạo mới chỉ khi chọn rõ trong phiên. Đổi chế độ reset bộ chống quét lặp để có thể đối soát cùng mã ngay. Hàng đợi phân biệt mục đích return/new_order, không dùng lại request/capture/history giữa chúng; ảnh gửi ngầm lấy snapshot đúng mục đích. ACK duplicate giữ là duplicate, không báo mới tạo.

Không xóa các yêu cầu tạo mới đã tồn tại hoặc tự đổi chúng sang nhận kho; đây là dữ liệu chờ riêng. Cache cũ không quyết định chế độ của lượt quét mới.

Kiểm tra: ba regression mới thất bại trước sửa và đạt sau; toàn bộ149 tests PASS với engine SheetJS tương thích0.18.5. Chromium hai trang PC/mobile thật với transport bridge cục bộ: stale saved mode new_order -> phiên return, quét RETURN001 lần đầu khớp, lần hai duplicate; newOrdersInbox vẫn0; mã chung ambiguous; IndexedDB reload giữ ACK membership; không pageerror. Windows/iPhone/4G thực tế NOT TESTED.

Phát hành: nhánh fix/return-parcels-sync; main tại thời điểm kiểm tra vẫn5fc5cd4 (PR#4). Các bản sửa1.8.8+ chưa có trên GitHub Pages cho đến khi merge nhánh này. Mở lại scanner GitHub sau khi Pages xanh; Brewpage không được workflow này cập nhật. PC1.8.10 tương thích thay đổi mobile này; version installer1.8.11 đóng kèm cùng nguồn để dễ nhận diện.
