# Tự nhập đơn từ sàn (v2) - kế hoạch và đặc tả rút từ app cũ (2026-11-23)

Chủ dự án CHỐT dùng 3 nguồn: **theo dõi thư mục**, **Google Sheet**, **Salework tự động**. (Nạp Excel thủ công bằng tay là phần chung của cả ba.)
Cả ba cùng đổ vào MỘT đường nạp: đọc bảng -> ánh xạ thành `Order` -> `ImportApplier` (đã có: cập nhật không cộng lặp, A/B/C riêng, đơn đã xóa không nạp lại).

## Đã có
- `Data/Import/SpreadsheetReader`: đọc xlsx/csv thành bảng, tự tìm dòng tiêu đề, giữ mã dài dạng chuỗi, ô chữ không khai báo kiểu (Salework) coi là chữ, ô ngày -> "yyyy-MM-dd HH:mm:ss", bỏ cột thông tin cá nhân (tên, SĐT, địa chỉ...). 15 test.
- `Data/ImportApplier`, `Core/Import/ParcelUpsert`.

## 1. Theo dõi thư mục (app cũ: v1.9.34, mã tại `Bang_Excel...html` dòng 18633-18740)
- Chỉ nạp file **TikTok** (file xuất từ TikTok Seller Center: đơn hoàn / đơn hủy) do người dùng tải vào một thư mục. Nhận diện: 5 dòng đầu có một trong các cột `returnorderid, returnlogisticstrackingid, returnreason, cancelationreturntype, skuquantityofreturn, cancelreason, rtstime, ordersubstatus` (đã chuẩn hóa bỏ dấu, chỉ a-z0-9). File không phải TikTok bị bỏ qua (đánh dấu đã thấy).
- Kiểm tra mỗi **15 giây**. Khóa "đã thấy" = `tên|dung lượng|mtime làm tròn`; giữ tối đa 3.000 khóa gần nhất.
- Chỉ nạp file MỚI: file có sẵn trước lúc bật (mtime < thời điểm bật) bị bỏ qua, trừ khi bấm tay "Nạp các file đang có".
- Chờ tải xong: dung lượng phải giữ nguyên qua 2 lần kiểm tra liên tiếp VÀ file đã cũ hơn 3 giây.
- KHÔNG xóa / sửa / di chuyển file nào trong thư mục.
- Đang có việc nạp khác thì để lần sau. Nạp xong: ghi nhật ký "TỰ NẠP FILE TIKTOK", thông báo, rồi gộp dòng trùng mã đơn (`autoRemoveShadowDuplicates`).
- Hỗ trợ `.csv` và Excel. Đọc cả lô trước rồi mới ghi: file hỏng không để lại lô dở.

## 2. Google Sheet (app cũ: dòng 18178-18630; Apps Script riêng của người dùng)
- Người dùng tạo một dự án Apps Script (script do app sinh ra, `sheetSyncScriptText`), dán URL `https://script.google.com/...` (chỉ chấp nhận host Google; bắt buộc https). App gọi `fetchSheetText(url + [&tab=...])` mỗi **10 phút**, có băm nội dung để bỏ qua khi không đổi, có `skipTypes` (mặc định "Huỷ trước chuẩn bị"), nhiều tab, `mergeSheetParcels` gộp kiện. Còn phải đọc kỹ: `parseSheetItemsCell`, `mergeSheetParcels`, `runSheetSync` (18196-18505) và test cũ `tests/sheet-merge.test.cjs`, `tests/sheet-sync.test.cjs`, `tests/sheet-type-label.test.cjs`.
- Rủi ro cần quyết: URL Apps Script là "bí mật" (ai có URL đọc được dữ liệu sheet): lưu bằng mã hóa của Windows (DPAPI), không ghi log, không đưa vào báo cáo.

## 3. Salework tự động (app cũ: `salework_auto.js` 25 KB, `salework_xlsx.js`, `salework_diag.js`)
- Cửa sổ ẩn đăng nhập Salework bằng phiên đã lưu, vào tab **"Đang chờ hoàn"**, tự bấm **"Xuất excel"**, đọc file tải về (xlsx, ô chữ không khai báo kiểu) rồi nạp như file TikTok/Salework. Có chế độ `watch` (lặp định kỳ), `login`, `save`, `forget`.
- v2: dùng WebView2 (thay cửa sổ Electron ẩn). Chức năng DỄ VỠ khi Salework đổi giao diện; coi là tiện lợi, không phải nguồn duy nhất.
- Bảo mật: lưu phiên đăng nhập qua DPAPI, KHÔNG lưu mật khẩu; cho "Quên phiên" xóa sạch. Việc tự bấm trên web bên thứ ba có thể trái điều khoản dịch vụ của họ (app cũ đã làm): chủ dự án đã biết và chọn dùng.
- Cần: `supersedeStaleRowsFromSalework` (dòng 18741+): đơn Salework "Đang chờ hoàn" là trạng thái mới nhất, thay dòng "chờ" cũ CHƯA có mã vận đơn của cùng mã đơn (chuyển dữ liệu kho sang dòng mới rồi gỡ dòng cũ; nhiều kiện thì để nguyên, không đoán). Test cũ: `tests/salework-supersede.test.cjs`, `tests/salework-auto.test.cjs`, `tests/tiktok-salework-status.test.cjs`.

## Bộ ánh xạ dùng chung (khối lượng lớn nhất)
- `ingestTikTokOnlyRecords` (dòng 5592-6306, ~700 dòng) và `ingestShopeeOnlyRecords`/`ingestShopeeRecords` (6307-~7265): ánh xạ cột -> kiện (đơn hoàn/hủy/giao không thành công/thất thoát, trạng thái, giá theo "chế độ giá" `priceModes`, `sheetType`, hoàn một phần...), nhận diện nhiều ứng viên (Ambiguous), `preparePlatformImportGroups` (khử dòng lặp, kế thừa mã cho dòng sản phẩm phụ). Cần chuyển thành `Core/Import/TikTokMapper`, `ShopeeMapper` có test đặc tả lấy từ test cũ (`tests/import-tiktok-189.test.cjs`, `import-general-189`, `parcel-import`...).

## Cách chuyển đúng: "đáp án chuẩn" lấy từ mã cũ (kiểm thử đối chiếu)
App cũ có test nạp hàm thẳng từ file HTML vào Node (`vm`). Tôi dùng cùng kỹ thuật làm công cụ `v2/tools/legacy-oracle/oracle.cjs`: chạy CHÍNH hàm nạp cũ (`ingestTikTokOnlyRecords`...) với dòng GIẢ rồi ghi kết quả ra `v2/tests/QuanLyHangHoan.Tests/Golden/*.golden.json`. Test C# nạp `*.cases.json` (cùng dòng giả), chạy bộ ánh xạ C#, và phải cho ĐÚNG kết quả trong `*.golden.json`. Đồng hồ được cố định (23/11/2026) để đáp án không đổi theo ngày chạy; chạy lại công cụ cho cùng kết quả (đã kiểm tra bằng băm tệp).
- Tạo lại đáp án: `cd v2\tools\legacy-oracle; node oracle.cjs tiktok ..\..\tests\QuanLyHangHoan.Tests\Golden\tiktok.cases.json ..\..\tests\QuanLyHangHoan.Tests\Golden\tiktok.golden.json` (cần Node; chỉ dữ liệu giả; không chạm dữ liệu thật).
- Hiện có 33 ca TikTok/Salework (số lượng hoàn, 4 chế độ giá, bộ lọc "chỉ đơn hoàn", dòng sản phẩm phụ, ranh giới file, mã dài/khoa học, định dạng ngày, hãng vận chuyển theo mã, Premium/màu/size, đặt trước, ghi chú khách, dịch lý do, hoàn một phần, cập nhật đơn có sẵn giữ trường kho, cùng đơn khác yêu cầu hoàn, điền mã vào dòng cũ chưa có mã).
- Chỗ ĐÃ BIẾT là khác với app cũ (có chủ ý, ghi lại để chủ dự án quyết): khi một dòng nhập khớp NHIỀU kiện, app cũ tạo thêm dòng mới (và dọn trùng sau), còn `ImportApplier` v2 BÁO "nhập nhằng" và không đổi gì (an toàn hơn theo quy tắc bất biến số 3).

## Kết quả đọc mã Google Sheet (app cũ v1.9.23-v1.9.73, dòng 18163-18506) - để khỏi đọc lại
**Google Sheet là nguồn SHOPEE** (không phải TikTok): dữ liệu đi qua `importParsedPlatformFiles(files, 'Shopee')` -> `ingestShopeeOnlyRecords`, nên phải chuyển bộ nạp SHOPEE (`ingestShopeeRecords`, dòng 6311-~7265) TRƯỚC; bộ nạp đó cũng phục vụ nạp Excel Shopee thủ công.
Quy trình `runSheetSync` (mỗi 10 phút, hoặc bấm tay): (1) với mỗi tab trong cấu hình, GET `<URL Apps Script>?token=<mã ngẫu nhiên 24 ký tự lưu cục bộ>&t=<giờ>[&tab=<tên tab>]` (chỉ cho phép https và host Google `script.google.com`/`*.googleusercontent.com`; kết quả là CSV; phản hồi bắt đầu bằng `{"error"` hoặc `<html` là lỗi: nhắc kiểm tra "Triển khai > Ai có quyền truy cập: Bất kỳ ai"); (2) băm toàn bộ nội dung, không đổi so với lần trước thì dừng (trừ khi bấm tay); (3) mỗi tab: đọc CSV, `keepLatestRequestPerOrder` (mỗi đơn chỉ giữ 1 yêu cầu: ưu tiên còn hiệu lực rồi mới nhất), nhận kiểu tab `sheetTabKind` (hanghoan = có cột Hàng + Mã đơn + Trạng thái về; trahang = Mã trả hàng + Mã đơn; huy = Mã đơn + Loại + Ai hủy/Hành trình), `sheetRowsToParcels` (cột "Hàng" dạng "TÊN · Màu, Size × SL; ..." tách `parseSheetItemsCell`; tab Huỷ bỏ các "Loại" trong `skipTypes`, mặc định "Huỷ trước chuẩn bị"); (4) `mergeSheetParcels` gộp 3 tab thành 1 kiện cho mỗi mã đơn (ô nhiều tab lấy giá trị MỚI NHẤT theo cột "Cập nhật"; loại = bom nếu bất kỳ tab nào nói bom; sản phẩm ưu tiên tab Hàng hoàn > Trả hàng > Huỷ); (5) lọc theo `maxDays` (số ngày hoặc ngày cố định dd/mm/yyyy); chỉ nạp đơn MỚI/ĐỔI (so chữ ký theo mã đơn, lưu lần trước); (6) `sheetParcelsToRows` đổi sang TÊN CỘT như file Excel Shopee ("Mã đơn hàng", "Mã vận đơn", "Mã số khiếu nại", "Lý do trả hàng/hoàn tiền", "Thời gian yêu cầu", "Thời gian giao hàng thành công", "Người bán cần phản hồi trước" = hạn 24h kể từ lúc hàng về, "Tổng số tiền hoàn trả", "Tên sản phẩm", "Tên phân loại hàng", "Số lượng", "Trạng thái Trả hàng/Hoàn tiền", "Lý do hủy"="Giao hàng thất bại" cho bom, "Trạng thái đơn hàng" cho hủy) rồi đưa vào bộ nạp Shopee; (7) sau nạp: `applySheetTypeLabels` (ghi chữ "Loại" của Sheet vào đơn) và `applyShopeeClaimDeadlines` (hạn khiếu nại = 24h sau "Về shop lúc", chỉ khi hạn đang trống/còn giá trị gốc của Sheet).
Test cũ làm đáp án/ca thử: `tests/sheet-merge.test.cjs`, `tests/sheet-sync.test.cjs`, `tests/sheet-type-label.test.cjs`.
Bảo mật: URL Apps Script + mã token là "chìa khóa" đọc sheet: lưu bằng DPAPI, không ghi log/báo cáo, không đưa vào phiếu. Mạng chặn/Google đổi giao diện: phải báo lỗi rõ, không im lặng.

## Thứ tự làm
1. [xong] SpreadsheetReader.  2. TikTokMapper (rows -> Order) + test đặc tả.  3. Dịch vụ theo dõi thư mục (+ màn hình cấu hình, giao Antigravity).  4. Nguồn Google Sheet (đọc kỹ 18178-18505 trước).  5. ShopeeMapper (Excel Shopee thủ công và Salework dùng chung phần lớn).  6. Salework tự động (WebView2) + supersede.
