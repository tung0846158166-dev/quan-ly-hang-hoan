# Bản sửa 1.8.8: kiện hoàn độc lập và đồng bộ điện thoại

Ngày 12/11/2026. Baseline main 5fc5cd4. Nhánh fix/return-parcels-sync. Không tự merge/publish.

## Quy tắc nghiệp vụ

Một mã đơn gốc có thể chứa A/B/C, được khách trả vào các ngày khác nhau bằng ba mã vận đơn. Mỗi kiện/lần hoàn có một dòng và trạng thái, ảnh, ngày nhận riêng; cùng orderId hoặc cùng mã yêu cầu hoàn không đủ để gộp hai mã kiện khác nhau. Nhận A không nhận B/C. Mã giao đi dùng chung hoặc mã đơn gốc có nhiều ứng viên phải yêu cầu kiểm tra. Không suy ra rằng mọi sản phẩm trong đơn gốc đều cần hoàn.

Các mã thay thế chỉ hợp nhất khi có liên kết tường minh, ví dụ Tracking Codes ghi mã cũ; không đoán rằng cùng claim ID nghĩa là mã mới thay mã cũ. JSON/backup và cột xuất Tracking Codes giữ các mã lịch sử. Nhập lại cùng kiện cập nhật, không cộng lặp sản phẩm. Tìm kiếm/định vị thông báo nhận được mã phụ.

## Đối chiếu audit

| ID | Thay đổi | Bằng chứng |
|---|---|---|
| S01–S02 | Group/index/lookup/enrich/import/bulk-push theo kiện; giữ aliases, không gộp sibling bằng orderId/shared forward | parcel-import, parcel-ui, excel-regression |
| S03 | Resolver chung tracking ưu tiên, đa ứng viên trả ambiguous, không tự nhận/gắn ảnh | pc-sync-safety; Chromium |
| S04 | Bỏ kết luận khớp/trùng/không thấy từ cache điện thoại; PC quyết định | mobile-sync-safety |
| S05 | Kết quả âm được đối chiếu lại; receipt thành công ràng buộc UID/trạng thái | pc-sync-safety, phone-regression |
| S06 | Lọc đúng phòng trước dispatch, không tự đổi phòng, bỏ kênh GLOBAL/fallback chung ở cả PC và điện thoại | pc-sync-safety, mobile-sync-safety |
| S07 | Catchup dùng cùng pipeline có Promise, chỉ mark processed sau commit; replay và attachment được xử lý | pc-sync-safety |
| S08 | Khôi phục queue/pending request từ dữ liệu bền, retry sau hydrate/reconnect | mobile-sync-safety; IndexedDB Chromium thật |
| S09 | Queue/ảnh vào IndexedDB, serialize snapshot; localStorage chỉ metadata; lỗi lưu hiển thị rõ | mobile-sync-safety |
| S10 | Không tìm thấy/đa ứng viên giữ ảnh cục bộ ở trạng thái cần kiểm tra; không retry vô hạn, không giả báo lưu | mobile-sync-safety |
| S11 | ACK error hiện nguyên nhân và giữ yêu cầu/ảnh; kiểm tra request/code/capture/room | mobile-sync-safety |
| S12 | Hoãn render/thống kê/định vị đến sau commit và phát ACK; bỏ tìm ảnh O(n²), không ghi lại ảnh IDB đã xác nhận không đổi; cache có bảo vệ cạnh tranh | pc-sync-safety; đo Chromium |

Ảnh tiếp tục gửi ngầm, không cản quét kiện tiếp. Quét lại một mã cần kiểm tra sau khi cập nhật danh sách tạo yêu cầu mới; ảnh cũ vẫn giữ. Đồng bộ kho nhận theo từng kiện, không nhận tất cả mã thuộc đơn gốc.

## Review tích hợp và sửa bổ sung

Review độc lập phát hiện ba lỗi Important; đã tái hiện trước sửa và có regression:

1. Đổi phòng trong lúc gửi ảnh có thể đưa phần còn lại của queue sang phòng mới. Các vòng async/hydration ràng buộc phòng nguồn, dừng khi phòng thay đổi.
2. Ảnh hộp đơn mới có thể rơi sang đơn hoàn trùng mã. Photo transport/chunks giữ kind; routing ràng buộc request/capture của inbox, không fallback vào bảng hoàn. Capture sai cũng không được rơi sang bảng hoàn.
3. Camera PC ưu tiên nhầm orderId trước tracking và nhận sai sibling. Dùng resolver chung; mã chung nhiều kiện không tự nhận. Kiện mới tạo bởi camera PC dùng trạng thái nhận chuẩn Unicode.

## Kết quả kiểm tra

- **PASS 97/97** `npm test`, gồm main storage, import, phone/mobile queue, offline PC pipeline, UI và restore.
- **PASS Chromium** với hai trang thật và cầu nối mạng cục bộ: nhập ba kiện cùng đơn; nhập tay gửi barcode nhận A riêng; mã giao đi chung báo ambiguous; IndexedDB thật khôi phục membership ACK ảnh qua reload. Không có page error.
- **PASS lượt browser hồi quy trước review**: nội dung thông báo/toast an toàn, định vị, khóa xóa ảnh, FileReader/Image/canvas theo UID và restore backup cũ. Lượt cuối cập nhật lại kiểm tra hai trang sau review.
- **PASS** cú pháp Main/Preload và 3 inline scripts, actionlint, git diff --check.
- Kiểm thử Excel dùng rows đã parse và parsed export/import roundtrip; **engine XLSX/file Excel thật: NOT TESTED**.
- Đo Chromium cục bộ dữ liệu giả, không ảnh, disk IPC mô phỏng, mạng ngoài bị chặn: 1.000 đơn tới phát ACK khoảng 31ms; 5.000 khoảng 56ms. Lượt audit trước tương ứng khoảng 282ms và 816ms. Không dùng các con số này làm cam kết tốc độ PC thật/4G; chúng chịu ảnh hưởng môi trường và không gồm RTT/ghi đĩa thật.
- Windows installer runtime, camera/iPhone/Safari, 4G–Wi-Fi/MQTT/ntfy thực tế, thiết bị mất điện/crash: **NOT TESTED**. Build GitHub chỉ được xác nhận nếu quan sát được run thành công.

## Cập nhật và giới hạn

1. Sao lưu dữ liệu cũ. Sau review/merge main, chờ Windows workflow và Pages xanh; cài PC 1.8.8 và mở lại mobile GitHub từ QR/phòng tương ứng. Cần cập nhật **cả hai phía**; không tự cập nhật Brewpage.
2. Dữ liệu lịch sử đã bị gộp/mất mã ở bản cũ không thể suy ra để tách chính xác. Cần nhập lại file nguồn đầy đủ và đối chiếu; không tự xóa/tách các dòng cũ bằng phỏng đoán.
3. Room isolation không thay cho xác thực. Broker/ntfy công cộng vẫn là hạ tầng hiện tại; ai biết phòng và giao thức vẫn có thể gửi tin. Không tuyên bố có cơ chế xác thực riêng hoặc hoạt động quét từ xa offline.
4. IndexedDB/quota/OS có thể lỗi. Ảnh chưa ACK không được coi đã lưu PC; nếu điện thoại báo không lưu được, giữ trang mở và xử lý dữ liệu, không xóa cache trình duyệt.
5. Main giữ snapshot JSON hiện có, chưa thay bằng DB hay journal. Cache receipts vẫn trong phiên, không bảo đảm exactly-once xuyên mọi crash; renderer debounce/thoát cưỡng bức không được chứng minh mất điện an toàn.
6. Lưu nguyên dòng theo kiện giữ tối đa UI hiện tại; chưa có màn hình tổng hợp nhiều kiện của một order gốc ngoài bảng hiện hữu. Đây không phải điều kiện để nhận từng kiện đúng.

Không sử dụng dữ liệu đơn thật trong tests/Artifacts. Không đổi kiến trúc hoặc thêm thư viện.
