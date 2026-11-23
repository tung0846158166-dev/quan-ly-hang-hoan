# Đồng bộ điện thoại (v2) - quyết định và thiết kế (2026-11-23)

## Quyết định của chủ dự án (chốt 2026-11-23)
1. **Đồng bộ điện thoại: MQTT + ntfy theo "mã phòng"** như app cũ (chuyển dần, không phá thói quen). Relay riêng mã hóa đầu cuối để SAU.
2. **Shopee "Hoàn hàng" = mọi đơn Shopee không phải "Đơn hủy"** (gồm cả 17 đơn "Giao không thành công"). Đã cài đúng như vậy ở T006.
3. Chủ dự án tự chạy thử app thật (`v2/CHAY-APP.md`) và so số liệu với app cũ; sẽ đưa file Excel mẫu đã ẩn danh (Shopee/TikTok/Salework) khi có.

## Giao thức (đọc từ `mobile_scanner.html` của app cũ; v2 giữ NGUYÊN định dạng để điện thoại cũ vẫn dùng được)
- Chủ đề MQTT: `agy_shopee_return_scanner_2026/<mã phòng>`. ntfy: topic `agy_shopee_<mã phòng viết thường, chỉ a-z0-9_->`.
- Điện thoại -> PC: `{"type":"barcode","room","requestId","code","captureId","time",...}` (ảnh gửi riêng, không kèm bản tin quét; ntfy giới hạn 4 KB nên ảnh chia mảnh).
- PC -> điện thoại: `{"type":"pc_ack","room","requestId","code","outcome":"success|duplicate|not_found|ambiguous|error","extra","ackStage":"scan"}`.
- Các loại khác app cũ còn dùng (CHƯA cài ở v2): `smart_label`, `sync_item`, `new_order_scan`, `inbox_push`, `pc_hello`, `pc_heartbeat`, `manifest_sync`, `latency_ping/pong`, ảnh theo mảnh (`ackStage:"photo"`).

## Đã cài (Data/Sync, 127 test xanh)
- `SyncProtocol`: đọc bản tin quét + dựng `pc_ack` đúng định dạng cũ; bản tin lạ/thiếu requestId/code bị bỏ qua.
- `SyncReceiver`: khác phòng thì bỏ qua; **chống xử lý lặp theo requestId** (bảng `sync_inbox`, gửi lại cùng requestId trả đúng xác nhận cũ); xác nhận `success` chỉ sinh SAU khi `ScanReceiver` đã commit (quy tắc 6); lỗi lưu => `error`, không bao giờ `success`; `Ambiguous` và `NotFound` báo rõ, không đổi dữ liệu.
- `ISyncTransport` + `InMemoryTransport` + `SyncService`: lõi không biết MQTT/ntfy; bản thật chỉ cần cài `ISyncTransport`.
- Schema DB lên phiên bản 2 (thêm `sync_inbox`).

## Đã cài thêm: bộ truyền tin MQTT thật + ảnh từ điện thoại (164 test xanh)
- `MqttTransport` (MQTTnet 4.3.7.1207): WebSocket+TLS, 3 broker công cộng như app cũ, tự kết nối lại, không ném lỗi khi chưa kết nối (ghi `LastError`). Thử tay vòng thật (dữ liệu giả, phòng ngẫu nhiên): kết nối 3/3 broker <= 12 giây, ack ~0,8 giây.
- `SyncRoom` (mã phòng ngẫu nhiên 24 ký tự, kiểm độ mạnh >= 16 ký tự và khác `SHP-2026`), `AppSettings` (mã phòng tạo tự động ở lần đầu).
- Ảnh: `photo` (trọn gói) và `photo_chunk` (mảnh, ghép theo `photoId`, chịu lệch thứ tự và mảnh lặp). Ack `{ackStage:"photo", captureId}` chỉ sau khi ảnh đã commit; chưa đủ mảnh thì KHÔNG ack. Ảnh KHÔNG đổi trạng thái nhận kiện, ảnh cũ không bị đè (mỗi ảnh một `p_<captureId>`), gửi lại cùng captureId trả đúng xác nhận cũ.
- Giới hạn vì bản tin đến từ mạng công cộng: chỉ jpeg/png/webp, đúng chữ ký tệp, <= 4 MB, <= 2.500 mảnh, tối đa 64 ảnh đang ghép dở, bỏ ảnh dở sau 10 phút. Ảnh sai => ack `error`, không lưu.

## Đã cài thêm: ntfy + gộp đường truyền (174 test xanh)
- `NtfyTransport`: POST vào `agy_shopee_<phòng>`, nghe SSE `/sse`, nối lại tự động và xin phần lỡ bằng `since=<id cuối>`, giới hạn 4096 byte/bản tin, chỉ mở kết nối cho phòng đã đăng ký. `CompositeTransport`: gửi/nghe nhiều đường cùng lúc (MQTT + ntfy), một đường lỗi không chặn đường khác. `AppSettings.NtfyEnabled`.
- **CHƯA kiểm chứng với ntfy.sh thật**: test chỉ dùng máy chủ GIẢ (định dạng SSE dựa theo mã app cũ). Thử vòng thật ngày 2026-11-23 thất bại vì **mạng của máy chủ dự án không tới được ntfy.sh** (kết nối quá hạn cả IPv4 lẫn IPv6, trong khi example.com vẫn vào được): đây là lỗi mạng, không phải của mã. Chủ dự án nên thử lại trên mạng khác/điện thoại; nếu ntfy.sh bị chặn thì chỉ MQTT chạy (đã kiểm chứng thật), và tắt ntfy trong màn hình đồng bộ (T011).

## Đã cài thêm: báo "PC đang online" cho điện thoại cũ (180 test xanh)
Điện thoại cũ coi PC là offline nếu 45 giây không thấy `pc_hello`/`pc_heartbeat` (rồi dồn mã vào hàng đợi và chặn quét khi quá 5 mã chưa được xác nhận). `SyncService` giờ gửi `pc_hello` khi khởi động và khi nhận `phone_connected` (cùng phòng), và `pc_heartbeat` mỗi 15 giây (`heartbeatInterval`, `TimeSpan.Zero` = tắt). Bản tin này nhỏ, không chứa dữ liệu đơn.
**Cố ý KHÔNG làm** (ghi để khỏi tưởng bị quên):
- `manifest_sync` (danh sách mọi mã vận đơn của PC gửi điện thoại): qua broker công cộng không mã hóa sẽ lộ toàn bộ mã trong kho. Điện thoại vẫn chạy được không có nó. Chỉ làm khi đã có mã hóa bản tin hoặc relay riêng.
- `latency_ping/pong`: chỉ để đo tốc độ từng đường, cần biết bản tin đến qua đường nào (`uplinkVia`) mà `ISyncTransport` hiện chưa phơi ra.

## Đã cài thêm: app iPhone gốc (`ios-native/`, IPA SwiftUI) xem và thao tác đơn (2026-11-23, +17 test)
App iPhone KHÔNG chỉ quét: nó xin danh sách đơn từ PC để xem/thống kê/thông báo hạn, và gửi thao tác ngược lại. v2 trước đó chỉ hiểu `barcode`/`photo`, nên app iPhone sẽ trống. Nay v2 nói đúng giao thức của `ios-native/ScannerCore/.../Protocol.swift` + `Snapshot.swift`:
- **PC -> điện thoại** (`Data/Sync/PhoneSnapshot` + `SyncService`): `snapshot_chunk` (danh sách đầy đủ: JSON -> deflate thô -> base64 -> mảnh 2.400 ký tự, gửi 2 lượt), `snapshot_delta` (chỉ dòng đổi/bỏ so với bản đầy đủ, khóa `k|c|o|ci`, trùng đánh số `#2`), `watch_sync` (danh sách theo dõi + lý do). Trường dòng: `k m/c/a`, `c o p n co sz q rd due st ty pre pd rv wl wr nt rcd lv ci ps x`. Cảnh báo `lv`: đỏ >= 24h, vàng >= 12h kể từ giờ hàng về (chưa nhận); `due` = giờ về + 24h. Đơn hủy (`OrderType = "Đơn hủy"`, chưa lưu trữ) gửi với `k=c`, `ci` = id đơn để điện thoại đánh dấu đã nhận. Kiện A/B/C cùng mã đơn là các dòng riêng. Không có tên/SĐT/địa chỉ.
- **Khi nào gửi**: chỉ sau khi một app điện thoại ĐÃ xin (`phone_connected` -> gửi sau ~1 giây; `snapshot_request` -> gửi đầy đủ ngay, cả ntfy). Sau đó, khi điện thoại còn hoạt động trong 30 phút: gửi phần thay đổi sau mỗi lần quét/sửa (~0,3 giây) và khi DB đổi (thăm dò 5 giây); gửi đầy đủ lại mỗi 10 phút hoặc khi thay đổi > 400 dòng. Trang quét cũ không xin nên không bị đẩy gì.
- **Chọn đường**: gói lớn (`PublishBulkAsync`) chỉ đi MQTT; ntfy chỉ nhận khi điện thoại xin đầy đủ hoặc gói rất nhỏ (<= 3 mảnh), vì ntfy.sh giới hạn số tin và sẽ chặn cả tin xác nhận quét.
- **Điện thoại -> PC** (`Data/Sync/PhoneActions`): `order_note` (bật/tắt theo dõi + lý do), `order_edit` (CHỈ đổi trạng thái; sửa sản phẩm/loại đơn bị từ chối `invalid` cả gói, không lưu một nửa; "Hư hỏng" không có ở v2), `order_delete` (xóa MỀM, khôi phục được), `cancel_receive` (đánh dấu đã nhận đơn hủy). Mọi thay đổi đi qua `OrderEditor` (lịch sử + nhật ký cùng giao dịch); mã khớp nhiều kiện = `ambiguous` (không đoán; mã giao đi dùng chung KHÔNG dùng để tìm); gửi lại cùng `requestId` trả đúng xác nhận cũ; chỉ `success` sau khi đã lưu.
- **BỎ HẲN (quyết định chủ dự án 23/11/2026)**: tạo ĐƠN HỦY từ điện thoại (`new_order_scan` có `cancelled:true`): trả `error` có lý do, không lưu. Việc đánh dấu đã nhận đơn hủy (`cancel_receive`) vẫn hoạt động.
- **Đơn mới quét từ điện thoại ĐI THẲNG VÀO BẢNG CHÍNH** (quyết định chủ dự án 23/11/2026; `new_order_scan` đơn thường): tạo một kiện mới ngay (sàn tự nhận theo mã như app cũ, hãng vận chuyển theo mã, trạng thái "Chưa trả", loại "Trả hàng/Hoàn tiền", ghi chú "Đơn mới tạo từ máy quét điện thoại", sản phẩm "Sản phẩm mới (Quét điện thoại)"), ghi nhật ký `phone_new_order`, rồi mới `success`. Mã đã có ở bảng chính/kho lưu trữ/ĐÃ XÓA (kể cả theo mã hoàn/mã phụ) => `duplicate`, KHÔNG tạo lại và không hồi sinh đơn đã xóa. Gửi lại cùng `requestId` không tạo hai lần. Ảnh gửi sau bằng `photo`/`photo_chunk` gắn vào đơn vừa tạo. `SyncHandled.Kind = "new_order"` để giao diện báo (phiếu T019).
- **CHƯA hỗ trợ (trả `error` rõ ràng, không lưu gì, điện thoại giữ trong hàng đợi)**: `inbox_push`/`inbox_push_many` (v2 không có Hộp Đơn Mới nên không có gì để đẩy), `inbox_push`/`inbox_push_many`. Lý do: v2 chưa có "Hộp Đơn Mới" làm nơi chờ duyệt; cần chủ dự án quyết định đơn tạo từ điện thoại đi đâu. `manifest_sync`, `latency_ping/pong` vẫn cố ý chưa làm.
- **Rủi ro giữ nguyên**: danh sách đơn đi qua broker công cộng KHÔNG mã hóa (mã vận đơn, mã đơn, tên sản phẩm, ghi chú). Chỉ mã phòng ngẫu nhiên 24 ký tự bảo vệ; điện thoại phải nhập đúng mã phòng của v2 (xem màn "Đồng bộ điện thoại"). Mã hóa/relay riêng vẫn là việc sau.
- **Chưa kiểm chứng**: chưa thử với app iPhone thật (chỉ thử với bản tin dựng theo mã Swift + mã JS cũ). App iPhone KHÔNG cần build lại: giao thức giữ nguyên, chỉ PC thay đổi.

## CHƯA làm / việc kế tiếp
1. Bộ truyền tin thật: MQTT (broker nào, thư viện nào: ví dụ MQTTnet) và ntfy (SSE + poll). Nên là phiếu riêng, có thể giao Antigravity cho phần cấu hình/giao diện, Claude viết bộ truyền tin trong `Data/Sync`.
2. ~~Ảnh từ điện thoại~~ (đã xong, xem trên). Còn: bộ truyền tin ntfy (SSE + poll `/json?poll=1`) để mảnh ảnh thực sự tới được; hiện chỉ có đường MQTT.
3. Màn hình trạng thái đồng bộ (đang kết nối, phòng, số điện thoại online) + ô nhập mã phòng.
4. Các loại bản tin còn lại (bảng trên).

## RỦI RO BẢO MẬT - cần chủ dự án biết
App cũ gửi qua **broker công cộng** (`broker.emqx.io`, `broker.hivemq.com`, `test.mosquitto.org`) và **ntfy.sh**, bản tin **không mã hóa**; "mã phòng" (mặc định `SHP-2026`) là thứ duy nhất bảo vệ. Ai biết/đoán được mã phòng đều đọc được mã vận đơn quét và có thể GỬI bản tin giả để làm app "nhận" kiện sai. Khuyến nghị cho v2 trước khi dùng thật:
- Đổi mã phòng mặc định sang chuỗi dài ngẫu nhiên (>= 16 ký tự), không dùng `SHP-2026`.
- Chấp nhận bản tin chỉ từ phòng đã cấu hình (đã làm); chống lặp theo requestId (đã làm).
- Về lâu dài: mã hóa nội dung bản tin (AES-GCM, khóa suy ra từ bí mật dùng chung) hoặc relay riêng, đúng mục "Relay riêng mã hóa đầu cuối".
Kẻ gửi giả chỉ làm được việc "nhận kiện" đúng mã có trong kho (không xóa/sửa dữ liệu khác), nhưng vẫn là sai lệch kho.
