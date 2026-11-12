# 1.8.18 – nhập dữ liệu sàn không còn quét lại toàn bộ bảng cho từng dòng

Đo trên dữ liệu giả lập (TikTok/Shopee):
- 2.000 dòng nhập mới: ~10–14 s → ~0,2 s. 5.000 dòng: ~0,5 s.
- 100 dòng nhập vào bảng đã có 20.000 đơn: ~12,7 s → ~0,5 s.
- `lookupShopeeDb` (dùng khi quét điện thoại): ~115 ms → ~0,05 ms.

Cách làm: dựng chỉ mục (mã vận đơn, mã đơn, mã yêu cầu hoàn) để thu hẹp ứng viên; `isSameReturnParcel` vẫn là bước quyết định cuối nên kết quả ghép đơn như cũ. Kết quả làm sạch mã vận đơn được ghi nhớ.
`tests/import-index-equivalence.test.cjs` chạy cả code mới và code cũ (tests/fixtures/import-reference-functions.txt) trên 300 bộ dữ liệu ngẫu nhiên và yêu cầu kết quả giống hệt.
