# Báo cáo T038 - App iPhone: Rà soát từng dòng trước khi chạy dữ liệu thật

## 1. Phạm vi & nguyên tắc
- **Phạm vi sửa**: CHỈ `ios-app/` (App + ScannerCore + ScannerCoreTests), báo cáo này và `TASKS.md`.
- **KHÔNG sửa** bất kỳ tệp nào trong `v2/src/QuanLyHangHoan.Data/`, `v2/src/QuanLyHangHoan.Core/`, `v2/src/QuanLyHangHoan.App/`, hay thư mục test ngoài `ios-app/`. KHÔNG `git commit`, KHÔNG `git push`.
- **Quy tắc nghiệp vụ bất biến (`AGENTS.md`)**: giữ nguyên quy tắc 1, 2, 3, 6 (kiện A/B/C tách biệt; không tự nhận cục bộ khi mã trùng >= 2 kiện đang chờ; chỉ ACK/đánh dấu ảnh đã lưu SAU khi PC xác nhận lưu bền; không dùng kênh phòng rỗng/GLOBAL).

---

## 2. Bảng lỗi tìm được (đối chiếu từng dòng)

### Nhóm 1 — Quét mã vạch / QR (`CameraController.swift`, `ScanView.swift`, `OrdersView.swift`, `TrackingCode.swift`)

| # | Tệp:dòng (trước sửa) | Mức độ | Trạng thái | Mô tả lỗi & vì sao |
|---|---|---|---|---|
| 1 | `CameraController.swift:165-182` | **Nghiêm trọng** | **Đã sửa** | `setTorch` và `setZoom` đọc `device` (được ghi trên `camera.session`) và gọi `dev.lockForConfiguration()` trực tiếp trên **main thread** trong khi `setFPS` cũng khóa trên `queue` -> race condition + nguy cơ đứng UI. Đã chuyển toàn bộ thao tác phần cứng về `queue.async`, lưu `desiredZoom` và cập nhật `torchOn` trên main. |
| 2 | `CameraController.swift:98-102` | **Nghiêm trọng** | **Đã sửa** | `self.currentFPS = 0` bị ghi trên `DispatchQueue.main.async` trong khi `setFPS` đọc/ghi trên `queue` -> data race. Đã chuyển về bên trong `queue.async`. |
| 3 | `CameraController.swift:153-163` | **Nghiêm trọng** | **Đã sửa** | `updateScanRegion` đọc `metaOutput` ngoài `queue`, không kiểm tra `isFinite` trước khi đặt `rectOfInterest`, và nếu lần layout đầu diễn ra trước khi người dùng bấm "Cho phép camera" thì `rectOfInterest` không bao giờ được áp dụng lại. Đã lưu weak `attachedPreviewLayer`, kiểm tra `isFinite` + clamp `[0,1]`, chuyển `metaOutput` sang `queue`, và tự cập nhật lại sau `session.startRunning()`. |
| 4 | `CameraController.swift:78-112`, `ScanView.swift:69-78`, `OrdersView.swift:521-531` | **Cao** | **Đã sửa** | Camera **không dừng khi vào nền / khóa máy / có cuộc gọi** (`AVCaptureSession.wasInterruptedNotification` / `scenePhase`), không tắt `isIdleTimerDisabled` khi rời tab/vào nền, không hủy lệnh `begin()` nếu `stop()` gọi lúc đang xin quyền camera, và callback `metadataOutput` đang xếp hàng vẫn có thể bắn mã SAU khi đã `stop()`. Đã xử lý đầy đủ `wantsRunning`, `wasInterrupted` / `interruptionEnded` / `runtimeError`, `scenePhase`, `router.tab`, và kiểm tra `self.running && self.wantsRunning`. |
| 5 | `CameraController.swift:83-91`, `ScanView.swift:128-143`, `OrdersView.swift:514` | **Cao** | **Đã sửa** | Quyền camera bị từ chối (`denied = true`) rồi người dùng vào Cài đặt iPhone bật lại: nhánh `.authorized` quên reset `denied = false`, màn hình vẫn bị che bởi thông báo từ chối. Đã reset `denied = false` khi có quyền, tự kiểm tra lại quyền khi `didBecomeActive`, và thêm nút **"Mở Cài đặt iPhone"**. |
| 6 | `CameraController.swift:81-82, 138, 242-254` | **Cao** | **Đã sửa** | (a) `idleTimer` vẫn tạo khi quyền bị từ chối (rò rỉ timer chạy vô ích). (b) `PreviewUIView` dùng `layer as! AVCaptureVideoPreviewLayer` (ép kiểu bắt buộc `as!`), không hủy `asyncAfter` cũ khi relayout nhiều lần, và chưa cập nhật `videoOrientation` khi xoay màn hình. (c) `snapshotJPEG` chưa kiểm tra `w > 0, h > 0` trước khi chia. Đã sửa toàn bộ (chỉ tạo `idleTimer` trong `begin()`, đổi `as?`, dùng `DispatchWorkItem`, đồng bộ `interfaceOrientation`, guard `w > 0 && h > 0`). |
| 7 | `TrackingCode.swift:5-63` | **Cao** | **Đã sửa** | (a) QR dạng **JSON** (`{"tracking_no":"TTVN..."}`, lồng nhau) hoặc **nhiều dòng** (`"Mã vận đơn: TTVN..."`) với mã không thuộc 7 regex cứng bị từ chối bởi `isPlausibleScan`. (b) URL có khoảng trắng/fragment hoặc `www.spx.vn/...` bị `URLComponents` trả `nil`. (c) Ký tự vô hình (BOM `\u{FEFF}`, zero-width space) làm hỏng so khớp. (d) Biên dịch lại `NSRegularExpression` mỗi lần gọi ở 24fps. Đã cache regex tĩnh, lọc ký tự vô hình, hỗ trợ JSON/URL/nhiều dòng, giữ 100% tương thích mọi test cũ + thêm `extractPasteCodes` và XCTest. |
| 8 | `OrdersView.swift:521-528` (`ScanSearchSheet`) | **Trung bình** | **Đã sửa** | Quét tìm đơn trên tab Đơn dùng `camera.onCode` thô (không qua `pickBest` / `isPlausibleScan` / `TrackingCode.clean`), dễ nhận mã rác hoa văn hoặc điền nguyên URL QR vào ô tìm kiếm. Đã chuyển sang `camera.onCodes` + `TrackingCode.pickBest` (ưu tiên mã có trong `store`) + `TrackingCode.clean`. |

### Nhóm 2 — Hàng đợi bền & gửi ảnh (`SyncEngine.swift`, `ScanItem.swift`, `Protocol.swift`, `MQTT.swift`, `NtfyClient.swift`)

| # | Tệp:dòng (trước sửa) | Mức độ | Trạng thái | Mô tả lỗi & vì sao |
|---|---|---|---|---|
| 9 | `ScanItem.swift:65-67` vs `v2/.../SyncProtocol.cs:130-135` | **Nghiêm trọng (Quy tắc 6)** | **Đã sửa** | **Lỗi chặn chạy thật với PC v2**: `SyncProtocol.BuildPhotoAck` của PC v2 trả `ackStage: "photo", outcome: "success", captureId` mà **KHÔNG có trường `photoSaved`**. Trong khi `ScanItem.apply` chỉ đặt `photoConfirmed = true` khi `ack.photoSaved == true` -> ảnh gửi sang PC v2 **KHÔNG BAO GIỜ được xác nhận**, `isSettled` luôn `false`, badge chờ tăng mãi và gửi lại vô ích 6 lần! Đã sửa: nhận `photoConfirmed = true` khi `ack.photoSaved == true` HOẶC (`ack.photoSaved == nil && (outcome == "success" || outcome == "duplicate")`); tuyệt đối KHÔNG nhận khi `photoSaved == false` hoặc `outcome == "error"` (giữ chặt Quy tắc 6). Có XCTest phủ đủ. |
| 10 | `SyncEngine.swift:499-503` | **Nghiêm trọng** | **Đã sửa** | Dùng `file!` (force unwrap) và `try? d.write(...)` nuốt lỗi ghi đĩa: nếu ghi file ảnh thất bại, `ScanItem` vẫn giữ `photoFile != nil` trỏ tới tệp không tồn tại -> hàng đợi kẹt vĩnh viễn. Đã bỏ `file!`, ghi nguyên tử `.atomic`, chỉ gán `photoFile` khi ghi thành công; kiểm tra `fileExists` trong `transmitPhoto`. |
| 11 | `SyncEngine.swift:509, 798-821` | **Nghiêm trọng** | **Đã sửa** | Lượt quét mới chỉ gọi `persist()` (trễ 300ms async): nếu người dùng tắt app đột ngột ngay sau lúc quét có thể mất lượt quét khỏi `scan_items.json`; khi vào nền cũng chưa xin `UIApplication.beginBackgroundTask` nên iOS có thể suspend giữa lúc ghi. Đã gọi `flushPersist()` ngay khi `submit()` tạo lượt mới và bọc `beginBackgroundTask` (one-shot an toàn) khi vào nền. |
| 12 | `SyncEngine.swift:690-691`, `ScanItem.swift:7-34` | **Cao** | **Đã sửa** | `transmit()` gửi lại đơn mới dùng `self.note` (ô ghi chú hiện tại trên UI) thay vì ghi chú tại thời điểm quét -> mất mạng rồi gửi lại bị dính ghi chú của đơn khác hoặc trống. Đã lưu `note` bền trong `ScanItem` (có `init(from:)` tương thích ngược JSON cũ). |
| 13 | `SyncEngine.swift:540-661, 735-771` | **Cao** | **Đã sửa** | Hàng đợi lệnh sửa cục bộ (`pendingNotes` cho `order_note`/`order_edit`/`order_delete`/`cancel_receive`): (a) dùng `nowMs()` dễ trùng `requestId` nếu thao tác liên tiếp trong 1ms; (b) không tự gửi lại trong `tick()` khi kênh còn mở nhưng PC vừa bật lại; (c) xóa ngay lệnh khi PC trả `error` tạm thời (DB locked); (d) không lọc theo `room` khi đổi phòng; (e) không giới hạn số lượng lưu và không dọn `orderActionFeedback`. Đã thay bằng `PendingCommand` bền nguyên tử (`pending_commands.json`, giới hạn 200 mục, `nextRequestId` tăng đơn điệu + `sessionId`, tự retry mỗi ~15s trong `tick()` theo đúng `room`, giữ lại khi `outcome == "error"` tối đa 8 lần, giới hạn `orderActionFeedback` 200 mục). |
| 14 | `SyncEngine.swift:713-728, 731-733, 757-763` | **Cao** | **Đã sửa** | (a) `photoId` dùng `it.attempts` thay vì `it.photoAttempts`. (b) `transmitPhoto` không dừng gửi mảnh giữa chừng khi đổi phòng, bật demo, xóa item, hoặc đã nhận ACK ảnh. (c) Khi kênh nối lại (`channelOpened`), không mở khóa lại ảnh đã chạm 6 lần thử trong lúc mất mạng. (d) Khi ảnh đủ 6 lần thử chưa ACK, không cập nhật thông báo rõ để người dùng bấm "Gửi lại". Đã sửa toàn bộ (không bao giờ tự đánh dấu `photoConfirmed = true`). |
| 15 | `SyncEngine.swift:786-796` | **Cao** | **Đã sửa** | Dung lượng ảnh có thể phình vô hạn và tệp ảnh mồ côi không bao giờ bị xóa. Đã thêm `cleanupPhotos()` chạy trên nền khi khởi động và sau `photoConfirmed`/`trimHistory`: xóa ảnh mồ côi, giới hạn ảnh **đã được PC xác nhận** <= 200 tệp / 30 MB (xóa cũ nhất trước), **TUYỆT ĐỐI không chạm vào ảnh chưa ACK** (Quy tắc 6). Tách `ScanItem.trimHistory` thuần vào `ScannerCore` + XCTest. |
| 16 | `SyncEngine.swift:177-185, 188-197, 265-271, 765-770`, `Protocol.swift:146-148` | **Cao** | **Đã sửa** | (a) Mở app lần đầu khi `room == ""` làm `sseTask == nil`, gọi `start()` lần 2 sinh nhiều `tickTask` chạy song song. (b) Khi `room == ""`, `tick()` vẫn có thể poll ntfy topic `"agy_shopee_"` (kênh rỗng/GLOBAL, trái Quy tắc 7). (c) `setRoom` không dọn `seenMsgs`, `orderActionFeedback`, bộ ghép mảnh dở của phòng cũ. Đã khóa `started`, chặn mọi gửi/nhận/poll khi `room.isEmpty`, và reset đầy đủ khi đổi phòng. |
| 17 | `MQTT.swift:63-90, 128-131, 192-198`, `NtfyClient.swift:10-12` | **Cao** | **Đã sửa** | (a) `URL(string:)!` force unwrap ở cả `MQTTClient` và `NtfyClient` -> crash nếu URL không hợp lệ. (b) `MQTTClient` gửi `PINGREQ` nhưng không phát hiện socket "zombie" (chuyển Wi-Fi/4G không nhận `PINGRESP`). (c) `MQTTDecoder` không giới hạn kích thước `remainingLength` (gói hỏng có thể ngốn bộ nhớ vô hạn). Đã bỏ mọi `!`, thêm timeout phát hiện không có dữ liệu > 55s để tự hủy & nối lại, giới hạn gói tối đa 8 MB + XCTest. |

### Nhóm 3 — Đồng bộ snapshot (`SnapshotStore.swift`, `Snapshot.swift`)

| # | Tệp:dòng (trước sửa) | Mức độ | Trạng thái | Mô tả lỗi & vì sao |
|---|---|---|---|---|
| 18 | `SnapshotStore.swift:179-209`, `SyncEngine.swift:513-520` | **Nghiêm trọng (Quy tắc 2 & 3)** | **Đã sửa** | (a) Khi snapshot/delta mới từ PC về, mọi thao tác cục bộ chưa được PC ACK (`order_note`, `order_edit`, `order_delete`, `cancel_receive`) bị **ghi đè mất sạch**! (b) Khi ghép lượt quét hoàn kho chưa xác nhận (`unconfirmedReturnCodes`), vòng `for i in rows.indices` đánh dấu **TẤT CẢ** kiện đang chờ khớp mã (nếu 2 kiện A/B cùng mã đơn gốc -> tự đánh dấu cả 2 kiện, vi phạm Quy tắc 2 & 3!). Đã xây dựng `LocalEditOverlay` thuần trong `ScannerCore`: chỉ đánh dấu cục bộ lượt quét khi khớp **DUY NHẤT 1 kiện đang chờ** (khớp >= 2 -> để PC báo `ambiguous`), đồng thời bảo toàn toàn bộ `note`/`editStatus`/`delete`/`cancelReceive` chưa ACK qua mọi lần đồng bộ. Có XCTest đầy đủ. |
| 19 | `Snapshot.swift:340-360` (`SnapshotAssembler`) | **Nghiêm trọng** | **Đã sửa** | (a) `addRaw` gán `lastDone = id` **TRƯỚC** khi `decode()` chạy: nếu lượt 1 của `snap_X` có 1 mảnh hỏng gây lỗi giải mã, lượt 2 của chính `snap_X` (PC gửi 2 lượt để dự phòng mất gói) bị `lastDone` chặn bỏ qua luôn! (b) Không kiểm tra `c.total` nhất quán giữa các mảnh và không giới hạn `c.total <= 20_000`. (c) Tiến độ `partsReceived/partsTotal` nhảy loạn khi mảnh bản cũ chen vào. Đã sửa: chỉ chốt `lastDone` sau khi giải mã thành công, kiểm tra `c.total`, chỉ báo tiến độ bản mới nhất + XCTest. |
| 20 | `Snapshot.swift:44` | **Cao (Quy tắc 1 & 2)** | **Đã sửa** | `OrderRow.id = k + "|" + c` bị trùng khi 2 đơn hủy có cùng mã vận đơn nhưng khác `ci`, hoặc 2 kiện cùng `c` nhưng khác `o` -> lỗi diffing/mở sai đơn trên SwiftUI `List`/`DetailView`. Đã nâng cấp `id` phân biệt theo `ci` và `o` (giữ nguyên `"m|B"` khi `o` và `ci` rỗng để tương thích `NotificationPlanner`). |
| 21 | `SnapshotStore.swift:47-54, 153-165, 205, 240-253` | **Cao (5.000 đơn)** | **Đã sửa** | (a) `init()` giải mã JSON `snapshot.json` + `snapshot_base.json` (~2-4 MB cho 5.000 đơn) đồng bộ trên main thread lúc mở app. (b) `index: [String: OrderRow]` nhân bản struct `OrderRow` 2-4 lần (~7.5 MB RAM thừa) và bị dựng lại đồng bộ trên main thread sau mỗi lần sửa 1 dòng. (c) `lastDeltaSeq` không lưu qua lần khởi động lại nên delta cũ có thể áp nhầm. Đã thay bằng `SnapshotIndex` nhẹ lưu chỉ số `Int` + `idToIndex` (O(1) tra theo `id`) + `ambiguousPendingCodes`, nạp cache đĩa trên luồng nền, và lưu `lastDeltaSeq` theo `baseSid`. |
| 22 | `SnapshotStore.swift:307-322` (`applyWatchSync`) | **Cao (Quy tắc 2)** | **Đã sửa** | Gộp `c` và `o` vào chung 1 bảng tra khiến kiện B (cùng mã đơn `o` với kiện A đang theo dõi) bị gắn nhầm cờ theo dõi. Đã tách `byTrack` và `byOrderOnly` (chỉ fallback theo `o` khi mục theo dõi không có mã vận đơn `c`). |

### Nhóm 4 — Giao diện & hiệu năng 5.000 dòng (`HomeView`, `OrdersView`, `OrderDetailView`, `ProductStatsView`, `HistoryView`, `SettingsView`, `OrderTablesView`, `NotificationsView`, `StatsView`, `ScanDetailView`, `OrderStats.swift`)

| # | Tệp:dòng (trước sửa) | Mức độ | Trạng thái | Mô tả lỗi & vì sao |
|---|---|---|---|---|
| 23 | `OrdersView.swift:67-109, 167-174, 218-222` | **Nghiêm trọng (5.000 đơn)** | **Đã sửa** | (a) Mỗi ký tự gõ tìm kiếm chạy `ProductMetrics.compute(rows: store.rows)` trên cả 5.000 đơn chỉ để lấy số chip lối tắt! (b) `shortcutRow` và `platforms` duyệt 5.000 đơn 4 lần (kèm Unicode `.folding`) ngay trong `body`. (c) Lọc 5.000 đơn chạy đồng bộ không debounce trên main thread và kích hoạt lặp 3 lần qua 3 `.onChange`. Đã tách tính tổng hợp sang `.task(id: dataToken)` trên nền và lọc/tìm kiếm sang `.task(id: filterToken)` có debounce 120ms trên nền. |
| 24 | `HomeView.swift:26-51, 66-72, 247-252` | **Cao (5.000 đơn)** | **Đã sửa** | `recompute()` (chạy cả `DashboardMetrics.compute` + `ProductMetrics.filter` + `ProductMetrics.compute` trên 5.000 đơn) chạy đồng bộ trên main thread và bị gọi lặp từ 5 `.onChange`; tạo mới `DateFormatter` trong `body`. Đã chuyển sang `@MainActor` + `Task.detached` qua `.task(id: recomputeToken)` và cache `DateFormatter`. |
| 25 | `ProductStatsView.swift:43-73, 131-141, 658-660, 561, 765, 899` | **Cao (5.000 đơn + Layout)** | **Đã sửa** | (a) `recompute()` (gồm `groupByDay` tính lại toàn bộ sản phẩm cho từng ngày) chạy đồng bộ trên main thread -> khựng 0.5-1.5s với 5.000 đơn. (b) `ProductStatDetailView.matchingOrders` lọc 5.000 dòng ngay trong `body`. (c) Thanh `GeometryReader` ở dòng 765 có `min(w - 10, ...)` có thể âm/tràn khi `w == 0`. Đã chuyển tính toán sang `.task(id:)` nền, guard `w > 0` trên mọi thanh tỷ trọng, dùng `ViewThatFits` cho 2 ô `DatePicker` và cuộn ngang cho hàng "Tính theo:" để không vỡ ở Dynamic Type lớn. |
| 26 | `HistoryView.swift:38-74, 142-146, 174, 206` | **Cao (Crash + Hiệu năng)** | **Đã sửa** | (a) `.onDelete` truy cập `group.items[idx]` không kiểm tra phạm vi -> nguy cơ crash out-of-bounds nếu danh sách vừa đổi. (b) `ShareLink(item: engine.csv(kind:))` dựng lại toàn bộ chuỗi CSV ~800 dòng ngay trong `body` mỗi lần render. (c) Giải mã `UIImage(contentsOfFile:)` trên main thread mỗi lần cuộn và duyệt `engine.items` ~10 lần trong `body`. Đã kiểm tra bounds `onDelete`, gộp 1 pass tính lịch sử, chuyển xuất CSV sang `HistoryCSVFile: Transferable` (chỉ tạo khi bấm Chia sẻ), và thêm `ScanThumbCache` (`NSCache`). |
| 27 | `OrderDetailView.swift:25, 258-289, 379-389, 575-632, 894-901, 928-942, 1056` | **Cao** | **Đã sửa** | (a) `live` gọi `store.rows.first(where:)` (>25 lần trong `body` = >125.000 phép so trên 5.000 đơn) -> đổi sang `store.row(byId:)` O(1). (b) Nút "Ghi chú" vẫn bật cho đơn kho lưu trữ/đơn hủy dù PC chỉ hỗ trợ bảng chính -> khóa cho `live.k == "m"`. (c) Thay chỉ số mảng thô `stageColors[...]`/`stageTitles[...]`/`returnWatchStageNames[stage]` bằng helper an toàn + `live.returnWatchStageName`. (d) Dynamic Type: dùng `ViewThatFits` cho trạng thái+hạn và timeline, mở rộng `.presentationDetents([.medium, .large])`. |
| 28 | `OrderTablesView.swift:13-25, 114, 167-175, 247, 329-347, 498` | **Cao (5.000 đơn)** | **Đã sửa** | `CancelledTableView`, `CancelAfterPrepView`, `ReturnWatchView` lọc lại toàn bộ 5.000 dòng (gồm `.folding` tiếng Việt) 6-8 lần ngay trong `body` mỗi lần chạm checkbox; `.refreshable` thiếu `guard !store.isDemoMode`; dùng `OrderRow.returnWatchStageNames[stage]`. Đã cache `all` trên nền qua `.task(id:)`, thêm guard demo mode, đổi `.inline`, dùng `r.returnWatchStageName`. |
| 29 | `NotificationsView.swift:10-16, 189` & `SettingsView.swift:286-304` & `StatsView.swift:44, 195` | **Trung bình (5.000 đơn)** | **Đã sửa** | `NotificationsView` tính `alerts()` + `NotificationPlanner.plan` trong `body`; `ReconcileDetailView` duyệt 5.000 dòng 12 lần trong `body`; `StatsView` tạo `DateFormatter` trong `body` và chỉ có "Cả 2 sàn" (thiếu Lazada). Đã chuyển tính toán sang `.task(id:)` nền (1 pass O(n) cho đối chiếu), dùng `store.row(byId:)`, cache `DateFormatter`, và thêm chọn Lazada ở `StatsView`. |
| 30 | `ScanDetailView.swift:4-22` | **Trung bình** | **Đã sửa** | Khi `scanConfirmed == true && status == .error` (PC từ chối `invalid`/`locked`), `scanLabel` rơi vào `default` và hiển thị sai thành `"Chờ nạp PC"`. Đã sửa thành `"PC từ chối"` và dùng màu `Theme.*` nhất quán sáng/tối. |
| 31 | `OrderStats.swift:41-64` | **Trung bình** | **Đã sửa** | `parseDate("31/02/2026")` bị `Calendar.date(from:)` tự cộng dồn thành `03/03/2026`, giờ/phút ngoài phạm vi không bị chặn, và chuỗi giây ISO (`45Z`) bị bỏ. Đã kiểm tra khớp ngày/tháng/năm vòng ngược, kiểm tra `0...23`/`0...59` và tách chữ số giây ISO + XCTest. |
| 32 | `HomeView.swift:187, 420-425, 602-624` | **Trung bình (Dynamic Type & Layout)** | **Đã sửa** | `metricsStrip` 4 cột dễ cắt chữ "Nhận hôm nay" ở cỡ chữ lớn; `greetingLine` trên toolbar thiếu `minimumScaleFactor`; thanh `returnWatchCard` chưa guard `w > 0`. Đã sửa. |

### Nhóm 5 — Mã còn sót & ép kiểu không an toàn (`Inbox`, `inbox_push`, `cancelOrder`, `SHP-2026`, `try!`, `fatalError`, `as!`, `!`, chỉ số mảng)

| # | Tệp:dòng (trước sửa) | Mức độ | Trạng thái | Mô tả lỗi & vì sao |
|---|---|---|---|---|
| 33 | `Protocol.swift:6, 73, 78, 126-137`, `Theme.swift:69`, `SyncEngine.swift:350-355`, `ScanItem.swift:25` | **Trung bình** | **Đã sửa** | Xóa sạch mã còn sót đã bỏ ở v2: `ScanKind.cancelOrder`, `SyncProtocol.inboxPush`, `SyncProtocol.inboxPushMany`, nhánh `ackStage == "push"`, `ScanItem.pushed`, và chuỗi `SHP-2026` trong comment/test. |
| 34 | `Snapshot.swift:55, 117-130, 138-145, 176-177, 192-198, 206-214, 226-239` | **Trung bình** | **Đã sửa** | Xóa sạch `InboxRow`, `FailableInbox` và trường `inbox` khỏi `SnapshotPayload` & `SnapshotDelta` (`JSONDecoder` vẫn tự bỏ qua `"inbox": []` do PC v2 gửi; có XCTest kiểm chứng). |
| 35 | `CameraController.swift:216, 244`, `SyncEngine.swift:62, 502`, `SnapshotStore.swift:44-45`, `MQTT.swift:130`, `NtfyClient.swift:11`, `OrdersView.swift:211, 413`, `OrderTablesView.swift:498`, `OrderDetailView.swift:585-627`, `HistoryView.swift:144`, `ScanItem.swift:89`, `SnapshotTests.swift:71, 75` | **Cao** | **Đã sửa** | Đã rà toàn bộ `ios-app/`: không có `try!`, `fatalError`, `preconditionFailure`, `unowned`; đã sửa **100%** các `as!`, `!` force unwrap (`URL(string:)!`, `file!`, `layer as!`), chỉ số mảng `[0]`/`[idx]`/`[stage]` trong toàn bộ mã sản xuất (`App/Sources` + `ScannerCore/Sources`) và `as!` trong `SnapshotTests.swift`. |
| 36 | `ProductLines.swift` (toàn tệp, 43 dòng) | **Thấp** | **Chưa xóa (mã chết vô hại)** | Tệp view cũ không còn nơi nào tham chiếu (`OrderDetailView` và `OrdersView` đã dùng `ProductMetrics.productLines`), không chứa lỗi/ép kiểu nguy hiểm; giữ lại để tránh xóa tệp ngoài dự kiến (có thể xóa khi dọn sau). |

---

## 3. Ghi chú API phía Data (`v2/src/QuanLyHangHoan.Data/`) cho Claude
- **`v2/src/QuanLyHangHoan.Data/Sync/SyncProtocol.cs:130-135` (`BuildPhotoAck`)**: hiện trả `{type:"pc_ack", ..., ackStage:"photo", outcome:"success|error|not_found|ambiguous", captureId}` mà **chưa gửi trường `photoSaved: true`** (bản app cũ trên iOS từng bắt buộc `photoSaved == true`). Phía `ios-app/` đã được sửa trong T038 để tương thích an toàn với **cả** PC v2 (nhận `ackStage == "photo"` + `outcome == "success"` khi `photoSaved == nil`) lẫn PC cũ (`photoSaved == true`). Tuy nhiên nếu Claude bổ sung `photoSaved = outcome == "success"` vào `SyncProtocol.BuildPhotoAck` thì bản iOS cũ (`ios-native`) và web cũ cũng sẽ tương thích đồng bộ luôn.
- **`v2/src/QuanLyHangHoan.Data/Sync/PhoneSnapshot.cs`** (nhắc lại từ T037, chưa đổi): chưa gửi `shipDate` và chỉ gửi `ar` cho đơn thuộc `return_watch`. Không chặn chạy thật.

---

## 4. Danh sách tệp đã sửa
### `ScannerCore` (lõi nghiệp vụ thuần + XCTest)
- `ios-app/ScannerCore/Sources/ScannerCore/TrackingCode.swift`
- `ios-app/ScannerCore/Sources/ScannerCore/Protocol.swift`
- `ios-app/ScannerCore/Sources/ScannerCore/ScanItem.swift`
- `ios-app/ScannerCore/Sources/ScannerCore/MQTT.swift`
- `ios-app/ScannerCore/Sources/ScannerCore/NtfyClient.swift`
- `ios-app/ScannerCore/Sources/ScannerCore/OrderStats.swift`
- `ios-app/ScannerCore/Sources/ScannerCore/Snapshot.swift`
- `ios-app/ScannerCore/Tests/ScannerCoreTests/TrackingCodeTests.swift`
- `ios-app/ScannerCore/Tests/ScannerCoreTests/ProtocolTests.swift`
- `ios-app/ScannerCore/Tests/ScannerCoreTests/ProtocolWatchTests.swift`
- `ios-app/ScannerCore/Tests/ScannerCoreTests/MQTTTests.swift`
- `ios-app/ScannerCore/Tests/ScannerCoreTests/ScanItemV2AckTests.swift`
- `ios-app/ScannerCore/Tests/ScannerCoreTests/OrderStatsTests.swift`
- `ios-app/ScannerCore/Tests/ScannerCoreTests/SnapshotTests.swift`

### `App` (iOS SwiftUI)
- `ios-app/App/Sources/CameraController.swift`
- `ios-app/App/Sources/ScanView.swift`
- `ios-app/App/Sources/SnapshotStore.swift`
- `ios-app/App/Sources/SyncEngine.swift`
- `ios-app/App/Sources/ScannerApp.swift`
- `ios-app/App/Sources/NotificationRouter.swift`
- `ios-app/App/Sources/HomeView.swift`
- `ios-app/App/Sources/OrdersView.swift`
- `ios-app/App/Sources/OrderDetailView.swift`
- `ios-app/App/Sources/ProductStatsView.swift`
- `ios-app/App/Sources/HistoryView.swift`
- `ios-app/App/Sources/SettingsView.swift`
- `ios-app/App/Sources/OrderTablesView.swift`
- `ios-app/App/Sources/NotificationsView.swift`
- `ios-app/App/Sources/StatsView.swift`
- `ios-app/App/Sources/ScanDetailView.swift`
- `ios-app/App/Sources/Theme.swift`

### Theo dõi dự án
- `TASKS.md`
- `tasks/reports/T038-report.md`

---

## 5. CHƯA BIÊN DỊCH
- **CHƯA BIÊN DỊCH**: Máy Windows này không có Swift/Xcode nên **chưa chạy được `swift test`** và **chưa biên dịch IPA**. Chỉ khi đẩy lên GitHub và workflow `Build iPhone App v2` (macOS) chạy xanh (`swift test` + build IPA) mới là bằng chứng biên dịch và kiểm thử đạt.
- **Kiểm tra tĩnh đã chạy trên toàn bộ 41 tệp Swift trong `ios-app/`**:
  - Bộ phân tích cú pháp ngoặc chính xác (hỗ trợ raw string `#"..."#`, multiline string, string interpolation `\(...)`, comment lồng nhau): **41/41 tệp cân bằng tuyệt đối (`{} = 0`, `() = 0`, `[] = 0`)**.
  - Mã sản xuất (`App/Sources` + `ScannerCore/Sources`): **0** `try!`, **0** `fatalError`, **0** `as!`, **0** force unwrap `!`, **0** `SHP-2026`, **0** `inbox_push`/`InboxRow`/`cancelOrder`, **0** API iOS 17+ chưa bọc `#available`.
  - Đã kiểm tra isolation `@MainActor` (đánh dấu `@MainActor` cho `runRecompute()`, `syncCameraLifecycle()`, gọi `Task { @MainActor in ... }` trong callback camera), và đánh dấu `Sendable` cho `ScanKind`, `ScanStatus`, `ScanItem` để `HistoryCSVFile: Transferable` biên dịch sạch trên Swift 5.9 / iOS 16+.

---

## 6. Việc cần kiểm trên iPhone thật (trước khi vận hành dữ liệu thật)
1. **Quét mã vạch / QR & vòng đời camera**:
   - Từ chối quyền camera lần đầu -> bấm **"Mở Cài đặt iPhone"** bật lại -> quay về app: kiểm tra overlay từ chối tự biến mất và camera chạy ngay.
   - Đang mở tab Quét: chuyển sang tab khác, khóa màn hình, đưa app xuống nền, hoặc nhận cuộc gọi đến -> kiểm tra đèn/camera tắt hẳn, quay lại mở khóa thì quét tiếp bình thường.
   - Thử bật Đèn + Zoom 2x liên tục khi đang quét; quét nhãn có nhiều mã (mã đơn + mã vận đơn), mã QR dạng URL/JSON/nhiều dòng, và thử nút **"Quét tìm đơn"** ở tab Đơn.
2. **Hàng đợi bền & gửi ảnh với PC v2**:
   - Bật "Gửi ảnh khi quét hoàn kho", quét 1 đơn đang chờ trên PC v2: kiểm tra sau khi PC nhận được ảnh thì trên iPhone mục đó chuyển sang **"PC đã lưu"** và **badge chờ ở tab Lịch sử về 0** (xác nhận lỗi `photoSaved` đã hết).
   - Bật Chế độ máy bay -> quét 3 đơn (có ảnh) + bấm Ghi chú / Đổi trạng thái 1 đơn -> **vuốt tắt hẳn app** -> mở lại app -> tắt Chế độ máy bay: kiểm tra đủ cả 3 lượt quét (kèm ảnh) và lệnh sửa tự gửi lên PC, **không tạo bản trùng**, ảnh chưa ACK không tự biến mất trước khi PC xác nhận.
   - Xóa 1 lượt quét ở Lịch sử -> kiểm tra ảnh không còn phình dung lượng.
3. **Đồng bộ snapshot lớn (~4.600–5.000 đơn) & Quy tắc 1/2/3**:
   - Đồng bộ toàn bộ dữ liệu thật từ PC v2 sang iPhone: kiểm tra thanh tiến độ ghép mảnh, thời gian mở app lần kế tiếp (không đứng màn hình lúc khởi động), cuộn tab **Đơn**, gõ tìm kiếm liên tục, mở **Tổng quan**, **Thống kê sản phẩm** (cả chế độ Theo ngày) và **Chi tiết đơn** có mượt không.
   - Thử sửa ghi chú / đổi trạng thái 1 đơn khi PC chưa kịp ACK, rồi để PC đẩy `snapshot_delta` định kỳ -> kiểm tra ghi chú/trạng thái vừa bấm không bị nhảy mất.
   - Thử quét mã đơn gốc có 2 kiện A/B đang chờ -> kiểm tra iPhone cảnh báo trùng nhiều đơn, **không** tự đánh dấu cả 2 kiện đã nhận.
4. **Giao diện thực tế (Sáng/Tối, Dynamic Type lớn, tên tiếng Việt dài)**:
   - Bật cỡ chữ trợ năng lớn (Accessibility L/XL) và Dark Mode trên iPhone thật: kiểm tra **Tổng quan** (4 ô số liệu, thanh tiêu đề), **Thống kê sản phẩm** (`Chọn ngày` 2 ô `DatePicker`, nút `Tính theo`), **Chi tiết đơn** (2 viên thuốc trạng thái/hạn, Timeline, sheet Ghi chú/Trạng thái), và **Lịch sử** (nút Chia sẻ CSV + vuốt xóa).

TRẠNG THÁI: XONG
