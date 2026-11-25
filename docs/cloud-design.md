# Thiáº¿t káº¿ chuyá»ƒn sang CLOUD (Cloudflare) â€” Báº¢N 2, CHá»œ CHá»¦ Dá»° ÃN DUYá»†T

Tráº¡ng thÃ¡i: **Äá»€ XUáº¤T, chÆ°a lÃ m gÃ¬.** Báº£n Electron cÅ© váº«n lÃ  chuáº©n Ä‘á»‘i chiáº¿u; báº£n v2 hiá»‡n táº¡i (SQLite + MQTT/ntfy) cháº¡y bÃ¬nh thÆ°á»ng cho tá»›i khi chá»§ dá»± Ã¡n duyá»‡t vÃ  kiá»ƒm tra báº£n cloud.

## 0. Nhá»¯ng Ä‘iá»u chá»§ dá»± Ã¡n Ä‘Ã£ chá»‘t (báº£n 2)
1. **Máº¥t máº¡ng = KHÃ”NG lÃ m viá»‡c.** MÃ¡y hiá»‡n tráº¡ng thÃ¡i "Máº¥t káº¿t ná»‘i" vÃ  cháº·n quÃ©t/sá»­a; khÃ´ng cÃ³ hÃ ng Ä‘á»£i ngoáº¡i tuyáº¿n. (TrÃ¡nh nhiá»u mÃ¡y quÃ©t cÃ¹ng má»™t Ä‘Æ¡n lÃºc máº¥t máº¡ng rá»“i dá»“n lÃªn lá»‡ch.)
2. KhÃ´ng Ä‘Æ°a dá»¯ liá»‡u cÃ¡ nhÃ¢n ngÆ°á»i nháº­n lÃªn cloud; pháº§n dá»¯ liá»‡u thÃ´ chá»‰ giá»¯ cÃ¡c trÆ°á»ng cáº§n thiáº¿t (xem 3.1).
3. **Cáº£ Ä‘iá»‡n thoáº¡i vÃ  mÃ¡y tÃ­nh Ä‘á»u xem Ä‘Æ°á»£c áº£nh cá»§a Ä‘Æ¡n; chá»‰ Ä‘iá»‡n thoáº¡i má»›i chá»¥p/quÃ©t áº£nh.** áº¢nh lÆ°u á»Ÿ R2.
4. GÃ³i: **R2 10 GB, D1 500 MB** (xem 3.7: D1 lÃ  giá»›i háº¡n cháº­t nháº¥t).
5. **Äiá»‡n thoáº¡i lÃ  má»™t á»©ng dá»¥ng Ä‘á»™c láº­p Ä‘áº§y Ä‘á»§** (xem danh sÃ¡ch, chi tiáº¿t, thá»‘ng kÃª, AI, quÃ©t...), khÃ´ng chá»‰ Ä‘á»ƒ quÃ©t.
6. **Google Sheet náº¡p tháº³ng lÃªn cloud** (Cron cá»§a Worker). CÃ¡c cÃ¡ch náº¡p khÃ¡c (file Excel, thÆ° má»¥c, Salework) váº«n cháº¡y á»Ÿ mÃ¡y tÃ­nh.
7. **Má»˜T cá»­a hÃ ng duy nháº¥t, pháº§n má»m ná»™i bá»™ cho nhÃ¢n viÃªn dÃ¹ng** (khÃ´ng bÃ¡n/cho thuÃª, khÃ´ng cÃ³ Ä‘Äƒng kÃ½ cá»­a hÃ ng). `shop_id` chá»‰ lÃ  má»™t háº±ng sá»‘, khÃ´ng lÃ m pháº§n quáº£n lÃ½ nhiá»u cá»­a hÃ ng.
ChÆ°a tráº£ lá»i: sá»‘ ngÃ y giá»¯ áº£nh (máº·c Ä‘á»‹nh 90, xem 3.7).

## 1. Tá»•ng quan

```
   iPhone x10 (SwiftUI, á»©ng dá»¥ng Ä‘áº§y Ä‘á»§)     MÃ¡y tÃ­nh x2 (WPF)
   â”Œâ”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”          â”Œâ”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”
   â”‚ giao diá»‡n (giá»¯)            â”‚          â”‚ giao diá»‡n (giá»¯)           â”‚
   â”‚ báº£n lÆ°u táº¡m CHá»ˆ Äá»ŒC khi    â”‚          â”‚ báº£n lÆ°u táº¡m CHá»ˆ Äá»ŒC khi   â”‚
   â”‚ máº¥t máº¡ng; má»i GHI cáº§n máº¡ng â”‚          â”‚ máº¥t máº¡ng; má»i GHI cáº§n máº¡ngâ”‚
   â”‚ chá»¥p + gá»­i áº£nh             â”‚          â”‚ xem áº£nh; bá»™ náº¡p file/     â”‚
   â””â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”¬â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”˜          â”‚ thÆ° má»¥c/Salework (giá»¯)    â”‚
                â”‚ HTTPS + WebSocket        â””â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”¬â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”˜
                â””â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”¬â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”˜
                                â–¼
                 â”Œâ”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”
                 â”‚ Cloudflare Worker (API)     â”‚ xÃ¡c thá»±c thiáº¿t bá»‹ + quy táº¯c nghiá»‡p vá»¥
                 â”œâ”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”¤
                 â”‚ D1  (500 MB): Ä‘Æ¡n, kiá»‡n,    â”‚ nguá»“n dá»¯ liá»‡u gá»‘c, dÃ²ng Gá»ŒN
                 â”‚      nháº­t kÃ½, chá»‰ má»¥c       â”‚
                 â”‚ DO  : 1 Ä‘á»‘i tÆ°á»£ng/cá»­a hÃ ng  â”‚ bÃ¡o thay Ä‘á»•i (WebSocket)
                 â”‚ R2  (10 GB): áº£nh, kho lÆ°u   â”‚ tá»‡p lá»›n, báº£n lÆ°u trá»¯ cÅ©
                 â”‚ Cron: náº¡p Google Sheet      â”‚ cháº¡y khi má»i mÃ¡y táº¯t
                 â””â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”˜
```

## 2. Quy táº¯c "máº¥t máº¡ng = dá»«ng"
- MÃ¡y khÃ¡ch cÃ³ cháº¥m tráº¡ng thÃ¡i: **ÄÃ£ káº¿t ná»‘i / Äang Ä‘á»“ng bá»™ / Máº¤T Káº¾T Ná»I**.
- Khi Máº¤T Káº¾T Ná»I: quÃ©t, sá»­a, Ä‘á»•i tráº¡ng thÃ¡i, náº¡p, chá»¥p áº£nh Ä‘á»u bá»‹ **cháº·n** kÃ¨m thÃ´ng bÃ¡o rÃµ. Chá»‰ **xem** Ä‘Æ°á»£c dá»¯ liá»‡u Ä‘Ã£ táº£i láº§n cuá»‘i (nhÃ£n "Dá»¯ liá»‡u cÅ© lÃºc HH:mm, chá»‰ xem").
- Má»i thao tÃ¡c ghi chá»‰ coi lÃ  thÃ nh cÃ´ng khi **server tráº£ vá» thÃ nh cÃ´ng** (quy táº¯c báº¥t biáº¿n sá»‘ 6: chá»‰ ACK sau khi Ä‘Ã£ lÆ°u bá»n).
- Máº¡ng Ä‘á»©t Ä‘Ãºng lÃºc Ä‘ang gá»­i (khÃ´ng biáº¿t server Ä‘Ã£ nháº­n chÆ°a): mÃ¡y khÃ¡ch gá»­i láº¡i vá»›i **cÃ¹ng mÃ£ thao tÃ¡c** (`client_op_id`), server nháº­n trÃ¹ng thÃ¬ tráº£ láº¡i káº¿t quáº£ cÅ©, khÃ´ng lÃ m Ä‘Ã´i. Náº¿u khÃ´ng xÃ¡c nháº­n Ä‘Æ°á»£c thÃ¬ bÃ¡o "chÆ°a cháº¯c Ä‘Ã£ nháº­n, hÃ£y kiá»ƒm tra láº¡i Ä‘Æ¡n trÆ°á»›c khi quÃ©t láº¡i".
- Thay Ä‘á»•i so vá»›i hiá»‡n táº¡i: iPhone hiá»‡n cÃ³ hÃ ng Ä‘á»£i quÃ©t khi máº¥t máº¡ng; báº£n cloud **bá» hÃ ng Ä‘á»£i nÃ y** (Ä‘Ãºng yÃªu cáº§u).

## 3. Cloud

### 3.1 Dá»¯ liá»‡u vÃ  "dá»¯ liá»‡u thÃ´" (Ä‘o trÃªn dá»¯ liá»‡u tháº­t hiá»‡n táº¡i: 4.962 Ä‘Æ¡n, DB 180 MB)
Sá»‘ liá»‡u Ä‘o (chá»‰ tÃªn trÆ°á»ng vÃ  dung lÆ°á»£ng, khÃ´ng Ä‘á»c ná»™i dung cÃ¡ nhÃ¢n):
| Pháº§n | Dung lÆ°á»£ng | LÃ  gÃ¬ | LÃªn cloud? |
|---|---|---|---|
| `orders.raw_json` | ~76 MB | báº£n sao nguyÃªn vÄƒn cá»§a tá»«ng Ä‘Æ¡n tá»« app cÅ© | **KhÃ´ng** (giá»¯ á»Ÿ mÃ¡y cÅ©/báº£n sao lÆ°u) |
| `extra_json` â†’ `photoHistory` | ~37 MB (chá»‰ 328 Ä‘Æ¡n) | áº£nh cÅ© nhÃºng tháº³ng vÃ o Ä‘Æ¡n | **KhÃ´ng nhÃºng**; chuyá»ƒn áº£nh sang R2 rá»“i bá» khá»i dÃ²ng Ä‘Æ¡n |
| `extra_json` â†’ `rawDetails` | ~2,2 MB | dÃ²ng gá»‘c cá»§a file nháº­p (Ä‘á»‹a chá»‰ giao, tÃªn Ä‘Äƒng nháº­p ngÆ°á»i mua, lÃ½ do hoÃ n, mÃ£ khiáº¿u náº¡i...) | **Chá»‰ giá»¯ vÃ i trÆ°á»ng cáº§n cho nghiá»‡p vá»¥** (bÃªn dÆ°á»›i) |
| `extra_json` â†’ giÃ¡/cháº¿ Ä‘á»™ nháº­p (`priceModes`...) | ~0,7 MB | cÃ¡ch tÃ­nh giÃ¡ khi nháº­p | Chá»‰ giá»¯ giÃ¡ cuá»‘i cÃ¹ng vÃ  cá» cáº§n xem láº¡i |
| `photos.data` | ~52 MB (2.230 áº£nh) | áº£nh trong CSDL | **Chuyá»ƒn sang R2** |
- TrÆ°á»ng trong `rawDetails` **Ä‘Æ°á»£c giá»¯** vÃ¬ giao diá»‡n/cáº£nh bÃ¡o dÃ¹ng: `returnSuccessTime` (giá» hÃ ng vá»), `returnStatus`, `returnShippingStatus`, `cancelType`, `returnReason`, mÃ£/hÃ£ng váº­n chuyá»ƒn giao Ä‘i vÃ  hoÃ n.
- **KhÃ´ng Ä‘Æ°a lÃªn:** `deliveryAddress`, `buyerUsername`, `buyerReturnNote`, `buyerOrderNote`, tÃªn/SÄT/Ä‘á»‹a chá»‰ náº¿u cÃ³, `paymentMethod`, `claimId`... (dá»¯ liá»‡u ngÆ°á»i mua).
- Báº£ng: `orders` (má»™t dÃ²ng = má»™t KIá»†N), `order_items`, `scan_events`, `audit_log` (lá»‹ch sá»­ sá»­a, khÃ´ng ghi Ä‘Ã¨), `cell_edit_history`, `return_watch`, `watchlist`, `photos` (chá»‰ tham chiáº¿u R2), `devices`, `order_index` (chá»‰ má»¥c kho lÆ°u trá»¯).
- Má»—i dÃ²ng cÃ³ `shop_id`, `rev` (sá»‘ thá»© tá»± tÄƒng dáº§n Ä‘á»ƒ Ä‘á»“ng bá»™ pháº§n thay Ä‘á»•i), `updated_at`, `deleted`, `archived`.
- DÃ²ng gá»n Æ°á»›c ~1â€“2 KB: 4.962 Ä‘Æ¡n â‰ˆ 5â€“10 MB. (Æ¯á»›c tÃ­nh, cáº§n Ä‘o láº¡i báº±ng cÃ´ng cá»¥ nháº­p thá»­.)

### 3.2 API
| Viá»‡c | CÃ¡ch |
|---|---|
| Láº¥y thay Ä‘á»•i | `GET /sync?since=<rev>` tráº£ má»i dÃ²ng Ä‘á»•i tá»« `rev` Ä‘Ã³, cÃ³ phÃ¢n trang |
| Gá»­i thao tÃ¡c | `POST /ops` (má»—i thao tÃ¡c cÃ³ `client_op_id`) |
| QuÃ©t nháº­n kiá»‡n | `POST /scan` â€” kiá»ƒm tra vÃ  cáº­p nháº­t **má»™t thao tÃ¡c nguyÃªn tá»­** |
| Náº¡p Ä‘Æ¡n | `POST /import` â€” upsert theo khÃ³a kiá»‡n (nháº­p láº¡i cÃ¹ng kiá»‡n thÃ¬ cáº­p nháº­t, khÃ´ng cá»™ng láº·p) |
| áº¢nh | Ä‘iá»‡n thoáº¡i `PUT /photos/<id>` lÃªn R2; mÃ¡y tÃ­nh vÃ  Ä‘iá»‡n thoáº¡i táº£i báº£n thu nhá» Ä‘á»ƒ xem |
| Thiáº¿t bá»‹ | ghÃ©p báº±ng mÃ£ QR, thu há»“i tá»«ng mÃ¡y |

### 3.3 Quy táº¯c nghiá»‡p vá»¥ cháº¡y á»Ÿ server (Báº¤T BIáº¾N, khÃ´ng Ä‘á»•i)
1. Má»™t mÃ£ Ä‘Æ¡n cÃ³ nhiá»u kiá»‡n A/B/C, má»—i kiá»‡n má»™t mÃ£ váº­n Ä‘Æ¡n, má»™t dÃ²ng, tráº¡ng thÃ¡i/áº£nh/ngÃ y nháº­n riÃªng.
2. Nháº­n kiá»‡n A **khÃ´ng** nháº­n B/C. CÃ¹ng mÃ£ Ä‘Æ¡n hoáº·c mÃ£ yÃªu cáº§u hoÃ n **khÃ´ng Ä‘á»§** Ä‘á»ƒ gá»™p.
3. MÃ£ giao Ä‘i dÃ¹ng chung hoáº·c mÃ£ Ä‘Æ¡n gá»‘c cÃ³ nhiá»u á»©ng viÃªn â†’ **ambiguous**, yÃªu cáº§u kiá»ƒm tra.
4. Chá»‰ gá»™p mÃ£ thay tháº¿ khi cÃ³ liÃªn káº¿t tÆ°á»ng minh; giá»¯ mÃ£ lá»‹ch sá»­.
5. Nháº­p láº¡i cÃ¹ng kiá»‡n â†’ cáº­p nháº­t, khÃ´ng cá»™ng láº·p sáº£n pháº©m.
6. Chá»‰ ACK sau khi server Ä‘Ã£ ghi bá»n; áº£nh chÆ°a ACK khÃ´ng coi lÃ  Ä‘Ã£ lÆ°u.
7. Cáº£nh bÃ¡o vÃ ng sau 12 giá», Ä‘á» sau 24 giá» ká»ƒ tá»« lÃºc hÃ ng vá» (mÃ¡y khÃ¡ch tá»± tÃ­nh).
8. KhÃ´ng xÃ³a/ghi Ä‘Ã¨ dá»¯ liá»‡u cÅ©; má»i sá»­a Ä‘á»•i ghi vÃ o `audit_log`.

### 3.4 Nhiá»u mÃ¡y cÃ¹ng lÃºc (khi Ä‘á»u cÃ³ máº¡ng)
QuÃ©t nháº­n lÃ  cÃ¢u lá»‡nh cÃ³ Ä‘iá»u kiá»‡n (`UPDATE ... WHERE tráº¡ng thÃ¡i = 'Äang chá»'`). NgÆ°á»i Ä‘áº¿n sau nháº­n "Ä‘Ã£ nháº­n trÆ°á»›c Ä‘Ã³ lÃºc â€¦ bá»Ÿi mÃ¡y â€¦". Sá»­a ghi chÃº/tráº¡ng thÃ¡i khÃ¡c: ngÆ°á»i ghi sau tháº¯ng nhÆ°ng báº£n cÅ© luÃ´n cÃ²n trong `audit_log`.

### 3.5 Thá»i gian thá»±c
Má»—i cá»­a hÃ ng cÃ³ má»™t Durable Object giá»¯ WebSocket; cÃ³ thay Ä‘á»•i thÃ¬ phÃ¡t `{rev}`, mÃ¡y khÃ¡ch gá»i `/sync` láº¥y pháº§n má»›i. Máº¥t káº¿t ná»‘i thÃ¬ chuyá»ƒn tráº¡ng thÃ¡i Máº¤T Káº¾T Ná»I; ná»‘i láº¡i thÃ¬ tá»± láº¥y pháº§n cÃ²n thiáº¿u rá»“i má»Ÿ khÃ³a.

### 3.6 Náº¡p Google Sheet ngay trÃªn cloud
Cron cá»§a Worker gá»i Apps Script má»—i vÃ i phÃºt (token lÃ  **Worker secret**), cháº¡y bá»™ gá»™p rá»“i upsert vÃ o D1. ÄÃ¢y lÃ  viá»‡c pháº£i **viáº¿t láº¡i báº±ng TypeScript**: nháº­n dáº¡ng tab (`hanghoan/trahang/huy/vandon`), gá»™p kiá»‡n, Ä‘iá»n mÃ£ váº­n Ä‘Æ¡n tá»« tab áº©n "Váº­n Ä‘Æ¡n huá»·" (khÃ´ng ghi Ä‘Ã¨ mÃ£ sáºµn cÃ³), bá» qua tab thiáº¿u. DÃ¹ng bá»™ ca thá»­ chung vá»›i C# (má»¥c 6).

### 3.6b Giáº£m táº£i Ä‘á»c/ghi khi náº¡p Google Sheet (Ä‘á» xuáº¥t, xáº¿p tá»« hiá»‡u quáº£ nháº¥t)
1. **Chá»‰ láº¥y pháº§n thay Ä‘á»•i, khÃ´ng láº¥y cáº£ Sheet.** Apps Script (phÃ­a Sheet cá»§a báº¡n) lá»c theo cá»™t "Cáº­p nháº­t" vÃ  tráº£ chá»‰ cÃ¡c dÃ²ng má»›i/Ä‘á»•i tá»« má»‘c láº§n trÆ°á»›c (trá»« biÃªn an toÃ n vÃ i phÃºt). Worker khÃ´ng pháº£i Ä‘á»c/so cáº£ 5.000 kiá»‡n.
2. **Ngay táº¡i Apps Script kiá»ƒm "chá»¯ kÃ½" Sheet** (sá»‘ dÃ²ng + thá»i Ä‘iá»ƒm sá»­a gáº§n nháº¥t, lÆ°u báº±ng `PropertiesService`): khÃ´ng Ä‘á»•i thÃ¬ tráº£ "khÃ´ng Ä‘á»•i" gáº§n nhÆ° khÃ´ng tá»‘n gÃ¬; Worker dá»«ng ngay, **khÃ´ng ghi D1**.
3. **Chá»‰ ghi kiá»‡n tháº­t sá»± khÃ¡c.** Báº£ng bÄƒm tá»«ng kiá»‡n lÆ°u thÃ nh **má»™t tá»‡p nhá» trÃªn R2** (khÃ´ng pháº£i 5.000 hÃ ng D1): má»—i vÃ²ng Ä‘á»c 1 tá»‡p, so trong bá»™ nhá»›, rá»“i chá»‰ upsert cÃ¡c kiá»‡n khÃ¡c; ghi láº¡i tá»‡p bÄƒm khi cÃ³ thay Ä‘á»•i. NhÆ° váº­y bÆ°á»›c so sÃ¡nh tá»‘n **0 hÃ ng Ä‘á»c D1**.
4. **Äá»‘i soÃ¡t Ä‘áº§y Ä‘á»§ má»—i ngÃ y má»™t láº§n** (ban Ä‘Ãªm) Ä‘á»ƒ báº¯t dÃ²ng sá»­a mÃ  khÃ´ng cáº­p nháº­t cá»™t "Cáº­p nháº­t". Tá»‘n khoáº£ng má»™t lÆ°á»£t Ä‘á»c cáº£ Sheet nhÆ°ng chá»‰ ghi pháº§n khÃ¡c biá»‡t.
5. **Chu ká»³ theo giá»:** ban ngÃ y (vÃ­ dá»¥ 8hâ€“22h) má»—i 3â€“5 phÃºt, ban Ä‘Ãªm 30â€“60 phÃºt; cÃ³ nÃºt "Äá»“ng bá»™ ngay".
6. **Má»™t lá»‡nh gá»i láº¥y má»i tab** (ká»ƒ cáº£ tab áº©n "Váº­n Ä‘Æ¡n huá»·") thay vÃ¬ má»—i tab má»™t lá»‡nh.
7. **Ãt chá»‰ má»¥c D1 hÆ¡n:** má»—i chá»‰ má»¥c lÃ m má»—i láº§n ghi tá»‘n thÃªm hÃ ng ghi; chá»‰ giá»¯ chá»‰ má»¥c tháº­t sá»± cáº§n.
8. **Gá»™p nhiá»u cÃ¢u lá»‡nh ghi vÃ o má»™t lÃ´** (batch) vÃ  khÃ´ng tÄƒng `rev` cho kiá»‡n khÃ´ng Ä‘á»•i.
PhÆ°Æ¡ng Ã¡n thay tháº¿: Apps Script **chá»§ Ä‘á»™ng Ä‘áº©y** (trigger theo thá»i gian, Ä‘áº©y chá»‰ khi chá»¯ kÃ½ Ä‘á»•i) thay vÃ¬ Worker kÃ©o; trigger `onEdit` khÃ´ng báº¯t Ä‘Æ°á»£c thay Ä‘á»•i do script/nháº­p tá»± Ä‘á»™ng nÃªn chá»‰ nÃªn dÃ¹ng trigger theo thá»i gian.
Æ¯á»›c tÃ­nh: náº¿u má»—i ngÃ y Sheet Ä‘á»•i khoáº£ng 100â€“300 kiá»‡n thÃ¬ ~1.000â€“3.000 hÃ ng ghi/ngÃ y thay vÃ¬ ~2,4 triá»‡u (cÃ¡ch ngÃ¢y thÆ¡), vÃ  gáº§n nhÆ° 0 hÃ ng Ä‘á»c cho bÆ°á»›c so sÃ¡nh. Cáº§n Ä‘o láº¡i báº±ng tá»‘c Ä‘á»™ Ä‘á»•i tháº­t cá»§a Sheet.

### 3.7 Dung lÆ°á»£ng
- **R2 10 GB (áº£nh):** áº£nh má»›i thu nhá» cáº¡nh dÃ i ~1000 px, JPEG ~60% â‰ˆ 80â€“120 KB. 500 lÆ°á»£t quÃ©t/ngÃ y â‰ˆ 40â€“60 MB/ngÃ y â†’ khoáº£ng 6 thÃ¡ng Ä‘áº§y. Máº·c Ä‘á»‹nh **tá»± xÃ³a áº£nh sau 90 ngÃ y** (chá»‰nh Ä‘Æ°á»£c) vÃ  tá»‘i Ä‘a 5 áº£nh/kiá»‡n. 2.230 áº£nh cÅ© â‰ˆ 52 MB chuyá»ƒn sang R2 khÃ´ng Ä‘Ã¡ng ká»ƒ.
- **D1 500 MB (dá»¯ liá»‡u):** cháº­t hÆ¡n R2. DÃ²ng gá»n 1â€“2 KB cá»™ng chá»‰ má»¥c thÃ¬ cháº¡m 500 MB khi khoáº£ng **150.000â€“250.000 kiá»‡n** (Æ°á»›c tÃ­nh thÃ´). CÃ¡ch xá»­ lÃ½: Ä‘Æ¡n á»Ÿ Kho lÆ°u trá»¯ lÃ¢u (máº·c Ä‘á»‹nh > 12 thÃ¡ng) Ä‘Æ°á»£c **nÃ©n thÃ nh tá»‡p theo thÃ¡ng trÃªn R2**, trong D1 chá»‰ giá»¯ `order_index` (mÃ£ Ä‘Æ¡n, mÃ£ váº­n Ä‘Æ¡n, sÃ n, thÃ¡ng, vÃ i chá»¥c byte/dÃ²ng) Ä‘á»ƒ váº«n tÃ¬m Ä‘Æ°á»£c vÃ  má»Ÿ láº¡i. ChÆ°a cáº§n lÃ m ngay, thiáº¿t káº¿ sáºµn Ä‘á»ƒ khÃ´ng pháº£i Ä‘á»•i lÆ°á»£c Ä‘á»“ sau.

### 3.7b NgÃ¢n sÃ¡ch háº¡n má»©c (theo sá»‘ liá»‡u chá»§ dá»± Ã¡n cung cáº¥p)
Háº¡n má»©c: **D1** 500 MB, 5 triá»‡u hÃ ng Ä‘á»c/ngÃ y, 100.000 hÃ ng ghi/ngÃ y. **R2** 10 GB, 1 triá»‡u lÆ°á»£t ghi/thÃ¡ng, 10 triá»‡u lÆ°á»£t Ä‘á»c/thÃ¡ng, táº£i ra miá»…n phÃ­.
Giáº£ Ä‘á»‹nh Ä‘á»ƒ Æ°á»›c tÃ­nh: ~5.000 kiá»‡n Ä‘ang dÃ¹ng, 500 lÆ°á»£t quÃ©t/ngÃ y, 12 thiáº¿t bá»‹. (Æ¯á»›c tÃ­nh, sáº½ Ä‘o láº¡i khi cháº¡y thá»­.)

| Viá»‡c | Æ¯á»›c tÃ­nh | So vá»›i háº¡n má»©c |
|---|---|---|
| QuÃ©t 500 kiá»‡n/ngÃ y (má»—i lÆ°á»£t ghi kiá»‡n + sá»± kiá»‡n quÃ©t + nháº­t kÃ½ + chá»‰ má»¥c â‰ˆ 10 hÃ ng) | ~5.000 hÃ ng ghi/ngÃ y | ~5% cá»§a 100.000 |
| Sá»­a tay, Ä‘á»•i tráº¡ng thÃ¡i, ghi chÃº | vÃ i nghÃ¬n hÃ ng ghi/ngÃ y | thoáº£i mÃ¡i |
| MÃ¡y khá»Ÿi Ä‘á»™ng láº¥y toÃ n bá»™ danh sÃ¡ch (12 mÃ¡y Ã— 5.000 kiá»‡n) | ~60.000 hÃ ng Ä‘á»c/láº§n má»Ÿ háº¿t | ~1% cá»§a 5 triá»‡u |
| Láº¥y pháº§n thay Ä‘á»•i (theo `rev`, cÃ³ chá»‰ má»¥c) | ráº¥t nhá» | khÃ´ng Ä‘Ã¡ng ká»ƒ |
| áº¢nh: 500 lÆ°á»£t quÃ©t/ngÃ y Ã— (áº£nh gá»‘c + thu nhá») | ~30.000 lÆ°á»£t ghi R2/thÃ¡ng | ~3% cá»§a 1 triá»‡u |
| Xem áº£nh (cÃ³ lÆ°u táº¡m trÃªn mÃ¡y + tiÃªu Ä‘á» lÆ°u táº¡m) | vÃ i trÄƒm nghÃ¬n lÆ°á»£t Ä‘á»c/thÃ¡ng | ~vÃ i % cá»§a 10 triá»‡u |

**Hai chá»— Dá»„ VÆ¯á»¢T háº¡n má»©c náº¿u lÃ m áº©u â€” thiáº¿t káº¿ pháº£i cháº·n:**
1. **Náº¡p Google Sheet báº±ng Cron.** Náº¿u má»—i vÃ²ng ghi láº¡i toÃ n bá»™ 5.000 kiá»‡n, cá»© 3 phÃºt má»™t láº§n = 5.000 Ã— 480 = 2,4 triá»‡u hÃ ng ghi/ngÃ y (vÆ°á»£t 24 láº§n). Báº¯t buá»™c: (a) so **chá»¯ kÃ½** ná»™i dung Sheet, khÃ´ng Ä‘á»•i thÃ¬ khÃ´ng lÃ m gÃ¬; (b) khi Ä‘á»•i, chá»‰ upsert **dÃ²ng tháº­t sá»± khÃ¡c** (so bÄƒm tá»«ng kiá»‡n); (c) chu ká»³ máº·c Ä‘á»‹nh 5â€“10 phÃºt, chá»‰nh Ä‘Æ°á»£c.
2. **Truy váº¥n khÃ´ng cÃ³ chá»‰ má»¥c** Ä‘á»c quÃ©t cáº£ báº£ng: má»—i láº§n tÃ­nh cáº£ 5.000 hÃ ng. Má»i truy váº¥n cá»§a API pháº£i dÃ¹ng chá»‰ má»¥c (theo `rev`, mÃ£ Ä‘Æ¡n, mÃ£ váº­n Ä‘Æ¡n, tráº¡ng thÃ¡i), cÃ³ test kiá»ƒm tra káº¿ hoáº¡ch truy váº¥n (`EXPLAIN`) vÃ  Ä‘áº¿m hÃ ng Ä‘á»c.
Quy táº¯c khÃ¡c: mÃ¡y khÃ¡ch **khÃ´ng há»i vÃ²ng** (polling), chá»‰ nháº­n Ä‘áº©y qua WebSocket rá»“i má»›i láº¥y pháº§n má»›i; áº£nh gá»­i kÃ¨m tiÃªu Ä‘á» lÆ°u táº¡m lÃ¢u; Ä‘á»‹nh ká»³ dá»n áº£nh quÃ¡ háº¡n.

**Khi cháº¡m háº¡n má»©c ghi/ngÃ y cá»§a D1** thÃ¬ má»i thao tÃ¡c ghi (cáº£ quÃ©t) sáº½ lá»—i cho tá»›i khi háº¡n má»©c lÃ m má»›i (háº±ng ngÃ y, theo giá» UTC). Cáº§n: Ä‘o háº¡n má»©c Ä‘Ã£ dÃ¹ng ngay trong app (hiá»‡n thanh "Háº¡n má»©c hÃ´m nay: xx%"), cáº£nh bÃ¡o khi quÃ¡ 70%, vÃ  thÃ´ng bÃ¡o lá»—i rÃµ rÃ ng. Náº¿u thá»±c táº¿ gáº§n cháº¡m thÃ¬ nÃ¢ng gÃ³i tráº£ phÃ­ lÃ  lá»‘i thoÃ¡t.

### 3.7c GÃ³i thá»±c táº¿ cá»§a chá»§ dá»± Ã¡n (Ä‘Ã£ xÃ¡c nháº­n)
Táº¥t cáº£ Ä‘á»u gÃ³i **Miá»…n phÃ­, hÃ³a Ä‘Æ¡n $0**: Workers 100.000 yÃªu cáº§u/ngÃ y (tá»‘i Ä‘a 1.000 yÃªu cáº§u/phÃºt, **10 ms CPU má»—i yÃªu cáº§u**); D1 500 MB, 5 triá»‡u hÃ ng Ä‘á»c + 100.000 hÃ ng ghi/ngÃ y; Durable Object 100.000 yÃªu cáº§u/ngÃ y, **5 GB SQLite**; R2 10 GB (Ä‘Ã£ gáº¯n tháº», vÆ°á»£t 10 GB tÃ­nh $0,015/GB/thÃ¡ng). Cháº¡m tráº§n Workers/D1 thÃ¬ há»‡ thá»‘ng **táº¡m dá»«ng tá»›i hÃ´m sau, khÃ´ng tá»± trá»« tiá»n**.

Há»‡ quáº£ thiáº¿t káº¿:
1. **Tráº§n 100.000 yÃªu cáº§u/ngÃ y dÃ¹ng chung cho má»i mÃ¡y** vÃ  khi cháº¡m lÃ  cáº£ cá»­a hÃ ng ngá»«ng quÃ©t tá»›i hÃ´m sau. Má»¥c tiÃªu: dÃ¹ng thÆ°á»ng xuyÃªn **dÆ°á»›i 30%** (â‰¤ 30.000/ngÃ y). Æ¯á»›c tÃ­nh: 500 lÆ°á»£t quÃ©t Ã— (1 lá»‡nh quÃ©t + 12 mÃ¡y láº¥y pháº§n má»›i) â‰ˆ 6.500; má»Ÿ app/táº£i danh sÃ¡ch vÃ i nghÃ¬n; áº£nh lÃ  pháº§n dá»… tá»‘n nháº¥t. áº¢nh **táº£i lÃªn vÃ  xem qua Ä‘Æ°á»ng dáº«n kÃ½ sáºµn (presigned URL) tháº³ng vá»›i R2** Ä‘á»ƒ khÃ´ng tÃ­nh vÃ o yÃªu cáº§u Worker; thÃ´ng bÃ¡o qua WebSocket cá»§a Durable Object (tÃ­nh 1/20 yÃªu cáº§u má»—i tin).
2. **10 ms CPU má»—i yÃªu cáº§u ráº¥t cháº­t.** KhÃ´ng thá»ƒ gá»™p/phÃ¢n tÃ­ch cáº£ Sheet 5.000 dÃ²ng trong Worker. VÃ¬ váº­y pháº§n náº·ng (nháº­n dáº¡ng tab, gá»™p, so bÄƒm, lá»c pháº§n Ä‘á»•i) cháº¡y á»Ÿ **Apps Script phÃ­a Google** (miá»…n phÃ­, thá»i gian cháº¡y dÃ i); Worker chá»‰ nháº­n lÃ´ nhá» Ä‘Ã£ gá»™p sáºµn vÃ  ghi (chá» I/O khÃ´ng tÃ­nh CPU). MÃ´ hÃ¬nh **Apps Script chá»§ Ä‘á»™ng Ä‘áº©y** (má»¥c 3.6b) trá»Ÿ thÃ nh lá»±a chá»n chÃ­nh, khÃ´ng cÃ²n lÃ  phÆ°Æ¡ng Ã¡n thay tháº¿. Logic gá»™p Sheet chuyá»ƒn sang JavaScript cháº¡y trong Apps Script (kiá»ƒm chá»©ng báº±ng bá»™ ca thá»­ chung).
3. **CÃ³ thá»ƒ dÃ¹ng SQLite cá»§a Durable Object (5 GB) lÃ m kho chÃ­nh thay D1 (500 MB):** má»™t Durable Object cho cáº£ cá»­a hÃ ng xá»­ lÃ½ ghi tuáº§n tá»± nÃªn quÃ©t nháº­n "má»™t thao tÃ¡c nguyÃªn tá»­" tá»± nhiÃªn vÃ  háº¿t lo tráº§n 500 MB. ÄÃ¡nh Ä‘á»•i: khÃ³ xem/sá»­a trá»±c tiáº¿p hÆ¡n D1 vÃ  cáº§n chá»©c nÄƒng xuáº¥t sao lÆ°u. **Quyáº¿t Ä‘á»‹nh chá»‘t á»Ÿ Ä‘áº§u giai Ä‘oáº¡n 1 sau má»™t láº§n thá»­ nhanh cáº£ hai** (D1 hay Durable Object SQLite); lÆ°á»£c Ä‘á»“ vÃ  API viáº¿t sao cho Ä‘á»•i Ä‘Æ°á»£c kho phÃ­a sau.
4. Hiá»ƒn thá»‹ thanh "Háº¡n má»©c hÃ´m nay" cho yÃªu cáº§u Worker vÃ  hÃ ng ghi D1; cáº£nh bÃ¡o á»Ÿ 50% vÃ  70%.
5. Workers AI (10.000 neurons/ngÃ y) khÃ´ng dÃ¹ng; trá»£ lÃ½ AI váº«n dÃ¹ng khÃ³a Groq/Gemini cá»§a báº¡n trÃªn tá»«ng mÃ¡y.

### 3.8 Báº£o máº­t
HTTPS; má»—i thiáº¿t bá»‹ má»™t token riÃªng (lÆ°u bÄƒm), vai trÃ² chá»§ cá»­a hÃ ng / nhÃ¢n viÃªn (Ä‘iá»‡n thoáº¡i váº«n lÃ  á»©ng dá»¥ng Ä‘áº§y Ä‘á»§), thu há»“i tá»«ng mÃ¡y; mÃ£ ghÃ©p QR sá»‘ng 10 phÃºt dÃ¹ng má»™t láº§n; khÃ³a bÃ­ máº­t á»Ÿ Worker secrets. KhÃ´ng mÃ£ hÃ³a Ä‘áº§u cuá»‘i (server cáº§n Ä‘á»c mÃ£ Ä‘á»ƒ khá»›p/tÃ¬m), bÃ¹ láº¡i khÃ´ng lÆ°u dá»¯ liá»‡u cÃ¡ nhÃ¢n ngÆ°á»i nháº­n.

## 4. MÃ¡y tÃ­nh (WPF)
**Giá»¯ nguyÃªn:** toÃ n bá»™ mÃ n hÃ¬nh, báº£ng, bá»™ lá»c, thÃ´ng bÃ¡o, tÃ¬m kiáº¿m, thá»‘ng kÃª, xuáº¥t file, trá»£ lÃ½ AI.
**Äá»•i:**
1. Lá»›p dá»¯ liá»‡u: cÃ i Ä‘áº·t má»›i "cloud + báº£n lÆ°u táº¡m chá»‰ Ä‘á»c" cho cÃ¡c giao diá»‡n trá»«u tÆ°á»£ng (`IReturnOrderProvider`, `IArchivedOrderProvider`, `IScanService`...).
2. Cháº¥m tráº¡ng thÃ¡i káº¿t ná»‘i (má»¥c 2); máº¥t máº¡ng thÃ¬ khÃ³a má»i nÃºt ghi.
3. **MÃ¡y tÃ­nh chá»‰ xem áº£nh**, khÃ´ng chá»¥p/Ä‘Æ°a áº£nh lÃªn.
4. Bá»™ náº¡p thÆ° má»¥c/Excel/Salework giá»¯ cÃ¡ch Ä‘á»c, Ä‘áº§u ra gá»­i `/import` (cáº§n máº¡ng). Hai mÃ¡y cÃ¹ng náº¡p má»™t file khÃ´ng gÃ¢y trÃ¹ng.
5. Táº¯t Ä‘á»“ng bá»™ MQTT/ntfy (ntfy giá»¯ táº¡m cho thÃ´ng bÃ¡o Ä‘áº©y Ä‘iá»‡n thoáº¡i).

## 5. Äiá»‡n thoáº¡i (SwiftUI) â€” á»©ng dá»¥ng Ä‘áº§y Ä‘á»§
**Giá»¯ nguyÃªn:** cÃ¡c tab (Tá»•ng quan, QuÃ©t, ÄÆ¡n, Lá»‹ch sá»­, CÃ i Ä‘áº·t), chi tiáº¿t Ä‘Æ¡n, trá»£ lÃ½ AI.
**Äá»•i:**
1. `SyncEngine` + MQTT/ntfy â†’ mÃ¡y khÃ¡ch cloud (HTTP + WebSocket); bá» hÃ ng Ä‘á»£i quÃ©t ngoáº¡i tuyáº¿n.
2. QuÃ©t â†’ `POST /scan`; chá»¥p áº£nh gáº¯n vá»›i káº¿t quáº£ **server xÃ¡c nháº­n Ä‘Ãºng má»™t kiá»‡n** rá»“i má»›i táº£i lÃªn R2; quÃ©t sai/khÃ´ng tÃ¬m tháº¥y/mÆ¡ há»“ thÃ¬ bá» áº£nh.
3. Tab ÄÆ¡n/chi tiáº¿t hiá»ƒn thá»‹ áº£nh cá»§a má»i mÃ¡y (báº£n thu nhá» tá»« R2), cÃ³ lÆ°u táº¡m trÃªn mÃ¡y Ä‘á»ƒ cuá»™n mÆ°á»£t.
4. MÃ n Äá»“ng bá»™ Ä‘á»•i tá»« mÃ£ phÃ²ng sang "ghÃ©p thiáº¿t bá»‹ báº±ng QR" + tráº¡ng thÃ¡i káº¿t ná»‘i.
5. ThÃ´ng bÃ¡o khi app Ä‘Ã³ng: táº¡m dÃ¹ng ntfy; vá» sau Apple Push khi cÃ³ tÃ i khoáº£n Apple Developer.

## 6. Kiá»ƒm chá»©ng (giá»¯ AGENTS.md: khÃ´ng tuyÃªn bá»‘ PASS cho thá»© chÆ°a cháº¡y tháº­t)
- Bá»™ ca thá»­ **dÃ¹ng chung** xuáº¥t tá»« test C# hiá»‡n cÃ³ (khá»›p mÃ£, ambiguous, kiá»‡n A/B/C, nháº­p láº¡i cÃ¹ng kiá»‡n, gá»™p Sheet, tab Váº­n Ä‘Æ¡n huá»·): C# vÃ  TypeScript cÃ¹ng cháº¡y pháº£i ra cÃ¹ng káº¿t quáº£.
- Worker kiá»ƒm thá»­ cá»¥c bá»™ báº±ng giáº£ láº­p (khÃ´ng cáº§n tÃ i khoáº£n); thá»­ tháº­t cáº§n tÃ i khoáº£n cá»§a báº¡n.
- Test: hai mÃ¡y quÃ©t cÃ¹ng kiá»‡n, gá»­i láº·p thao tÃ¡c, máº¥t máº¡ng giá»¯a chá»«ng, khÃ³a ghi khi máº¥t káº¿t ná»‘i.
- Äiá»‡n thoáº¡i: chá»‰ kiá»ƒm tra biÃªn dá»‹ch + `swift test` trÃªn GitHub Actions; cháº¡y tháº­t trÃªn mÃ¡y do báº¡n xÃ¡c nháº­n.

## 7. Chuyá»ƒn Ä‘á»•i dá»¯ liá»‡u
1. CÃ´ng cá»¥ nháº­p dá»¯ liá»‡u cÅ© lÃªn cloud cÃ³ **cháº¿ Ä‘á»™ cháº¡y thá»­** (Ä‘áº¿m, bÃ¡o lá»—i, khÃ´ng ghi) vÃ  bÃ¡o cÃ¡o so khá»›p: sá»‘ kiá»‡n, sá»‘ áº£nh, trÆ°á»ng bá»‹ bá».
2. TrÆ°á»›c khi bá» `photoHistory` khá»i dÃ²ng Ä‘Æ¡n, cÃ´ng cá»¥ kiá»ƒm tra áº£nh Ä‘Ã³ **Ä‘Ã£ cÃ³ trong báº£ng `photos`** hay lÃ  áº£nh duy nháº¥t; áº£nh duy nháº¥t Ä‘Æ°á»£c Ä‘Æ°a lÃªn R2 chá»© khÃ´ng bá» (quy táº¯c 8: khÃ´ng xÃ³a dá»¯ liá»‡u cÅ©).
3. Báº£n SQLite cá»¥c bá»™ giá»¯ nguyÃªn lÃ m báº£n lÆ°u Ä‘á»ƒ quay láº¡i.
4. Cháº¡y song song báº£n cÅ© vÃ  báº£n cloud má»™t thá»i gian rá»“i má»›i chuyá»ƒn háº³n.

## 8. CÃ¡c giai Ä‘oáº¡n vÃ  cá»•ng nghiá»‡m thu
| GÄ | Ná»™i dung | Xong khi |
|---|---|---|
| 1 | LÆ°á»£c Ä‘á»“ D1, API, Ä‘Äƒng nháº­p thiáº¿t bá»‹, quy táº¯c quÃ©t, **bá»™ gá»™p Google Sheet báº±ng TypeScript + Cron**, bá»™ ca thá»­ chung, cÃ´ng cá»¥ nháº­p dá»¯ liá»‡u cÅ© (cháº¡y thá»­) | Bá»™ ca thá»­ Ä‘áº¡t á»Ÿ C# vÃ  TypeScript; nháº­p thá»­ khá»›p sá»‘ liá»‡u; Sheet náº¡p Ä‘Æ°á»£c trÃªn mÃ´i trÆ°á»ng thá»­ |
| 2 | MÃ¡y tÃ­nh dÃ¹ng cloud (báº£n lÆ°u táº¡m chá»‰ Ä‘á»c, khÃ³a khi máº¥t máº¡ng), cháº¡y song song báº£n cÅ© | Hai mÃ¡y tÃ­nh cÃ¹ng dÃ¹ng, quÃ©t trÃ¹ng xá»­ lÃ½ Ä‘Ãºng, máº¥t máº¡ng thÃ¬ khÃ³a ghi, cÃ³ máº¡ng thÃ¬ má»Ÿ khÃ³a |
| 3 | iPhone dÃ¹ng cloud, áº£nh lÃªn R2 | `swift test` + build Ä‘áº¡t; báº¡n thá»­ trÃªn Ä‘iá»‡n thoáº¡i tháº­t |
| 4 | Táº¯t MQTT/ntfy cÅ©, Ä‘Ã³ng gÃ³i báº£n cháº¡y tháº­t | Báº¡n xÃ¡c nháº­n |

Khá»‘i lÆ°á»£ng lá»›n (nhiá»u tuáº§n). Má»—i giai Ä‘oáº¡n ná»™p riÃªng Ä‘á»ƒ báº¡n kiá»ƒm tra trÆ°á»›c khi sang giai Ä‘oáº¡n tiáº¿p.

## 9. Rá»§i ro Ä‘Ã£ biáº¿t
- Logic nghiá»‡p vá»¥ cÃ³ **hai báº£n cÃ i** (C# vÃ  TypeScript), nay gá»“m cáº£ bá»™ gá»™p Sheet: rá»§i ro lá»‡ch â†’ bá»™ ca thá»­ chung.
- Máº¥t máº¡ng thÃ¬ cáº£ cá»­a hÃ ng khÃ´ng quÃ©t Ä‘Æ°á»£c (Ä‘Ã£ cháº¥p nháº­n theo yÃªu cáº§u); Cloudflare lá»—i cÅ©ng váº­y.
- D1 500 MB sáº½ Ä‘áº§y khi dá»¯ liá»‡u ráº¥t lá»›n â†’ chuyá»ƒn Ä‘Æ¡n cÅ© sang R2 (3.7).
- áº¢nh cÃ³ thá»ƒ Ä‘áº§y 10 GB náº¿u khÃ´ng giá»›i háº¡n â†’ thu nhá» + tá»± xÃ³a sau N ngÃ y.
- KhÃ´ng cÃ³ mÃ£ hÃ³a Ä‘áº§u cuá»‘i.

## 10. Viá»‡c cÃ²n cáº§n chá»§ dá»± Ã¡n
1. **Duyá»‡t** báº£n 2 nÃ y (hoáº·c nÃªu chá»— cáº§n sá»­a).
2. Tráº£ lá»i: má»™t hay nhiá»u cá»­a hÃ ng; giá»¯ áº£nh bao nhiÃªu ngÃ y (máº·c Ä‘á»‹nh 90).
3. Khi tá»›i lÃºc triá»ƒn khai: táº¡o tÃ i khoáº£n Cloudflare vÃ  cháº¡y `! npx wrangler login` (tÃ´i khÃ´ng tá»± Ä‘Äƒng nháº­p há»™).

