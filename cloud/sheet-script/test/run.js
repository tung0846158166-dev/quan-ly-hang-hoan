// Chạy: node cloud/sheet-script/test/run.js  — so bản JavaScript (Apps Script) với bộ ca thử do C# sinh (cloud/golden/sheet-vectors.json).
"use strict";
const path = require("path");
const fs = require("fs");
const QLHH = require("../qlhh-core.js");

const data = JSON.parse(fs.readFileSync(path.join(__dirname, "..", "..", "golden", "sheet-vectors.json"), "utf8"));
let pass = 0;
const fails = [];
const t0 = Date.now();

for (const c of data.cases) {
  const inp = c.input;
  const [y, mo, d, h, mi, s] = inp.now;
  const now = QLHH.makeDate(y, mo, d, h, mi, s);
  let got;
  try {
    const r = QLHH.sheetToParcels(inp.tabs, { skipTypes: inp.skipTypes, maxDays: inp.maxDays, onlyReturns: inp.onlyReturns, now, year: y });
    got = {
      parcels: r.parcels.map((p) => ({
        shipDate: p.shipDate, returnDate: p.returnDate, recvDate: p.recvDate, platform: p.platform, orderType: p.orderType, isPartialReturn: p.isPartialReturn,
        platformStatus: p.platformStatus, returnedAt: p.returnedAt, preOrder: p.preOrder, orderId: p.orderId, returnOrderId: p.returnOrderId,
        forwardTrack: p.forwardTrack, returnTrack: p.returnTrack, trackingCodes: p.trackingCodes, track: p.track, altTrack: p.altTrack, carrier: p.carrier,
        price: p.price, priceHasValue: p.priceHasValue, priceNeedsReview: p.priceNeedsReview, status: p.status, note: p.note, buyerNote: p.buyerNote,
        deadline: p.deadline, importPriceBasis: p.importPriceBasis,
        items: p.items.map((i) => ({ product: i.product, color: i.color, size: i.size, qty: i.qty, returnedQty: i.returnedQty, sellerSku: i.sellerSku === undefined ? null : i.sellerSku })),
        rawDetails: p.rawDetails,
      })),
      build: {
        parcelCount: r.build.parcels.length, rowCount: r.build.rows.length, oldRequestsDropped: r.build.oldRequestsDropped,
        skippedCancelTypes: r.build.skippedCancelTypes, trackLookupTabs: r.build.trackLookupTabs, tracksFilled: r.build.tracksFilled, tooOld: r.build.tooOld,
      },
      skippedSuccess: r.skippedSuccess,
    };
  } catch (e) {
    got = /Số lượng không hợp lệ/.test(String(e && e.message)) ? { error: "format" } : { exception: String(e && e.stack) };
  }
  const a = JSON.stringify(got), b = JSON.stringify(c.expected);
  if (a === b) { pass++; continue; }
  let i = 0;
  while (i < a.length && a[i] === b[i]) i++;
  fails.push(`${inp.name}\n   C#: ...${b.slice(Math.max(0, i - 150), i + 80)}\n   JS: ...${a.slice(Math.max(0, i - 150), i + 80)}`);
}

// hiệu năng: 5.000 dòng giả trong một tab "Hàng hoàn"
const big = [];
for (let n = 0; n < 5000; n++)
  big.push({ "Mã": "RR" + n, "Mã đơn": "SP" + n, "Loại": "Trả hàng + hoàn tiền", "Mã vận đơn": "SPXVN" + String(100000000 + n), "Trạng thái về": "Đang về",
    "Về shop lúc": "21/11/2026 10:00", "Hạn khiếu nại": "", "Hàng": "Áo thun · Đen, M × 2", "SL": "2", "Tiền": "250000", "Lý do": "Sai mô tả", "Ngày": "20/11/2026", "Cập nhật": "22/11/2026" });
const tb = Date.now();
const bigRes = QLHH.sheetToParcels([big], { now: QLHH.makeDate(2026, 11, 23, 12, 0, 0), year: 2026 });
const bigMs = Date.now() - tb;

console.log(`Ca thử: ${pass}/${data.cases.length} khớp C# (${Date.now() - t0} ms). 5.000 dòng -> ${bigRes.parcels.length} kiện trong ${bigMs} ms.`);
if (fails.length) {
  console.log(fails.slice(0, 8).join("\n\n"));
  console.log(`... ${fails.length} ca lệch`);
  process.exit(1);
}
if (bigMs > 2000 || bigRes.parcels.length !== 5000) { console.log("Quá chậm hoặc sai số kiện"); process.exit(1); }
