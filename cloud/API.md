# HỢP ĐỒNG API CLOUD (v1) — nguồn chuẩn cho Worker, máy tính, iPhone, Apps Script, công cụ nhập

Trạng thái: **bản đề xuất đã duyệt về hướng** (xem docs/cloud-design.md). Mọi bên viết theo hợp đồng này; muốn đổi phải sửa file này trước.
Dữ liệu trong ví dụ là GIẢ. Mọi thời gian dạng chuỗi giữ **định dạng app cũ** (`dd/MM/yyyy HH:mm[:ss]`) giống `Order` của v2; riêng trường `*At`/`updatedAt` của máy chủ là mili-giây UTC (số nguyên).

## 0. Quy ước chung
- Gốc: `https://<tên-worker>.<tài-khoản>.workers.dev/v1`. JSON UTF-8. Mọi lỗi: `{"error":{"code":"...","message":"tiếng Việt cho người dùng"}}`.
- Mã lỗi HTTP: `401 unauthorized` (token sai/bị thu hồi), `403 forbidden` (không đủ quyền), `404 not_found`, `409 conflict`, `422 invalid`, `429 quota_exceeded` (hết hạn mức ngày → khóa ghi tới hôm sau), `503 unavailable`.
- Header: `Authorization: Bearer <token thiết bị>`; tuỳ chọn `X-Client: pc/1.0` | `ios/1.0` để chẩn đoán.
- **Mất mạng = khóa ghi** (docs/cloud-design.md mục 2): máy khách không được gửi thao tác ghi khi không chắc có mạng; không có hàng đợi ngoại tuyến. Gửi lặp cùng `opId` là an toàn (idempotent).
- Một cửa hàng duy nhất: không có `shopId` trong API.

## 1. Kiểu dữ liệu: Parcel (kiện, "slim")
Một kiện = một `Order` của v2, **bỏ** dữ liệu thô và dữ liệu người mua. Trường (JSON camelCase):

| Trường | Kiểu | Ghi chú |
|---|---|---|
| `id` | string | khóa nội bộ ổn định (v2 `Order.Id`) — **khóa chính** |
| `orderId`, `returnOrderId` | string | mã đơn gốc, mã yêu cầu hoàn |
| `track`, `returnTrack`, `altTrack`, `forwardTrack` | string | mã vận đơn |
| `trackingCodes`, `scanAliases` | string[] | mã thay thế liên kết tường minh |
| `platform`, `orderType`, `platformStatus`, `preOrder`, `status`, `carrier` | string | `status` ∈ `Hàng đã trả`/`Chưa trả`/`Không cần nhận`/`Bị mất`/`Chưa về` |
| `shipDate`, `returnDate`, `recvDate`, `returnedAt`, `deadline` | string | chuỗi định dạng cũ |
| `price` | number | đồng |
| `note` | string | lý do hoàn/hủy |
| `userNote` | string | ghi chú của chủ shop |
| `importWeek`, `importedAt` | string | |
| `isWatchlist`, `watchlistReason`, `isPartialReturn` | bool/string/bool | |
| `photoId` | string | ảnh mới nhất của kiện (xem mục 6) |
| `items` | Item[] | `{product,color,size,qty,returnedQty,sku?,skuId?}` (không gồm `importPrices`) |
| `extra` | object | CHỈ các khóa trong danh sách trắng: `returnReason`, `returnStatus`, `returnShippingStatus`, `returnSuccessTime`, `cancelType`, `forwardCarrier`, `returnCarrier`, `priceNeedsReview`, `priceHasValue` |
| `isArchived`, `archivedAt`, `isDeleted`, `deletedAt`, `deletedFrom` | bool/string | |

**KHÔNG BAO GIỜ gửi:** `buyerNote`, `rawDetails` (địa chỉ giao, tên đăng nhập người mua, phương thức thanh toán, mã khiếu nại, ...), `raw_json`, `photoHistory`, `priceModes`/`importModeSlots`, tên/SĐT/địa chỉ người nhận. Máy chủ **từ chối (422)** khi thấy các khóa này.
Trên máy chủ mỗi kiện có thêm: `rev` (số nguyên tăng dần theo mỗi thay đổi), `updatedAt`, `hash` (băm nội dung để bỏ qua kiện không đổi).

## 2. Ghép thiết bị và quyền
Vai trò: `owner` (chủ) | `staff` (nhân viên). Điện thoại là ứng dụng đầy đủ nên cả hai vai trò dùng được mọi chức năng xem/quét; chỉ `owner` được quản lý thiết bị, thu hồi, xem hạn mức chi tiết, nhập dữ liệu hàng loạt.
- `POST /pair/start` (owner) → `{ "code": "483920", "expiresAt": 1764000000000 }` — mã 6 số, sống 10 phút, dùng một lần.
- `POST /pair/complete` (không cần token) `{ "code": "483920", "deviceName": "Điện thoại kho 1", "kind": "phone"|"pc" }` → `{ "deviceId": "...", "token": "...", "role": "staff" }`. Token chỉ trả MỘT lần; máy chủ lưu băm.
- Thiết bị đầu tiên (chủ) được tạo bằng **khóa khởi tạo** do chủ đặt trong Worker secrets (`BOOTSTRAP_KEY`): `POST /pair/bootstrap {bootstrapKey, deviceName, kind}` → role `owner`; chỉ dùng được khi chưa có thiết bị nào.
- `GET /devices` (owner) → `[{deviceId, deviceName, kind, role, lastSeenAt, revoked}]`; `DELETE /devices/{deviceId}` (owner) thu hồi.
- `GET /me` → `{deviceId, deviceName, role}` (kiểm tra token còn dùng được).

## 3. Đồng bộ phần thay đổi
`GET /sync?since=<rev>&limit=500` → 
```json
{ "rev": 1234, "more": false, "changes": [
  { "type": "parcel", "rev": 1201, "parcel": { "...Parcel + rev/updatedAt..." } },
  { "type": "returnWatch", "rev": 1210, "key": "<id kiện>", "deleted": false, "data": { "stage": 2, "since": 1764000000000 } }
] }
```
- `since=0` lấy toàn bộ. `more=true` thì gọi tiếp với `since` = `rev` lớn nhất vừa nhận.
- Kiện bị xóa/lưu trữ KHÔNG biến mất khỏi luồng: có `isDeleted`/`isArchived` (máy khách tự ẩn). Không bao giờ xóa cứng (quy tắc 8).
- Máy khách lưu `rev` đã xử lý xong; mất kết nối thì lúc nối lại gọi `/sync` từ `rev` đó.

## 4. Thao tác ghi
`POST /ops` `{ "ops": [ { "opId": "uuid", "type": "...", ... }, ... ] }` (tối đa 50) → 
`{ "results": [ { "opId": "uuid", "ok": true, "rev": 1235, "result": { ... } }, { "opId":"..", "ok": false, "error": {"code":"conflict","message":".."} } ] }`.
`opId` là UUID do máy khách sinh; gửi lại cùng `opId` trả lại kết quả cũ, không làm lần hai (lưu 7 ngày).

| `type` | Tham số | Ghi chú |
|---|---|---|
| `scan` | `code`, `capturedAt?` | quyết định + ghi trong MỘT giao dịch; xem mục 5 |
| `setStatus` | `id`, `status`, `expectedStatus?` | đổi thủ công; có `expectedStatus` thì chỉ ghi khi trạng thái hiện tại khớp, không thì `conflict` |
| `editNote` | `id`, `note?`, `userNote?` | ghi lại lịch sử sửa |
| `setWatch` | `id`, `on`, `reason?` | theo dõi / bỏ theo dõi |
| `archive` / `unarchive` | `id` | |
| `delete` / `restore` | `id` | xóa mềm |
| `editCell` | `id`, `field`, `value` | sửa ô (ghi `cell_edit_history`) |

Mọi thao tác ghi vào `audit_log` (ai, lúc nào, giá trị cũ → mới); không bao giờ ghi đè mất dấu.

## 5. Quét nhận kiện
Op `scan` `{ "type":"scan","opId":"..","code":"spx123" }` → `result`:
```json
{ "outcome": "received", "ack": true, "message": "Đã nhận kiện.", "parcel": { ... }, "receivedBy": "Điện thoại kho 1", "receivedAt": "25/11/2026 14:30:12" }
```
`outcome` ∈ `received` | `alreadyReceived` | `ambiguous` | `notFound` | `needsManualCheck` | `error`. `ack` là `true` CHỈ khi máy chủ đã ghi bền (quy tắc 6). `alreadyReceived` kèm `receivedBy`/`receivedAt` của lượt nhận trước (hai máy cùng quét một kiện thì chỉ máy đến trước nhận được).
Quyết định theo `cloud/core` (`decide`), chuẩn đối chiếu `cloud/golden/scan-vectors.json`. Chỉ xét kiện `isArchived=false` và `isDeleted=false`. Ghi bằng điều kiện trạng thái cũ (quy tắc nhận kiện A không nhận B/C giữ nguyên).

## 6. Ảnh
Điện thoại là nơi duy nhất chụp. Máy tính chỉ xem.
- `PUT /photos/{photoId}?parcelId=<id>&mime=image/jpeg&kind=full|thumb` (thân là byte ảnh; điện thoại tự thu nhỏ: bản `full` ≤ 400 KB, bản `thumb` ~300 px; máy chủ từ chối > 1 MB) → `{ "ok": true, "photoId": "...", "sha256": "..." }`. `photoId` gồm chữ, số, `_`, `-` (6–80 ký tự). Máy chủ chỉ nhận ảnh MỚI khi chính thiết bị này vừa quét xác nhận kiện đó (`received`/`alreadyReceived`) trong vòng **15 phút**; không thì `409`. Thiết bị loại `pc` bị `403` (máy tính chỉ xem). Gửi bản `full` xong thì kiện được gắn `photoId` này (các máy khác nhận qua `/sync`). Gửi `thumb` sau cùng `photoId`. Lưu ở R2.
- `GET /photos/{photoId}?size=thumb|full` → byte ảnh, có `Cache-Control: private, max-age=2592000` (máy khách lưu tạm).
- `GET /parcels/{id}/photos` → `[{photoId, takenAt, deviceName}]`.
- Ảnh tự xóa sau **90 ngày** (Cron của Worker); xóa là thao tác duy nhất được phép xóa cứng và được ghi `audit_log`.
- Tối ưu về sau (không bắt buộc): đường dẫn ký sẵn thẳng với R2 để không tính yêu cầu Worker.

## 7. Nhập đơn (Apps Script / máy tính)
`POST /import` `{ "source": "sheet"|"file"|"folder"|"salework", "signature": "<chữ ký nguồn>", "parcels": [ { ...Parcel không có rev/updatedAt, "hash": "..." } ] }` (tối đa 500 kiện/lần) → `{ "added": 3, "updated": 2, "unchanged": 495, "rejected": [ { "id": "...", "reason": "..." } ] }`.
- Upsert theo `id` (khóa kiện); nhập lại cùng kiện thì **cập nhật**, không cộng lặp sản phẩm (quy tắc 5). Kiện `hash` trùng hash hiện có thì bỏ qua, không ghi.
- Không ghi đè trường do người dùng sửa tay trên cloud (`userNote`, `isWatchlist`, `watchlistReason`, trạng thái nhận đã `Hàng đã trả`) trừ khi nguồn nói rõ; quy tắc gộp chi tiết nằm ở bộ gộp phía nguồn (`SheetPipeline`/`ParcelMerge`), không đoán ở máy chủ.
- `GET /import/state?source=sheet` → `{ "signature": "...", "lastRunAt": 1764000000000, "parcelCount": 4962 }` để nguồn bỏ qua vòng không đổi.
- Quyền: khóa nhập riêng cho Apps Script (`X-Import-Key`, Worker secret `IMPORT_KEY`, chỉ gọi được `/import*`); thiết bị `owner`/`staff` cũng gọi được.

## 8. Thời gian thực
`GET /ws?token=<token>` (nâng cấp WebSocket). Máy chủ gửi `{"t":"rev","rev":1235}` khi có thay đổi và `{"t":"quota","level":"warn"|"stop"}` khi gần/hết hạn mức; máy khách nhận `rev` mới thì gọi `/sync`. Máy khách gửi `{"t":"ping"}` mỗi 30 giây, nhận `{"t":"pong"}`. **Mất WebSocket > 20 giây = trạng thái MẤT KẾT NỐI** (khóa ghi); nối lại thì `/sync` từ `rev` đã lưu rồi mở khóa.

## 9. Hạn mức
`GET /quota` → `{ "day": "2026-11-25", "requests": { "used": 8200, "limit": 100000 }, "rowsWritten": { "used": 5100, "limit": 100000 }, "rowsRead": {"used": 90000, "limit": 5000000}, "level": "ok"|"warn"|"stop" }`. `warn` ≥ 70%, `stop` ≥ 98% (từ chối ghi không khẩn cấp, vẫn cho quét). Số liệu là **đếm của chính ứng dụng** (ước lượng), không phải số của Cloudflare.

## 9b. Cổng AI (khóa Groq/Gemini chỉ nằm trên cloud)
- `POST /ai/groq/chat/completions` và `POST /ai/gemini/chat/completions`: nhận NGUYÊN thân yêu cầu kiểu OpenAI (`model`, `messages`, `tools`, `tool_choice`, `temperature`...) rồi chuyển tiếp tới nhà cung cấp bằng khóa của cloud; trả lại đúng mã trạng thái, thân và `Retry-After` của nhà cung cấp.
- Máy khách dùng lại NGUYÊN logic gọi AI hiện có (chuỗi mô hình dự phòng, chờ khi 429 ngắn, lọc công cụ cho Gemini...), chỉ đổi địa chỉ gốc sang `<worker>/v1/ai/<groq|gemini>` và dùng **token thiết bị** thay cho khóa API. Công cụ AI vẫn chạy trên máy khách với dữ liệu đã đồng bộ (không gửi tên/SĐT/địa chỉ).
- `GET /ai/status` → `{ groq: bool, gemini: bool, usedToday, dailyLimit }` (khóa nào đã cấu hình; số lượt hỏi hôm nay cho cả cửa hàng).
- Lỗi: `503 ai_not_configured` (chưa đặt khóa), `429 ai_quota` (hết lượt AI trong ngày, mặc định 3.000, biến `AI_DAILY_LIMIT`), `429 quota_exceeded` (hết hạn mức cloud), `422 invalid` (rỗng hoặc > 1 MB), `503 unavailable` (không tới được nhà cung cấp).
- Cloud KHÔNG ghi lại nội dung hỏi/đáp; chỉ đếm số lượt trong ngày.

## 10. Trạng thái kết nối phía máy khách
`connected` (WebSocket sống, /sync đã bắt kịp) · `syncing` (đang lấy thay đổi) · `connecting` · `disconnected` (MẤT KẾT NỐI, khóa ghi, chỉ xem dữ liệu đã tải kèm nhãn "chỉ xem") · `unpaired` (chưa ghép) · `revoked` (token bị thu hồi).
