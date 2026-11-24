# Báo cáo T040 - App iPhone: Chỉ giữ ảnh khi quét được xác nhận đúng; không đúng thì xóa ảnh ngay

## 1. Phạm vi & tuân thủ quy tắc nghiệp vụ
- **Phạm vi thực hiện**: CHỈ `ios-app/` (`ScannerCore`, `ScannerCoreTests`, `App`, `README.md`), báo cáo này (`tasks/reports/T040-report.md`) và cập nhật `TASKS.md`.
- **KHÔNG sửa** bất kỳ tệp nào trong `v2/` (`QuanLyHangHoan.Data/`, `Core/`, `App/`, `Tests/`). KHÔNG `git commit`, KHÔNG `git push`.
- **Sửa đè T039 (mục 4 và mục 8)**:
  - **Quy tắc 1 (Đúng -> giữ & gắn vào kho kiện)**: Ảnh chụp lúc quét được giữ ở thư mục tạm (`Documents/photos/`) trong lúc chờ PC ACK (KHÔNG gắn sớm vào kho trước khi có PC ACK). Chỉ khi PC trả ACK quét thành công (`scanConfirmed == true`, `status == .success`, `!reviewRequired` — Quy tắc 6) VÀ khớp duy nhất 1 kiện (`OrderRow.id` + dự phòng mã vận đơn chuẩn hóa — Quy tắc 1 & 2, tuyệt đối không lẫn kiện A/B/C) thì mới chuyển vào kho `Documents/order-photos/` bằng ghi nguyên tử rồi xóa tệp tạm.
  - **Quy tắc 2 (Không tìm thấy / mơ hồ / lỗi / bị từ chối / quét trùng -> xóa ngay)**: Khi PC trả `not_found`, `ambiguous`, `duplicate`, `error`, `invalid`, `locked` hoặc snapshot khớp >= 2 ứng viên (Quy tắc 3), tệp ảnh tạm và mọi tham chiếu (`photoFile`, `attachedOrderId`, `attachedPhotoId`) bị xóa ngay lập tức. Khi quét trùng, ảnh đã gắn từ lần quét hợp lệ trước trong `Documents/order-photos/` vẫn còn nguyên vẹn. **Đã gỡ bỏ hoàn toàn giao diện gắn tay ở Lịch sử** (`ManualOrderPickerSheet`, mục "Gắn ảnh vào đúng kiện", nút "Bỏ qua gắn ảnh này", `attachScanPhotoManually`, `attachScanItemPhoto`, `dismissScanItemPhotoAttach`, `unattachedPhotoCount`).
  - **Quy tắc 3 (Chưa có kết quả -> giữ ảnh tạm, cảnh báo treo > 7 ngày)**: Khi đang mất mạng / chờ PC ACK, ảnh được giữ tạm ở `Documents/photos/` và sẽ được xử lý theo mục 1 hoặc 2 ngay khi nhận kết quả (và nếu PC đã ACK `success` nhưng snapshot cục bộ cũ hơn thời điểm quét chưa kịp chứa đơn mới thì giữ ảnh tạm + xin snapshot mới hơn rồi gắn ngay khi snapshot về). Với lượt quét treo quá 7 ngày chưa có kết quả và vẫn còn ảnh tạm: hiển thị cảnh báo ở **Lịch sử**, **Chi tiết lần quét** và **Cài đặt** để người dùng chủ động quyết định; khi người dùng chọn bỏ, **chỉ xóa tệp ảnh tạm và gỡ `photoFile`**, TUYỆT ĐỐI KHÔNG tự xóa lượt quét (Quy tắc 8).
  - **Quy tắc 4 & 5 (Hủy/xóa lượt quét vs ảnh đã gắn)**: Xóa hoặc hủy một lượt quét (hay "Xóa hết") trong Lịch sử sẽ xóa luôn tệp ảnh tạm tương ứng trong `Documents/photos/`, nhưng KHÔNG chạm ảnh đã gắn vào đơn đúng trong `Documents/order-photos/` (ảnh đã gắn chỉ xóa khi người dùng xác nhận trong Chi tiết đơn hoặc dọn đơn hoàn tất trong Cài đặt).
  - **Quy tắc 6 (Dọn tệp ảnh tạm mồ côi khi mở app)**: Khi mở app (`SyncEngine.start()`), `ScanPhotoLifecycle.cleanOrphanTempPhotos` quét `Documents/photos/` và chỉ xóa tệp `.jpg`/`.jpeg` cũ hơn 120 giây **không còn lượt quét nào tham chiếu VÀ không nằm trong chỉ mục kho**, chỉ chạy khi chắc chắn cả `scan_items.json` lẫn chỉ mục kho `index.json` đều nạp sạch (không hỏng), và ghi nhật ký qua `os.Logger`.

---

## 2. Danh sách tệp thêm / sửa / xóa

### Tệp thêm mới
- `ios-app/ScannerCore/Sources/ScannerCore/ScanPhotoLifecycle.swift` — Lõi thuần quản lý vòng đời ảnh tạm: `ScanPhotoDiscardReason`, `ScanPhotoDecision`, `ScanPhotoReconcileReport`, `ScanPhotoLifecycle` (`evaluateDecision`, `reconcile`, `stalePendingTempPhotoItems`, `tempPhotoSummary`, `discardTempPhotos`, `deleteScanItem`, `clearScanItems`, `cleanOrphanTempPhotos`).
- `ios-app/ScannerCore/Tests/ScannerCoreTests/ScanPhotoLifecycleTests.swift` — Bộ XCTest thuần phủ đầy đủ các kịch bản T040: đúng giữ (tách kiện A/B), không tìm thấy/mơ hồ/lỗi/bị từ chối (`invalid`/`locked`)/trùng xóa ngay (không mất ảnh cũ của kiện), chờ PC ACK giữ tạm rồi xử lý khi có kết quả, chờ snapshot mới hơn sau ACK success, treo > 7 ngày cảnh báo & bỏ ảnh tạm không xóa lượt quét, hủy/xóa lượt quét xóa ảnh tạm nhưng giữ ảnh kho, dọn ảnh tạm mồ côi an toàn (bỏ qua khi chỉ mục kho hoặc lịch sử lỗi, không dọn nhầm ảnh đang tham chiếu/ảnh kho), vượt trần 5 ảnh/kiện giữ nguyên 5 ảnh cũ.
- `tasks/reports/T040-report.md` — Báo cáo này.

### Tệp đã sửa
- `ios-app/ScannerCore/Sources/ScannerCore/ScanItem.swift` — Đổi `hasUnattachedPhoto` -> `hasTemporaryPhoto`; sửa chuỗi `detail` khi `not_found`/`ambiguous` không còn nói "ảnh vẫn giữ trên điện thoại"; giữ trường Codable cũ `photoAttachDismissed` để tương thích ngược `scan_items.json`.
- `ios-app/ScannerCore/Sources/ScannerCore/OrderPhotoStore.swift` — Cập nhật chú thích bỏ tham chiếu "chọn tay".
- `ios-app/ScannerCore/Tests/ScannerCoreTests/ScanItemV2AckTests.swift` — Cập nhật assertion `hasTemporaryPhoto`.
- `ios-app/App/Sources/OrderPhotoManager.swift` — Thay `attachScanPhoto`/`attachScanPhotoManually` bằng `reconcileScanPhotos(...)` và `runSafeTempOrphanCleanup(...)` gọi `ScanPhotoLifecycle`, ghi nhật ký qua `os.Logger`.
- `ios-app/App/Sources/SyncEngine.swift` — Không gọi `reconcileScanPhotos()` trước khi PC ACK; đối soát ngay khi nhận `pc_ack` (cả đúng lẫn sai/trùng/mơ hồ/lỗi/từ chối); thêm theo dõi `scanItemsLoadedCleanly` cho dọn mồ côi an toàn lúc mở app; thêm `tempPhotoSummary`, `stalePendingTempPhotoItems`, `isStalePendingTempPhoto`, `stalePendingTempPhotoSummary`, `discardTempPhoto`, `discardStalePendingTempPhotos`; gỡ `unattachedPhotoCount`, `attachScanItemPhoto`, `dismissScanItemPhotoAttach`; nối `delete`/`clear` qua `ScanPhotoLifecycle`.
- `ios-app/App/Sources/OrderPhotoViews.swift` — Xóa hoàn toàn `ManualOrderPickerSheet`.
- `ios-app/App/Sources/ScanDetailView.swift` — Bỏ toàn bộ giao diện gắn tay ảnh (danh sách ứng viên, nút Gắn, nút Tìm chọn đơn, nút Bỏ qua); thêm cảnh báo & nút xác nhận "Bỏ ảnh tạm (giữ nguyên lượt quét)" nếu lượt treo > 7 ngày; cập nhật nhãn trạng thái ảnh.
- `ios-app/App/Sources/HistoryView.swift` — Bỏ nhãn "Ảnh chờ gắn đơn"; thêm thẻ cảnh báo trên cùng khi có lượt quét treo > 7 ngày còn ảnh tạm kèm nút "Bỏ ảnh tạm quá 7 ngày" (hỏi xác nhận, giữ nguyên lượt quét); cập nhật phụ đề dòng lịch sử và hộp xác nhận "Xóa hết".
- `ios-app/App/Sources/SettingsView.swift` — Thay dòng "Ảnh chờ gắn đơn (Lịch sử)" bằng "Ảnh tạm chờ PC xác nhận" (nếu > 0) và nút cảnh báo "Bỏ N ảnh tạm treo quá 7 ngày" (có xác nhận); cập nhật mô tả quy tắc mới T040.
- `ios-app/App/Sources/OrderDetailView.swift` — Cập nhật chú thích mục Ảnh khi trống theo quy tắc T040.
- `ios-app/App/Sources/SnapshotStore.swift` — Bỏ wrapper `candidateRows(for:)` không còn dùng ở tầng App.
- `ios-app/README.md` — Cập nhật mô tả lưu ảnh theo T040.
- `TASKS.md` — Cập nhật mục viết lại app iPhone với T040.

### Tệp đã xóa
- Không xóa tệp nào.

### Ghi chú API phía Data (`v2/src/QuanLyHangHoan.Data/`)
- **Không thiếu API nào ở `v2/src/QuanLyHangHoan.Data/`**. Hợp đồng `pc_ack` hiện có của PC v2 (`success`, `duplicate`, `not_found`, `ambiguous`, `error`, `invalid`, `locked`) đã đủ để iPhone quyết định giữ/xóa ảnh tạm chính xác.

---

## 3. CHƯA BIÊN DỊCH
- **CHƯA BIÊN DỊCH**: Máy Windows này không có Swift/Xcode nên **chưa chạy được `swift test`** và **chưa biên dịch IPA**. Chỉ khi đẩy lên GitHub và workflow `Build iPhone App v2` (macOS) chạy xanh (`swift test` + build IPA) mới là bằng chứng biên dịch và kiểm thử đạt.
- **Kiểm tra tĩnh đã chạy trên toàn bộ 47 tệp Swift trong `ios-app/`**:
  - Cân bằng ngoặc `{}` / `()` / `[]` (có xử lý raw string, multiline string, string interpolation `\(...)`, comment lồng nhau): **47/47 tệp cân bằng tuyệt đối (0 lỗi)**.
  - Rà sót T039 gắn tay & gửi ảnh: **0** `ManualOrderPickerSheet`, **0** `hasUnattachedPhoto`, **0** `unattachedPhotoCount`, **0** `attachScanPhotoManually`, **0** `attachScanItemPhoto`, **0** `dismissScanItemPhotoAttach`, **0** `"Ảnh chờ gắn đơn"`, **0** `"Gắn ảnh vào đúng kiện"`, **0** `"Bỏ qua gắn ảnh này"`, **0** `photoChunks`/`transmitPhoto`/`"Shopee Photo Chunk"`.
  - Mã sản xuất (`App/Sources` + `ScannerCore/Sources`): **0** `try!`, **0** `fatalError`, **0** `as!`, **0** force unwrap `!` trong toàn bộ các tệp đã thêm/sửa, **0** API iOS 17+ chưa bọc `#available`.

---

## 4. Việc cần kiểm trên iPhone thật
1. **Quét đúng -> giữ ảnh vào đúng kiện**:
   - Quét 1 kiện hợp lệ đang nối PC v2 -> kiểm tra PC trả `success`, ảnh xuất hiện trong **Chi tiết đơn** của đúng kiện đó (kiện anh em B/C cùng mã đơn gốc không có ảnh này), và trong `Documents/photos/` không còn tệp tạm.
2. **Quét sai / mơ hồ / lỗi / bị từ chối / trùng -> xóa ảnh ngay**:
   - Quét mã không tồn tại (`not_found`), mã đơn có 2 kiện (`ambiguous`), mã của đơn đã nhận trước đó (`duplicate`), đơn bị từ chối/lỗi (`invalid`/`locked`/`error`): kiểm tra mở **Lịch sử -> Chi tiết lần quét** không còn ảnh, không còn giao diện gắn tay, dung lượng ảnh tạm về 0; riêng ca **quét trùng** kiểm tra ảnh cũ trong Chi tiết đơn từ lần quét đầu tiên **vẫn còn nguyên**.
3. **Mất mạng (chưa có PC ACK) -> giữ tạm, có mạng xử lý tiếp**:
   - Bật Chế độ máy bay -> quét 2 mã (1 đúng, 1 sai) -> kiểm tra cả 2 giữ ảnh tạm ở Lịch sử & Cài đặt hiển thị `"Ảnh tạm chờ PC xác nhận: 2 ảnh"`, và **chưa** xuất hiện trong Chi tiết đơn trước khi có PC ACK.
   - Tắt Chế độ máy bay -> sau khi PC trả ACK, ảnh của mã đúng chuyển vào Chi tiết đơn, ảnh của mã sai tự xóa ngay.
4. **Hủy/xóa lượt quét & cảnh báo treo > 7 ngày**:
   - Bật Chế độ máy bay -> quét 1 mã có ảnh tạm -> vuốt xóa lượt quét đó trong Lịch sử -> kiểm tra số ảnh tạm trong Cài đặt giảm ngay (tệp tạm bị xóa); thử xóa 1 dòng lịch sử đã quét thành công -> kiểm tra ảnh đã lưu trong Chi tiết đơn vẫn còn nguyên.
   - Kiểm tra hiển thị cảnh báo lượt quét treo quá 7 ngày (khi có lượt quá hạn chưa ACK) và xác nhận nút **"Bỏ ảnh tạm"** chỉ xóa ảnh tạm, giữ nguyên dòng lịch sử.
5. **Khởi động app & dọn ảnh tạm mồ côi**:
   - Đóng hẳn app rồi mở lại: kiểm tra ảnh đã gắn vào đơn vẫn nguyên vẹn, nhật ký dọn mồ côi hoạt động bình thường, không làm mất ảnh đang chờ PC ACK.

TRẠNG THÁI: XONG
