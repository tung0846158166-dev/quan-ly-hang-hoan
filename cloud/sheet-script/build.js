// Gộp qlhh-core.js + qlhh-sync.js thành MỘT tệp để dán vào Apps Script: node cloud/sheet-script/build.js -> cloud/sheet-script/dist/QLHH.gs
"use strict";
const fs = require("fs");
const path = require("path");
const dir = __dirname;
const core = fs.readFileSync(path.join(dir, "qlhh-core.js"), "utf8").replace(/\nif \(typeof module[^\n]*\n?/, "\n");
const sync = fs.readFileSync(path.join(dir, "qlhh-sync.js"), "utf8");
fs.mkdirSync(path.join(dir, "dist"), { recursive: true });
const out = "// TỆP SINH TỰ ĐỘNG từ cloud/sheet-script (qlhh-core.js + qlhh-sync.js). Đừng sửa tay: sửa tệp gốc rồi chạy build.js.\n" + core + "\n" + sync;
fs.writeFileSync(path.join(dir, "dist", "QLHH.gs"), out);
console.log("Đã tạo dist/QLHH.gs (" + out.length + " ký tự)");
