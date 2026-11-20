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
    const norm = s => String(s || '').replace(/\s+/g, ' ').trim().toLowerCase();
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
        title: document.title
      };
    }
    if (action === 'click') {
      if (FORBID.test(arg.text)) return { ok: false, error: 'từ chối bấm nút có chữ "cập nhật"' };
      const within = arg.inDialog ? dialogRoot() : null;
      if (arg.inDialog && !within) return { ok: false, error: 'chưa thấy cửa sổ xuất' };
      const c = find(arg.text, arg.mode || 'exact', within);
      if (!c.length) return { ok: false, error: 'không thấy "' + arg.text + '"' };
      if (FORBID.test(c[0].textContent)) return { ok: false, error: 'phần tử khớp có chữ "cập nhật", bỏ qua' };
      click(c[0]);
      return { ok: true, count: c.length };
    }
    if (action === 'dialogHas') {
      const d = dialogRoot();
      return { ok: !!d && norm(d.textContent).includes(norm(arg.text)) };
    }
    return { ok: false, error: 'action lạ' };
  };
  return '(' + code.toString() + ')(' + JSON.stringify(action) + ',' + JSON.stringify(arg === undefined ? null : arg) + ')';
}

class SaleworkAuto {
  /** electron: module electron (hoặc bản giả khi test). cfgDir: thư mục lưu địa chỉ trang. */
  constructor(electron, cfgDir, opts) {
    this.electron = electron;
    this.cfgFile = path.join(cfgDir, 'salework_auto.json');
    this.opts = Object.assign({ pageTimeout: 45000, stepPause: 2500, downloadTimeout: 120000 }, opts || {});
    this.loginWin = null;
    this.running = false;
  }
  _cfg() { try { return JSON.parse(fs.readFileSync(this.cfgFile, 'utf8')) || {}; } catch (e) { return {}; } }
  _save(c) { try { fs.mkdirSync(path.dirname(this.cfgFile), { recursive: true }); fs.writeFileSync(this.cfgFile, JSON.stringify(c)); } catch (e) {} }
  status() { const c = this._cfg(); return { configured: !!c.url, url: c.url || '', lastOk: c.lastOk || null, lastNote: c.lastNote || '', running: this.running, loginOpen: !!(this.loginWin && !this.loginWin.isDestroyed()) }; }

  /** Cửa sổ HIỆN: người dùng tự đăng nhập Salework và mở trang Đơn hoàn (kênh Tiktok). Bấm "Lưu" -> nhớ địa chỉ trang hiện tại. */
  openLogin(startUrl) {
    const { BrowserWindow } = this.electron;
    if (this.loginWin && !this.loginWin.isDestroyed()) { this.loginWin.focus(); return { success: true }; }
    const url = String(startUrl || this._cfg().url || 'https://salework.net');
    if (!/^https:\/\//i.test(url)) return { success: false, error: 'Địa chỉ phải bắt đầu bằng https://' };
    const win = new BrowserWindow({ width: 1280, height: 860, title: 'Salework — đăng nhập rồi mở trang Đơn hoàn', webPreferences: { partition: PARTITION, contextIsolation: true, nodeIntegration: false, sandbox: true } });
    this.loginWin = win;
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
  closeLogin() { if (this.loginWin && !this.loginWin.isDestroyed()) this.loginWin.close(); return { success: true }; }
  async forget() {
    const c = this._cfg(); delete c.url; this._save(c);
    try { await this.electron.session.fromPartition(PARTITION).clearStorageData(); } catch (e) {}
    return { success: true };
  }

  async _exec(win, action, arg) { return win.webContents.executeJavaScript(pageScript(action, arg), true); }
  async _waitFor(win, pred, timeout, label) {
    const t0 = Date.now(); let last = null;
    while (Date.now() - t0 < timeout) {
      try { last = await this._exec(win, 'state'); if (pred(last)) return last; } catch (e) {}
      await sleep(700);
    }
    const err = new Error(label); err.state = last; throw err;
  }

  /** Chạy một lượt. -> { success, rows, count, fileName } hoặc { success:false, step, error } */
  async run() {
    if (this.running) return { success: false, step: 'busy', error: 'Đang chạy lượt trước.' };
    const cfg = this._cfg();
    if (!cfg.url) return { success: false, step: 'config', error: 'Chưa cài đặt: bấm "Đăng nhập Salework" và lưu trang Đơn hoàn.' };
    this.running = true;
    const { BrowserWindow } = this.electron;
    let win = null, tmp = null, step = 'mở trang';
    const note = (ok, msg) => { const c = this._cfg(); c.lastOk = ok; c.lastNote = new Date().toISOString() + ' ' + msg; this._save(c); };
    try {
      win = new BrowserWindow({ show: false, width: 1366, height: 900, webPreferences: { partition: PARTITION, contextIsolation: true, nodeIntegration: false, sandbox: true, backgroundThrottling: false } });
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
      const st = await this._waitFor(win, s => s.hasExport || s.hasPassword, this.opts.pageTimeout, 'Trang Salework không hiện nút "Xuất excel" sau ' + Math.round(this.opts.pageTimeout / 1000) + 's');
      if (st.hasPassword && !st.hasExport) { const e = new Error('Salework yêu cầu đăng nhập lại.'); e.relogin = true; throw e; }
      step = 'chọn tab Đang chờ hoàn';
      let r = await this._exec(win, 'click', { text: 'Đang chờ hoàn', mode: 'prefix' });
      if (!r.ok) throw new Error(r.error);
      await sleep(this.opts.stepPause);
      step = 'kiểm tra kênh Tiktok';
      const s2 = await this._exec(win, 'state');
      if (!s2.channelTiktok) throw new Error('Không thấy kênh Tiktok trên trang — mở cửa sổ đăng nhập, chọn kênh Tiktok rồi lưu lại trang.');
      step = 'bấm Xuất excel';
      r = await this._exec(win, 'click', { text: 'Xuất excel' });
      if (!r.ok) throw new Error(r.error);
      await sleep(this.opts.stepPause);
      step = 'chọn "Theo bộ lọc"';
      r = await this._exec(win, 'click', { text: 'Theo bộ lọc', inDialog: true });
      if (!r.ok) throw new Error(r.error);
      await sleep(600);
      step = 'bấm Bắt Đầu Xuất';
      arm();
      r = await this._exec(win, 'click', { text: 'Bắt Đầu Xuất', inDialog: true });
      if (!r.ok) throw new Error(r.error);
      step = 'chờ file tải về';
      const f = await got;
      step = 'đọc file';
      const rows = dropPersonalColumns(readXlsxRows(fs.readFileSync(f.file)));
      note(true, 'Lấy ' + rows.length + ' dòng');
      return { success: true, rows, count: rows.length, fileName: f.name };
    } catch (e) {
      const msg = (e && e.message) || String(e);
      note(false, 'Lỗi ở bước "' + step + '": ' + msg);
      return { success: false, step, error: msg, relogin: !!(e && e.relogin) };
    } finally {
      this.running = false;
      try { if (win && !win.isDestroyed()) win.destroy(); } catch (e) {}
      try { if (tmp) fs.rmSync(tmp, { recursive: true, force: true }); } catch (e) {}
    }
  }
}

module.exports = { SaleworkAuto, pageScript, PARTITION, FORBIDDEN };
