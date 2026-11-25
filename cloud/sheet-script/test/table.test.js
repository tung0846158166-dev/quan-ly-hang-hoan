// Chạy: node cloud/sheet-script/test/table.test.js — bước ĐỌC TAB của Apps Script (qlhhTable_) phải ra đúng như máy tính đọc
// (cloud/golden/table-vectors.json do C# sinh: SpreadsheetReader.ReadCsv + DropPersonalColumns trên CSV của Sheets API).
"use strict";
const fs = require("fs");
const path = require("path");
const vm = require("vm");

require("child_process").execFileSync(process.execPath, [path.join(__dirname, "..", "build.js")], { stdio: "ignore" });
const ctx = {};
vm.createContext(ctx);
vm.runInContext(fs.readFileSync(path.join(__dirname, "..", "dist", "QLHH.gs"), "utf8"), ctx);

const data = JSON.parse(fs.readFileSync(path.join(__dirname, "..", "..", "golden", "table-vectors.json"), "utf8"));
let pass = 0;
const fails = [];
data.cases.forEach((c, i) => {
  // getDisplayValues trả ma trận chữ nhật: đệm ô trống cho đủ cột
  const width = Math.max(0, ...c.matrix.map((r) => r.length));
  const matrix = c.matrix.map((r) => r.concat(Array(width - r.length).fill("")));
  const got = JSON.stringify(ctx.qlhhTable_(matrix).map((o) => Object.entries(o)));
  const want = JSON.stringify(c.rows);
  if (got === want) pass++;
  else fails.push(`ca ${i}\n   C#: ${want.slice(0, 300)}\n   JS: ${got.slice(0, 300)}`);
});
console.log(`Đọc tab: ${pass}/${data.cases.length} khớp máy tính`);
if (fails.length) { console.log(fails.slice(0, 5).join("\n")); process.exit(1); }
