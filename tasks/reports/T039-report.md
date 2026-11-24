# Báo cáo T039 - App iPhone: Bỏ truyền ảnh sang PC, lưu ảnh cục bộ trong chi tiết đơn

## 1. Phạm vi & quy tắc nghiệp vụ
- **Phạm vi thực hiện**: CHỈ `ios-app/` (`ScannerCore`, `ScannerCoreTests`, `App`), báo cáo này (`tasks/reports/T039-report.md`) và cập nhật `TASKS.md`.
- **KHÔNG sửa** bất kỳ tệp nào trong `v2/` (Data/Core/App/Tests). KHÔNG `git commit`, KHÔNG `git push`.
- **Quy tắc nghiệp vụ bất biến (`AGENTS.md`)**:
  - **Quy tắc 1 & 2**: Kho ảnh cục bộ (`OrderPhotoDiskStore`) khóa chính theo mã dòng kiện `OrderRow.id` và dự phòng theo mã vận đơn chuẩn hóa `TrackingCode.clean(row.c)` (chỉ kích hoạt khi mã đó duy nhất và bản ghi không thuộc dòng sống khác); **tuyệt đối không khóa theo mã đơn gốc `OrderRow.o`**. Ảnh kiện A không bao giờ hiện ở kiện B/C.
  - **Quy tắc 3**: Mã mơ hồ (>= 2 ứng viên: cùng mã đơn gốc hoặc mã giao đi dùng chung) và mã không tìm thấy **không bao giờ tự đoán/tự gắn**; ảnh được giữ nguyên ở lượt quét trong Lịch sử để người dùng gắn tay vào đúng kiện từ danh sách ứng viên (hoặc tìm chọn thủ công) hay bấm bỏ qua.
  - **Quy tắc 6**: Áp dụng đầy đủ cho lệnh quét: chỉ đánh dấu `ScanItem.isSettled == true` khi PC trả ACK xác nhận đã lưu bền lệnh quét (`scanConfirmed`); bỏ hoàn toàn trạng thái chờ ACK ảnh.
  - **Quy tắc 8**: Không tự động xóa ảnh đơn hàng (đã bỏ `cleanupPhotos()` tự cắt 200 ảnh / 30 MB trước đây). Ảnh chưa gửi từ hàng đợi cũ được chuyển thẳng vào kho cục bộ (nếu khớp duy nhất 1 kiện) hoặc giữ nguyên ở lượt quét (nếu chưa khớp/mơ hồ), không xóa tệp ảnh nào. Nút dọn ảnh đơn đã hoàn tất cũ hơn N ngày chỉ chạy khi người dùng chủ động bấm và xác nhận sau khi xem trước số ảnh/MB.

---

## 2. Tóm tắt thay đổi đã thực hiện
1. **Bỏ hẳn luồng gửi ảnh sang PC (`SyncEngine.swift`, `Protocol.swift`, `ScanItem.swift`)**:
   - Xóa `transmitPhoto`, `SyncProtocol.photoChunks`, ngân sách `photoBudget` trong `tick()`, không còn gửi `Shopee Photo Chunk` qua MQTT hay ntfy, không reset `photoSentAt`/`photoAttempts` khi nối lại, không chờ ACK ảnh (`ackStage == "photo"` bị bỏ qua an toàn).
   - Giữ nguyên luồng gửi lệnh quét (`barcode` / `new_order_scan`) và chờ `pc_ack` xác nhận quét (Quy tắc 6).
2. **Hoàn tất lượt quét chỉ cần `scanConfirmed` (`ScanItem.swift`, `HistoryView.swift`, `ScanDetailView.swift`, `SettingsView.swift`)**:
   - `ScanItem.isSettled` rút gọn còn `scanConfirmed`.
   - Giữ nguyên toàn bộ trường Codable cũ (`photoConfirmed`, `photoSentAt`, `photoAttempts`, `photoFile`, `captureId`, …) để đọc `scan_items.json` cũ không lỗi; bổ sung các trường tùy chọn mới (`attachedOrderId`, `attachedPhotoId`, `photoAttachDismissed`).
   - Bỏ mọi chuỗi/nhãn "Ảnh gửi ngầm", "PC đã lưu / Đang chờ PC lưu", "Gửi ảnh khi quét hoàn kho".
3. **Kho ảnh cục bộ nguyên tử (`OrderPhotoStore.swift`, `OrderPhotoManager.swift`)**:
   - Lưu tại `Documents/order-photos/`: mỗi ảnh một tệp JPEG và tệp chỉ mục `index.json` (kèm bản dự phòng `index.json.bak`) ghi nguyên tử qua tệp tạm -> `synchronize()` (fsync) -> POSIX `rename`.
   - Chịu được tệp chỉ mục hỏng: tự cứu từ `index.json.bak`; nếu cả hai hỏng thì cách ly tệp hỏng sang `index.json.corrupt.<ts>`, không crash, giữ nguyên mọi tệp JPEG và khóa dọn tệp mồ côi.
   - Snapshot mới/đồng bộ lại không làm mất liên kết ảnh-đơn (khớp chính xác theo `OrderRow.id`, dự phòng an toàn theo mã vận đơn chuẩn hóa nếu PC đổi `id`, ví dụ chuyển `m|...` -> `a|...`).
   - Dọn tệp JPEG mồ côi trong `Documents/order-photos/` chỉ chạy khi `indexLoadedCleanly == true`, tệp cũ hơn 120 giây, và luôn ghi nhật ký qua `os.Logger`.
4. **Gắn ảnh tự động & thủ công (`Snapshot.swift`, `SyncEngine.swift`, `ScanDetailView.swift`, `OrderPhotoViews.swift`)**:
   - Bổ sung `SnapshotIndex.matchParcel(for:in:)` & `candidateRows(for:in:)` phân loại `.unique` / `.ambiguous` / `.notFound`.
   - Khi quét hoàn kho: lưu ảnh ngay vào lượt quét, sau đó `reconcileScanPhotos()` tự động chuyển vào `Documents/order-photos/` nếu khớp duy nhất 1 kiện (chạy lúc quét, khi khởi động để chuyển hàng đợi ảnh chưa gửi cũ, khi nhận `pc_ack`, và mỗi khi snapshot/delta cập nhật).
   - Khi mã mơ hồ hoặc không tìm thấy: giữ ảnh ở lượt quét, hiển thị danh sách ứng viên + màn tìm kiếm `ManualOrderPickerSheet` để người dùng gắn tay vào đúng kiện, hoặc bấm "Bỏ qua gắn ảnh này" (vẫn giữ tệp, không tự xóa).
5. **Mục "Ảnh" trong Chi tiết đơn & Trình xem toàn màn hình (`OrderDetailView.swift`, `OrderPhotoViews.swift`)**:
   - Thêm thẻ **Ảnh** hiển thị ảnh nhỏ của đúng kiện (`N/5`), chạm mở `ZoomablePhotoViewer` toàn màn hình dùng `UIScrollView` (phóng to 2 ngón 1x–5x, chạm đúp 2.8x tại điểm chạm / thu về 1x, kéo di chuyển, vuốt ngang đổi ảnh, vuốt xuống đóng).
   - Có nút xóa từng ảnh (cả trên ảnh nhỏ lẫn trong trình xem toàn màn hình) đều có hộp thoại xác nhận.
   - Cho **Chụp ảnh** (`UIImagePickerController(.camera)`) và **Chọn ảnh** (`PhotosPicker` iOS 16) thủ công; giới hạn tối đa **5 ảnh mỗi kiện**, hết chỗ báo rõ bằng thông báo, không tự xóa ảnh cũ.
6. **Quản lý dung lượng trong Cài đặt (`SettingsView.swift`)**:
   - Hiển thị `"Ảnh đã lưu: N ảnh, X MB"` (và số ảnh đang chờ gắn ở Lịch sử nếu có).
   - Có chọn số ngày (7 / 14 / 30 / 60 / 90 ngày) và nút **"Xóa ảnh đơn đã hoàn tất cũ hơn N ngày"**: tính xem trước số ảnh & MB sẽ xóa rồi hỏi xác nhận trước khi xóa; đơn đang chờ hoàn luôn được giữ nguyên.

---

## 3. Phép tính cấu hình nén ảnh cho lưu trữ (tự kiểm)
Thay cấu hình đường truyền cũ (`maxDim: 560, quality: 0.55`) bằng `PhotoCompressionConfig.storageDefault` (`PhotoCompressor.compressForStorage`):
- **Đầu vào camera**: phiên quay `AVCaptureSession` dùng `.hd1920x1080` (`1080 × 1920 px` dọc, ~2,07 MP); ảnh chụp tay/thư viện thường `3024 × 4032 px` (12 MP).
- **Bước 1 — Resize chuẩn**: cạnh dài tối đa `1280 px` (giữ tỷ lệ, không phóng to ảnh nhỏ):
  - Từ `1080 × 1920` -> `720 × 1280 px` (~0,92 MP, ~2,76 MB RGB thô).
  - Từ `3024 × 4032` (3:4) -> `960 × 1280 px` (~1,23 MP, ~3,69 MB RGB thô).
- **Bước 2 — Vòng nén JPEG mục tiêu <= 150 KB**:
  - Thử chất lượng khởi đầu `0,48`, giảm dần bước `0,06` về tối thiểu `0,28`: `[0.48, 0.42, 0.36, 0.30, 0.28]`.
  - Với ảnh phiếu gửi hàng Shopee/TikTok (nền giấy trắng, chữ + mã vạch đen, ít chi tiết màu phức tạp), tỷ lệ nén JPEG 4:2:0 ở `q = 0,48` thường đạt **25:1 đến 40:1**:
    - `720 × 1280` (~2,76 MB thô) / 30 ≈ **92 KB** (dải thực tế khoảng **65–120 KB**).
    - `960 × 1280` (~3,69 MB thô) / 35 ≈ **105 KB** (nếu ảnh có nền phức tạp > 150 KB, vòng lặp hạ `q = 0,42` hoặc `0,36` đưa về ~95–135 KB).
- **Bước 3 — Sàn kích thước tối thiểu 960 px**:
  - Chỉ khi đã hạ xuống `q = 0,28` mà vẫn > 150 KB mới giảm cạnh dài về **`960 px`** (`540 × 960` hoặc `720 × 960`), **TUYỆT ĐỐI không giảm cạnh dài dưới `960 px`**.
- **Độ đọc được chữ số khi zoom**:
  - Phiếu vận đơn 100×150 mm chiếm ~60% khung hình dọc ở 1280 px -> chiều cao phiếu ~768 px; chữ số mã vận đơn cao 3–4 mm tương ứng ~15–21 px chiều cao (và ở sàn 960 px vẫn đạt ~11–16 px), đủ để phân biệt rõ từng chữ số/ký tự khi zoom 2x–4x.
- **Ước tính dung lượng**: trung bình ~90 KB/ảnh -> 1.000 ảnh ≈ ~90 MB; 5.000 ảnh ≈ ~450 MB.
- **Cần kiểm trên iPhone thật**: đọc được chữ số mã vận đơn & mã đơn khi zoom trong điều kiện ánh sáng kho thực tế, và dung lượng tệp thực tế (KB/ảnh).

---

## 4. Danh sách tệp thêm / sửa / xóa

### Tệp thêm mới
- `ios-app/ScannerCore/Sources/ScannerCore/OrderPhotoStore.swift` — `PhotoCompressionConfig`, `OrderPhotoRecord`, `OrderPhotoDiskStore` (chỉ mục nguyên tử + `.bak` + chịu lỗi tệp hỏng, khóa theo kiện, giới hạn 5 ảnh, tính dung lượng, xem trước & xóa đơn hoàn tất, dọn mồ côi an toàn).
- `ios-app/ScannerCore/Tests/ScannerCoreTests/OrderPhotoStoreTests.swift` — bộ XCTest thuần cho kho ảnh cục bộ và cấu hình nén.
- `ios-app/App/Sources/OrderPhotoManager.swift` — `PhotoCompressor` (UIKit) & `OrderPhotoManager` (`ObservableObject`, cache ảnh, ghi nhật ký `os.Logger`).
- `ios-app/App/Sources/OrderPhotoViews.swift` — `ZoomablePhotoViewer` (`UIScrollView` zoom 2 ngón/chạm đúp/kéo/vuốt xuống đóng), `CameraPhotoPicker`, `ManualOrderPickerSheet`.
- `tasks/reports/T039-report.md` — báo cáo này.

### Tệp đã sửa
- `ios-app/ScannerCore/Sources/ScannerCore/ScanItem.swift`
- `ios-app/ScannerCore/Sources/ScannerCore/Protocol.swift`
- `ios-app/ScannerCore/Sources/ScannerCore/Snapshot.swift`
- `ios-app/ScannerCore/Tests/ScannerCoreTests/ScanItemV2AckTests.swift`
- `ios-app/ScannerCore/Tests/ScannerCoreTests/ProtocolTests.swift`
- `ios-app/App/Sources/CameraController.swift`
- `ios-app/App/Sources/SnapshotStore.swift`
- `ios-app/App/Sources/SyncEngine.swift`
- `ios-app/App/Sources/ScannerApp.swift`
- `ios-app/App/Sources/OrderDetailView.swift`
- `ios-app/App/Sources/ScanDetailView.swift`
- `ios-app/App/Sources/HistoryView.swift`
- `ios-app/App/Sources/SettingsView.swift`
- `ios-app/README.md`
- `TASKS.md`

### Tệp đã xóa
- Không xóa tệp nào.

### Ghi chú API phía Data (`v2/`)
- Không cần thêm API mới ở `v2/src/QuanLyHangHoan.Data/` cho T039; phía PC v2 giữ nguyên tương thích với điện thoại cũ còn gửi ảnh, bản iPhone mới chỉ gửi lệnh quét và nhận `pc_ack` như hiện có.

---

## 5. CHƯA BIÊN DỊCH
- **CHƯA BIÊN DỊCH**: Máy Windows này không có Swift/Xcode nên **chưa chạy được `swift test`** và **chưa biên dịch IPA**. Chỉ khi đẩy lên GitHub và workflow `Build iPhone App v2` (macOS) chạy xanh (`swift test` + build IPA) mới là bằng chứng biên dịch và kiểm thử đạt.
- **Kiểm tra tĩnh đã chạy trên toàn bộ 45 tệp Swift trong `ios-app/`**:
  - Cân bằng ngoặc `{}` / `()` / `[]` (có xử lý raw string, multiline string, string interpolation `\(...)`, comment lồng nhau): **45/45 tệp cân bằng tuyệt đối**.
  - Mã sản xuất (`App/Sources` + `ScannerCore/Sources`): **0** `try!`, **0** `fatalError`, **0** `as!`, **0** force unwrap `!`, **0** `photoChunks`, **0** `transmitPhoto`, **0** `"Shopee Photo Chunk"`, **0** `"Ảnh gửi ngầm"`, **0** `"Gửi ảnh khi quét"`, **0** API iOS 17+ chưa bọc `#available`.
  - Đã tự rà lỗi thực tế tiềm ẩn và sửa ngay trước khi chốt: `ZoomableImageScrollView` khóa theo `url: URL` (kèm `Coordinator`) thay vì tạo mới `UIImage(contentsOfFile:)` trong `body`, tránh bị reset zoom về 1x ngay khi đang chụm 2 ngón.

---

## 6. Việc cần kiểm trên iPhone thật
1. **Chất lượng ảnh khi zoom & dung lượng thật**:
   - Quét hoàn kho 5–10 đơn trong điều kiện ánh sáng kho thật (có và không bật đèn flash), mở **Chi tiết đơn** -> chạm ảnh -> chụm 2 ngón / chạm đúp phóng to: kiểm tra **đọc rõ chữ số mã vận đơn và mã đơn hàng** trên phiếu; kiểm tra dung lượng hiển thị (KB/ảnh) có nằm quanh mức ~60–120 KB hay không.
   - Chụp thêm ảnh bằng nút **"Chụp ảnh"** và chọn ảnh 12 MP từ nút **"Chọn ảnh"** (`PhotosPicker`): kiểm tra ảnh cũng được nén gọn về ~60–150 KB và vẫn đọc được chữ khi zoom.
2. **Thao tác xem ảnh & giới hạn 5 ảnh/kiện**:
   - Kiểm tra mượt mà: phóng to bằng 2 ngón, chạm đúp phóng to tại điểm chạm / chạm đúp lần nữa thu về 1x, kéo di chuyển khi đang phóng to, vuốt ngang chuyển giữa nhiều ảnh, vuốt xuống để đóng khi đang ở tỷ lệ 1x.
   - Thử thêm tới ảnh thứ 5 rồi bấm thêm ảnh thứ 6: kiểm tra hiện thông báo đủ tối đa 5 ảnh và **không xóa ảnh cũ**.
   - Xóa 1 ảnh (trên thẻ ảnh nhỏ và trong trình xem toàn màn hình): kiểm tra đều hỏi xác nhận trước khi xóa.
3. **Không gửi ảnh sang PC & hoàn tất ngay khi PC ACK quét**:
   - Quét hoàn kho có chụp ảnh khi nối PC v2: kiểm tra nhật ký PC/broker không nhận bất kỳ gói `photo_chunk` nào; trên iPhone ngay khi PC ACK lệnh quét thì badge chờ ở Lịch sử về 0 (không còn "Ảnh gửi ngầm").
4. **Tách kiện A/B/C, mã mơ hồ, mất mạng & nâng cấp từ bản cũ**:
   - Quét kiện A trong một đơn có 2 kiện A/B cùng mã đơn gốc: kiểm tra ảnh chỉ hiện ở kiện A, mở kiện B không thấy ảnh kiện A.
   - Quét mã đơn gốc trùng 2 kiện A/B: kiểm tra app **không** tự gắn ảnh; mở Lịch sử -> Chi tiết lần quét thấy danh sách 2 ứng viên và gắn tay được vào đúng kiện (hoặc bấm "Bỏ qua").
   - Bật Chế độ máy bay -> quét 1 đơn -> tắt Chế độ máy bay nhận snapshot -> kiểm tra ảnh tự chuyển vào đúng kiện và tồn tại ổn định qua các lần đồng bộ snapshot mới (kể cả khi đơn chuyển sang Kho lưu trữ).
   - Nâng cấp đè lên bản có ảnh chưa gửi trong `Documents/photos/`: kiểm tra ảnh cũ được chuyển vào chi tiết đơn (hoặc giữ ở Lịch sử nếu chưa khớp), không mất tệp ảnh nào và không còn gửi sang PC.
5. **Dung lượng & dọn ảnh đơn hoàn tất trong Cài đặt**:
   - Mở **Cài đặt -> Ảnh cục bộ trên iPhone**: kiểm tra số lượng `"Ảnh đã lưu: N ảnh, X MB"` khớp thực tế; thử bấm **"Xóa ảnh đơn đã hoàn tất cũ hơn N ngày"** kiểm tra hộp xác nhận hiện đúng số ảnh/MB trước khi xóa và không chạm ảnh của đơn đang chờ hoàn.

TRẠNG THÁI: XONG
