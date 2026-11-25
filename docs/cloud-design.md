# Thiết kế chuyển sang CLOUD (Cloudflare) — BẢN 2, CHỜ CHỦ DỰ ÁN DUYỆT

Trạng thái: **ĐỀ XUẤT, chưa làm gì.** Bản Electron cũ vẫn là chuẩn đối chiếu; bản v2 hiện tại (SQLite + MQTT/ntfy) chạy bình thường cho tới khi chủ dự án duyệt và kiểm tra bản cloud.

## 0. Những điều chủ dự án đã chốt (bản 2)
1. **Mất mạng = KHÔNG làm việc.** Máy hiện trạng thái "Mất kết nối" và chặn quét/sửa; không có hàng đợi ngoại tuyến. (Tránh nhiều máy quét cùng một đơn lúc mất mạng rồi dồn lên lệch.)
2. Không đưa dữ liệu cá nhân người nhận lên cloud; phần dữ liệu thô chỉ giữ các trường cần thiết (xem 3.1).
3. **Cả điện thoại và máy tính đều xem được ảnh của đơn; chỉ điện thoại mới chụp/quét ảnh.** Ảnh lưu ở R2.
4. Gói: **R2 10 GB, D1 500 MB** (xem 3.7: D1 là giới hạn chật nhất).
5. **Điện thoại là một ứng dụng độc lập đầy đủ** (xem danh sách, chi tiết, thống kê, AI, quét...), không chỉ để quét.
6. **Google Sheet nạp thẳng lên cloud** (Cron của Worker). Các cách nạp khác (file Excel, thư mục, Salework) vẫn chạy ở máy tính.
7. **MỘT cửa hàng duy nhất, phần mềm nội bộ cho nhân viên dùng** (không bán/cho thuê, không có đăng ký cửa hàng). `shop_id` chỉ là một hằng số, không làm phần quản lý nhiều cửa hàng.
Chưa trả lời: số ngày giữ ảnh (mặc định 90, xem 3.7).

## 1. Tổng quan

```
   iPhone x10 (SwiftUI, ứng dụng đầy đủ)     Máy tính x2 (WPF)
   ┌────────────────────────────┐          ┌───────────────────────────┐
   │ giao diện (giữ)            │          │ giao diện (giữ)           │
   │ bản lưu tạm CHỈ ĐỌC khi    │          │ bản lưu tạm CHỈ ĐỌC khi   │
   │ mất mạng; mọi GHI cần mạng │          │ mất mạng; mọi GHI cần mạng│
   │ chụp + gửi ảnh             │          │ xem ảnh; bộ nạp file/     │
   └────────────┬───────────────┘          │ thư mục/Salework (giữ)    │
                │ HTTPS + WebSocket        └─────────────┬─────────────┘
                └───────────────┬────────────────────────┘
                                ▼
                 ┌─────────────────────────────┐
                 │ Cloudflare Worker (API)     │ xác thực thiết bị + quy tắc nghiệp vụ
                 ├─────────────────────────────┤
                 │ D1  (500 MB): đơn, kiện,    │ nguồn dữ liệu gốc, dòng GỌN
                 │      nhật ký, chỉ mục       │
                 │ DO  : 1 đối tượng/cửa hàng  │ báo thay đổi (WebSocket)
                 │ R2  (10 GB): ảnh, kho lưu   │ tệp lớn, bản lưu trữ cũ
                 │ Cron: nạp Google Sheet      │ chạy khi mọi máy tắt
                 └─────────────────────────────┘
```

## 2. Quy tắc "mất mạng = dừng"
- Máy khách có chấm trạng thái: **Đã kết nối / Đang đồng bộ / MẤT KẾT NỐI**.
- Khi MẤT KẾT NỐI: quét, sửa, đổi trạng thái, nạp, chụp ảnh đều bị **chặn** kèm thông báo rõ. Chỉ **xem** được dữ liệu đã tải lần cuối (nhãn "Dữ liệu cũ lúc HH:mm, chỉ xem").
- Mọi thao tác ghi chỉ coi là thành công khi **server trả về thành công** (quy tắc bất biến số 6: chỉ ACK sau khi đã lưu bền).
- Mạng đứt đúng lúc đang gửi (không biết server đã nhận chưa): máy khách gửi lại với **cùng mã thao tác** (`client_op_id`), server nhận trùng thì trả lại kết quả cũ, không làm đôi. Nếu không xác nhận được thì báo "chưa chắc đã nhận, hãy kiểm tra lại đơn trước khi quét lại".
- Thay đổi so với hiện tại: iPhone hiện có hàng đợi quét khi mất mạng; bản cloud **bỏ hàng đợi này** (đúng yêu cầu).

## 3. Cloud

### 3.1 Dữ liệu và "dữ liệu thô" (đo trên dữ liệu thật hiện tại: 4.962 đơn, DB 180 MB)
Số liệu đo (chỉ tên trường và dung lượng, không đọc nội dung cá nhân):
| Phần | Dung lượng | Là gì | Lên cloud? |
|---|---|---|---|
| `orders.raw_json` | ~76 MB | bản sao nguyên văn của từng đơn từ app cũ | **Không** (giữ ở máy cũ/bản sao lưu) |
| `extra_json` → `photoHistory` | ~37 MB (chỉ 328 đơn) | ảnh cũ nhúng thẳng vào đơn | **Không nhúng**; chuyển ảnh sang R2 rồi bỏ khỏi dòng đơn |
| `extra_json` → `rawDetails` | ~2,2 MB | dòng gốc của file nhập (địa chỉ giao, tên đăng nhập người mua, lý do hoàn, mã khiếu nại...) | **Chỉ giữ vài trường cần cho nghiệp vụ** (bên dưới) |
| `extra_json` → giá/chế độ nhập (`priceModes`...) | ~0,7 MB | cách tính giá khi nhập | Chỉ giữ giá cuối cùng và cờ cần xem lại |
| `photos.data` | ~52 MB (2.230 ảnh) | ảnh trong CSDL | **Chuyển sang R2** |
- Trường trong `rawDetails` **được giữ** vì giao diện/cảnh báo dùng: `returnSuccessTime` (giờ hàng về), `returnStatus`, `returnShippingStatus`, `cancelType`, `returnReason`, mã/hãng vận chuyển giao đi và hoàn.
- **Không đưa lên:** `deliveryAddress`, `buyerUsername`, `buyerReturnNote`, `buyerOrderNote`, tên/SĐT/địa chỉ nếu có, `paymentMethod`, `claimId`... (dữ liệu người mua).
- Bảng: `orders` (một dòng = một KIỆN), `order_items`, `scan_events`, `audit_log` (lịch sử sửa, không ghi đè), `cell_edit_history`, `return_watch`, `watchlist`, `photos` (chỉ tham chiếu R2), `devices`, `order_index` (chỉ mục kho lưu trữ).
- Mỗi dòng có `shop_id`, `rev` (số thứ tự tăng dần để đồng bộ phần thay đổi), `updated_at`, `deleted`, `archived`.
- Dòng gọn ước ~1–2 KB: 4.962 đơn ≈ 5–10 MB. (Ước tính, cần đo lại bằng công cụ nhập thử.)

### 3.2 API
| Việc | Cách |
|---|---|
| Lấy thay đổi | `GET /sync?since=<rev>` trả mọi dòng đổi từ `rev` đó, có phân trang |
| Gửi thao tác | `POST /ops` (mỗi thao tác có `client_op_id`) |
| Quét nhận kiện | `POST /scan` — kiểm tra và cập nhật **một thao tác nguyên tử** |
| Nạp đơn | `POST /import` — upsert theo khóa kiện (nhập lại cùng kiện thì cập nhật, không cộng lặp) |
| Ảnh | điện thoại `PUT /photos/<id>` lên R2; máy tính và điện thoại tải bản thu nhỏ để xem |
| Thiết bị | ghép bằng mã QR, thu hồi từng máy |

### 3.3 Quy tắc nghiệp vụ chạy ở server (BẤT BIẾN, không đổi)
1. Một mã đơn có nhiều kiện A/B/C, mỗi kiện một mã vận đơn, một dòng, trạng thái/ảnh/ngày nhận riêng.
2. Nhận kiện A **không** nhận B/C. Cùng mã đơn hoặc mã yêu cầu hoàn **không đủ** để gộp.
3. Mã giao đi dùng chung hoặc mã đơn gốc có nhiều ứng viên → **ambiguous**, yêu cầu kiểm tra.
4. Chỉ gộp mã thay thế khi có liên kết tường minh; giữ mã lịch sử.
5. Nhập lại cùng kiện → cập nhật, không cộng lặp sản phẩm.
6. Chỉ ACK sau khi server đã ghi bền; ảnh chưa ACK không coi là đã lưu.
7. Cảnh báo vàng sau 12 giờ, đỏ sau 24 giờ kể từ lúc hàng về (máy khách tự tính).
8. Không xóa/ghi đè dữ liệu cũ; mọi sửa đổi ghi vào `audit_log`.

### 3.4 Nhiều máy cùng lúc (khi đều có mạng)
Quét nhận là câu lệnh có điều kiện (`UPDATE ... WHERE trạng thái = 'Đang chờ'`). Người đến sau nhận "đã nhận trước đó lúc … bởi máy …". Sửa ghi chú/trạng thái khác: người ghi sau thắng nhưng bản cũ luôn còn trong `audit_log`.

### 3.5 Thời gian thực
Mỗi cửa hàng có một Durable Object giữ WebSocket; có thay đổi thì phát `{rev}`, máy khách gọi `/sync` lấy phần mới. Mất kết nối thì chuyển trạng thái MẤT KẾT NỐI; nối lại thì tự lấy phần còn thiếu rồi mở khóa.

### 3.6 Nạp Google Sheet ngay trên cloud
Cron của Worker gọi Apps Script mỗi vài phút (token là **Worker secret**), chạy bộ gộp rồi upsert vào D1. Đây là việc phải **viết lại bằng TypeScript**: nhận dạng tab (`hanghoan/trahang/huy/vandon`), gộp kiện, điền mã vận đơn từ tab ẩn "Vận đơn huỷ" (không ghi đè mã sẵn có), bỏ qua tab thiếu. Dùng bộ ca thử chung với C# (mục 6).

### 3.6b Giảm tải đọc/ghi khi nạp Google Sheet (đề xuất, xếp từ hiệu quả nhất)
1. **Chỉ lấy phần thay đổi, không lấy cả Sheet.** Apps Script (phía Sheet của bạn) lọc theo cột "Cập nhật" và trả chỉ các dòng mới/đổi từ mốc lần trước (trừ biên an toàn vài phút). Worker không phải đọc/so cả 5.000 kiện.
2. **Ngay tại Apps Script kiểm "chữ ký" Sheet** (số dòng + thời điểm sửa gần nhất, lưu bằng `PropertiesService`): không đổi thì trả "không đổi" gần như không tốn gì; Worker dừng ngay, **không ghi D1**.
3. **Chỉ ghi kiện thật sự khác.** Bảng băm từng kiện lưu thành **một tệp nhỏ trên R2** (không phải 5.000 hàng D1): mỗi vòng đọc 1 tệp, so trong bộ nhớ, rồi chỉ upsert các kiện khác; ghi lại tệp băm khi có thay đổi. Như vậy bước so sánh tốn **0 hàng đọc D1**.
4. **Đối soát đầy đủ mỗi ngày một lần** (ban đêm) để bắt dòng sửa mà không cập nhật cột "Cập nhật". Tốn khoảng một lượt đọc cả Sheet nhưng chỉ ghi phần khác biệt.
5. **Chu kỳ theo giờ:** ban ngày (ví dụ 8h–22h) mỗi 3–5 phút, ban đêm 30–60 phút; có nút "Đồng bộ ngay".
6. **Một lệnh gọi lấy mọi tab** (kể cả tab ẩn "Vận đơn huỷ") thay vì mỗi tab một lệnh.
7. **Ít chỉ mục D1 hơn:** mỗi chỉ mục làm mỗi lần ghi tốn thêm hàng ghi; chỉ giữ chỉ mục thật sự cần.
8. **Gộp nhiều câu lệnh ghi vào một lô** (batch) và không tăng `rev` cho kiện không đổi.
Phương án thay thế: Apps Script **chủ động đẩy** (trigger theo thời gian, đẩy chỉ khi chữ ký đổi) thay vì Worker kéo; trigger `onEdit` không bắt được thay đổi do script/nhập tự động nên chỉ nên dùng trigger theo thời gian.
Ước tính: nếu mỗi ngày Sheet đổi khoảng 100–300 kiện thì ~1.000–3.000 hàng ghi/ngày thay vì ~2,4 triệu (cách ngây thơ), và gần như 0 hàng đọc cho bước so sánh. Cần đo lại bằng tốc độ đổi thật của Sheet.

### 3.7 Dung lượng
- **R2 10 GB (ảnh):** ảnh mới thu nhỏ cạnh dài ~1000 px, JPEG ~60% ≈ 80–120 KB. 500 lượt quét/ngày ≈ 40–60 MB/ngày → khoảng 6 tháng đầy. Mặc định **tự xóa ảnh sau 90 ngày** (chỉnh được) và tối đa 5 ảnh/kiện. 2.230 ảnh cũ ≈ 52 MB chuyển sang R2 không đáng kể.
- **D1 500 MB (dữ liệu):** chật hơn R2. Dòng gọn 1–2 KB cộng chỉ mục thì chạm 500 MB khi khoảng **150.000–250.000 kiện** (ước tính thô). Cách xử lý: đơn ở Kho lưu trữ lâu (mặc định > 12 tháng) được **nén thành tệp theo tháng trên R2**, trong D1 chỉ giữ `order_index` (mã đơn, mã vận đơn, sàn, tháng, vài chục byte/dòng) để vẫn tìm được và mở lại. Chưa cần làm ngay, thiết kế sẵn để không phải đổi lược đồ sau.

### 3.7b Ngân sách hạn mức (theo số liệu chủ dự án cung cấp)
Hạn mức: **D1** 500 MB, 5 triệu hàng đọc/ngày, 100.000 hàng ghi/ngày. **R2** 10 GB, 1 triệu lượt ghi/tháng, 10 triệu lượt đọc/tháng, tải ra miễn phí.
Giả định để ước tính: ~5.000 kiện đang dùng, 500 lượt quét/ngày, 12 thiết bị. (Ước tính, sẽ đo lại khi chạy thử.)

| Việc | Ước tính | So với hạn mức |
|---|---|---|
| Quét 500 kiện/ngày (mỗi lượt ghi kiện + sự kiện quét + nhật ký + chỉ mục ≈ 10 hàng) | ~5.000 hàng ghi/ngày | ~5% của 100.000 |
| Sửa tay, đổi trạng thái, ghi chú | vài nghìn hàng ghi/ngày | thoải mái |
| Máy khởi động lấy toàn bộ danh sách (12 máy × 5.000 kiện) | ~60.000 hàng đọc/lần mở hết | ~1% của 5 triệu |
| Lấy phần thay đổi (theo `rev`, có chỉ mục) | rất nhỏ | không đáng kể |
| Ảnh: 500 lượt quét/ngày × (ảnh gốc + thu nhỏ) | ~30.000 lượt ghi R2/tháng | ~3% của 1 triệu |
| Xem ảnh (có lưu tạm trên máy + tiêu đề lưu tạm) | vài trăm nghìn lượt đọc/tháng | ~vài % của 10 triệu |

**Hai chỗ DỄ VƯỢT hạn mức nếu làm ẩu — thiết kế phải chặn:**
1. **Nạp Google Sheet bằng Cron.** Nếu mỗi vòng ghi lại toàn bộ 5.000 kiện, cứ 3 phút một lần = 5.000 × 480 = 2,4 triệu hàng ghi/ngày (vượt 24 lần). Bắt buộc: (a) so **chữ ký** nội dung Sheet, không đổi thì không làm gì; (b) khi đổi, chỉ upsert **dòng thật sự khác** (so băm từng kiện); (c) chu kỳ mặc định 5–10 phút, chỉnh được.
2. **Truy vấn không có chỉ mục** đọc quét cả bảng: mỗi lần tính cả 5.000 hàng. Mọi truy vấn của API phải dùng chỉ mục (theo `rev`, mã đơn, mã vận đơn, trạng thái), có test kiểm tra kế hoạch truy vấn (`EXPLAIN`) và đếm hàng đọc.
Quy tắc khác: máy khách **không hỏi vòng** (polling), chỉ nhận đẩy qua WebSocket rồi mới lấy phần mới; ảnh gửi kèm tiêu đề lưu tạm lâu; định kỳ dọn ảnh quá hạn.

**Khi chạm hạn mức ghi/ngày của D1** thì mọi thao tác ghi (cả quét) sẽ lỗi cho tới khi hạn mức làm mới (hằng ngày, theo giờ UTC). Cần: đo hạn mức đã dùng ngay trong app (hiện thanh "Hạn mức hôm nay: xx%"), cảnh báo khi quá 70%, và thông báo lỗi rõ ràng. Nếu thực tế gần chạm thì nâng gói trả phí là lối thoát.

### 3.7c Gói thực tế của chủ dự án (đã xác nhận)
Tất cả đều gói **Miễn phí, hóa đơn $0**: Workers 100.000 yêu cầu/ngày (tối đa 1.000 yêu cầu/phút, **10 ms CPU mỗi yêu cầu**); D1 500 MB, 5 triệu hàng đọc + 100.000 hàng ghi/ngày; Durable Object 100.000 yêu cầu/ngày, **5 GB SQLite**; R2 10 GB (đã gắn thẻ, vượt 10 GB tính $0,015/GB/tháng). Chạm trần Workers/D1 thì hệ thống **tạm dừng tới hôm sau, không tự trừ tiền**.

Hệ quả thiết kế:
1. **Trần 100.000 yêu cầu/ngày dùng chung cho mọi máy** và khi chạm là cả cửa hàng ngừng quét tới hôm sau. Mục tiêu: dùng thường xuyên **dưới 30%** (≤ 30.000/ngày). Ước tính: 500 lượt quét × (1 lệnh quét + 12 máy lấy phần mới) ≈ 6.500; mở app/tải danh sách vài nghìn; ảnh là phần dễ tốn nhất. Ảnh **tải lên và xem qua đường dẫn ký sẵn (presigned URL) thẳng với R2** để không tính vào yêu cầu Worker; thông báo qua WebSocket của Durable Object (tính 1/20 yêu cầu mỗi tin).
2. **10 ms CPU mỗi yêu cầu rất chật.** Không thể gộp/phân tích cả Sheet 5.000 dòng trong Worker. Vì vậy phần nặng (nhận dạng tab, gộp, so băm, lọc phần đổi) chạy ở **Apps Script phía Google** (miễn phí, thời gian chạy dài); Worker chỉ nhận lô nhỏ đã gộp sẵn và ghi (chờ I/O không tính CPU). Mô hình **Apps Script chủ động đẩy** (mục 3.6b) trở thành lựa chọn chính, không còn là phương án thay thế. Logic gộp Sheet chuyển sang JavaScript chạy trong Apps Script (kiểm chứng bằng bộ ca thử chung).
3. **Có thể dùng SQLite của Durable Object (5 GB) làm kho chính thay D1 (500 MB):** một Durable Object cho cả cửa hàng xử lý ghi tuần tự nên quét nhận "một thao tác nguyên tử" tự nhiên và hết lo trần 500 MB. Đánh đổi: khó xem/sửa trực tiếp hơn D1 và cần chức năng xuất sao lưu. **Quyết định chốt ở đầu giai đoạn 1 sau một lần thử nhanh cả hai** (D1 hay Durable Object SQLite); lược đồ và API viết sao cho đổi được kho phía sau.
4. Hiển thị thanh "Hạn mức hôm nay" cho yêu cầu Worker và hàng ghi D1; cảnh báo ở 50% và 70%.
5. Workers AI (10.000 neurons/ngày) không dùng; trợ lý AI vẫn dùng khóa Groq/Gemini của bạn trên từng máy.

### 3.8 Bảo mật
HTTPS; mỗi thiết bị một token riêng (lưu băm), vai trò chủ cửa hàng / nhân viên (điện thoại vẫn là ứng dụng đầy đủ), thu hồi từng máy; mã ghép QR sống 10 phút dùng một lần; khóa bí mật ở Worker secrets. Không mã hóa đầu cuối (server cần đọc mã để khớp/tìm), bù lại không lưu dữ liệu cá nhân người nhận.

## 4. Máy tính (WPF)
**Giữ nguyên:** toàn bộ màn hình, bảng, bộ lọc, thông báo, tìm kiếm, thống kê, xuất file, trợ lý AI.
**Đổi:**
1. Lớp dữ liệu: cài đặt mới "cloud + bản lưu tạm chỉ đọc" cho các giao diện trừu tượng (`IReturnOrderProvider`, `IArchivedOrderProvider`, `IScanService`...).
2. Chấm trạng thái kết nối (mục 2); mất mạng thì khóa mọi nút ghi.
3. **Máy tính chỉ xem ảnh**, không chụp/đưa ảnh lên.
4. Bộ nạp thư mục/Excel/Salework giữ cách đọc, đầu ra gửi `/import` (cần mạng). Hai máy cùng nạp một file không gây trùng.
5. Tắt đồng bộ MQTT/ntfy (ntfy giữ tạm cho thông báo đẩy điện thoại).

## 5. Điện thoại (SwiftUI) — ứng dụng đầy đủ
**Giữ nguyên:** các tab (Tổng quan, Quét, Đơn, Lịch sử, Cài đặt), chi tiết đơn, trợ lý AI.
**Đổi:**
1. `SyncEngine` + MQTT/ntfy → máy khách cloud (HTTP + WebSocket); bỏ hàng đợi quét ngoại tuyến.
2. Quét → `POST /scan`; chụp ảnh gắn với kết quả **server xác nhận đúng một kiện** rồi mới tải lên R2; quét sai/không tìm thấy/mơ hồ thì bỏ ảnh.
3. Tab Đơn/chi tiết hiển thị ảnh của mọi máy (bản thu nhỏ từ R2), có lưu tạm trên máy để cuộn mượt.
4. Màn Đồng bộ đổi từ mã phòng sang "ghép thiết bị bằng QR" + trạng thái kết nối.
5. Thông báo khi app đóng: tạm dùng ntfy; về sau Apple Push khi có tài khoản Apple Developer.

## 6. Kiểm chứng (giữ AGENTS.md: không tuyên bố PASS cho thứ chưa chạy thật)
- Bộ ca thử **dùng chung** xuất từ test C# hiện có (khớp mã, ambiguous, kiện A/B/C, nhập lại cùng kiện, gộp Sheet, tab Vận đơn huỷ): C# và TypeScript cùng chạy phải ra cùng kết quả.
- Worker kiểm thử cục bộ bằng giả lập (không cần tài khoản); thử thật cần tài khoản của bạn.
- Test: hai máy quét cùng kiện, gửi lặp thao tác, mất mạng giữa chừng, khóa ghi khi mất kết nối.
- Điện thoại: chỉ kiểm tra biên dịch + `swift test` trên GitHub Actions; chạy thật trên máy do bạn xác nhận.

## 7. Chuyển đổi dữ liệu
1. Công cụ nhập dữ liệu cũ lên cloud có **chế độ chạy thử** (đếm, báo lỗi, không ghi) và báo cáo so khớp: số kiện, số ảnh, trường bị bỏ.
2. Trước khi bỏ `photoHistory` khỏi dòng đơn, công cụ kiểm tra ảnh đó **đã có trong bảng `photos`** hay là ảnh duy nhất; ảnh duy nhất được đưa lên R2 chứ không bỏ (quy tắc 8: không xóa dữ liệu cũ).
3. Bản SQLite cục bộ giữ nguyên làm bản lưu để quay lại.
4. Chạy song song bản cũ và bản cloud một thời gian rồi mới chuyển hẳn.

## 8. Các giai đoạn và cổng nghiệm thu
| GĐ | Nội dung | Xong khi |
|---|---|---|
| 1 | Lược đồ D1, API, đăng nhập thiết bị, quy tắc quét, **bộ gộp Google Sheet bằng TypeScript + Cron**, bộ ca thử chung, công cụ nhập dữ liệu cũ (chạy thử) | Bộ ca thử đạt ở C# và TypeScript; nhập thử khớp số liệu; Sheet nạp được trên môi trường thử |
| 2 | Máy tính dùng cloud (bản lưu tạm chỉ đọc, khóa khi mất mạng), chạy song song bản cũ | Hai máy tính cùng dùng, quét trùng xử lý đúng, mất mạng thì khóa ghi, có mạng thì mở khóa |
| 3 | iPhone dùng cloud, ảnh lên R2 | `swift test` + build đạt; bạn thử trên điện thoại thật |
| 4 | Tắt MQTT/ntfy cũ, đóng gói bản chạy thật | Bạn xác nhận |

Khối lượng lớn (nhiều tuần). Mỗi giai đoạn nộp riêng để bạn kiểm tra trước khi sang giai đoạn tiếp.

## 9. Rủi ro đã biết
- Logic nghiệp vụ có **hai bản cài** (C# và TypeScript), nay gồm cả bộ gộp Sheet: rủi ro lệch → bộ ca thử chung.
- Mất mạng thì cả cửa hàng không quét được (đã chấp nhận theo yêu cầu); Cloudflare lỗi cũng vậy.
- D1 500 MB sẽ đầy khi dữ liệu rất lớn → chuyển đơn cũ sang R2 (3.7).
- Ảnh có thể đầy 10 GB nếu không giới hạn → thu nhỏ + tự xóa sau N ngày.
- Không có mã hóa đầu cuối.

## 10. Việc còn cần chủ dự án
1. **Duyệt** bản 2 này (hoặc nêu chỗ cần sửa).
2. Trả lời: một hay nhiều cửa hàng; giữ ảnh bao nhiêu ngày (mặc định 90).
3. Khi tới lúc triển khai: tạo tài khoản Cloudflare và chạy `! npx wrangler login` (tôi không tự đăng nhập hộ).
