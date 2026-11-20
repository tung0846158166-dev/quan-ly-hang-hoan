// Bộ đọc .xlsx tối giản cho file xuất của Salework (không cần thư viện ngoài).
// Lý do: file Salework có ô chữ không khai báo kiểu (t) — một số trình đọc coi đó là số và báo lỗi.
// Ở đây: không có t mà nội dung không phải số -> coi là chữ. Mọi giá trị trả về là CHUỖI (giữ nguyên mã 18 chữ số).
'use strict';
const zlib = require('zlib');

function unzip(buf) {
  let eocd = -1;
  for (let i = buf.length - 22; i >= Math.max(0, buf.length - 65557); i--) {
    if (buf.readUInt32LE(i) === 0x06054b50) { eocd = i; break; }
  }
  if (eocd < 0) throw new Error('Không phải file .xlsx hợp lệ');
  const count = buf.readUInt16LE(eocd + 10);
  let p = buf.readUInt32LE(eocd + 16);
  const files = {};
  for (let n = 0; n < count; n++) {
    if (buf.readUInt32LE(p) !== 0x02014b50) throw new Error('File .xlsx bị hỏng');
    const method = buf.readUInt16LE(p + 10), csize = buf.readUInt32LE(p + 20);
    const nlen = buf.readUInt16LE(p + 28), elen = buf.readUInt16LE(p + 30), clen = buf.readUInt16LE(p + 32);
    const lho = buf.readUInt32LE(p + 42);
    const name = buf.toString('utf8', p + 46, p + 46 + nlen);
    const ds = lho + 30 + buf.readUInt16LE(lho + 26) + buf.readUInt16LE(lho + 28);
    const raw = buf.subarray(ds, ds + csize);
    files[name] = () => (method === 0 ? raw : zlib.inflateRawSync(raw)).toString('utf8');
    p += 46 + nlen + elen + clen;
  }
  return files;
}

function unesc(s) {
  return s.replace(/&(#x[0-9a-f]+|#\d+|lt|gt|amp|quot|apos);/gi, (m, e) => {
    if (e[0] === '#') return String.fromCodePoint(e[1] === 'x' || e[1] === 'X' ? parseInt(e.slice(2), 16) : parseInt(e.slice(1), 10));
    return { lt: '<', gt: '>', amp: '&', quot: '"', apos: "'" }[e.toLowerCase()];
  });
}
function textOf(xml) {   // gộp các đoạn <t> (chuỗi dùng chung / inlineStr)
  const out = []; const re = /<t(?:\s[^>]*)?>([\s\S]*?)<\/t>/g; let m;
  while ((m = re.exec(xml))) out.push(m[1]);
  return unesc(out.join(''));
}
function colIndex(ref) {
  const L = /^[A-Z]+/.exec(ref)[0]; let n = 0;
  for (const ch of L) n = n * 26 + (ch.charCodeAt(0) - 64);
  return n - 1;
}

/** -> { sheetName, matrix: string[][] } của trang đầu tiên */
function readXlsxMatrix(buf) {
  const z = unzip(buf);
  const wb = z['xl/workbook.xml'] ? z['xl/workbook.xml']() : '';
  const sn = /<sheet\s[^>]*name="([^"]*)"/.exec(wb);
  const sheetKey = Object.keys(z).filter(k => /^xl\/worksheets\/sheet\d+\.xml$/.test(k)).sort((a, b) => parseInt(a.match(/\d+/)[0]) - parseInt(b.match(/\d+/)[0]))[0];
  if (!sheetKey) throw new Error('File .xlsx không có trang dữ liệu');
  const shared = [];
  if (z['xl/sharedStrings.xml']) {
    const re = /<si>([\s\S]*?)<\/si>|<si\/>/g; let m; const x = z['xl/sharedStrings.xml']();
    while ((m = re.exec(x))) shared.push(m[1] ? textOf(m[1]) : '');
  }
  const xml = z[sheetKey]();
  const matrix = [];
  const rowRe = /<row\b[^>]*?(?:\/>|>([\s\S]*?)<\/row>)/g; let rm;
  while ((rm = rowRe.exec(xml))) {
    const cells = []; const body = rm[1] || '';
    const cRe = /<c\b([^>]*?)(?:\/>|>([\s\S]*?)<\/c>)/g; let cm;
    while ((cm = cRe.exec(body))) {
      const attrs = cm[1], inner = cm[2] || '';
      const ref = /\br="([A-Z]+\d+)"/.exec(attrs); if (!ref) continue;
      const t = (/\bt="(\w+)"/.exec(attrs) || [])[1];
      let val = '';
      if (t === 'inlineStr') val = textOf(inner);
      else {
        const v = /<v(?:\s[^>]*)?>([\s\S]*?)<\/v>/.exec(inner);
        if (v) val = t === 's' ? (shared[parseInt(v[1], 10)] || '') : unesc(v[1]);
      }
      cells[colIndex(ref[1])] = val;
    }
    for (let i = 0; i < cells.length; i++) if (cells[i] === undefined) cells[i] = '';
    matrix.push(cells);
  }
  return { sheetName: sn ? unesc(sn[1]) : '', matrix };
}

/** -> mảng đối tượng {tên cột: giá trị}; cột trùng tên được đánh _1, _2 (giống SheetJS); dòng trống bị bỏ. */
function readXlsxRows(buf) {
  const { matrix } = readXlsxMatrix(buf);
  if (!matrix.length) return [];
  const seen = {};
  const head = matrix[0].map((h, i) => {
    let name = String(h || '').trim() || ('__col' + i);
    if (seen[name] === undefined) seen[name] = 0; else name = name + '_' + (++seen[name]);
    return name;
  });
  const rows = [];
  for (let r = 1; r < matrix.length; r++) {
    const o = {}; let any = false;
    head.forEach((h, i) => { const v = matrix[r][i] === undefined ? '' : matrix[r][i]; o[h] = v; if (String(v).trim()) any = true; });
    if (any) rows.push(o);
  }
  return rows;
}

/** Bỏ các cột thông tin cá nhân khách hàng (app không cần) — gọi trước khi đưa dữ liệu sang giao diện. */
const PII_HEADERS = ['Tên khách hàng', 'Số điện thoại', 'Địa chỉ', 'Tỉnh', 'Huyện', 'Ghi chú vận chuyển', 'Người tạo đơn'];
function dropPersonalColumns(rows) {
  return rows.map(r => { const o = Object.assign({}, r); PII_HEADERS.forEach(h => { delete o[h]; }); return o; });
}

module.exports = { readXlsxRows, readXlsxMatrix, dropPersonalColumns, PII_HEADERS };
