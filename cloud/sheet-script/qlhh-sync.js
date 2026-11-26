/**
 * QLHH - đồng bộ Google Sheet lên cloud (chạy trong Apps Script, cùng dự án với Sheet).
 * Đọc các tab ngay trong bảng tính (không gọi mạng để đọc), gộp + chuyển dòng bằng QLHH (qlhh-core), chỉ gửi kiện ĐÃ ĐỔI lên cloud.
 * Thuộc tính cần đặt (Cài đặt dự án > Thuộc tính tập lệnh): QLHH_CLOUD_URL, QLHH_IMPORT_KEY. Tùy chọn: QLHH_TABS (tên tab, cách nhau dấu phẩy),
 * QLHH_MAX_DAYS, QLHH_SKIP_TYPES, QLHH_ONLY_RETURNS ("false" để nạp cả đơn giao thành công).
 * KHÔNG bao giờ ghi khóa ra nhật ký. Không gửi cột cá nhân (tên/SĐT/địa chỉ người nhận) và không gửi ghi chú người mua.
 */

var QLHH_STATE_SHEET = "_qlhh_state";   // tab ẩn lưu dấu vân tay các kiện đã gửi (PropertiesService giới hạn 500 KB)
var QLHH_BATCH = 50;

// cột cá nhân bỏ ngay khi đọc (= SpreadsheetReader.PersonalNormalized của máy tính, thêm vài tên SĐT cho chắc)
var QLHH_PERSONAL = ["tennguoimua", "nguoimua", "tennguoidungnguoimua", "tendangnhapnguoimua", "buyerusername", "buyername", "diachinhanhang",
  "diachilayhanghoantra", "tennguoinhan", "deliveryaddress", "returnaddress", "recipientname", "buyerphone", "recipient", "phone", "province",
  "district", "commune", "detailaddress", "additionaladdressinformation", "buyermessage", "sodienthoai", "sdt", "dienthoai"];

function qlhhProps_() {
  var p = PropertiesService.getScriptProperties();
  return {
    url: (p.getProperty("QLHH_CLOUD_URL") || "").replace(/\/+$/, "").replace(/\/v1$/, ""),
    key: p.getProperty("QLHH_IMPORT_KEY") || "",
    tabs: p.getProperty("QLHH_TABS") || "",
    maxDays: p.getProperty("QLHH_MAX_DAYS") || "",
    skipTypes: p.getProperty("QLHH_SKIP_TYPES"),
    onlyReturns: (p.getProperty("QLHH_ONLY_RETURNS") || "true") !== "false",
    sheetId: qlhhSheetId_(p.getProperty("QLHH_SHEET_ID") || ""),
  };
}

/** Nhận cả mã Sheet lẫn cả đường link ".../spreadsheets/d/<mã>/edit...". */
function qlhhSheetId_(s) {
  s = String(s).trim();
  var m = s.match(/\/d\/([a-zA-Z0-9_-]{20,})/);
  return m ? m[1] : s;
}

/**
 * Sheet nguồn + nơi lưu trạng thái.
 * - Có QLHH_SHEET_ID (dự án Apps Script RIÊNG, tài khoản chỉ cần quyền XEM Sheet): đọc Sheet theo mã,
 *   trạng thái lưu ở một bảng tính riêng "QLHH - trạng thái đồng bộ" trong Drive của tài khoản chạy (tự tạo lần đầu). Không ghi gì vào Sheet gốc.
 * - Không có: mã gắn ngay trong Sheet (cần quyền sửa), trạng thái ở tab ẩn của chính Sheet đó.
 */
function qlhhSources_(cfg) {
  if (!cfg.sheetId) { var ss = SpreadsheetApp.getActiveSpreadsheet(); return { source: ss, stateBook: ss }; }
  var props = PropertiesService.getScriptProperties();
  var source = SpreadsheetApp.openById(cfg.sheetId);
  var stateId = props.getProperty("QLHH_STATE_BOOK_ID");
  var book = null;
  if (stateId) { try { book = SpreadsheetApp.openById(stateId); } catch (e) { book = null; } }
  if (!book) { book = SpreadsheetApp.create("QLHH - trạng thái đồng bộ (đừng xóa)"); props.setProperty("QLHH_STATE_BOOK_ID", book.getId()); }
  return { source: source, stateBook: book };
}

// ---- Đọc tab GIỐNG HỆT máy tính (SpreadsheetReader.ReadCsv + BestTable + DropPersonalColumns trong C#):
// tự tìm dòng tiêu đề (bỏ qua dòng tiêu đề/hướng dẫn phía trên), cột trùng tên đánh _1/_2, cột tên trống thành __col{i}.
var QLHH_ID_HEADERS = ["orderid", "madonhang", "iddonhang", "madon", "trackingid", "trackingnumber", "mavandon", "mavandontrahang", "mvdtrahang",
  "returnlogisticstrackingid", "returntrackingid", "returnorderid", "returnid", "masokhieunai", "makhieunai"];
var QLHH_CAT_HEADERS = [
  ["productname", "product", "tensanpham", "tensanphamtrensan", "tenhanghoa", "itemname", "sellersku", "masku", "skuname", "tenphanloai", "variation", "hang", "sanpham"],
  ["quantity", "qty", "soluong", "returnquantity", "soluonghoan", "skuquantityofreturn", "sl"],
  ["orderstatus", "returnstatus", "trangthaidonhang", "trangthaitrahang", "trangthaitrahanghoantien", "trangthaihoan", "trangthaive", "trangthai"]];

/** = SpreadsheetReader.NormalizeHeader: bỏ dấu, đ->d, chữ thường, chỉ giữ a-z0-9. */
function qlhhNormHeader_(v) {
  var s = String(v == null ? "" : v).normalize("NFD").replace(/[̀-ͯ]/g, "").replace(/đ/g, "d").replace(/Đ/g, "D").toLowerCase();
  return s.replace(/[^a-z0-9]/g, "");
}

function qlhhHeaderNames_(cells) {
  var seen = {}, names = [];
  for (var i = 0; i < cells.length; i++) {
    var name = String(cells[i] == null ? "" : cells[i]).trim();
    if (name.length === 0) name = "__col" + i;
    if (!Object.prototype.hasOwnProperty.call(seen, name)) seen[name] = 0;
    else name = name + "_" + (++seen[name]);
    names.push(name);
  }
  return names;
}

/** Bỏ ô trống ở cuối mỗi dòng như Sheets API (Values.get) mà máy tính đang nhận qua CSV. */
function qlhhTrimRow_(r) {
  var n = r.length;
  while (n > 0 && String(r[n - 1] == null ? "" : r[n - 1]) === "") n--;
  return r.slice(0, n).map(function (v) { return String(v == null ? "" : v); });
}

/** Ma trận ô -> dòng {tên cột: chữ}; không thấy bảng đơn thì trả mảng rỗng (máy tính cũng bỏ tab đó). */
function qlhhTable_(values) {
  var matrix = values.map(qlhhTrimRow_);
  var best = null, bestScore = -1;
  for (var h = 0; h < matrix.length; h++) {
    var keys = matrix[h].map(qlhhNormHeader_);
    var idc = keys.filter(function (k) { return QLHH_ID_HEADERS.indexOf(k) >= 0; }).length;
    var cats = QLHH_CAT_HEADERS.filter(function (set) { return keys.some(function (k) { return set.indexOf(k) >= 0; }); }).length;
    if (idc === 0 || cats === 0) continue;
    var names = qlhhHeaderNames_(matrix[h]), rows = [];
    for (var r = h + 1; r < matrix.length; r++) {
      var d = {}, any = false;
      for (var c = 0; c < names.length; c++) {
        var v = c < matrix[r].length ? matrix[r][c] : "";
        d[names[c]] = v;
        if (v.trim() !== "") any = true;
      }
      if (any) rows.push(d);
    }
    var score = cats * 10 + idc;
    if (rows.length > 0 && score > bestScore) { best = rows; bestScore = score; }
  }
  if (!best) return [];
  var personal = QLHH_PERSONAL.concat(["Tên khách hàng", "Số điện thoại", "Địa chỉ", "Tỉnh", "Huyện", "Ghi chú vận chuyển", "Người tạo đơn"].map(qlhhNormHeader_));
  return best.map(function (row) {
    var o = {};
    Object.keys(row).forEach(function (k) { if (personal.indexOf(qlhhNormHeader_(k)) < 0) o[k] = row[k]; });
    return o;
  });
}

/**
 * Tab ẩn "Vận đơn huỷ" chỉ có 2 cột mã (Mã đơn, Mã vận đơn) và có dòng tên + mô tả phía trên: bộ đọc bảng chung (cần thêm cột
 * hàng/SL/trạng thái) bỏ qua nó. Đọc riêng: dòng tiêu đề đầu tiên có CẢ "Mã đơn" và "Mã vận đơn". Không thấy thì trả [].
 */
function qlhhTrackTable_(values) {
  var matrix = values.map(qlhhTrimRow_);
  for (var h = 0; h < matrix.length; h++) {
    var keys = matrix[h].map(qlhhNormHeader_);
    if (keys.indexOf("madon") < 0 || keys.indexOf("mavandon") < 0) continue;
    var names = qlhhHeaderNames_(matrix[h]), rows = [];
    for (var r = h + 1; r < matrix.length; r++) {
      var d = {}, any = false;
      for (var c = 0; c < names.length; c++) {
        var v = c < matrix[r].length ? matrix[r][c] : "";
        d[names[c]] = v;
        if (v.trim() !== "") any = true;
      }
      if (any) rows.push(d);
    }
    return rows;
  }
  return [];
}

/** Đọc một tab như máy tính đọc (xem qlhhTable_); tab tra mã "Vận đơn huỷ" đọc bằng qlhhTrackTable_. */
function qlhhReadTab_(sheet) {
  var values = sheet.getDataRange().getDisplayValues();
  return QLHH.key(sheet.getName()) === QLHH.key(QLHH.TRACK_TAB_NAME) ? qlhhTrackTable_(values) : qlhhTable_(values);
}

function qlhhSheets_(ss, cfg) {
  var all = ss.getSheets();
  // tên tab cách nhau dấu phẩy hoặc chấm phẩy (như ô "Tab" trên máy tính); so không phân biệt dấu/hoa thường/khoảng trắng
  var wanted = cfg.tabs ? cfg.tabs.split(/[,;]/).map(function (s) { return QLHH.key(s); }).filter(function (s) { return s; }) : null;
  return all.filter(function (sh) {
    var n = sh.getName();
    if (n === QLHH_STATE_SHEET) return false;
    if (QLHH.key(n) === QLHH.key(QLHH.TRACK_TAB_NAME)) return true;   // tab ẩn "Vận đơn huỷ" luôn đọc nếu có
    return wanted ? wanted.indexOf(QLHH.key(n)) >= 0 : n.charAt(0) !== "_";
  });
}

/** "Giờ Việt Nam" dạng mili-giây với thành phần ngày giờ coi như UTC (quy ước của QLHH). */
function qlhhNowLocal_() {
  var s = Utilities.formatDate(new Date(), "Asia/Ho_Chi_Minh", "yyyy-MM-dd HH:mm:ss").split(/[- :]/).map(Number);
  return { ms: QLHH.makeDate(s[0], s[1], s[2], s[3], s[4], s[5]), year: s[0] };
}

function qlhhHash_(obj) {
  var bytes = Utilities.computeDigest(Utilities.DigestAlgorithm.SHA_256, JSON.stringify(obj), Utilities.Charset.UTF_8);
  return Utilities.base64Encode(bytes).slice(0, 22);
}

function qlhhParcelKey_(p) {
  return [p.orderId, p.returnOrderId, p.track, p.returnTrack].join("|");
}

function qlhhStateSheet_(ss) {
  var sh = ss.getSheetByName(QLHH_STATE_SHEET);
  if (!sh) { sh = ss.insertSheet(QLHH_STATE_SHEET); sh.hideSheet(); sh.getRange(1, 1, 1, 2).setValues([["key", "hash"]]); }
  return sh;
}

function qlhhLoadState_(sh) {
  var map = {};
  var n = sh.getLastRow();
  if (n < 2) return map;
  sh.getRange(2, 1, n - 1, 2).getValues().forEach(function (r) { if (r[0]) map[String(r[0])] = String(r[1]); });
  return map;
}

function qlhhSaveState_(sh, map) {
  var keys = Object.keys(map);
  sh.clearContents();
  var rows = [["key", "hash"]].concat(keys.map(function (k) { return [k, map[k]]; }));
  sh.getRange(1, 1, rows.length, 2).setValues(rows);
}

/** Gửi một lô; trả về kết quả JSON hoặc ném lỗi (đã ghi lý do, KHÔNG có khóa). */
function qlhhPost_(cfg, parcels, signature) {
  var res = UrlFetchApp.fetch(cfg.url + "/v1/import/sheet", {
    method: "post", contentType: "application/json; charset=utf-8", muteHttpExceptions: true,
    headers: { "X-Import-Key": cfg.key }, payload: JSON.stringify({ signature: signature, parcels: parcels }),
  });
  var code = res.getResponseCode();
  var text = res.getContentText();
  if (code === 200) return JSON.parse(text);
  var msg = "";
  try { msg = (JSON.parse(text).error || {}).message || ""; } catch (e) { msg = ""; }
  var err = new Error("Cloud báo lỗi " + code + (msg ? ": " + msg : ""));
  err.code = code;
  throw err;
}

/**
 * Hàm chạy theo lịch (trigger). full = true: gửi lại TẤT CẢ kiện (đối soát), cloud tự bỏ qua kiện không đổi.
 * Trả về chuỗi tóm tắt (cũng ghi vào nhật ký Apps Script).
 */
function qlhhSyncToCloud(full) {
  var lock = LockService.getScriptLock();
  if (!lock.tryLock(5000)) { console.log("QLHH: lần chạy trước chưa xong, bỏ qua."); return "busy"; }
  try {
    var cfg = qlhhProps_();
    if (!cfg.url || !cfg.key) throw new Error("Chưa đặt QLHH_CLOUD_URL hoặc QLHH_IMPORT_KEY trong Thuộc tính tập lệnh.");
    var src = qlhhSources_(cfg);
    var tabs = qlhhSheets_(src.source, cfg).map(qlhhReadTab_);
    var now = qlhhNowLocal_();
    var out = QLHH.sheetToParcels(tabs, { skipTypes: cfg.skipTypes, maxDays: cfg.maxDays, onlyReturns: cfg.onlyReturns, now: now.ms, year: now.year });

    var stateSheet = qlhhStateSheet_(src.stateBook);
    var state = full === true ? {} : qlhhLoadState_(stateSheet);
    var changed = [], hashes = {};
    out.parcels.forEach(function (p) {
      p.buyerNote = "";                       // không bao giờ gửi ghi chú người mua
      if (p.rawDetails) { delete p.rawDetails.buyerReturnNote; delete p.rawDetails.buyerOrderNote; delete p.rawDetails.buyerUsername; delete p.rawDetails.deliveryAddress; delete p.rawDetails.returnAddress; }
      var k = qlhhParcelKey_(p), h = qlhhHash_(p);
      hashes[k] = h;
      if (state[k] !== h) changed.push({ k: k, h: h, p: p });
    });

    var sent = 0, added = 0, updated = 0, ambiguous = 0, skippedDeleted = 0;
    var signature = qlhhHash_(Object.keys(hashes).sort().map(function (k) { return hashes[k]; }));
    for (var i = 0; i < changed.length; i += QLHH_BATCH) {
      var batch = changed.slice(i, i + QLHH_BATCH);
      var r;
      try { r = qlhhPost_(cfg, batch.map(function (x) { return x.p; }), signature); }
      catch (e) {
        // 401 (sai khóa), 429 (hết hạn mức ngày): dừng, lần sau gửi tiếp phần còn lại. Không thử lại dồn dập.
        console.log("QLHH: dừng sau " + sent + " kiện. " + e.message);
        break;
      }
      batch.forEach(function (x) { state[x.k] = x.h; });
      sent += batch.length; added += r.added; updated += r.updated; ambiguous += (r.ambiguous || []).length; skippedDeleted += r.skippedDeleted || 0;
    }
    // bỏ dấu của kiện không còn trên Sheet
    Object.keys(state).forEach(function (k) { if (!Object.prototype.hasOwnProperty.call(hashes, k)) delete state[k]; });
    qlhhSaveState_(stateSheet, state);

    var note = "QLHH: " + out.parcels.length + " kiện trên Sheet, gửi " + sent + "/" + changed.length + " kiện đổi (thêm " + added + ", cập nhật " + updated +
      (ambiguous ? ", " + ambiguous + " nhập nhằng cần kiểm tra trên app" : "") + (skippedDeleted ? ", " + skippedDeleted + " đã xóa nên bỏ qua" : "") + ").";
    console.log(note);
    return note;
  } finally {
    lock.releaseLock();
  }
}

/** Trigger mỗi 5 phút. Ban đêm (0h-7h giờ VN) chỉ chạy mỗi 30 phút để đỡ tốn hạn mức. */
function qlhhTimerSync() {
  var h = Number(Utilities.formatDate(new Date(), "Asia/Ho_Chi_Minh", "H"));
  var m = Number(Utilities.formatDate(new Date(), "Asia/Ho_Chi_Minh", "m"));
  if (h < 7 && m % 30 >= 5) return;
  qlhhSyncToCloud(false);
}

/** Trigger mỗi đêm: đối soát đầy đủ (gửi lại mọi kiện; cloud chỉ ghi kiện thật sự đổi). */
function qlhhNightlyFull() { qlhhSyncToCloud(true); }

/** Chạy MỘT lần bằng tay để tạo lịch tự động (xóa lịch QLHH cũ nếu có). */
function qlhhInstallTriggers() {
  ScriptApp.getProjectTriggers().forEach(function (t) {
    var f = t.getHandlerFunction();
    if (f === "qlhhTimerSync" || f === "qlhhNightlyFull") ScriptApp.deleteTrigger(t);
  });
  ScriptApp.newTrigger("qlhhTimerSync").timeBased().everyMinutes(5).create();
  ScriptApp.newTrigger("qlhhNightlyFull").timeBased().atHour(2).everyDays(1).inTimezone("Asia/Ho_Chi_Minh").create();
  return "Đã tạo lịch: mỗi 5 phút + đối soát lúc 2 giờ sáng.";
}

/**
 * Chẩn đoán (KHÔNG gửi gì lên cloud, KHÔNG ghi nội dung đơn ra nhật ký): tên tab, số dòng, dòng tiêu đề,
 * tab nào được đọc, và bao nhiêu kiện có mã đơn / mã yêu cầu / mã vận đơn.
 */
function qlhhDiagnose() {
  var cfg = qlhhProps_();
  var src = qlhhSources_(cfg);
  var picked = qlhhSheets_(src.source, cfg).map(function (s) { return s.getName(); });
  src.source.getSheets().forEach(function (sh) {
    var lastCol = sh.getLastColumn(), lastRow = sh.getLastRow();
    var header = lastCol > 0 && lastRow > 0 ? sh.getRange(1, 1, 1, lastCol).getDisplayValues()[0] : [];
    console.log("TAB [" + sh.getName() + "]" + (sh.isSheetHidden() ? " (ẩn)" : "") + (picked.indexOf(sh.getName()) >= 0 ? " ĐƯỢC ĐỌC" : " bỏ qua") +
      " | " + lastRow + " dòng | tiêu đề: " + header.map(function (h) { return "«" + h + "»"; }).join(" "));
  });
  var tabs = qlhhSheets_(src.source, cfg).map(qlhhReadTab_);
  var now = qlhhNowLocal_();
  var out = QLHH.sheetToParcels(tabs, { skipTypes: cfg.skipTypes, maxDays: cfg.maxDays, onlyReturns: cfg.onlyReturns, now: now.ms, year: now.year });
  var ps = out.parcels;
  var c = function (f) { return ps.filter(function (p) { return p[f]; }).length; };
  console.log("KẾT QUẢ: " + ps.length + " kiện | có mã đơn " + c("orderId") + " | có mã yêu cầu " + c("returnOrderId") + " | có mã vận đơn " + c("track") +
    " | dòng sau gộp " + out.build.rows.length + " | bỏ do quá cũ " + out.build.tooOld + " | bỏ theo loại " + out.build.skippedCancelTypes);
}

/** Chạy thử bằng tay (xem kết quả ở Nhật ký thực thi). */
function qlhhTestNow() { return qlhhSyncToCloud(false); }
