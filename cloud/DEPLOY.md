# Triá»ƒn khai Worker lÃªn Cloudflare (lÃ m Má»˜T láº§n, khoáº£ng 10 phÃºt)

Worker tÃªn **`qlhh-cloud`** (KHÃ”NG Ä‘á»¥ng tá»›i Worker `app-backend` hay D1 `app-db` báº¡n Ä‘Ã£ cÃ³). Kho dá»¯ liá»‡u chÃ­nh lÃ  SQLite cá»§a Durable Object (5 GB, gÃ³i Miá»…n phÃ­ Ä‘á»§ dÃ¹ng); D1 khÃ´ng dÃ¹ng. áº¢nh lÆ°u á»Ÿ R2 `app-images` (khÃ³a áº£nh báº¯t Ä‘áº§u báº±ng `p/` vÃ  `t/`, khÃ´ng Ä‘á»¥ng dá»¯ liá»‡u khÃ¡c trong kho; muá»‘n tÃ¡ch riÃªng thÃ¬ táº¡o kho `qlhh-images` rá»“i sá»­a `bucket_name` trong `cloud/worker/wrangler.jsonc`).

Má»i lá»‡nh cháº¡y trong thÆ° má»¥c `cloud\worker` (PowerShell). GÃµ `! lá»‡nh` á»Ÿ Ä‘áº§u dÃ²ng náº¿u cháº¡y ngay trong phiÃªn Claude Code.

1. **CÃ i gÃ³i:** `npm install`
2. **ÄÄƒng nháº­p Cloudflare:** `npx wrangler login` (má»Ÿ trÃ¬nh duyá»‡t, báº¥m cho phÃ©p).
3. **Äáº·t 2 khÃ³a bÃ­ máº­t** (tá»± nghÄ© chuá»—i dÃ i ngáº«u nhiÃªn â‰¥ 32 kÃ½ tá»±; gÃµ khi Ä‘Æ°á»£c há»i, KHÃ”NG gá»­i cho ai ká»ƒ cáº£ Claude):
   - `npx wrangler secret put BOOTSTRAP_KEY` â€” dÃ¹ng má»™t láº§n Ä‘á»ƒ táº¡o mÃ¡y cá»§a chá»§ cá»­a hÃ ng Ä‘áº§u tiÃªn.
   - `npx wrangler secret put IMPORT_KEY` â€” Apps Script dÃ¹ng khi Ä‘áº©y Ä‘Æ¡n tá»« Google Sheet.
   Gá»£i Ã½ táº¡o chuá»—i: `[Convert]::ToBase64String((1..32 | % { Get-Random -Max 256 }))`
4. **Triá»ƒn khai:** `npx wrangler deploy` â†’ in ra Ä‘á»‹a chá»‰ dáº¡ng `https://qlhh-cloud.<tÃªn-báº¡n>.workers.dev`. Äá»‹a chá»‰ nÃ y lÃ  Ä‘á»‹a chá»‰ Worker nháº­p vÃ o app (CÃ i Ä‘áº·t > Cloud).
5. **Táº¡o mÃ¡y chá»§ cá»­a hÃ ng Ä‘áº§u tiÃªn** (cháº¡y Má»˜T láº§n, thay Ä‘á»‹a chá»‰ vÃ  khÃ³a báº¡n Ä‘áº·t á»Ÿ bÆ°á»›c 3):
   ```powershell
   $b = '{"bootstrapKey":"<BOOTSTRAP_KEY>","deviceName":"MÃ¡y chá»§ cá»­a hÃ ng","kind":"pc"}'
   Invoke-RestMethod -Method Post -Uri "https://qlhh-cloud.<tÃªn-báº¡n>.workers.dev/v1/pair/bootstrap" -ContentType "application/json; charset=utf-8" -Body ([Text.Encoding]::UTF8.GetBytes($b))
   ```
   Káº¿t quáº£ cÃ³ `token`: **lÆ°u láº¡i á»Ÿ nÆ¡i an toÃ n** (chá»‰ hiá»‡n má»™t láº§n). Sau nÃ y giao diá»‡n ghÃ©p thiáº¿t bá»‹ sáº½ lÃ m viá»‡c nÃ y thay báº¡n; cÃ¡c mÃ¡y cÃ²n láº¡i ghÃ©p báº±ng mÃ£ 6 sá»‘ do chá»§ táº¡o.
6. **Kiá»ƒm tra:** má»Ÿ `https://qlhh-cloud.<tÃªn-báº¡n>.workers.dev/v1/me` â€” pháº£i tháº¥y lá»—i `unauthorized` (Ä‘Ãºng, vÃ¬ chÆ°a cÃ³ token).

Cháº¡y thá»­ cá»¥c bá»™ (khÃ´ng cáº§n Ä‘Äƒng nháº­p, khÃ´ng Ä‘á»¥ng cloud tháº­t): táº¡o `cloud/worker/.dev.vars` gá»“m hai dÃ²ng `BOOTSTRAP_KEY=...` vÃ  `IMPORT_KEY=...` rá»“i `npx wrangler dev --local`. Cháº¡y test: `npm test`.

## Háº¡n má»©c gÃ³i Miá»…n phÃ­ (xem docs/cloud-design.md má»¥c 3.7)
Workers 100.000 yÃªu cáº§u/ngÃ y (1.000/phÃºt, 10 ms CPU/yÃªu cáº§u); Durable Object 100.000 yÃªu cáº§u/ngÃ y, 5 GB; R2 10 GB, 1 triá»‡u lÆ°á»£t ghi + 10 triá»‡u lÆ°á»£t Ä‘á»c má»—i thÃ¡ng. Cháº¡m tráº§n ngÃ y thÃ¬ dá»«ng tá»›i 00:00 UTC (07:00 giá» Viá»‡t Nam), khÃ´ng bá»‹ tÃ­nh tiá»n. API cÃ³ `GET /v1/quota` Ä‘áº¿m má»©c dÃ¹ng trong ngÃ y vÃ  cáº£nh bÃ¡o á»Ÿ 70%.

