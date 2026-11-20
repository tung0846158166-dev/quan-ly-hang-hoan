// Chẩn đoán Salework — CHỈ QUAN SÁT.
// Mở một cửa sổ Salework bình thường để người dùng tự đăng nhập và tự mở các trang họ vẫn dùng hằng ngày (đơn hoàn / đơn huỷ TikTok).
// Module này không gửi yêu cầu nào, không bấm gì, không sửa gì. Nó chỉ ghi lại:
//   - địa chỉ đã được gọi (đã che mã đơn / mã định danh, chỉ giữ TÊN tham số, không giữ giá trị),
//   - mã trạng thái,
//   - TÊN các trường trong dữ liệu trả về và TÊN các cột của bảng đang hiện.
// Không ghi: mật khẩu, token, cookie, header, nội dung/giá trị dữ liệu, tên khách, địa chỉ, số điện thoại.
'use strict';

const ID_SEG = [/^\d{4,}$/, /^[0-9a-f]{16,}$/i, /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i];
function maskSegment(seg) {
  if (!seg) return seg;
  if (ID_SEG.some(r => r.test(seg))) return '{id}';
  if (seg.length >= 12 && /\d/.test(seg) && /[a-z]/i.test(seg)) return '{id}';
  return seg.length > 40 ? '{long}' : seg;
}

/** Địa chỉ -> { host, path, query:[tên tham số] }, đã che mã định danh. Chỉ nhận http(s). */
function sanitizeUrl(raw) {
  let u;
  try { u = new URL(String(raw)); } catch (e) { return null; }
  if (u.protocol !== 'https:' && u.protocol !== 'http:') return null;
  const path = u.pathname.split('/').map(maskSegment).join('/');
  const query = Array.from(new Set(Array.from(u.searchParams.keys()).map(k => (k.length > 40 ? '{long}' : k)))).sort();
  // phần sau # của trang web (vd /#/orders/123) cũng che như đường dẫn
  const hash = u.hash && u.hash.length > 1 ? '#' + u.hash.slice(1).split('?')[0].split('/').map(maskSegment).join('/') : '';
  return { host: u.host, path: path + hash, query };
}

/** Các đường dẫn khoá trong JSON (không có giá trị): "data.items[].order_id". Khoá giống mã định danh bị che. */
function collectKeyPaths(v, out, prefix = '', depth = 0) {
  if (depth > 4 || out.size > 300) return out;
  if (Array.isArray(v)) {
    if (v.length) collectKeyPaths(v[0], out, prefix + '[]', depth + 1);
    return out;
  }
  if (v && typeof v === 'object') {
    for (const k of Object.keys(v).slice(0, 80)) {
      const kk = maskSegment(k);
      const p = prefix ? prefix + '.' + kk : kk;
      out.add(p);
      collectKeyPaths(v[k], out, p, depth + 1);
    }
  }
  return out;
}

/** Cũng cho biết số phần tử của mảng lớn nhất (để biết trang trả bao nhiêu dòng), không có nội dung. */
function maxArrayLen(v, depth = 0) {
  if (depth > 4) return 0;
  if (Array.isArray(v)) return Math.max(v.length, v.length ? maxArrayLen(v[0], depth + 1) : 0);
  if (v && typeof v === 'object') return Math.max(0, ...Object.keys(v).slice(0, 80).map(k => maxArrayLen(v[k], depth + 1)));
  return 0;
}

const SKIP_TYPES = new Set(['Image', 'Font', 'Stylesheet', 'Media', 'Script', 'Ping', 'Manifest', 'Other', 'TextTrack', 'WebSocket', 'EventSource']);
const CHAT_HINT = /(chat|message|conversation|notif|socket|presence|heartbeat|keepalive)/i;

class SaleworkDiag {
  constructor(electron, opts = {}) {
    this.electron = electron;
    this.win = null;
    this.eps = new Map();       // "METHOD host/path" -> { n, status:Set, mime:Set, query:Set, keys:Set, rows:number, writes:boolean }
    this.pages = new Map();     // trang -> Set(tên cột)
    this.reqs = new Map();      // requestId -> { method, key, type }
    this.startedAt = 0;
    this.stoppedAt = 0;
    this.timer = null;
    this.onChange = opts.onChange || (() => {});
  }

  get running() { return !!(this.win && !this.win.isDestroyed()); }

  async start(startUrl) {
    const san = sanitizeUrl(startUrl);
    if (!san || !/^https:$/.test(new URL(startUrl).protocol)) throw new Error('Địa chỉ phải bắt đầu bằng https://');
    if (this.running) { this.win.focus(); return; }
    const { BrowserWindow } = this.electron;
    this.eps.clear(); this.pages.clear(); this.reqs.clear();
    this.startedAt = Date.now(); this.stoppedAt = 0;
    // Phiên riêng của chẩn đoán: không dùng chung đăng nhập/cookie với phần còn lại của app.
    this.win = new BrowserWindow({
      width: 1280, height: 860, title: 'Chẩn đoán Salework — chỉ quan sát',
      webPreferences: { partition: 'persist:salework-diag', contextIsolation: true, nodeIntegration: false, sandbox: true }
    });
    const wc = this.win.webContents;
    wc.setWindowOpenHandler(({ url }) => { try { wc.loadURL(url); } catch (e) {} return { action: 'deny' }; });
    const dbg = wc.debugger;
    dbg.attach('1.3');
    await dbg.sendCommand('Network.enable');
    dbg.on('message', (_e, method, params) => { this._onCdp(method, params).catch(() => {}); });
    this.timer = setInterval(() => this._readHeaders().catch(() => {}), 2500);
    this.win.on('closed', () => { this._finish(); });
    wc.loadURL(startUrl);
  }

  async _onCdp(method, p) {
    if (method === 'Network.requestWillBeSent') {
      if (SKIP_TYPES.has(p.type)) return;
      const s = sanitizeUrl(p.request.url);
      if (!s) return;
      const m = (p.request.method || 'GET').toUpperCase();
      const key = m + ' ' + s.host + s.path;
      this.reqs.set(p.requestId, { method: m, key, type: p.type });
      const e = this.eps.get(key) || { n: 0, status: new Set(), mime: new Set(), query: new Set(), keys: new Set(), rows: 0, type: p.type, noisy: CHAT_HINT.test(s.path) };
      e.n++; s.query.forEach(q => e.query.add(q));
      this.eps.set(key, e);
      this.onChange();
    } else if (method === 'Network.responseReceived') {
      const r = this.reqs.get(p.requestId); if (!r) return;
      const e = this.eps.get(r.key); if (!e) return;
      e.status.add(p.response.status); if (p.response.mimeType) e.mime.add(p.response.mimeType);
    } else if (method === 'Network.loadingFinished') {
      const r = this.reqs.get(p.requestId); if (!r) return;
      this.reqs.delete(p.requestId);
      const e = this.eps.get(r.key); if (!e || e.keys.size > 300) return;
      if (![...e.mime].some(t => /json/i.test(t))) return;
      if ((p.encodedDataLength || 0) > 8 * 1024 * 1024) return;
      const body = await this.win.webContents.debugger.sendCommand('Network.getResponseBody', { requestId: p.requestId });
      const text = body.base64Encoded ? Buffer.from(body.body, 'base64').toString('utf8') : body.body;
      let j; try { j = JSON.parse(text); } catch (err) { return; }
      collectKeyPaths(j, e.keys);
      e.rows = Math.max(e.rows, maxArrayLen(j));
    }
  }

  async _readHeaders() {
    if (!this.running) return;
    const wc = this.win.webContents;
    const page = sanitizeUrl(wc.getURL());
    if (!page) return;
    const heads = await wc.executeJavaScript(`(() => {
      const t = Array.from(document.querySelectorAll('th, [role="columnheader"], .ant-table-thead td')).map(x => (x.innerText || '').trim().replace(/\\s+/g, ' ')).filter(x => x && x.length <= 40);
      return Array.from(new Set(t)).slice(0, 80);
    })()`, true);
    if (!heads || !heads.length) return;
    const key = page.host + page.path;
    const set = this.pages.get(key) || new Set();
    heads.forEach(h => set.add(h));
    this.pages.set(key, set);
    this.onChange();
  }

  _finish() {
    if (this.timer) { clearInterval(this.timer); this.timer = null; }
    try { if (this.win && !this.win.isDestroyed()) this.win.webContents.debugger.detach(); } catch (e) {}
    if (!this.stoppedAt) this.stoppedAt = Date.now();
    this.onChange();
  }

  stop() {
    this._finish();
    if (this.running) { try { this.win.close(); } catch (e) {} }
  }

  status() {
    return { running: this.running, endpoints: this.eps.size, pages: this.pages.size, startedAt: this.startedAt };
  }

  /** Báo cáo dạng văn bản để xem/chép. Không có giá trị dữ liệu nào. */
  report() {
    const L = [];
    L.push('BÁO CÁO CHẨN ĐOÁN SALEWORK (chỉ tên trang, tên trường, tên cột — không có dữ liệu, mật khẩu hay token)');
    L.push('Thời gian: ' + new Date(this.startedAt || Date.now()).toISOString());
    L.push('');
    L.push('== CÁC CỘT CỦA BẢNG ĐÃ THẤY, THEO TRANG ==');
    if (!this.pages.size) L.push('(chưa thấy bảng nào — hãy mở trang danh sách đơn hoàn / đơn huỷ rồi đợi vài giây)');
    for (const [pg, cols] of this.pages) L.push(pg + '\n   ' + [...cols].join(' | '));
    L.push('');
    L.push('== CÁC YÊU CẦU DỮ LIỆU (đã che mã đơn) ==');
    const rows = [...this.eps.entries()].sort((a, b) => (a[1].noisy - b[1].noisy) || (b[1].keys.size - a[1].keys.size));
    for (const [k, e] of rows) {
      const write = !/^(GET|HEAD|OPTIONS) /.test(k);
      L.push(`${write ? '[GHI?] ' : ''}${e.noisy ? '[nền] ' : ''}${k}  x${e.n}  mã=${[...e.status].join(',') || '?'}${e.rows ? '  dòng=' + e.rows : ''}`);
      if (e.query.size) L.push('   tham số: ' + [...e.query].join(', '));
      if (e.keys.size) L.push('   trường: ' + [...e.keys].slice(0, 120).join(', '));
    }
    L.push('');
    L.push('Chú thích: [GHI?] = yêu cầu không phải đọc thuần (POST/PUT/DELETE…). Phần lớn trang danh sách vẫn dùng POST để tìm kiếm, nên cần xem từng yêu cầu. [nền] = trang tự gọi nền (chat, thông báo).');
    return L.join('\n');
  }
}

module.exports = { SaleworkDiag, sanitizeUrl, maskSegment, collectKeyPaths, maxArrayLen };
