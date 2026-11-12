# 1.8.9 — Sửa nhập file và đóng gói Windows

Ngày 12/11/2026. Bản này tiếp nối f4dcabe/1.8.8 trên nhánh fix/return-parcels-sync, bao gồm các sửa kiện hoàn/đồng bộ trước đó, chưa tự merge main.

## Đối chiếu I01–I15

| Audit | Sửa | Kiểm tra |
|---|---|---|
| I01 | Ánh xạ giá theo tên/ý nghĩa cột, loại mã vận đơn khỏi đơn giá | import-general-189 |
| I02–I03 | Ưu tiên số lượng hoàn; 0 loại SKU không trả, rỗng mới fallback; parse 1.00 không nhân100 | import-general/tiktok/state-189 |
| I04–I05 | Phân biệt đơn giá×số lượng, tổng dòng, tổng đơn lặp; giữ các mode giá TikTok | import-general/tiktok-189, excel-regression |
| I06 | Dòng chờ chưa có mã hoàn được bổ sung khi cùng đơn/claim/mã giao duy nhất; hai mã hoàn đã có vẫn tách | import-state-189, parcel-import |
| I07 | TikTok giữ cả mã giao đi và mã hoàn, aliases | import-tiktok-189 |
| I08 | Giá0 hợp lệ cập nhật; thiếu cột giá không ghi đè tổng cũ bằng0 | import-state-189 |
| I09 | Lọc Refunded đúng; không suy ra hoàn chỉ từ header trống hoặc substring tra trong trạng thái thường | import-tiktok-189 |
| I10 | Cập nhật partial flag; ghi nhận cả SKU hoàn0 bất kể thứ tự | import-state-189 |
| I11 | Tách nhóm theo schema/file, không dùng một mapping cho mọi file | import-workflow-189 |
| I12 | Loại chồng lặp giữa file bằng multiset tối đa, giữ các dòng giống nhau hợp lệ trong một file | import-workflow-189 |
| I13 | Ranh giới claim/source file rõ, không cho continuation ghép nhầm | import-general/tiktok/workflow-189 |
| I14 | Commit DB/main được chờ; lỗi rollback và thông báo rõ, không báo thành công; chặn lưu nền publish batch đang dở | import-transaction-189 |
| I15 | Chọn sheet/header có dữ liệu đơn, bỏ Instructions; tính đúng offset sheet bắt đầu hàng3 | import-workflow/workbook-engine-189 |

## Cách nhập cập nhật

Lựa chọn dùng chung hai tab:

- **Nhập bổ sung** (mặc định): giữ SKU không có trong file mới; cập nhật SKU theo mã ổn định (SKU/sellerSKU/skuId), nếu không có mã thì theo đúng tên sản phẩm/màu/size. Không đoán sản phẩm đổi tên là cùng SKU.
- **Thay toàn bộ sản phẩm của từng kiện có trong file**: file được coi là snapshot đầy đủ cho những kiện đó; không xóa các kiện khác.

Đơn giá từng SKU được giữ để cộng lại khi bổ sung. Khi dữ liệu cũ không đủ đơn giá từng SKU, giữ tổng cũ và cảnh báo cần đối chiếu giá, không tự cộng đoán. Giá tổng cấp đơn được coi là snapshot số tiền do nguồn cung cấp. Nhập thiếu giá giữ giá cũ và cảnh báo kiểm tra. Dữ liệu lịch sử đã mất mã không được tự suy đoán phục hồi.

## Review độc lập

Bốn lỗi Important được review tái hiện và đã sửa với regression RED→GREEN:

1. Offset !ref làm đọc lệch tiêu đề khi sheet bắt đầu hàng3.
2. Materialize định danh continuation có thể làm sống lại dòng của đơn giao thành công đã bị lọc; nay kế thừa ngữ cảnh trạng thái.
3. Số lượng Excel dạng 1.00 bị nhân100; dùng parser số nguyên hợp lệ, từ chối âm/phân số/không hợp lệ.
4. Cờ hoàn một phần sai khi SKU hoàn0 xuất hiện trước SKU được trả.

Bổ sung kiểm thử nhập thiếu giá, tính lại tổng khi bổ sung SKU, sellerSKU đổi tên, lưu nền giữa batch và quét chen lúc nhập. Quét được trả lỗi giữ hàng đợi trong lúc batch đang commit, tránh rollback ghi đè cập nhật mới.

## Kết quả thực tế

- **PASS 144/144**, `XLSX_ENGINE_PATH=/tmp/sheetjs-compat/package/dist/xlsx.full.min.js npm test`, không skip.
- Có byte XLSX thật và UTF8 BOM CSV thật: Instructions trước Orders, tiêu đề lùi hàng, ID dài dạng text, dấu phẩy trong tên, sheet không hợp lệ. Engine thực chạy cục bộ **SheetJS 0.18.5** từ bản phân phối npm, không phải mock.
- **PASS toàn trang Chromium** với FileReader và lưu localStorage/IndexedDB thật: workbook Instructions + Orders bắt đầu hàng3, Quantity5.00, return1.00 và SKU return0 -> 1 kiện, 1 sản phẩm, giá100, partial=true, lưu rồi mới hiện thành công; không có page error. Chỉ bỏ khóa quyền thao tác cho test, không mock business/persistence.
- Cú pháp inline/Main/Preload, actionlint và diff check được chạy khi bàn giao.
- Tải trực tiếp engine sản xuất **0.20.1** trong môi trường này bị HTTP403. Workflow Windows bắt buộc tải đúng engine0.20.1, kiểm tra version và chạy toàn bộ suite trước build; kết quả này **NOT TESTED locally / chưa xác minh từ GitHub run**.
- Windows installer runtime, file Excel thực của shop, mã hóa CSV legacy, file mật khẩu/hỏng lớn, iPhone/4G: **NOT TESTED**. Không dùng kết quả compat0.18.5 để khẳng định tất cả hành vi0.20.1/thiết bị.

## Đóng gói

package/app version1.8.9; workflow windows-latest chạy npm install, syntax, tải engine0.20.1 + npm test, electron-builder --win --x64 -p never. Artifact: **QuanLyHangHoan-Windows-Installer-v1.8.9**, chứa installer.exe và mobile_scanner.html, lưu30 ngày. Nếu bước kiểm thử/build lỗi phải sửa trước tải, không coi source/lint PASS là đã cóexe.

GitHub API từng bị Forbidden. Trạng thái push/build và URL tải chỉ được xác nhận theo kết quả thực khi bàn giao. Không tự merge main hoặc deploy Brewpage. Sao lưu dữ liệu trước cài, cập nhật PC và mobile tương ứng nếu chưa cài1.8.8.
