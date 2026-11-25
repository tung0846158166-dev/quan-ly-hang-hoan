# Thiết kế chuyển sang CLOUD (Cloudflare) — BẢN ĐỀ XUẤT CHỜ CHỦ DỰ ÁN DUYỆT

Trạng thái: **ĐỀ XUẤT, chưa làm gì.** Bản Electron cũ vẫn là chuẩn đối chiếu; bản v2 hiện tại (SQLite + MQTT/ntfy) vẫn chạy bình thường cho tới khi chủ dự án duyệt và kiểm tra bản cloud.

## 1. Vì sao đổi
- Chủ dự án có **2 máy tính + 10 điện thoại** dùng chung; không thể bắt máy tính luôn bật để làm trung tâm.
- Vì vậy **cloud là nguồn dữ liệu gốc**. Máy tính và điện thoại là máy khách, có bản lưu tạm để chạy nhanh và dùng khi mất mạng.

## 2. Tổng quan

```
   iPhone x10 (SwiftUI)                 Máy tính x2 (WPF)
   ┌──────────────────┐                ┌──────────────────────┐
   │ giao diện (giữ)  │                │ giao diện (giữ)      │
   │ bản lưu tạm      │                │ bản lưu tạm (SQLite) │
   │ hàng đợi gửi lên │                │ hàng đợi gửi lên     │
   └───────┬──────────┘                │ bộ nạp file/thư mục/ │
           │ HTTPS + WebSocket         │ Salework (giữ)       │
           │                           └──────────┬───────────┘
           └─────────────────┬────────────────────┘
                             ▼
                ┌────────────────────────────┐
                │  Cloudflare Worker (API)   │  xác thực thiết bị, quy tắc nghiệp vụ
                ├────────────────────────────┤
                │ D1  : đơn, kiện, nhật ký   │  nguồn dữ liệu gốc
                │ DO  : 1 đối tượng/cửa hàng │  báo thay đổi thời gian thực (WebSocket)
                │ R2  : ảnh quét, sao lưu    │  tệp lớn
                │ Cron: nạp Google Sheet     │  chạy khi không máy nào bật (giai đoạn sau)
                └────────────────────────────┘
```

## 3. Cloud (Worker + D1 + R2 + Durable Object)

### 3.1 Dữ liệu (D1)
- Bảng chính: `orders` (một dòng = một KIỆN), `order_items`, `scan_events`, `audit_log` (lịch sử sửa, không ghi đè), `watchlist`, `return_watch`, `notes`, `devices`, `photos`.
- Mỗi dòng có `shop_id` (sẵn sàng nhiều cửa hàng), `rev` (số thứ tự tăng dần theo cửa hàng, dùng để đồng bộ phần thay đổi), `updated_at`, `deleted`, `archived`.
- **KHÔNG lưu dữ liệu cá nhân người nhận** (tên, SĐT, địa chỉ) và **bỏ phần dữ liệu thô nặng** (`extra_json` ~41 MB ở bản hiện tại). Cloud chỉ giữ: mã đơn, mã vận đơn, sàn, hãng vận chuyển, sản phẩm/màu/size/số lượng, giá, ngày giờ, trạng thái, lý do, ghi chú, theo dõi. Ước tính ~1–3 KB mỗi kiện, 100.000 kiện ≈ 100–300 MB (cần đo lại bằng dữ liệu thật).

### 3.2 API chính
| Việc | Cách |
|---|---|
| Lấy thay đổi | `GET /sync?since=<rev>` trả mọi dòng đổi từ `rev` đó, có phân trang |
| Gửi thay đổi | `POST /ops` gửi lô thao tác, mỗi thao tác có `client_op_id` (gửi lại không bị làm đôi) |
| Quét nhận kiện | `POST /scan` — kiểm tra và cập nhật trong **một thao tác nguyên tử** |
| Nạp đơn | `POST /import` — upsert theo khóa kiện (nhập lại cùng kiện thì cập nhật, không cộng lặp) |
| Ảnh | `PUT /photos/<id>` lên R2; ảnh thu nhỏ khi tải |
| Thiết bị | ghép bằng mã QR, thu hồi từng máy |

### 3.3 Quy tắc nghiệp vụ chạy ở server (BẤT BIẾN, không đổi)
1. Một mã đơn có nhiều kiện A/B/C, mỗi kiện một mã vận đơn, một dòng, trạng thái/ảnh/ngày nhận riêng.
2. Nhận kiện A **không** nhận B/C. Cùng mã đơn hoặc mã yêu cầu hoàn **không đủ** để gộp.
3. Mã giao đi dùng chung hoặc mã đơn gốc có nhiều ứng viên → trả **ambiguous**, yêu cầu kiểm tra.
4. Chỉ gộp mã thay thế khi có liên kết tường minh; giữ mã lịch sử.
5. Nhập lại cùng kiện → cập nhật, không cộng lặp sản phẩm.
6. **Chỉ xác nhận thành công SAU khi cloud đã ghi bền** (ACK từ server). Máy khách ghi vào hàng đợi bền trước và hiện "chờ đồng bộ"; chỉ khi server xác nhận mới coi là xong. Ảnh chưa xác nhận không coi là đã lưu.
7. Cảnh báo vàng sau 12 giờ, đỏ sau 24 giờ kể từ lúc hàng về: máy khách tự tính từ giờ hàng về (không cần server).
8. Không xóa/ghi đè dữ liệu cũ; mọi sửa đổi ghi vào `audit_log`; không đoán để tách/gộp dữ liệu lịch sử.

### 3.4 Hai người quét cùng lúc
Quét nhận là câu lệnh có điều kiện (`UPDATE ... WHERE trạng thái = 'Đang chờ'`). Người đến sau nhận kết quả "đã nhận trước đó lúc … bởi …". Sửa ghi chú/trạng thái khác: người ghi sau thắng nhưng bản cũ luôn còn trong `audit_log`.

### 3.5 Thời gian thực
Mỗi cửa hàng có một Durable Object giữ WebSocket. Có thay đổi thì phát `{rev}`; máy khách nhận được thì gọi `/sync` lấy phần mới. Mất kết nối thì lúc nối lại tự lấy phần còn thiếu theo `rev`.

### 3.6 Bảo mật
- Chỉ HTTPS. Mỗi thiết bị một token riêng (lưu băm trong D1), vai trò **chủ cửa hàng / nhân viên / chỉ quét**; thu hồi từng máy được.
- Mã ghép nối QR sống 10 phút, dùng một lần.
- Khóa bí mật (token Apps Script, khóa AI nếu sau này đưa lên) đặt ở **Worker secrets**, không nằm trong mã hay máy khách.
- Không có mã hóa đầu cuối (server cần đọc mã để khớp và tìm kiếm); bù lại không lưu dữ liệu cá nhân của người nhận.

### 3.7 Dung lượng (gói có 10 GB)
- Dữ liệu đơn: vài trăm MB ở 100.000 kiện → thoải mái.
- **Ảnh là phần tốn nhất.** Đề xuất: thu nhỏ còn cạnh dài ~1000 px, JPEG chất lượng ~60% (≈ 80–120 KB/ảnh). Với 500 lượt quét/ngày ≈ 40–60 MB/ngày ≈ 10 GB sau khoảng 6 tháng. Đề xuất mặc định **tự xóa ảnh sau 90 ngày** (chỉnh được) và tối đa 5 ảnh/kiện như hiện tại. Cần xác nhận lại 10 GB của bạn là của dịch vụ nào (R2 hay D1) và các giới hạn lượt đọc/ghi theo bảng giá hiện hành của Cloudflare.

## 4. Máy tính (WPF)
**Giữ nguyên:** toàn bộ màn hình, bảng, bộ lọc, thông báo, tìm kiếm, thống kê, xuất file, trợ lý AI.
**Đổi:**
1. Lớp dữ liệu: các giao diện trừu tượng hiện có (`IReturnOrderProvider`, `IArchivedOrderProvider`, `IScanService`…) có thêm cài đặt mới "cloud + bản lưu tạm". Giao diện không biết thay đổi.
2. Bản lưu tạm = SQLite cục bộ (như hiện nay) đóng vai bản sao + bảng `outbox` (hàng đợi gửi lên). Giao diện luôn đọc bản sao nên mở nhanh, dùng được khi mất mạng.
3. Thanh trạng thái hiển thị: "Đã đồng bộ" / "Đang đồng bộ" / "N thao tác chờ gửi" / "Mất mạng".
4. Bộ nạp (thư mục, Excel, Salework): giữ cách đọc, đầu ra gửi lên `/import`. Hai máy cùng nạp một file không gây trùng.
5. MQTT/ntfy bị thay bằng đồng bộ qua cloud (ntfy giữ lại tạm cho thông báo đẩy).
6. Google Sheet: giai đoạn đầu vẫn chạy ở máy tính và gửi lên `/import`; sau đó chuyển sang Cron của Worker (cần viết lại bộ gộp Sheet bằng TypeScript).

## 5. Điện thoại (SwiftUI)
**Giữ nguyên:** các tab, quét camera, chi tiết đơn, trợ lý AI.
**Đổi:**
1. `SyncEngine` + lớp chuyển tin MQTT/ntfy → máy khách cloud (HTTP + WebSocket), có bản lưu tạm và hàng đợi bền (đã có ý tưởng hàng đợi quét ngoại tuyến, mở rộng thành mọi thao tác).
2. Ảnh quét: gửi lên R2 ngầm sau khi quét được **server xác nhận đúng một kiện**; chưa xác nhận hoặc quét sai thì bỏ ảnh (giữ quy tắc "chỉ giữ ảnh khi xác nhận đúng một kiện"). Máy khác xem ảnh qua bản thu nhỏ tải từ R2.
3. Thông báo khi app đóng: tạm dùng ntfy; thay bằng Apple Push khi có tài khoản Apple Developer.
4. Màn "Đồng bộ" đổi từ mã phòng sang "ghép thiết bị bằng QR".

## 6. Kiểm chứng (giữ AGENTS.md: không tuyên bố PASS cho thứ chưa chạy thật)
- Bộ ca thử **dùng chung** (JSON xuất từ test C# hiện có: khớp mã, ambiguous, kiện A/B/C, nhập lại cùng kiện, cảnh báo): C# và TypeScript cùng chạy và phải ra cùng kết quả. Đây là cách chặn lệch quy tắc nghiệp vụ giữa hai bản cài.
- Worker kiểm thử cục bộ bằng giả lập (không cần tài khoản Cloudflare); thử thật cần tài khoản của bạn.
- Test ngoại tuyến, hai người quét cùng lúc, gửi lặp thao tác, mất mạng giữa chừng.
- Điện thoại: chỉ kiểm tra biên dịch/`swift test` trên GitHub Actions; chạy thật trên máy do bạn xác nhận.

## 7. Chuyển đổi dữ liệu
1. Công cụ nhập dữ liệu cũ lên cloud có **chế độ chạy thử** (đếm, báo lỗi, không ghi) và báo cáo so khớp số lượng.
2. Bản sao SQLite cục bộ được giữ nguyên để quay lại.
3. Chạy song song bản cũ và bản cloud một thời gian, so kết quả, rồi mới chuyển hẳn.

## 8. Các giai đoạn và cổng nghiệm thu
| GĐ | Nội dung | Xong khi |
|---|---|---|
| 1 | Lược đồ D1, API, đăng nhập thiết bị, bộ ca thử dùng chung, công cụ nhập dữ liệu cũ (chạy thử) | Bộ ca thử đạt ở cả C# và TypeScript; nhập thử khớp số liệu |
| 2 | Máy tính dùng cloud (bản lưu tạm + hàng đợi), chạy song song bản cũ | Hai máy tính cùng dùng, quét trùng được xử lý đúng, mất mạng rồi có mạng lại không mất dữ liệu |
| 3 | iPhone dùng cloud, ảnh lên R2 | `swift test` + build đạt; bạn thử trên điện thoại thật |
| 4 | Chuyển nạp Google Sheet sang Cron của Worker | Sheet tự nạp khi mọi máy tắt |
| 5 | Tắt MQTT/ntfy cũ, đóng gói bản chạy thật | Bạn xác nhận |

Đây là khối lượng lớn (nhiều tuần); mỗi giai đoạn nộp riêng để bạn kiểm tra trước khi sang giai đoạn tiếp.

## 9. Rủi ro đã biết
- Logic nghiệp vụ có **hai bản cài** (C# và TypeScript): rủi ro lệch quy tắc → giảm bằng bộ ca thử dùng chung.
- Ảnh có thể đầy 10 GB nếu không giới hạn → thu nhỏ + tự xóa sau N ngày.
- Mọi người cùng dùng nên cần xác nhận lại quyền hạn từng vai trò.
- Cloud là một điểm hỏng chung: mất mạng/Cloudflare lỗi thì máy khách vẫn quét được nhờ hàng đợi, nhưng không có xác nhận tức thì.
- Không có mã hóa đầu cuối; chỉ bảo vệ bằng việc không lưu dữ liệu cá nhân của người nhận.

## 10. Điều cần chủ dự án duyệt / trả lời
1. Cloud là nguồn gốc + máy khách có bản lưu tạm và hàng đợi ngoại tuyến: **duyệt?**
2. **Không** đưa tên/SĐT/địa chỉ người nhận lên cloud: **duyệt?** (Nếu cần xem lại thông tin này thì xem ở nguồn gốc như Salework/Sheet.)
3. Ảnh lên R2 (thay quyết định cũ "chỉ trên điện thoại"), thu nhỏ + tự xóa sau **N ngày** (đề xuất 90): **duyệt, N = ?**
4. 10 GB của bạn là dịch vụ nào của Cloudflare (R2 hay D1)? Một cửa hàng hay nhiều cửa hàng?
5. Vai trò thiết bị: chủ / nhân viên / chỉ quét — đủ chưa?
6. Google Sheet: giai đoạn đầu để máy tính nạp, đến GĐ4 mới chuyển lên Worker — **đồng ý?**
7. Việc bạn cần làm: tạo tài khoản Cloudflare và chạy `! npx wrangler login` khi tới lúc triển khai (tôi không tự đăng nhập hộ).
