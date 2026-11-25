/**
 * QLHH - bộ xử lý Google Sheet chạy NGAY TRONG Apps Script (JavaScript thuần, V8, không thư viện ngoài).
 * Bản JavaScript của v2: Core/Import/SheetPipeline.cs + ShopeeMapper.cs + các hàm LegacyText cần thiết.
 * Đầu vào: các tab (mảng dòng {tên cột: chữ}); đầu ra: danh sách KIỆN NHẬP (ImportedParcel) để đẩy lên cloud (POST /v1/import/sheet).
 * Phần đối chiếu với dữ liệu đã có chạy trên cloud (cloud/core/src/merge.ts). Kiểm chứng: cloud/golden/sheet-vectors.json do C# sinh.
 * Ngày giờ biểu diễn bằng "mili-giây theo giờ địa phương" (thành phần ngày giờ coi như UTC) để không phụ thuộc múi giờ máy chạy.
 */
var QLHH = (function () {
  "use strict";

  // ================================================================ chữ
  function removeTones(s) {
    return String(s == null ? "" : s).normalize("NFD").replace(/[̀-ͯ]/g, "").replace(/đ/g, "d").replace(/Đ/g, "D").toLowerCase();
  }
  function key(s) { return removeTones(s).replace(/[^a-z0-9]/g, ""); }
  function str(v) { return v == null ? "" : String(v); }
  function trim(v) { return str(v).trim(); }
  function pad2(s) { s = String(s); return s.length >= 2 ? s : ("00" + s).slice(-2); }

  // ================================================================ mã vận đơn
  function cleanTrackingCode(raw) {
    if (!raw) return "";
    var s = String(raw).trim();
    if (s.indexOf("http://") >= 0 || s.indexOf("https://") >= 0 || s.indexOf("spx.vn") >= 0 || s.indexOf("ghn.vn") >= 0) {
      var mp = /(?:spx|track|code|c|id|bill|order_code|s)=([A-Za-z0-9.\-_]+)/i.exec(s);
      if (mp && mp[1]) s = mp[1];
    }
    var pats = [
      /(SPXVN[0-9A-Za-z]{6,18}|VN2[4-9][0-9A-Za-z]{8,14})/i, /(SOC[0-9A-Za-z]{6,16})/i, /((?:NIVN|NLVN|SHPVN)[0-9A-Za-z]{6,16})/i,
      /(TTVN[0-9A-Za-z]{6,16})/i, /(SHOPEEVTP[0-9A-Za-z]{6,18}|VTP?[0-9A-Za-z]{6,15})/i, /(GHN[0-9A-Za-z]{6,14}|G8[0-9A-Za-z]{6,14}|GY[0-9A-Za-z]{6,10})/i,
      /(S[0-9]+[.\-][0-9A-Za-z.\-]{6,24})/i,
    ];
    for (var i = 0; i < pats.length; i++) { var m = pats[i].exec(s); if (m && m[1]) return m[1].toUpperCase(); }
    var mj = /(?:^|[^0-9])(8[0246][0-9]{10,13})(?![0-9])/.exec(s);
    if (mj) return mj[1];
    var cleaned = s.replace(/^[*"'<>#\s[\]()]+|[*"'<>#\s[\]()]+$/g, "").trim();
    if (cleaned.indexOf("/") >= 0 || cleaned.indexOf("|") >= 0 || cleaned.indexOf(";") >= 0) {
      var parts = cleaned.split(/[/|;]/);
      for (var j = 0; j < parts.length; j++) { var pc = cleanTrackingCode(parts[j]); if (pc) return pc; }
    }
    return cleaned.toUpperCase();
  }

  function normalizeScientificOrderId(val) {
    if (val == null) return "";
    var s = String(val).trim().replace(/^['"]+|['"]+$/g, "").trim();
    if (!s) return "";
    var m = /^([+-]?[0-9]+)(?:\.([0-9]+))?[eE]\+?([0-9]+)$/.exec(s);
    if (m && /[.+]/.test(s)) {
      var exp = parseInt(m[3], 10);
      if (exp <= 40) {
        var intPart = m[1].replace(/^[+-]+/, "");
        var frac = m[2] || "";
        return exp >= frac.length ? intPart + frac + new Array(exp - frac.length + 1).join("0") : intPart + frac.slice(0, exp);
      }
    }
    return s;
  }

  // ================================================================ số / tiền / ngày
  function parseQuantity(value, fallback) {
    if (fallback === undefined) fallback = 0;
    if (value == null || String(value).trim() === "") return fallback;
    var text = String(value).trim().replace(/\s/g, "");
    if (/^[0-9]+[.,]0{1,2}$/.test(text)) text = text.split(/[.,]/)[0];
    else if (/^[0-9]{1,3}(?:,[0-9]{3})+$/.test(text)) text = text.replace(/,/g, "");
    var q = /^[+-]?(\d+\.?\d*|\.\d+)([eE][+-]?\d+)?$/.test(text) ? Number(text) : NaN;
    if (!isFinite(q) || q !== Math.floor(q) || q < 0 || q > 9007199254740991 || q > 2147483647)
      throw new Error("Số lượng không hợp lệ: " + value + ". Cần số nguyên không âm.");
    return q;
  }

  function parseMoney(val) {
    var s = trim(val);
    if (!s) return 0;
    s = s.replace(/[.,][0-9]{1,2}$/, "");
    var digits = s.replace(/[^0-9]/g, "");
    if (!digits || digits.length > 18) return 0;
    return Number(digits);
  }

  function normalizeShortDate(val, currentYear) {
    var s = trim(val);
    if (!s) return "";
    var timeSuffix = "", datePart = s;
    var mt = /\s+([0-9]{1,2}:[0-9]{2}(?::[0-9]{2})?)$/.exec(s);
    if (mt) { timeSuffix = " " + mt[1]; datePart = s.slice(0, mt.index).trim(); }
    var mIso = /^([0-9]{4})[/\-.]([0-9]{1,2})[/\-.]([0-9]{1,2})/.exec(datePart);
    if (mIso) return pad2(mIso[3]) + "/" + pad2(mIso[2]) + "/" + mIso[1] + timeSuffix;
    var mShort = /^([0-9]{1,2})[/\-.]([0-9]{1,2})$/.exec(datePart);
    if (mShort) return pad2(mShort[1]) + "/" + pad2(mShort[2]) + "/" + currentYear + timeSuffix;
    var mFull = /^([0-9]{1,2})[/\-.]([0-9]{1,2})[/\-.]([0-9]{2,4})$/.exec(datePart);
    if (mFull) { var y = mFull[3]; if (y.length === 2) y = "20" + y; return pad2(mFull[1]) + "/" + pad2(mFull[2]) + "/" + y + timeSuffix; }
    return s;
  }

  // \b của .NET coi chữ Unicode là "chữ"; JS chỉ coi [A-Za-z0-9_]: tự kiểm biên bằng ký tự chữ/số Unicode.
  var WORD = /[\p{L}\p{N}_]/u;
  function isWord(ch) { return ch !== undefined && ch !== "" && WORD.test(ch); }
  function matchBounded(s, re) {   // re KHÔNG có \b: tự kiểm \b ở đầu (trước nhóm 1) và cuối
    var g = new RegExp(re.source, "g" + re.flags.replace("g", ""));
    var m;
    while ((m = g.exec(s)) !== null) {
      var start = m.index, end = m.index + m[0].length;
      var okStart = !isWord(s[start - 1]) || !isWord(s[start]) ? true : false;
      var okEnd = !isWord(s[end]) || !isWord(s[end - 1]) ? true : false;
      if (okStart && okEnd) return m;
      g.lastIndex = m.index + 1;
    }
    return null;
  }

  function extractDate(val, currentYear) {
    var s = trim(val);
    if (!s) return "";
    var timeStr = "";
    var mTime = /(?:\s|^)([0-9]{1,2}:[0-9]{2}(?::[0-9]{2})?)(?:\s|$)/.exec(s);
    if (mTime) {
      var p = mTime[1].split(":");
      var ss = p.length > 2 ? pad2(p[2]) : "";
      timeStr = pad2(p[0]) + ":" + pad2(p[1]) + (ss && ss !== "00" ? ":" + ss : "");
    }
    var suf = timeStr ? " " + timeStr : "";
    var dmy = matchBounded(s, /([0-9]{1,2})[/\-.]([0-9]{1,2})[/\-.]([0-9]{4})/);
    if (dmy) return pad2(dmy[1]) + "/" + pad2(dmy[2]) + "/" + dmy[3] + suf;
    var iso = matchBounded(s, /([0-9]{4})[/\-.]([0-9]{1,2})[/\-.]([0-9]{1,2})/);
    if (iso) return pad2(iso[3]) + "/" + pad2(iso[2]) + "/" + iso[1] + suf;
    var sh = matchBounded(s, /([0-9]{1,2})[/\-.]([0-9]{1,2})[/\-.]([0-9]{2})/);
    if (sh) return pad2(sh[1]) + "/" + pad2(sh[2]) + "/20" + sh[3] + suf;
    return normalizeShortDate(s, currentYear);
  }

  /** "Ngày giờ địa phương" -> số mili-giây (thành phần coi như UTC); null nếu không hợp lệ. */
  function makeDate(y, mo, d, h, mi, se) {
    if (y < 1 || y > 9999) return null;
    var t = new Date(0);
    t.setUTCFullYear(y, 0, 1);
    t.setUTCHours(0, 0, 0, 0);
    var base = t.getTime();
    var dt = new Date(base);
    dt.setUTCMonth(mo - 1);
    var ms = dt.getTime() + (d - 1) * 86400000 + h * 3600000 + mi * 60000 + se * 1000;
    var out = new Date(ms);
    if (out.getUTCFullYear() < 1 || out.getUTCFullYear() > 9999) return null;
    return ms;
  }

  function parseFullDate(val) {
    var s = trim(val);
    if (!s || /^dd[/\-.]mm/i.test(s)) return null;
    var g = function (m, i) { return m[i] !== undefined ? parseInt(m[i], 10) : 0; };
    var m = /^(\d{1,2})[/\-.](\d{1,2})[/\-.](\d{2,4})(?:\s+(\d{1,2}):(\d{1,2})(?::(\d{1,2}))?)?/.exec(s);
    if (m) {
      var y = parseInt(m[3], 10); if (y < 100) y += 2000;
      var d = makeDate(y, parseInt(m[2], 10), parseInt(m[1], 10), g(m, 4), g(m, 5), g(m, 6));
      if (d !== null) return d;
    }
    m = /^(\d{4})[/\-.](\d{1,2})[/\-.](\d{1,2})(?:\s+(\d{1,2}):(\d{1,2})(?::(\d{1,2}))?)?/.exec(s);
    if (m) {
      var d2 = makeDate(parseInt(m[1], 10), parseInt(m[2], 10), parseInt(m[3], 10), g(m, 4), g(m, 5), g(m, 6));
      if (d2 !== null) return d2;
    }
    // C# còn thử DateTime.TryParse(InvariantCulture): "M/d" hoặc "M-d" (kiểu Mỹ, năm hiện tại) và "HH:mm" (hôm nay).
    m = /^(\d{1,2})[/\-](\d{1,2})(?:\s+(\d{1,2}):(\d{2})(?::(\d{2}))?)?$/.exec(s);
    if (m) {
      var mo = parseInt(m[1], 10), dd = parseInt(m[2], 10), hh = g(m, 3), mm = g(m, 4), sc = g(m, 5);
      if (mo >= 1 && mo <= 12 && dd >= 1 && dd <= daysIn(currentYear, mo) && hh < 24 && mm < 60 && sc < 60) return makeDate(currentYear, mo, dd, hh, mm, sc);
      return null;
    }
    m = /^(\d{1,2}):(\d{2})(?::(\d{2}))?$/.exec(s);
    if (m && currentDay !== null && parseInt(m[1], 10) < 24 && parseInt(m[2], 10) < 60 && g(m, 3) < 60)
      return currentDay + parseInt(m[1], 10) * 3600000 + parseInt(m[2], 10) * 60000 + g(m, 3) * 1000;
    return null;
  }
  function daysIn(y, mo) { return new Date(Date.UTC(2000, mo, 0)).getUTCDate() === 29 && mo === 2 ? ((y % 4 === 0 && y % 100 !== 0) || y % 400 === 0 ? 29 : 28) : new Date(Date.UTC(2001, mo, 0)).getUTCDate(); }
  /** Năm/ngày "hiện tại" cho các ô ngày thiếu năm (như DateTime.Now của C#): đặt theo thời điểm chạy. */
  var currentYear = new Date().getFullYear(), currentDay = null;
  function setNow(nowMs) { var d = new Date(nowMs); currentYear = d.getUTCFullYear(); currentDay = makeDate(currentYear, d.getUTCMonth() + 1, d.getUTCDate(), 0, 0, 0); }

  function fmtDateTime(ms) {
    var d = new Date(ms);
    return pad2(d.getUTCDate()) + "/" + pad2(d.getUTCMonth() + 1) + "/" + d.getUTCFullYear() + " " + pad2(d.getUTCHours()) + ":" + pad2(d.getUTCMinutes());
  }

  // ================================================================ hãng vận chuyển
  function detectCarrierByTrackingCode(rawTrack) {
    var code = trim(rawTrack).toUpperCase();
    if (!code) return null;
    if (code.indexOf("SPXVN") === 0 || code.indexOf("SPX") === 0 || /^VN2[4-9][0-9A-Z]{8,14}$/.test(code)) return "SPX Express";
    if (code.indexOf(".") >= 0 || code.indexOf("GHTK") === 0) return "GHTK";
    if (code.indexOf("SHOPEEVTP") !== 0 && (code.indexOf("NIVN") === 0 || code.indexOf("SHP") === 0 || code.indexOf("NVSG") === 0 || code.indexOf("NVVN") === 0)) return "Ninja Van";
    if (code.indexOf("AHA") === 0) return "AhaMove";
    if (/^[A-Z]{2}[0-9]{9}VN$/.test(code) || (code.slice(-2) === "VN" && code.length === 13)) return "VNPost";
    if (code.indexOf("SHOPEEVTP") === 0 || code.indexOf("VTP") === 0 || code.indexOf("VT") === 0) return "Viettel Post";
    if (code.indexOf("TTVN") === 0 || code.indexOf("BEST") === 0) return "Best Express";
    if (/^01M[0-9A-Z]{20,25}$/.test(code)) return "Khác";
    if (/^[0-9]+$/.test(code)) {
      if (code.length === 12 && /^(80|82|84|86|66)/.test(code)) return "J&T Express";
      if (code.length >= 13 && code.length <= 14) return "Best Express";
      if (code.length === 12) return "Best Express";
      if (code.length >= 9 && code.length <= 11) return "Viettel Post";
    }
    if (code.indexOf("GHN") === 0 || /^GY[A-Z0-9]{6,8}$/.test(code) || (/^[A-Z0-9]{6,10}$/.test(code) && /[A-Z]/.test(code) && /[0-9]/.test(code))) return "GHN";
    return null;
  }

  // ================================================================ ghi chú khách / màu / size
  var BUYER_NOTE_DIRECT = ["Ghi chú của Người mua khi trả hàng", "Nhận xét từ Người mua", "Ghi chú của Người mua khi đặt hàng", "Ghi chú của khách",
    "Lưu ý từ người mua", "Ghi chú của người mua", "Ghi chú người mua", "Ghi chú khách hàng", "Lời nhắn từ người mua", "Buyer Message", "Customer Note", "Buyer Note"];
  function isNoneNote(v) { return /^(không|khong|none|-)$/i.test(removeTones(v)); }
  function extractBuyerNote(r) {
    var i, v;
    for (i = 0; i < BUYER_NOTE_DIRECT.length; i++) {
      if (Object.prototype.hasOwnProperty.call(r, BUYER_NOTE_DIRECT[i])) { v = trim(r[BUYER_NOTE_DIRECT[i]]); if (v && !isNoneNote(v)) return v; }
    }
    var ks = Object.keys(r);
    for (i = 0; i < ks.length; i++) {
      var kn = key(ks[i]);
      var hit = (kn.indexOf("nhanxet") >= 0 && kn.indexOf("nguoimua") >= 0) || (kn.indexOf("ghichu") >= 0 && (kn.indexOf("nguoimua") >= 0 || kn.indexOf("khach") >= 0))
        || kn === "buyermessage" || kn === "customernote" || kn === "buyernote";
      if (!hit) continue;
      if (/nguoiban|tien|phi|gia|lydo|lido/.test(kn)) continue;
      v = trim(r[ks[i]]);
      if (v && !isNoneNote(v)) return v;
    }
    return "";
  }

  var COLOR_WORDS = ["xanh navy", "xanh den", "xanh duong", "xanh la", "xanh reu", "xanh ngoc", "xanh coban", "xanh bo", "xanh nhat", "xanh dam", "xanh pastel", "xanh lam",
    "xanh than", "xanh nau", "xanh kem", "xam than", "xam dam", "xam nhat", "mau mo", "kaki", "retro", "mori", "urban", "koda",
    "den", "trang", "trang nga", "trang tinh", "do do", "do tuoi", "do man", "do ruou", "hong", "hong phan", "hong baby",
    "vang", "vang bo", "vang chanh", "nau", "nau dat", "nau bo", "nau tay", "nau socola",
    "be", "be nhat", "be dam", "kem", "kem sua", "xam", "xam tieu", "xam chuot", "xam khoi", "xam ghi", "ghi", "tim", "tim than", "cam", "cam dat", "reu"];
  var REASON_KEYWORDS = ["khong nhan", "nguoi nhan", "tu choi", "that bai", "khong thanh", "huy don", "hoan tien", "tra hang", "ly do", "be vo", "hu hong",
    "thieu hang", "doi y", "chua nhan", "lien lac", "dia chi", "sdt", "nghe may", "hen giao", "bom hang", "khong mua", "sai hang",
    "kem chat luong", "khac mo ta", "khong dung", "dong kiem", "thu tien", "chuyen hoan", "luu kho", "buyer cancelled", "delivery failed",
    "rejected", "return reason", "package lost"];

  function isReturnReasonText(v) {
    var s = trim(v);
    if (!s) return false;
    var n = removeTones(s);
    for (var i = 0; i < REASON_KEYWORDS.length; i++) if (n.indexOf(REASON_KEYWORDS[i]) >= 0) return true;
    return s.length > 30;
  }
  function isKnownColorName(v) {
    var n = removeTones(trim(v));
    if (!n) return false;
    for (var i = 0; i < COLOR_WORDS.length; i++) {
      var c = COLOR_WORDS[i];
      if (n === c || n.indexOf(c + " ") === 0 || (n.length > c.length && n.slice(-(c.length + 1)) === " " + c)) return true;
    }
    return false;
  }
  function isValidColorString(v) {
    var s = trim(v);
    if (!s) return false;
    if (/^[0-9]{4,}$/.test(s)) return false;
    if (isReturnReasonText(s)) return false;
    if (s.length > 25) return false;
    return !/^(xxxl|xxl|xl|xs|s|m|l|[2-5]xl|[0-9]{2}|freesize|free size|onesize|one size)$/i.test(removeTones(s));
  }
  function isValidSizeString(v) {
    var s = trim(v);
    if (!s) return false;
    if (isReturnReasonText(s)) return false;
    if (/^[0-9]{4,}$/.test(s)) return false;
    var n = removeTones(s);
    if (n === "freesize" || n === "free size" || n === "onesize" || n === "one size" || n === "fs") return true;
    if (s.length > 10) return false;
    if (s.split(/\s+/).filter(function (w) { return w.length > 0; }).length > 2) return false;
    if (isKnownColorName(s)) return false;
    return true;
  }

  // \b của .NET (Unicode) quanh chữ ASCII: dùng biên "không phải chữ/số Unicode" (lookbehind/lookahead có u).
  var B0 = "(?<![\\p{L}\\p{N}_])", B1 = "(?![\\p{L}\\p{N}_])";
  function stripQualitySuffix(v) {
    var s = str(v);
    s = s.replace(new RegExp("\\s*[\\-|/]\\s*premium\\s*cao\\s*c[aấ]p?" + B1, "giu"), "");
    s = s.replace(new RegExp("\\s*[\\-|/]\\s*cao\\s*c[aấ]p?" + B1, "giu"), "");
    s = s.replace(new RegExp("\\s*[\\-|/]\\s*premium" + B1, "giu"), "");
    s = s.replace(/\(\s*(?:pre\s*)?cao\s*c[aấ]p?\s*\)/gi, "");
    s = s.replace(/\(\s*premium\s*\)/gi, "");
    s = s.replace(new RegExp(B0 + "premium\\s*cao\\s*c[aấ]p?" + B1, "giu"), "");
    s = s.replace(/\s+premium$/gi, "");
    s = s.trim();
    s = s.replace(/^[\-,;/|]+|[\-,;/|]+$/g, "");
    return s.trim();
  }

  function parseProductAndVariation(rawProd, rawVar, rawSellerSku) {
    var cleanProd = trim(rawProd), cleanVar = trim(rawVar), cleanSku = trim(rawSellerSku);
    var combined = cleanProd + " " + cleanVar;
    var premium = "";
    if (/premium\s*cao\s*c[aấ]p?/i.test(combined)) premium = "Premium Cao Cấp";
    else if (/\(\s*premium\s*\)/i.test(combined)) premium = "Premium Cao Cấp";
    else if (new RegExp("[\\-|/]\\s*premium" + B1, "iu").test(combined) || /\s+premium$/i.test(cleanVar)) premium = "Premium Cao Cấp";

    if (cleanProd && cleanVar) {
      var varNoSuffix = stripQualitySuffix(cleanVar);
      var lowerProd = cleanProd.toLowerCase();
      var cands = [cleanVar.toLowerCase(), varNoSuffix.toLowerCase()];
      for (var ci = 0; ci < cands.length; ci++) {
        if (!cands[ci]) continue;
        var dash = " - " + cands[ci];
        if (lowerProd.length >= dash.length && lowerProd.slice(-dash.length) === dash) { cleanProd = cleanProd.slice(0, cleanProd.length - dash.length).trim(); break; }
      }
    }
    cleanProd = cleanProd.replace(/\s*[\-|/]\s*premium\s*cao\s*c[aấ]p?\s*$/i, "");
    cleanProd = cleanProd.replace(/\s*\(\s*premium\s*\)\s*$/i, "").trim();

    var mTrail = /^(.*?)\s+-\s+([^,\-]{1,35},\s*(?:XXXL|XXL|XL|XS|S|M|L|[2-5]XL|[0-9]{2}|Free\s*size))$/i.exec(cleanProd);
    if (mTrail) { cleanProd = mTrail[1].trim(); if (!cleanVar) cleanVar = mTrail[2].trim(); }

    if (premium && cleanProd && !/premium/i.test(cleanProd)) cleanProd = cleanProd + " - " + premium;

    var cleanedVar = stripQualitySuffix(cleanVar);
    var color = "", size = "", extra = "";
    if (cleanedVar && !/^[0-9]{4,}$/.test(cleanedVar) && !/^[0-9]+\.[0-9]+e\+[0-9]+$/i.test(cleanedVar)) {
      var mColor = /(?:màu\s*sắc|màu|mau\s*sac|mau|color)\s*[:\-]\s*([^,;/\n|]+)/i.exec(cleanedVar);
      var mSize = /(?:kích\s*thước|kích\s*cỡ|size|kich\s*thuoc|kich\s*co)\s*[:\-]\s*([^,;/\n|]+)/i.exec(cleanedVar);
      if (mColor && isValidColorString(mColor[1])) color = stripQualitySuffix(mColor[1]);
      if (mSize && isValidSizeString(mSize[1])) size = mSize[1].trim().toUpperCase();
      if (!color || !size) {
        var sizeExact = /^(XXXL|XXL|XL|XS|S|M|L|[2-5]XL|[0-9]{2}|Free\s*size|Freesize|One\s*size|[<>]\s*[0-9]+\s*kg)$/i;
        var parts = cleanedVar.split(/[,;/|]/).map(stripQualitySuffix).filter(function (x) { return x.length > 0; });
        for (var pi = 0; pi < parts.length; pi++) {
          var subs = parts[pi].split(/\s*-\s*/).map(stripQualitySuffix).filter(function (x) { return x.length > 0; });
          for (var si = 0; si < subs.length; si++) {
            var sp = subs[si];
            if (sizeExact.test(sp) && !size && isValidSizeString(sp)) size = sp.toUpperCase().replace(/\s+/g, "");
            else if (!color && isValidColorString(sp)) color = sp.replace(/^(?:màu\s*sắc|màu|mau\s*sac|mau|color)\s*[:\-]\s*/i, "").trim();
            else if (isReturnReasonText(sp) && !extra) extra = sp;
          }
        }
        if (!color && parts.length > 0 && isValidColorString(parts[0]) && !sizeExact.test(parts[0])) color = parts[0];
      }
    }

    if (!color && cleanProd) {
      var withoutPrem = stripQualitySuffix(cleanProd);
      var normProd = removeTones(withoutPrem);
      var sorted = COLOR_WORDS.slice().sort(function (a, b) { return b.length - a.length; });   // ổn định như OrderByDescending
      for (var wi = 0; wi < sorted.length; wi++) {
        var cw = sorted[wi];
        if (normProd.length > cw.length && normProd.slice(-(cw.length + 1)) === " " + cw) { color = withoutPrem.slice(withoutPrem.length - cw.length).trim(); break; }
      }
    }

    if ((!color || !size) && cleanSku && cleanSku.indexOf("_") >= 0) {
      var sk = cleanSku.split("_").map(function (x) { return x.trim(); }).filter(function (x) { return x.length > 0; });
      if (sk.length >= 2) {
        var last = sk[sk.length - 1];
        if (/^(XXXL|XXL|XL|XS|S|M|L|[2-5]XL|[0-9]{2})$/i.test(last) && !size) size = last.toUpperCase();
        if (sk.length >= 3 && !color) color = sk.slice(1, sk.length - 1).join(" ");
        if (!cleanProd && sk[0]) cleanProd = sk[0];
      }
    }
    return { product: cleanProd, color: color, size: size, extraNote: extra };
  }

  // ================================================================ SheetPipeline
  var DEFAULT_SKIP_TYPES = ["huy truoc chuan bi"];
  var TRACK_TAB_NAME = "Vận đơn huỷ";

  function parseItemsCell(text, totalQty) {
    var items = str(text).split(/\s*;\s*/).map(function (t) { return t.trim(); }).filter(function (t) { return t.length > 0; }).map(function (p) {
      var qty = 0, body = p;
      var mq = /\s*[×xX*]\s*(\d+)\s*$/.exec(body);
      if (mq) { qty = parseInt(mq[1], 10); body = body.slice(0, mq.index).trim(); }
      var dot = /\s+[·•]\s+/.exec(body);
      var name = dot ? body.slice(0, dot.index).trim() : body;
      var variation = dot ? body.slice(dot.index).replace(/^\s+[·•]\s+/, "").trim() : "";
      return { name: name, variation: variation, qty: qty };
    });
    if (items.length === 1 && items[0].qty === 0) {
      var digits = str(totalQty).replace(/[^\d]/g, "");
      var n = digits ? parseInt(digits, 10) : 0;
      items[0].qty = n > 0 && n <= 2147483647 ? n : 1;
    }
    items.forEach(function (it) { if (it.qty === 0) it.qty = 1; });
    return items;
  }

  function keepLatestRequestPerOrder(rows) {
    if (rows.length < 2) return { rows: rows, removed: 0 };
    var keys = Object.keys(rows[0]);
    function pick(names) { for (var i = 0; i < keys.length; i++) if (names.indexOf(key(keys[i])) >= 0) return keys[i]; return null; }
    var orderK = pick(["madon", "madonhang"]), claimK = pick(["ma", "matrahang", "masokhieunai", "mayeucau"]);
    var dateK = pick(["ngay", "ngayyeucau", "thoigianyeucau"]), statusK = pick(["trangthai", "trangthaive"]);
    if (orderK === null || claimK === null) return { rows: rows, removed: 0 };
    function V(r, k) { return k !== null && r[k] != null ? str(r[k]) : ""; }
    function ts(v) { var d = parseFullDate(v); return d === null ? 0 : d; }
    function inactive(r) { return /khach huy|da dong/.test(removeTones(V(r, statusK))); }
    var best = {};
    rows.forEach(function (r) {
      var o = V(r, orderK).trim(), c = V(r, claimK).trim();
      if (!o || !c) return;
      var cand = { c: c, active: inactive(r) ? 0 : 1, t: dateK !== null ? ts(V(r, dateK)) : 0 };
      var cur = best[o];
      if (!cur || cand.active > cur.active || (cand.active === cur.active && cand.t > cur.t)) best[o] = cand;
    });
    var out = rows.filter(function (r) {
      var o = V(r, orderK).trim(), c = V(r, claimK).trim();
      return !o || !c || best[o].c === c;
    });
    return { rows: out, removed: rows.length - out.length };
  }

  function tabKind(rows) {
    var ks = {};
    (rows.length > 0 ? Object.keys(rows[0]) : []).forEach(function (k) { ks[key(k)] = true; });
    if (ks.hang && ks.madon && ks.trangthaive) return "hanghoan";
    if (ks.matrahang && ks.madon) return "trahang";
    if (ks.madon && ks.loai && (ks.aihuy || ks.hanhtrinh)) return "huy";
    if (ks.madon && ks.mavandon) return "vandon";
    return "";
  }

  function trackMap(rows) {
    var map = {};
    rows.forEach(function (r) {
      var o = "", t = "";
      Object.keys(r).forEach(function (k) { var kk = key(k), v = trim(r[k]); if (kk === "madon") o = v; else if (kk === "mavandon") t = v; });
      if (o && t) map[o.toUpperCase()] = t;
    });
    return map;
  }

  function applyTrackMap(parcels, map) {
    var n = 0;
    if (Object.keys(map).length === 0) return 0;
    parcels.forEach(function (p) {
      var k = p.orderId.trim().toUpperCase();
      if (!p.track && p.orderId && Object.prototype.hasOwnProperty.call(map, k)) { p.track = map[k]; n++; }
    });
    return n;
  }

  function skipTypes(configured) {
    if (configured == null) return DEFAULT_SKIP_TYPES.slice();
    return String(configured).split(/[;\n]/).map(function (t) { return removeTones(t.trim()); }).filter(function (t) { return t.length > 0; });
  }

  function rowsToParcels(rows, kind, skip) {
    skip = skip || DEFAULT_SKIP_TYPES;
    function G(r) {
      var names = Array.prototype.slice.call(arguments, 1);
      var ks = Object.keys(r);
      for (var i = 0; i < ks.length; i++) if (names.indexOf(key(ks[i])) >= 0) { var v = trim(r[ks[i]]); if (v) return v; }
      return "";
    }
    var map = {}, order = [], skipped = 0;
    rows.forEach(function (r) {
      var orderId = G(r, "madon"); if (!orderId) return;
      var loai = G(r, "loai"), loaiN = removeTones(loai);
      if (kind === "huy" && skip.some(function (t) { return loaiN.indexOf(t) >= 0; })) { skipped++; return; }
      var p = Object.prototype.hasOwnProperty.call(map, orderId) ? map[orderId] : null;
      if (!p) {
        var ud = parseFullDate(G(r, "capnhat"));
        p = {
          orderId: orderId, src: kind, items: [], claimId: G(r, "ma", "matrahang"), track: kind === "hanghoan" || kind === "huy" ? G(r, "mavandon") : "",
          type: "", parcelStatus: "", requestStatus: "", reason: G(r, "lydo"), buyerNote: G(r, "khachghi"), whoCancelled: G(r, "aihuy"),
          journey: G(r, "hanhtrinh"), returnDate: G(r, "ngay", "ngayyeucau"), deliveredAt: G(r, "veshopluc"), deadline: G(r, "hankhieunai"),
          refund: G(r, "tien", "hoantien"), typeLabel: "", updatedAt: ud === null ? 0 : ud,
        };
        if (kind === "hanghoan") {
          p.typeLabel = loai; p.parcelStatus = G(r, "trangthaive");
          p.type = /bom|that bai|khong thanh cong/.test(loaiN) ? "bom" : loaiN.indexOf("huy") >= 0 ? "cancel" : "return";
        } else {
          p.requestStatus = G(r, "trangthai");
          p.type = kind === "trahang" ? "return" : loaiN.indexOf("bom") >= 0 ? "bom" : /tra hang|hoan tien/.test(loaiN) ? "return" : "cancel";
          p.typeLabel = loai;
        }
        map[orderId] = p; order.push(orderId);
      }
      if (kind === "hanghoan") Array.prototype.push.apply(p.items, parseItemsCell(G(r, "hang"), G(r, "sl")));
      else {
        var mq = /^\s*-?\d+/.exec(G(r, "sl"));
        var q = mq ? parseInt(mq[0], 10) : 0;
        p.items.push({ name: G(r, "tensanpham", "sanpham"), variation: "", qty: q !== 0 && isFinite(q) && Math.abs(q) <= 2147483647 ? q : 1 });
      }
    });
    return { parcels: order.map(function (k) { return map[k]; }), skipped: skipped };
  }

  function stableSortDesc(arr, f) {
    return arr.map(function (v, i) { return { v: v, i: i }; }).sort(function (a, b) { var x = f(b.v) - f(a.v); return x !== 0 ? x : a.i - b.i; }).map(function (x) { return x.v; });
  }

  function mergeSheetParcels(lists) {
    function rank(s) { return s === "hanghoan" ? 3 : s === "trahang" ? 2 : s === "huy" ? 1 : 0; }
    var by = {}, order = [];
    lists.forEach(function (list) { list.forEach(function (p) { if (!Object.prototype.hasOwnProperty.call(by, p.orderId)) { by[p.orderId] = []; order.push(p.orderId); } by[p.orderId].push(p); }); });
    return order.map(function (id) {
      var ps = by[id];
      var newest = stableSortDesc(ps, function (p) { return p.updatedAt; });
      function pickF(f) { for (var i = 0; i < newest.length; i++) { var v = f(newest[i]); if (v) return v; } return ""; }
      var m = {
        orderId: ps[0].orderId, items: [], claimId: pickF(function (p) { return p.claimId; }), track: pickF(function (p) { return p.track; }),
        parcelStatus: pickF(function (p) { return p.parcelStatus; }), requestStatus: pickF(function (p) { return p.requestStatus; }),
        reason: pickF(function (p) { return p.reason; }), buyerNote: pickF(function (p) { return p.buyerNote; }), whoCancelled: pickF(function (p) { return p.whoCancelled; }),
        journey: pickF(function (p) { return p.journey; }), returnDate: pickF(function (p) { return p.returnDate; }), deliveredAt: pickF(function (p) { return p.deliveredAt; }),
        deadline: pickF(function (p) { return p.deadline; }), refund: pickF(function (p) { return p.refund; }), typeLabel: pickF(function (p) { return p.typeLabel; }),
        type: "", src: "", updatedAt: 0,
      };
      var firstTyped = null;
      for (var i = 0; i < newest.length; i++) if (newest[i].type) { firstTyped = newest[i]; break; }
      m.type = ps.some(function (p) { return p.type === "bom"; }) ? "bom" : firstTyped && firstTyped.type ? firstTyped.type : "return";
      var ranked = stableSortDesc(ps, function (p) { return rank(p.src); });
      for (var j = 0; j < ranked.length; j++) if (ranked[j].items.some(function (it) { return it.name; })) { Array.prototype.push.apply(m.items, ranked[j].items); break; }
      var srcs = [];
      ps.forEach(function (p) { if (srcs.indexOf(p.src) < 0) srcs.push(p.src); });
      m.src = srcs.join(",");
      return m;
    });
  }

  /** now: số mili-giây "giờ địa phương" (xem đầu tệp). */
  function filterByDays(parcels, days, now) {
    var v = trim(days), cut = null;
    var dm = /^(\d{1,2})[/\-.](\d{1,2})[/\-.](\d{4})$/.exec(v);
    if (dm) cut = makeDate(parseInt(dm[3], 10), parseInt(dm[2], 10), parseInt(dm[1], 10), 0, 0, 0);
    else { var nm = /^\s*(\d+)/.exec(v); if (nm && parseInt(nm[1], 10) > 0) cut = now - parseInt(nm[1], 10) * 86400000; }
    if (cut === null) return { parcels: parcels, dropped: 0 };
    var kept = parcels.filter(function (p) { var d = parseFullDate(p.returnDate); return d === null || d >= cut; });
    return { parcels: kept, dropped: parcels.length - kept.length };
  }

  function claimDeadline(deliveredAt) {
    var d = parseFullDate(deliveredAt);
    return d === null ? "" : fmtDateTime(d + 24 * 3600000);
  }

  function toRows(parcels) {
    var rows = [];
    parcels.forEach(function (m) {
      var dl = claimDeadline(m.deliveredAt);
      var base = {
        "Mã đơn hàng": m.orderId, "Mã vận đơn": m.track, "Mã số khiếu nại": m.claimId, "Lý do trả hàng/hoàn tiền": m.reason,
        "Ghi chú của Người mua khi trả hàng": m.buyerNote, "Thời gian yêu cầu": m.returnDate, "Thời gian giao hàng thành công": m.deliveredAt,
        "Người bán cần phản hồi trước": dl ? dl : m.deadline, "Tổng số tiền hoàn trả": m.refund,
      };
      var status = m.parcelStatus ? m.parcelStatus : m.requestStatus;
      if (m.type === "bom") { base["Lý do hủy"] = "Giao hàng thất bại"; base["Trạng thái Trả hàng/Hoàn tiền"] = status; }
      else if (m.type === "cancel") base["Trạng thái đơn hàng"] = (m.typeLabel ? m.typeLabel + " · " : "") + (status ? status : "Đã hủy");
      else base["Trạng thái Trả hàng/Hoàn tiền"] = status;
      var items = m.items.length > 0 ? m.items : [{ name: "", variation: "", qty: 1 }];
      items.forEach(function (it, i) {
        var r = {};
        Object.keys(base).forEach(function (k) { r[k] = base[k]; });
        r["Tên sản phẩm"] = it.name; r["Tên phân loại hàng"] = it.variation; r["Số lượng"] = String(it.qty === 0 ? 1 : it.qty);
        if (i > 0) r["Tổng số tiền hoàn trả"] = "";
        rows.push(r);
      });
    });
    return rows;
  }

  function buildSheet(tabs, skipTypesConfig, maxDays, now) {
    setNow(now);
    var res = { rows: [], parcels: [], oldRequestsDropped: 0, skippedCancelTypes: 0, trackLookupTabs: 0, tracksFilled: 0, tooOld: 0, skippedTabs: [], otherRows: [] };
    var lists = [], other = [], tmap = {};
    tabs.forEach(function (tab) {
      if (tab.length === 0) { res.skippedTabs.push("tab"); return; }
      if (tabKind(tab) === "vandon") { var tm = trackMap(tab); Object.keys(tm).forEach(function (k) { tmap[k] = tm[k]; }); res.trackLookupTabs++; return; }
      var kl = keepLatestRequestPerOrder(tab);
      res.oldRequestsDropped += kl.removed;
      var kind = tabKind(kl.rows);
      if (kind) { var rp = rowsToParcels(kl.rows, kind, skipTypes(skipTypesConfig)); res.skippedCancelTypes += rp.skipped; lists.push(rp.parcels); }
      else Array.prototype.push.apply(other, kl.rows);
    });
    if (lists.length > 0) {
      var merged = mergeSheetParcels(lists);
      res.tracksFilled = applyTrackMap(merged, tmap);
      var f = filterByDays(merged, maxDays, now);
      res.tooOld = f.dropped;
      Array.prototype.push.apply(res.parcels, f.parcels);
      Array.prototype.push.apply(res.rows, toRows(f.parcels));
    }
    Array.prototype.push.apply(res.otherRows, other);
    Array.prototype.push.apply(res.rows, other);
    return res;
  }

  // ================================================================ ShopeeMapper
  function parcelKey(orderId, track, returnOrderId, returnTrack, forwardTrack) {
    var code = cleanTrackingCode(returnTrack ? returnTrack : track ? track : forwardTrack);
    var id = trim(returnOrderId).toUpperCase();
    return code ? "TRACK:" + code + (id ? ":RETURN:" + id : "") : id ? "RETURN:" + id : "ORDER:" + trim(orderId).toUpperCase();
  }
  function trackingCodesOf(p) {
    var out = [];
    [p.track, p.altTrack, p.forwardTrack, p.returnTrack].concat(p.trackingCodes).forEach(function (c) { var x = cleanTrackingCode(c); if (x && out.indexOf(x) < 0) out.push(x); });
    return out;
  }
  function parseTrackingColumn(row) {
    var raw = row["Tracking Codes"] ? row["Tracking Codes"] : row["Mã vận đơn lịch sử"] != null ? row["Mã vận đơn lịch sử"] : "";
    raw = str(raw);
    if (!raw) return [];
    var codes;
    try { codes = JSON.parse(raw); if (!Array.isArray(codes)) throw new Error("x"); } catch (e) { codes = raw.split(/[;,|]/); }
    return codes.map(function (c) { return cleanTrackingCode(c == null ? "" : String(c)); }).filter(function (c) { return c.length > 0; });
  }
  function newParcel() {
    return {
      shipDate: "", returnDate: "", recvDate: "", platform: "TikTok Shop", orderType: "", isPartialReturn: false, platformStatus: "", returnedAt: "",
      preOrder: "Đơn thường", orderId: "", returnOrderId: "", forwardTrack: "", returnTrack: "", trackingCodes: [], track: "", altTrack: "", carrier: "",
      price: 0, priceHasValue: false, priceNeedsReview: null, importModeSlots: {}, importSelectedPriceMode: "tong_gia_tri", priceModes: {}, priceModeFlags: {},
      status: "Chưa về", note: "", buyerNote: "", items: [], deadline: "", importPriceBasis: "", rawDetails: null,
    };
  }

  function mapShopee(rawRows, options) {
    options = options || {};
    var onlyReturns = options.onlyReturns !== false;
    var year = options.year;
    var result = { parcels: [], skippedSuccess: 0, multiItemAppended: 0 };
    if (rawRows.length === 0) return result;
    var keys = [], seen = {};
    rawRows.forEach(function (r) { Object.keys(r).forEach(function (k) { if (!seen[k]) { seen[k] = true; keys.push(k); } }); });

    function findKey(patterns, excludes) {
      excludes = excludes || [];
      for (var i = 0; i < patterns.length; i++) {
        var p = patterns[i].toLowerCase();
        for (var j = 0; j < keys.length; j++) {
          var n = key(keys[j]);
          if (excludes.some(function (ex) { return n.indexOf(ex.toLowerCase()) >= 0; })) continue;
          if (n.indexOf(p) >= 0) return keys[j];
        }
      }
      return null;
    }
    function moneyHeader(k) { return key(k).replace(/(?:vnd|usd)$/, ""); }

    var returnTrackKey = findKey(["mavandontrahang", "mvdtrahang", "returntracking"]);
    var forwardTrackKey = findKey(["mavdchieugiaogoc", "mavandongiaohang", "mavandon", "tracking", "mavan", "mvd", "matheodoi", "makienhang", "idgoihang", "packageid", "mabuugui", "sovandon", "trackingnumber", "trackingid"],
      ["mavandontrahang", "mvdtrahang", "returntracking"]);
    var trackKey = forwardTrackKey !== null ? forwardTrackKey : returnTrackKey;
    var orderIdKey = findKey(["madonhang", "orderid", "madon", "iddonhang", "iddon", "suborderid", "madonhangcon", "order"], ["masokhieunai", "makhieunai"]);
    var returnCarrierKey = findKey(["donvivanchuyentrahang"]);
    var forwardCarrierKey = findKey(["donvivanchuyengiaohang", "donvivanchuyen", "nhavanchuyen", "shippingprovider", "dvvc", "carrier", "donvigiaohang", "logisticsprovider", "kenhvanchuyen", "shippingcarrier"], ["donvivanchuyentrahang"]);
    var productKey = findKey(["tensanphamtrensan", "tensanpham", "sanpham", "tensp", "productname", "product", "tenhanghoa", "itemname", "tenmathang"], ["masanpham", "id", "masku", "skuid", "skusanpham"]);
    var varKey = findKey(["tenphanloaitrensan", "tenphanloaihang", "tenphanloai", "tuychonbienthe", "tenbienthe", "bienthe", "phanloaihang", "phanloai", "variationname", "variation", "thuoctinh", "specification", "skuname"],
      ["maphanloai", "masanpham", "masku", "skuid", "skuphanloai", "idbienthe", "idphanloai", "idgoihang", "packageid"]);
    var sellerSkuKey = findKey(["masanphamsalework", "skuphanloai", "sellersku", "maskuphanloai", "masku", "masanpham", "skusanpham"]);
    var skuIdKey = findKey(["skuid", "idbienthe", "maphanloai"]);
    var explicitColorKey = findKey(["mausac", "mau", "color", "colour"], ["masku", "skuid", "id", "lydo", "reason", "note", "ghichu", "chitiet", "huy", "hoan", "tra", "maphanloai"]);
    var explicitSizeKey = findKey(["kichthuoc", "kichco", "size"], ["masku", "skuid", "id", "lydo", "reason", "note", "ghichu", "chitiet", "huy", "hoan", "tra", "status", "trangthai", "maphanloai"]);
    var noteKey = findKey(["lidotrahanghoantien", "lydotrahanghoantien", "lydohoantien", "lydotrahang", "lydohuy", "lidohuy", "lydo", "lido", "reason", "returnreason", "chitiethoantien", "chitietlydo", "noidung", "motanoidung"],
      ["ghichucuanguoimuakhitrahang", "nhanxettunguoimua", "ghichucuanguoimuakhidathang", "ghichucuakhach"]);
    var returnShipmentStatusKey = findKey(["trangthaitrahang"], ["trangthaitrahanghoantien"]);
    var returnMethodKey = findKey(["phuongan", "phuonganhoantien"]);
    var claimIdKey = findKey(["masokhieunai", "makhieunai", "claimid", "returnid", "returnorderid"]);
    var buyerKey = findKey(["nguoimua", "buyer", "buyerusername", "username", "khachhang", "tenkhachhang"], ["nguoiban"]);
    var orderDateKey = findKey(["ngaydathang", "thoidiemdathang", "ngaydat", "orderdate"]);
    var claimTimeKey = findKey(["thoigiankhieunai", "thoidiemkhieunai", "claimtime"]);
    var isFullReturnKey = findKey(["trahanghoantientoanbodonhang", "isallreturn"]);
    var returnSuccessTimeKey = findKey(["thoigianhoantrahangthanhcong", "ngayhoantrathanhcong", "thoigiangiaohangthanhcong"]);
    var deliveryAddressKey = findKey(["diachinhanhang", "deliveryaddress"]);
    var returnAddressKey = findKey(["diachilayhanghoantra", "returnaddress"]);
    var sellerResponseDueKey = findKey(["nguoibancanphanhoitruoc", "hancanxuly", "thoihanphanhoi"]);
    var claimReasonKey = findKey(["lidokhieunai", "lydokhieunai"]);
    var sellerNoteKey = findKey(["ghichucuanguoiban"]);
    var compensationKey = findKey(["sotiendenbu", "denbu"]);
    var orderHandlingTypeKey = findKey(["loaixulydonhang", "xulydonhang"]);
    var totalValueKey = findKey(["tonggiatri", "tonggiatridonhang", "totalamount"]);
    var paymentMethodKey = findKey(["phuongthucthanhtoan", "paymentmethod"]);
    var buyerOrderNoteKey = findKey(["ghichucuanguoimuakhidathang"]);
    var revenueKey = findKey(["doanhthu", "revenue"]);
    var forwardShippingFeeKey = findKey(["phivanchuyenchieugiao"]);
    var returnShippingFeeKey = findKey(["phivanchuyenchieuhoantra"]);
    var refundAmountKey = findKey(["tongsotienhoantra", "sotienhoantra", "refundamount"]);
    var refundTimeKey = findKey(["thoigianhoantien", "ngayhoantien", "refundtime"]);
    var returnQtyPatterns = ["soluonghoan", "soluongsanphamduochoantra", "returnquantity", "returnedquantity", "returnqty", "returnedqty"];
    var returnQtyKey = findKey(returnQtyPatterns);
    var orderQtyKey = findKey(["soluong", "quantity", "sl", "soluongmathang", "itemquantity", "qty"], returnQtyPatterns.concat(["tongsoluong"]));

    var priceColumns = [];
    var priceGroups = [
      ["order", ["tonggiatridonhang", "tongtienhangsauchietkhau", "doanhthuuoctinh", "tongsotiennguoimuathanhtoan", "tongtienthanhtoan", "ordertotal"]],
      ["order", ["tongsotienhoantra", "refundamount"]],
      ["order", ["tonggiatri", "totalamount", "orderamount", "tongtien"]],
      ["line", ["thanhtien", "tonggiaban", "linesubtotal", "linetotal", "itemsubtotal", "subtotal"]],
      ["unit", ["giauda", "giauudai", "giaban", "giatien", "dongia", "skuunitprice", "unitprice", "price", "giasantmdt", "giasalework", "giagoc", "giabanle"]],
    ];
    priceGroups.forEach(function (pg) {
      pg[1].forEach(function (alias) {
        var k = null;
        for (var i = 0; i < keys.length; i++) if (moneyHeader(keys[i]) === alias) { k = keys[i]; break; }
        if (k !== null && !priceColumns.some(function (c) { return c.key === k; })) priceColumns.push({ key: k, kind: pg[0] });
      });
    });

    var shipDateKeys = [findKey(["thoigiangui", "ngaygui", "shippedtime"]), findKey(["thoigiandong", "ngaydong", "rtstime"]), findKey(["thoigianxuly", "ngayxuly"]),
      findKey(["ngaytaodon", "thoigiantao", "ngaydat", "orderdate", "thoidiemtao", "thoidiemdathang", "createdtime", "paidtime"]),
      findKey(["timerequested", "thoigiankhieunai", "thoigianyeucau", "thoigianhoan", "ngayyeucau"])].filter(function (k) { return k !== null; });
    var returnDateKeys = [findKey(["timerequested", "thoigiankhieunai", "ngayhuythanhcong", "ngayhoan", "thoigianhoan", "ngaytrahang", "ngayhuy", "thoigianhoantien", "refundtime", "thoigianyeucau", "ngayyeucau"]),
      findKey(["ngaygiaohangthanhcong", "thoigiangiao", "deliverytime"])].filter(function (k) { return k !== null; });
    var statusKey = findKey(["trangthaidonhang", "orderstatus", "status", "tinhtrang", "trangthaigoihang"], ["trangthaitrahang", "trangthaitrahanghoantien", "trangthaikhieunai"]);
    var returnStatusKey = findKey(["trangthaitrahanghoantien", "trangthaihoantien", "trangthaikhieunai", "trangthaitrahang", "returnstatus", "refundstatus"]);
    var cancelReasonColKey = findKey(["lydohuy", "lidohuy"]);
    var preOrderKey = findKey(["hangdattruoc", "dattruoc", "preorder", "loaidonhang"], ["loaixuly", "xuly"]);

    function get(row, k) { return k !== null && k !== undefined && row[k] != null ? str(row[k]) : ""; }
    function has(row, k) { return k !== null && k !== undefined && row[k] != null && str(row[k]) !== ""; }
    function isNone(v) { return /^(không|khong|none|-)$/i.test(removeTones(v)); }
    function tone(s) { return removeTones(s); }

    var groups = {}, groupOrder = [], zeroReturned = {};
    var lastGroupKey = "", lastSkippedRow = false, lastSourceFileId = null, hasLastSource = false;

    rawRows.forEach(function (row) {
      var sourceId = row.__sourceFileId != null ? row.__sourceFileId : null;
      if (!hasLastSource || sourceId !== lastSourceFileId) { lastGroupKey = ""; lastSkippedRow = false; lastSourceFileId = sourceId; hasLastSource = true; }

      function clean(k) { return k === null ? "" : cleanTrackingCode(get(row, k).trim()); }
      var rawReturnTrack = clean(returnTrackKey), rawForwardTrack = clean(forwardTrackKey);
      var rawTrack = rawReturnTrack ? rawReturnTrack : rawForwardTrack ? rawForwardTrack : clean(trackKey);
      var rawAltTrack = rawReturnTrack && rawForwardTrack && rawReturnTrack !== rawForwardTrack ? rawForwardTrack : "";
      var rawOrderId = orderIdKey !== null ? normalizeScientificOrderId(get(row, orderIdKey)) : "";
      var rawProd = get(row, productKey).trim(), rawVar = get(row, varKey).trim(), rawSellerSku = get(row, sellerSkuKey).trim(), rawClaimId = get(row, claimIdKey).trim();

      var isContinuation = false;
      if (!rawTrack && !rawOrderId) {
        var previous = Object.prototype.hasOwnProperty.call(groups, lastGroupKey) ? groups[lastGroupKey] : null;
        var previousClaim = previous && previous.p.rawDetails ? trim(previous.p.rawDetails.claimId).toUpperCase() : "";
        var sameClaim = !rawClaimId || rawClaimId.toUpperCase() === previousClaim;
        if (previous && !lastSkippedRow && sameClaim && (rawProd || rawVar || rawSellerSku)) isContinuation = true;
        else if (!rawClaimId) return;
      }

      if (!isContinuation && onlyReturns) {
        var rawOrderSt = statusKey !== null ? tone(get(row, statusKey)) : "";
        var rawRetSt = returnStatusKey !== null ? tone(get(row, returnStatusKey)) : "";
        var retQtyVal = returnQtyKey !== null ? parseQuantity(get(row, returnQtyKey)) : 0;
        if (rawOrderSt) {
          var isDelivered = /thanh cong|da giao|hoan thanh|delivered|completed/.test(rawOrderSt);
          var hasActive = rawRetSt.length > 0 || retQtyVal > 0;
          var isRetOrCancel = hasActive || /hoan|huy|tra|that thu|that bai|khong thanh|cancel|return|refund/.test(rawOrderSt);
          if (isDelivered && !isRetOrCancel) { result.skippedSuccess++; lastSkippedRow = true; return; }
        }
      }
      if (!isContinuation) lastSkippedRow = false;

      var groupKey = isContinuation ? lastGroupKey : parcelKey(rawOrderId, rawTrack, rawClaimId, rawReturnTrack, "");
      if (!isContinuation) lastGroupKey = groupKey;

      var rawQty = 1, hasExplicitReturnQty = false;
      if (returnQtyKey !== null && has(row, returnQtyKey) && get(row, returnQtyKey).trim()) {
        var rq = parseQuantity(get(row, returnQtyKey));
        var explicitReturnContext = rawReturnTrack.length > 0 || get(row, claimIdKey).trim().length > 0 || get(row, returnStatusKey).trim().length > 0;
        var cancelOrFailed = /cancel|huy|failed_delivery|that bai|that thu/.test(tone(get(row, "__sourceFileName") + " " + get(row, statusKey)));
        if (rq === 0 && (explicitReturnContext || !cancelOrFailed)) { zeroReturned[groupKey] = true; return; }
        if (rq > 0) { rawQty = rq; hasExplicitReturnQty = true; }
      }
      if (!hasExplicitReturnQty && orderQtyKey !== null && has(row, orderQtyKey)) { var oq = parseQuantity(get(row, orderQtyKey)); if (oq > 0) rawQty = oq; }

      var fullText = get(row, isFullReturnKey).trim().toLowerCase();
      var isShopeePartial = fullText.indexOf("một phần") >= 0 || fullText.indexOf("mot phan") >= 0 || fullText.indexOf("partial") >= 0;
      var isPartialQty = false;
      if (hasExplicitReturnQty && orderQtyKey !== null && has(row, orderQtyKey) && parseQuantity(get(row, orderQtyKey)) > rawQty) isPartialQty = true;

      var rawPreOrder = "Đơn thường";
      if (preOrderKey !== null && has(row, preOrderKey)) {
        var po = tone(get(row, preOrderKey)).trim();
        if (po.indexOf("dat truoc") >= 0 || po.indexOf("preorder") >= 0 || po.indexOf("pre-order") >= 0 || po === "co" || po === "yes" || po === "true") rawPreOrder = "Hàng đặt trước";
      }

      var entry = Object.prototype.hasOwnProperty.call(groups, groupKey) ? groups[groupKey] : null;
      var totals = entry ? entry.priceTotals : {};
      priceColumns.forEach(function (column) {
        if (!has(row, column.key) || !get(row, column.key).trim()) return;
        var value = parseMoney(get(row, column.key));
        var had = Object.prototype.hasOwnProperty.call(totals, column.key);
        var prev = had ? totals[column.key] : 0;
        totals[column.key] = column.kind === "order" ? (had ? prev : value) : prev + value * (column.kind === "unit" ? rawQty : 1);
      });
      var chosen = null, ci;
      for (ci = 0; ci < priceColumns.length; ci++) if (Object.prototype.hasOwnProperty.call(totals, priceColumns[ci].key) && totals[priceColumns[ci].key] > 0) { chosen = priceColumns[ci]; break; }
      if (!chosen) for (ci = 0; ci < priceColumns.length; ci++) if (Object.prototype.hasOwnProperty.call(totals, priceColumns[ci].key)) { chosen = priceColumns[ci]; break; }
      var rawPrice = chosen ? totals[chosen.key] : 0;

      var rawShipDate = "", rawReturnDate = "", di, ex;
      for (di = 0; di < shipDateKeys.length; di++) if (get(row, shipDateKeys[di])) { ex = extractDate(get(row, shipDateKeys[di]), year); if (ex) { rawShipDate = ex; break; } }
      for (di = 0; di < returnDateKeys.length; di++) if (get(row, returnDateKeys[di])) { ex = extractDate(get(row, returnDateKeys[di]), year); if (ex) { rawReturnDate = ex; break; } }
      if (!rawReturnDate && rawShipDate) rawReturnDate = rawShipDate;
      if (!rawShipDate && rawReturnDate) rawShipDate = rawReturnDate;

      var noteParts = [];
      var returnShipStRaw = get(row, returnShipmentStatusKey).trim();
      var returnShipSt = isNone(returnShipStRaw) ? "" : returnShipStRaw;
      var returnMethod = get(row, returnMethodKey).trim();
      var mainReason = get(row, noteKey).trim();
      var cancelReasonText = get(row, cancelReasonColKey).trim();

      var buyerComment = extractBuyerNote(row);
      if (!buyerComment) {
        var abks = ["Ghi chú của Người mua khi trả hàng", "Nhận xét từ Người mua", "Ghi chú của khách", "Ghi chú của Người mua khi đặt hàng", "Ghi chú"];
        for (var ai = 0; ai < abks.length; ai++) {
          var target = removeTones(abks[ai]).replace(/[^a-z0-9]/g, "");
          var foundK = null;
          for (var kk = 0; kk < keys.length; kk++) if (removeTones(keys[kk]).replace(/[^a-z0-9]/g, "") === target) { foundK = keys[kk]; break; }
          if (foundK !== null && get(row, foundK)) { var bv = get(row, foundK).trim(); if (bv && !isNone(bv)) { buyerComment = bv; break; } }
        }
      }
      if (isNone(buyerComment)) buyerComment = "";

      if (returnShipSt) noteParts.push("[" + returnShipSt + "]");
      else if (returnMethod && tone(returnMethod).indexOf("hoan tien ngay") >= 0) noteParts.push("[" + returnMethod + "]");
      if (mainReason) noteParts.push(mainReason);
      else if (cancelReasonText && noteParts.indexOf(cancelReasonText) < 0) noteParts.push(cancelReasonText);

      var rawNote = noteParts.join(" | ");
      var rawStatusText = get(row, statusKey).trim();
      if (rawStatusText && !rawNote) {
        var stNorm = tone(rawStatusText);
        if (stNorm === "that thu") rawNote = "THẤT THU (Hoàn về kho)";
        else if (stNorm === "huy" || stNorm.indexOf("da huy") >= 0) rawNote = "Đơn HỦY";
        else if (stNorm.indexOf("that bai") >= 0 || stNorm.indexOf("khong thanh") >= 0) rawNote = "Giao hàng thất bại";
      }

      var pv = parseProductAndVariation(rawProd, rawVar, rawSellerSku);
      var cleanProd = pv.product, color = pv.color, size = pv.size;
      if (pv.extraNote && !rawNote) rawNote = pv.extraNote;

      if (explicitColorKey !== null && get(row, explicitColorKey)) {
        var cVal = get(row, explicitColorKey).trim();
        if (isValidColorString(cVal)) color = cVal; else if (isReturnReasonText(cVal) && !rawNote) rawNote = cVal;
      }
      if (explicitSizeKey !== null && get(row, explicitSizeKey)) {
        var sVal = get(row, explicitSizeKey).trim();
        if (isValidSizeString(sVal)) size = sVal.toUpperCase(); else if (isReturnReasonText(sVal) && !rawNote) rawNote = sVal;
      }
      if (size && !color && isKnownColorName(size)) { color = size; size = ""; }
      else if (color && !size && isValidSizeString(color) && !isKnownColorName(color)) { size = color; color = ""; }

      if (/^\d{4,}$/.test(color) || isReturnReasonText(color)) { if (isReturnReasonText(color) && !rawNote) rawNote = color; color = ""; }
      if (/^\d{4,}$/.test(size) || isReturnReasonText(size)) { if (isReturnReasonText(size) && !rawNote) rawNote = size; size = ""; }

      if ((!color || !size) && cleanProd) {
        var prodParts = cleanProd.split(/[\-|/]/).map(function (s) { return s.trim(); }).filter(function (s) { return s.length > 0; });
        if (prodParts.length > 1) {
          var sizeRe = /^(XXXL|XXL|XL|XS|S|M|L|[2-5]XL|\d{2}|Free\s*size)$/i;
          var qualityRe = /(?:premium|cao\s*c[aấ]p|th[eê]u|l[oô]ng\s*c[aá]o|d[eệ]t\s*kim|local\s*brand|lavi\s*studio|unisex)/i;
          prodParts.slice(1).forEach(function (part) {
            if (qualityRe.test(part)) return;
            if (sizeRe.test(part) && !size && isValidSizeString(part)) size = part.toUpperCase().replace(/\s+/g, "");
            else if (!color && !/^\d+$/.test(part) && part.length < 25 && !isReturnReasonText(part) && isKnownColorName(part)) color = part;
          });
        }
      }

      var rawReturnCarrier = get(row, returnCarrierKey).trim(), rawForwardCarrier = get(row, forwardCarrierKey).trim();
      var rawCarrierFromExcel = rawReturnCarrier ? rawReturnCarrier : rawForwardCarrier;
      var carrier = [rawReturnCarrier, detectCarrierByTrackingCode(rawReturnTrack), rawForwardCarrier, detectCarrierByTrackingCode(rawForwardTrack), "SPX Express"].filter(function (x) { return x; })[0];
      if (rawCarrierFromExcel) {
        var cu = removeTones(rawCarrierFromExcel).toUpperCase();
        if (cu.indexOf("J&T") >= 0 || cu.indexOf("JT") >= 0) carrier = "J&T Express";
        else if (cu.indexOf("NINJA") >= 0) carrier = "Ninja Van";
        else if (cu.indexOf("SHOPEE") >= 0 || cu.indexOf("SPX") >= 0 || cu.indexOf("TRONG NGAY") >= 0) carrier = "SPX Express";
        else if (cu.indexOf("TIET KIEM") >= 0 || cu.indexOf("GHTK") >= 0) carrier = "GHTK";
        else if (cu.indexOf("NHANH") >= 0 || cu.indexOf("GHN") >= 0) carrier = "GHN";
        else if (cu.indexOf("VIETTEL") >= 0 || cu.indexOf("VTP") >= 0) carrier = "Viettel Post";
        else if (cu.indexOf("BEST") >= 0 || rawTrack.indexOf("TTVN") === 0) carrier = "Best Express";
        else if (cu.indexOf("VNPOST") >= 0 || cu.indexOf("BUU DIEN") >= 0) carrier = "VNPost";
        else if (cu.indexOf("AHA") >= 0) carrier = "AhaMove";
        else if (cu.indexOf("GREEN") >= 0 || cu.indexOf("XANH") >= 0 || cu.indexOf("GRAB") >= 0 || cu.indexOf("BE ") >= 0) carrier = "Khác";
      }

      var srcFileNorm = get(row, "__sourceFileName").toLowerCase();
      var stT = tone(rawStatusText);
      var retT = returnStatusKey !== null ? tone(get(row, returnStatusKey)) : "";
      var cancelT = tone(cancelReasonText + " " + mainReason + " " + rawNote);
      var hasRetQtyCol = returnQtyKey !== null && parseQuantity(get(row, returnQtyKey)) > 0;
      var orderType;
      if (stT.indexOf("that thu") >= 0) orderType = "THẤT THU";
      else if (srcFileNorm.indexOf("failed_delivery") >= 0) orderType = "Giao không thành công";
      else if (srcFileNorm.indexOf("return_refund") >= 0) orderType = "Trả hàng/Hoàn tiền";
      else if (srcFileNorm.indexOf("cancel") >= 0) orderType = cancelT.indexOf("giao hang that bai") >= 0 || cancelT.indexOf("giao khong thanh cong") >= 0 ? "Giao không thành công" : "Đơn hủy";
      else if (cancelT.indexOf("giao hang that bai") >= 0 || cancelT.indexOf("giao khong thanh cong") >= 0 || cancelT.indexOf("failed delivery") >= 0
        || stT.indexOf("that bai") >= 0 || stT.indexOf("khong thanh") >= 0) orderType = "Giao không thành công";
      else if ((retT && retT !== "khong" && retT !== "none") || hasRetQtyCol || (returnMethod && tone(returnMethod) !== "khong") || (returnShipSt && tone(returnShipSt) !== "khong")) orderType = "Trả hàng/Hoàn tiền";
      else if (stT.indexOf("huy") >= 0 || stT.indexOf("cancel") >= 0 || cancelReasonText) orderType = "Đơn hủy";
      else orderType = "Trả hàng/Hoàn tiền";

      function statusFrom(k) { var v = get(row, k).trim(); return v && !isNone(v) ? v : ""; }
      var platformStatus = statusFrom(returnStatusKey);
      if (!platformStatus) platformStatus = statusFrom(returnShipmentStatusKey);
      if (!platformStatus) platformStatus = statusFrom(statusKey);
      if (!platformStatus) platformStatus = orderType === "Trả hàng/Hoàn tiền" ? "Chờ bạn xác nhận hàng trả về" : orderType === "Đơn hủy" ? "Đã hủy" : orderType === "Giao không thành công" ? "Giao hàng thất bại" : "Chờ xử lý";

      if (!entry) {
        var S = function (k) { return get(row, k).trim(); };
        var M = function (k) { return k === null ? "0" : String(parseMoney(get(row, k))); };
        entry = { p: newParcel(), priceTotals: {} };
        var raw = {
          claimId: S(claimIdKey), buyerUsername: S(buyerKey), orderDate: S(orderDateKey), claimTime: S(claimTimeKey), returnStatus: S(returnStatusKey),
          isFullReturn: S(isFullReturnKey), returnMethod: S(returnMethodKey), returnReason: S(noteKey), buyerReturnNote: buyerComment, refundAmount: M(refundAmountKey),
          refundTime: S(refundTimeKey), returnShippingStatus: S(returnShipmentStatusKey), returnCarrier: rawReturnCarrier, returnTrack: rawReturnTrack,
          returnSuccessTime: S(returnSuccessTimeKey), deliveryAddress: S(deliveryAddressKey), returnAddress: S(returnAddressKey), sellerResponseDue: S(sellerResponseDueKey),
          claimReason: S(claimReasonKey), sellerNote: S(sellerNoteKey), compensation: M(compensationKey), forwardCarrier: rawForwardCarrier,
          orderHandlingType: S(orderHandlingTypeKey), forwardTrack: rawForwardTrack, totalValue: M(totalValueKey), paymentMethod: S(paymentMethodKey),
          buyerOrderNote: S(buyerOrderNoteKey), revenue: M(revenueKey), forwardShippingFee: M(forwardShippingFeeKey), returnShippingFee: M(returnShippingFeeKey),
        };
        var p0 = entry.p;
        p0.platform = "Shopee"; p0.platformStatus = platformStatus; p0.orderType = orderType; p0.isPartialReturn = isShopeePartial || isPartialQty; p0.preOrder = rawPreOrder;
        p0.deadline = sellerResponseDueKey !== null ? get(row, sellerResponseDueKey).trim() : "";
        p0.orderId = rawOrderId; p0.track = rawTrack; p0.altTrack = rawAltTrack; p0.forwardTrack = rawForwardTrack; p0.returnTrack = rawReturnTrack;
        p0.carrier = carrier; p0.shipDate = rawShipDate; p0.returnDate = rawReturnDate; p0.note = rawNote; p0.buyerNote = buyerComment; p0.rawDetails = raw;
        groups[groupKey] = entry; groupOrder.push(groupKey);
        Object.keys(totals).forEach(function (k) { entry.priceTotals[k] = totals[k]; });
      } else result.multiItemAppended++;

      var p = entry.p;
      var tc = trackingCodesOf(p).concat([rawTrack]).concat(parseTrackingColumn(row)).concat([rawAltTrack]);
      var dedup = [];
      tc.forEach(function (c) { if (c && dedup.indexOf(c) < 0) dedup.push(c); });
      p.trackingCodes = dedup;
      p.priceHasValue = chosen !== null;
      p.importPriceBasis = chosen && chosen.kind === "order" ? "order" : "line";
      if (isShopeePartial || isPartialQty) p.isPartialReturn = true;
      if (!p.platformStatus && platformStatus) p.platformStatus = platformStatus;
      if (rawPreOrder === "Hàng đặt trước") p.preOrder = rawPreOrder;
      if (rawForwardTrack && !p.forwardTrack) p.forwardTrack = rawForwardTrack;
      if (rawReturnTrack && !p.returnTrack) p.returnTrack = rawReturnTrack;
      if (rawTrack && !p.track) p.track = rawTrack;
      if (rawAltTrack && !p.altTrack) p.altTrack = rawAltTrack;
      if (rawOrderId && !p.orderId) p.orderId = rawOrderId;
      p.price = rawPrice;
      if (rawShipDate && !p.shipDate) p.shipDate = rawShipDate;
      if (rawReturnDate && !p.returnDate) p.returnDate = rawReturnDate;
      if (rawNote && !p.note) p.note = rawNote;
      if (buyerComment && !p.buyerNote) p.buyerNote = buyerComment;

      if (cleanProd || color || size) {
        var sku = rawSellerSku ? rawSellerSku : skuIdKey !== null ? get(row, skuIdKey).trim() : "";
        p.items.push({ product: cleanProd, sellerSku: sku, skuId: null, color: color, size: size, qty: rawQty, returnedQty: rawQty });
      }
    });

    groupOrder.forEach(function (gk) {
      var p = groups[gk].p;
      var claim = p.rawDetails && p.rawDetails.claimId != null ? p.rawDetails.claimId : "";
      if (zeroReturned[parcelKey(p.orderId, p.track, claim, p.returnTrack, p.forwardTrack)]) p.isPartialReturn = true;
      if (p.items.length === 0) p.items.push({ product: "", color: "", size: "", qty: 1, returnedQty: 0, sellerSku: null, skuId: null });
      if (p.priceHasValue) p.priceNeedsReview = false;
      result.parcels.push(p);
    });
    return result;
  }

  // ================================================================ toàn bộ: các tab -> kiện nhập
  /**
   * tabs: mảng tab, mỗi tab là mảng dòng {tên cột: chữ}. opts: { skipTypes, maxDays, onlyReturns, now (ms giờ địa phương), year }.
   * Trả { parcels: kiện nhập (ImportedParcel), build: số liệu gộp tab }.
   */
  function sheetToParcels(tabs, opts) {
    var build = buildSheet(tabs, opts.skipTypes == null ? null : opts.skipTypes, opts.maxDays || "", opts.now);
    var mapped = mapShopee(build.rows, { onlyReturns: opts.onlyReturns !== false, year: opts.year });
    return { parcels: mapped.parcels, build: build, skippedSuccess: mapped.skippedSuccess };
  }

  return {
    removeTones: removeTones, key: key, cleanTrackingCode: cleanTrackingCode, normalizeScientificOrderId: normalizeScientificOrderId,
    parseQuantity: parseQuantity, parseMoney: parseMoney, extractDate: extractDate, normalizeShortDate: normalizeShortDate, parseFullDate: parseFullDate,
    makeDate: makeDate, detectCarrierByTrackingCode: detectCarrierByTrackingCode, parseProductAndVariation: parseProductAndVariation,
    parseItemsCell: parseItemsCell, keepLatestRequestPerOrder: keepLatestRequestPerOrder, tabKind: tabKind, trackMap: trackMap,
    buildSheet: buildSheet, toRows: toRows, mapShopee: mapShopee, sheetToParcels: sheetToParcels, TRACK_TAB_NAME: TRACK_TAB_NAME,
  };
})();

if (typeof module !== "undefined" && module.exports) module.exports = QLHH;
