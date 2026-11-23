# Cấu trúc dữ liệu cũ (phân tích 2026-11-23, bởi claude)

Nguồn gốc: `C:\Users\kimth\OneDrive\Documents\QuanLyHangHoan` (KHÔNG phải `Documents\` thường).
Đã sao chép 16 tệp JSON sang `C:\Users\kimth\Projects\_legacy-data-copy\2026-11-23\` (hash khớp). Mọi phân tích chạy trên BẢN SAO. Thư mục sao chép nằm NGOÀI repo, không commit.
Thư mục khác: `photos/` (1.865 tệp), `sao_luu_tu_dong/` (94 tệp), `khoi_phuc/` (1 tệp). Chưa phân tích.

## Quy tắc đọc quan trọng
Các tệp `phu_*.json` đều bọc dạng `{"v": "<chuỗi JSON>"}`: phải parse 2 lớp (lấy `v`, rồi parse chuỗi đó). Riêng `phu_shopee_excel_active_import_week_v2` và `phu_shopee_preferred_platform_workspace` bên trong là chuỗi thường, KHÔNG phải JSON (không parse, chỉ lưu nguyên văn).

## Bảng tổng hợp
| Tệp | Kiểu | Số lượng | Ghi chú |
|---|---|---|---|
| `du_lieu_hang_hoan.json` | list đơn chính | 2.307 | đọc trực tiếp (không bọc `v`) |
| `phu_shopee_archived_orders_v1` | list đơn lưu trữ | 2.195 | 69 MB; ảnh nhúng base64 (xem kết quả dry-run: 1.032 ảnh nhúng tổng cộng) |
| `phu_shopee_cancelled_orders_v1` | list đơn hủy | 66 | TÊN TRƯỜNG KHÁC đơn chính (xem dưới) |
| `phu_shopee_deleted_orders_v1` | list đơn đã xóa | 166 | như đơn chính + `deletedAt`, `deletedFrom` |
| `phu_shopee_excel_audit_log_v1` | list nhật ký | 200 | `{time,type,detail}` |
| `phu_ShopeeReturnCellEditHistory_v1` | obj lịch sử sửa ô | nhiều khóa | khóa dạng `"<số>:<trường>"` -> list `{time,label,oldVal,newVal,editType}` |
| `phu_deadline_notified_v1` | obj | ~60 | khóa `m:<orderId>\|<track>` -> chuỗi |
| `phu_shopee_app_notifications_v1` | list | 3 | `{id,type,title,detail,code,orderIdx,orderUid,time,timeFormatted}` |
| `khoi_phuc_don_da_nhan` | obj `{version,source,createdFor,rows[203]}` | 203 | mỗi dòng `{orderId,claimId,platform,track,fields}` |
| `phu_shopee_excel_cell_locks_v1` | obj | 0 | rỗng |
| `phu_shopee_excel_accountant_stock_v1` | obj | 0 | rỗng |
| `phu_shopee_new_orders_inbox_v1` | list | 0 | rỗng |
| `phu_shopee_excel_custom_week_tabs_v2` | list | 1 | |
| `salework_auto.json` | obj `{url,lastOk,lastNote}` | - | cấu hình, có thể chứa URL riêng: không đưa vào log/báo cáo |

## Đơn chính (`du_lieu_hang_hoan.json`, 2.307 dòng)
Trường có ở 100% dòng: `shipDate, returnDate, recvDate, platform, orderType, platformStatus, preOrder, orderId, track, altTrack, carrier, price(num), status, photo, note, buyerNote, items[], importWeek, importedAt, importedAtFormatted`.
Trường tùy chọn (số dòng có): `isPartialReturn` 2306, `returnTrack` 2101, `forwardTrack` 2100, `deadline` 1573, `forwardCarrier` 1573, `returnCarrier` 1573, `rawDetails{}` 1573, `priceNeedsReview` 1565, `trackingCodes[]` 1541, `priceHasValue` 1527, `importPriceBasis` 1013, `sheetType` 1013, `_notificationOrderUid` 863, `returnOrderId` 734, `priceModes{}` 733, `rwSince` 613, `importModeSlots{}` 552, `importSelectedPriceMode` 552, `phonePhotoCaptureTime/Id` + `photoId` 133, `isWatchlist/watchlistReason` 116, `scanAliases[]` 57, `returnedAt` 41, `photoHistory[]` 15, `userNote` 1.

## Đơn lưu trữ / đã xóa
Cùng bộ trường như đơn chính; lưu trữ thêm `archivedAt`; đã xóa thêm `deletedAt`, `deletedFrom`.

## Đơn hủy (66) - schema KHÁC
`id, orderId, track, returnTrack, platform, orderType, carrier(có thể null), shipDate, returnDate, deadline, recvDate, platStatus (không phải platformStatus), preorder (không phải preOrder), items[], received(bool), source, createdAt, photoRequests[]`; tùy chọn `photoId`, `price`.
=> Importer cần bộ ánh xạ riêng cho đơn hủy.

## Kết quả dry-run trên bản sao phiên mới nhất (2026-11-23, `LegacyDryRun.Analyze`, <1 giây)
Nguồn: `Projects\_legacy-data-copy\2026-11-23-phien-moi` (hash khớp thư mục gốc; giống hệt bản sao trước đó).
- Đọc/ánh xạ: chính 2.307, lưu trữ 2.195, đã xóa 166 = **4.668 bản ghi, 0 lỗi dòng**.
- Ảnh nhúng data-URI: 1.032; bản ghi có `photoId`: 2.191; nhật ký: 200 dòng.
- Sàn: 2.119 bản ghi ghi "TikTok Shop" được chuẩn hóa thành "TikTok" theo quyết định chủ dự án; JSON gốc vẫn giữ trong `RawJson`.
- Trùng khóa (sàn+mã đơn+mã vận đơn+mã yêu cầu hoàn): **3 nhóm / 6 bản ghi**: 2 nhóm trong chính kho lưu trữ, 1 nhóm trong đã xóa. **Không có trùng giữa đơn chính và lưu trữ.** Chưa nhập thật; chủ dự án cần xem 3 nhóm này (importer không tự gộp/xóa).
- Bỏ theo quyết định: `phu_shopee_cancelled_orders_v1` (bảng đơn hủy), `khoi_phuc_don_da_nhan`.
- Chưa có bộ nhập: deadline_notified, CellEditHistory, app_notifications, accountant_stock, audit_log (đã đếm nhưng chưa nhập), cell_locks, custom_week_tabs, new_orders_inbox, salework_auto. Hai tệp lưu nguyên văn (không phải JSON): active_import_week_v2, preferred_platform_workspace.
- Mã nguồn: `v2/src/QuanLyHangHoan.Data/Legacy/`, test: `v2/tests/QuanLyHangHoan.Tests/ImporterTests/`.

## Chạy thử nhập THẬT trên bản sao (2026-11-23, claude, build riêng ở thư mục tạm, chỉ đọc bản sao, đã xóa DB tạm sau khi chạy)
`LegacyImportService.Run` (sao chép + kiểm hash -> dry-run -> nhập -> đối chiếu), bản `phien-moi` + ảnh từ bản `anh-sao-luu`:
- Kết quả: **nhập 4.668 / đối chiếu DB 4.668 = khớp** (chính 2.307 hiện tại; lưu trữ 2.195; đã xóa 166); 0 lỗi dòng; 3 nhóm trùng khóa (giữ riêng); 1.032 ảnh nhúng; 2.191 photoId; 2.119 sàn đổi "TikTok Shop" -> "TikTok".
- Tổng thời gian ~7 giây (gồm chép 65 MB ảnh + hash); `LoadActive` 2.307 đơn ~240 ms.
- Chia bảng giao diện: Shopee Đơn hủy 1.033; TikTok (mọi loại) 734.
- Ảnh mất cả hai nguồn: **322** (khớp báo cáo T005, hai bên tính độc lập).
- Tự dời lưu trữ (>= 24h) trên bản chính: 0 đơn cần dời (khớp: app cũ đã tự dời).
- Chưa chạy: giao diện thật với dữ liệu thật (cần user mở app), nhập nhiều lần trên dữ liệu đã đổi.

## Quyết định của chủ dự án (2026-11-23)
- **Bỏ hẳn bảng đơn hủy** (`phu_shopee_cancelled_orders_v1`, 66 bản ghi): không nhập và không còn trong giao diện/mã importer. Đơn chính có `orderType = "Đơn hủy"` (1.033 đơn) vẫn nhập bình thường. Tệp được ghi vào `SkippedByDecision` của báo cáo.
- **Nhãn sàn ngắn "TikTok"** (app cũ ghi lẫn "TikTok Shop"): importer chuẩn hóa, giữ JSON gốc.
- **Tệp `khoi_phuc_don_da_nhan.json` bỏ**: app bản mới đã sửa lỗi mất file; nguồn dữ liệu là thư mục app đang chạy (phiên mới nhất). Mỗi lần nhập thật phải sao chép bản mới nhất rồi đọc từ bản sao.
- **Kho lưu trữ = cùng một đơn ở trạng thái "đã dời"** (đơn đã nhận về kho >= 24h tự dời khỏi bảng chính để bảng nhẹ; có thể khôi phục/xuất/xóa; thống kê vẫn tính). v2: một bảng đơn, thêm cờ lưu trữ + ngày lưu trữ, không tách bảng.

## Rủi ro / điều cần chủ dự án quyết hoặc kiểm tra
1. **Lịch sử sửa ô khóa theo chỉ số dòng** (`"290:status"`), không phải theo mã đơn. Chỉ số dòng có thể đã lệch sau khi sắp xếp/xóa. KHÔNG đoán ghép; nhập nguyên văn làm bản ghi lịch sử, đánh dấu "chưa liên kết" cho tới khi có cách xác định chắc chắn.
2. `myNote` vs `userNote` (T001): dữ liệu thật chỉ thấy `note` (đủ 100%) và `userNote` (1 dòng). Không thấy `myNote` ở dữ liệu này.
3. Ảnh: vừa có data-URI base64 nhúng trong JSON, vừa có `photoId` trỏ tới tệp trong `photos/`. Cần đối chiếu hai nguồn khi nhập; không xóa nguồn nào.
4. Cùng một đơn có thể nằm ở nhiều tệp (chính/lưu trữ/đã xóa): importer phải khử trùng lặp theo (platform, orderId, track), không gộp A/B/C (quy tắc bất biến số 1-2).
5. `khoi_phuc_don_da_nhan.json` (203 dòng) là tệp khôi phục tạo thủ công lúc 00:28: chưa rõ nên áp dụng hay chỉ để đối chiếu. Hỏi chủ dự án trước khi nhập.
