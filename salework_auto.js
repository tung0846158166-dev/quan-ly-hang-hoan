// Tự lấy danh sách "Đang chờ hoàn" từ Salework — lặp lại đúng thao tác người dùng vẫn làm bằng tay:
//   mở trang Đơn hoàn -> tab "Đang chờ hoàn" -> "Xuất excel" -> "Theo bộ lọc" -> "Bắt Đầu Xuất" -> file tự tải về.
// Cửa sổ chạy ẩn, dùng phiên đăng nhập riêng của app (partition) — KHÔNG đụng TikTok, KHÔNG bấm "Cập nhật đơn hoàn".
// Chỉ lưu: địa chỉ trang Đơn hoàn. Không lưu mật khẩu / token / cookie ngoài kho cookie của chính cửa sổ.
'use strict';
const fs = require('fs');
const os = require('os');
const path = require('path');
const { readXlsxRows, dropPersonalColumns } = require('./salework_xlsx');

const PARTITION = 'persist:salework-auto';
const FORBIDDEN = /c[aậ]p\s*nh[aậ]t/i;       // "Cập nhật đơn hoàn" — bắt Salework đồng bộ lại TikTok: tuyệt đối không bấm
const sleep = ms => new Promise(r => setTimeout(r, ms));

/** Mã chạy TRONG trang Salework (chuỗi). Trả về JSON-able. Chỉ đọc chữ trên giao diện và bấm đúng phần tử được yêu cầu. */
function pageScript(action, arg) {
  const code = function (action, arg) {
    const norm = s => String(s || '').normalize('NFC').replace(/\s+/g, ' ').trim().toLowerCase();
    const visible = el => !!(el.offsetWidth || el.offsetHeight || (el.getClientRects && el.getClientRects().length));
    const FORBID = /c[aậ]p\s*nh[aậ]t/i;
    const SEL = 'button,a,[role=tab],[role=button],[role=radio],li,span,label,div,p';
    function find(text, mode, within) {
      const t = norm(text);
      const root = within || document;
      const all = Array.from(root.querySelectorAll(SEL)).filter(el => visible(el));
      const hit = all.filter(el => { const x = norm(el.textContent); return mode === 'prefix' ? x.startsWith(t) : x === t; });
      // lấy phần tử sâu nhất (không có con nào cũng khớp), ưu tiên phần tử ngắn nhất
      const leaf = hit.filter(el => !hit.some(o => o !== el && el.contains(o)));
      leaf.sort((a, b) => norm(a.textContent).length - norm(b.textContent).length);
      return leaf;
    }
    function dialogRoot() {
      const d = Array.from(document.querySelectorAll('[role=dialog],.ant-modal,.modal,.el-dialog,.v-dialog,.MuiDialog-root,.q-dialog'))
        .filter(visible);
      return d.length ? d[d.length - 1] : null;
    }
    function click(el) {
      let target = el;
      const inp = el.querySelector && el.querySelector('input[type=radio]');
      if (inp) target = inp;
      target.scrollIntoView && target.scrollIntoView({ block: 'center' });
      ['mousedown', 'mouseup'].forEach(n => target.dispatchEvent(new MouseEvent(n, { bubbles: true, cancelable: true, view: window })));
      target.click();
    }
    if (action === 'state') {
      const body = norm(document.body ? document.body.innerText : '');
      return {
        url: location.href,
        hasPassword: !!document.querySelector('input[type=password]'),
        hasExport: find('xuất excel', 'exact').length > 0,
        hasTab: find('đang chờ hoàn', 'prefix').length > 0,
        channelTiktok: /k[eê]nh\s*tiktok/.test(body) || /tiktok/.test(body.slice(0, 4000)),
        title: document.title,
        path: location.host + location.pathname.replace(/\d{5,}/g, '#'),
        buttons: Array.from(new Set(Array.from(document.querySelectorAll('button,[role=tab],[role=button],a')).filter(visible)
          .map(el => String(el.textContent || '').replace(/\s+/g, ' ').trim()).filter(t => t && t.length <= 30))).slice(0, 14)
      };
    }
    function locate(a) {
      if (FORBID.test(a.text)) return { error: 'từ chối bấm nút có chữ "cập nhật"' };
      const within = a.inDialog ? dialogRoot() : null;
      if (a.inDialog && !within) return { error: 'chưa thấy cửa sổ xuất' };
      const c = find(a.text, a.mode || 'exact', within);
      if (!c.length) return { error: 'không thấy "' + a.text + '"' };
      if (FORBID.test(c[0].textContent)) return { error: 'phần tử khớp có chữ "cập nhật", bỏ qua' };
      return { el: c[0], count: c.length };
    }
    if (action === 'click') {
      const L = locate(arg);
      if (L.error) return { ok: false, error: L.error };
      click(L.el);
      return { ok: true, count: L.count };
    }
    if (action === 'mark') {   // chế độ xem: viền đỏ quanh nút sắp bấm (chưa bấm)
      const L = locate(arg);
      if (L.error) return { ok: false, error: L.error };
      L.el.scrollIntoView && L.el.scrollIntoView({ block: 'center' });
      L.el.style.outline = '5px solid #ef4444'; L.el.style.outlineOffset = '2px';
      return { ok: true };
    }
    if (action === 'banner') {
      let b = document.getElementById('__app_banner');
      if (!b && document.body) {
        b = document.createElement('div'); b.id = '__app_banner';
        b.style.cssText = 'position:fixed;top:0;left:0;right:0;z-index:2147483647;background:#dc2626;color:#fff;font:bold 16px sans-serif;padding:10px;text-align:center;pointer-events:none';
        document.body.appendChild(b);
      }
      if (b) b.textContent = arg.text;
      return { ok: true };
    }
    if (action === 'dialogHas') {
      const d = dialogRoot();
      return { ok: !!d && norm(d.textContent).includes(norm(arg.text)) };
    }
    return { ok: false, error: 'action lạ' };
  };
  return '(' + code.toString() + ')(' + JSON.stringify(action) + ',' + JSON.stringify(arg === undefined ? null : arg) + ')';
}

/** Salework hay mở app con (Kho, Chat…) bằng cửa sổ mới. Giữ tất cả trong MỘT cửa sổ để địa chỉ lưu / bấm nút luôn ở đúng cửa sổ. */
function keepSingleWindow(win) {
  try {
    win.webContents.setWindowOpenHandler(({ url }) => {
      if (/^https:\/\//i.test(String(url || ''))) { try { win.loadURL(url).catch(() => {}); } catch (e) {} }
      return { action: 'deny' };
    });
  } catch (e) {}
}

class SaleworkAuto {
  /** electron: module electron (hoặc bản giả khi test). cfgDir: thư mục lưu địa chỉ trang. */
  constructor(electron, cfgDir, opts) {
    this.electron = electron;
    this.cfgFile = path.join(cfgDir, 'salework_auto.json');
    this.opts = Object.assign({ pageTimeout: 45000, stepPause: 2500, downloadTimeout: 120000, watchPause: 2500 }, opts || {});
    this.abort = false;
    this.curWin = null;
    this.loginWin = null;
    this.running = false;
  }
  _cfg() { try { return JSON.parse(fs.readFileSync(this.cfgFile, 'utf8')) || {}; } catch (e) { return {}; } }
  _save(c) { try { fs.mkdirSync(path.dirname(this.cfgFile), { recursive: true }); fs.writeFileSync(this.cfgFile, JSON.stringify(c)); } catch (e) {} }
  status() { const c = this._cfg(); return { watch: c.watch !== false, configured: !!c.url, url: c.url || '', lastOk: c.lastOk || null, lastNote: c.lastNote || '', running: this.running, loginOpen: !!(this.loginWin && !this.loginWin.isDestroyed()) }; }

  /** Cửa sổ HIỆN: người dùng tự đăng nhập Salework và mở trang Đơn hoàn (kênh Tiktok). Bấm "Lưu" -> nhớ địa chỉ trang hiện tại. */
  openLogin(startUrl) {
    const { BrowserWindow } = this.electron;
    const url = String(startUrl || this._cfg().url || 'https://salework.net').trim();
    if (!/^https:\/\//i.test(url)) return { success: false, error: 'Địa chỉ phải bắt đầu bằng https://' };
    if (this.loginWin && !this.loginWin.isDestroyed()) {
      // đã mở sẵn: nếu người dùng dán link mới thì chuyển sang link đó
      if (startUrl && String(startUrl).trim() && this.loginWin.webContents.getURL() !== url) this.loginWin.loadURL(url).catch(() => {});
      this.loginWin.focus(); return { success: true };
    }
    const win = new BrowserWindow({ width: 1280, height: 860, title: 'Salework — đăng nhập rồi mở trang Đơn hoàn', webPreferences: { partition: PARTITION, contextIsolation: true, nodeIntegration: false, sandbox: true } });
    this.loginWin = win;
    keepSingleWindow(win);
    win.on('closed', () => { this.loginWin = null; });
    win.loadURL(url).catch(() => {});
    return { success: true };
  }
  /** Lưu địa chỉ trang đang mở trong cửa sổ đăng nhập. */
  saveCurrentPage() {
    const w = this.loginWin;
    if (!w || w.isDestroyed()) return { success: false, error: 'Cửa sổ đăng nhập chưa mở' };
    const url = w.webContents.getURL();
    if (!/^https:\/\//i.test(url) || /login|signin|dang-?nhap/i.test(url)) return { success: false, error: 'Hãy mở trang Đơn hoàn (đã đăng nhập) rồi lưu.' };
    const c = this._cfg(); c.url = url; this._save(c);
    return { success: true, url };
  }
  setWatch(on) { const c = this._cfg(); c.watch = !!on; this._save(c); return { success: true, watch: !!on }; }
  /** Dừng ngay: huỷ lượt đang chạy, đóng cửa sổ, không bấm thêm gì. */
  stop() {
    this.abort = true;
    try { if (this.curWin && !this.curWin.isDestroyed()) this.curWin.destroy(); } catch (e) {}
    return { success: true };
  }
  closeLogin() { if (this.loginWin && !this.loginWin.isDestroyed()) this.loginWin.close(); return { success: true }; }
  async forget() {
    const c = this._cfg(); delete c.url; this._save(c);
    try { await this.electron.session.fromPartition(PARTITION).clearStorageData(); } catch (e) {}
    return { success: true };
  }

  async _exec(win, action, arg) { return win.webContents.executeJavaScript(pageScript(action, arg), true); }
  _diag(st) {
    if (!st) return '';
    return ' — trang đang ở: ' + (st.path || '?') + ', tiêu đề "' + String(st.title || '').slice(0, 60) + '", thấy các nút: [' + (st.buttons || []).join(' | ') + ']';
  }
  /** Thao tác có kiểm soát: ở chế độ xem thì viền đỏ + chờ vài giây (để người dùng kịp dừng) rồi mới bấm. */
  async _act(win, arg, label, watch) {
    if (this.abort) throw new Error('Đã dừng theo yêu cầu.');
    if (watch) {
      try { await this._exec(win, 'banner', { text: '🤖 App đang: ' + label + ' — đóng cửa sổ này hoặc bấm "Dừng" trong app để DỪNG NGAY' }); } catch (e) {}
      const m = await this._exec(win, 'mark', arg);
      if (!m.ok) throw new Error(m.error);
      await sleep(this.opts.watchPause);
      if (this.abort) throw new Error('Đã dừng theo yêu cầu.');
    }
    const r = await this._exec(win, 'click', arg);
    if (!r.ok) throw new Error(r.error);
    return r;
  }
  async _waitFor(win, pred, timeout, label) {
    const t0 = Date.now(); let last = null;
    while (Date.now() - t0 < timeout) {
      if (this.abort) throw new Error('Đã dừng theo yêu cầu.');
      try { last = await this._exec(win, 'state'); if (pred(last)) return last; } catch (e) {}
      await sleep(700);
    }
    const err = new Error(label + this._diag(last)); err.state = last; throw err;
  }

  /** Chạy một lượt. -> { success, rows, count, fileName } hoặc { success:false, step, error } */
  async run() {
    if (this.running) return { success: false, step: 'busy', error: 'Đang chạy lượt trước.' };
    const cfg = this._cfg();
    if (!cfg.url) return { success: false, step: 'config', error: 'Chưa cài đặt: bấm "Đăng nhập Salework" và lưu trang Đơn hoàn.' };
    this.running = true; this.abort = false;
    const watch = cfg.watch !== false;
    const { BrowserWindow } = this.electron;
    let win = null, tmp = null, step = 'mở trang';
    const note = (ok, msg) => { const c = this._cfg(); c.lastOk = ok; c.lastNote = new Date().toISOString() + ' ' + msg; this._save(c); };
    try {
      win = new BrowserWindow({ show: watch, width: 1366, height: 900, title: 'Salework — app đang thao tác (đóng cửa sổ này để dừng)', webPreferences: { partition: PARTITION, contextIsolation: true, nodeIntegration: false, sandbox: true, backgroundThrottling: false } });
      this.curWin = win;
      win.on('closed', () => { this.abort = true; });   // người dùng đóng cửa sổ = dừng ngay
      keepSingleWindow(win);
      tmp = fs.mkdtempSync(path.join(os.tmpdir(), 'sw-auto-'));
      // bắt file tải về — chỉ bắt đầu đếm giờ khi đã bấm sẵn sàng (arm), không tính thời gian tải trang
      let got = null;
      const arm = () => {
        got = new Promise((resolve, reject) => {
          const ses = win.webContents.session;
          const timer = setTimeout(() => { ses.removeListener('will-download', onDl); reject(new Error('Không thấy file tải về sau ' + Math.round(this.opts.downloadTimeout / 1000) + 's')); }, this.opts.downloadTimeout);
          const onDl = (ev, item) => {
            const name = path.basename(String(item.getFilename() || 'don_hoan.xlsx')).replace(/[^\w.\-]+/g, '_');
            const file = path.join(tmp, name);
            item.setSavePath(file);
            item.once('done', (e2, state) => { clearTimeout(timer); ses.removeListener('will-download', onDl); state === 'completed' ? resolve({ file, name }) : reject(new Error('Tải file không hoàn tất (' + state + ')')); });
          };
          ses.on('will-download', onDl);
        });
        got.catch(() => {});
      };
      step = 'mở trang';
      win.loadURL(cfg.url).catch(() => {});
      let st = null;
      try {
        st = await this._waitFor(win, s => s.hasExport || s.hasPassword, Math.min(this.opts.pageTimeout, 25000), 'Trang Salework không hiện nút "Xuất excel" sau ' + Math.round(Math.min(this.opts.pageTimeout, 25000) / 1000) + 's');
      } catch (e0) {
        if (this.abort || !/Xuất excel/.test(String(e0.message))) throw e0;
        // Trang đã nạp khung (menu bên trái) nhưng phần Đơn hoàn trống: đi bằng menu như người dùng vẫn làm
        // Kiểm tra, đối soát › Đơn hoàn (chỉ bấm menu điều hướng).
        step = 'vào Đơn hoàn bằng menu';
        try { await this._act(win, { text: 'Kiểm tra, đối soát' }, 'mở menu "Kiểm tra, đối soát"', watch); } catch (e1) { /* có thể đã mở sẵn */ }
        await sleep(this.opts.stepPause);
        await this._act(win, { text: 'Đơn hoàn' }, 'vào "Đơn hoàn"', watch);
        st = await this._waitFor(win, s => s.hasExport || s.hasPassword, this.opts.pageTimeout, 'Đã vào Đơn hoàn bằng menu nhưng vẫn không thấy nút "Xuất excel" sau ' + Math.round(this.opts.pageTimeout / 1000) + 's');
      }
      if (st.hasPassword && !st.hasExport) { const e = new Error('Salework yêu cầu đăng nhập lại.'); e.relogin = true; throw e; }
      step = 'chọn tab Đang chờ hoàn';
      await this._act(win, { text: 'Đang chờ hoàn', mode: 'prefix' }, 'chọn tab "Đang chờ hoàn"', watch);
      await sleep(this.opts.stepPause);
      step = 'kiểm tra kênh Tiktok';
      const s2 = await this._exec(win, 'state');
      if (!s2.channelTiktok) throw new Error('Không thấy kênh Tiktok trên trang — mở cửa sổ đăng nhập, chọn kênh Tiktok rồi lưu lại trang.' + this._diag(s2));
      step = 'bấm Xuất excel';
      await this._act(win, { text: 'Xuất excel' }, 'bấm "Xuất excel"', watch);
      await sleep(this.opts.stepPause);
      step = 'chọn "Theo bộ lọc"';
      await this._act(win, { text: 'Theo bộ lọc', inDialog: true }, 'chọn "Theo bộ lọc"', watch);
      await sleep(600);
      step = 'bấm Bắt Đầu Xuất';
      arm();
      await this._act(win, { text: 'Bắt Đầu Xuất', inDialog: true }, 'bấm "Bắt Đầu Xuất"', watch);
      step = 'chờ file tải về';
      const f = await got;
      step = 'đọc file';
      const rows = dropPersonalColumns(readXlsxRows(fs.readFileSync(f.file)));
      note(true, 'Lấy ' + rows.length + ' dòng');
      return { success: true, rows, count: rows.length, fileName: f.name };
    } catch (e) {
      let msg = (e && e.message) || String(e);
      if (this.abort && !/dừng/i.test(msg)) msg = 'Đã dừng (cửa sổ bị đóng) — ' + msg;
      note(false, 'Lỗi ở bước "' + step + '": ' + msg);
      return { success: false, step, error: msg, relogin: !!(e && e.relogin) };
    } finally {
      this.running = false; this.curWin = null;
      try { if (win && !win.isDestroyed()) win.destroy(); } catch (e) {}
      try { if (tmp) fs.rmSync(tmp, { recursive: true, force: true }); } catch (e) {}
    }
  }
}

module.exports = { SaleworkAuto, pageScript, PARTITION, FORBIDDEN };
