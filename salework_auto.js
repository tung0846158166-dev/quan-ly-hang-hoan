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
      // Một số nút có biểu tượng dạng chữ (vd "file_upload Xuất excel"): bỏ chữ biểu tượng ở đầu / cuối khi so khớp
      const variants = x => [x, x.replace(/^[a-z_]{3,}\s+(?=\S)/, ''), x.replace(/(?<=\S)\s+[a-z_]{3,}$/, '')];   // bỏ chữ biểu tượng ở đầu HOẶC cuối (không bỏ cả hai)
      const hit = all.filter(el => variants(norm(el.textContent)).some(v => mode === 'prefix' ? v.startsWith(t) : v === t));
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
      // Chữ "Theo bộ lọc" / "Bắt Đầu Xuất" chỉ có trong cửa sổ xuất: ưu tiên tìm trong cửa sổ nếu nhận ra, không thì tìm cả trang.
      let within = a.inDialog ? dialogRoot() : null;
      let c = find(a.text, a.mode || 'exact', within);
      if (!c.length && within) c = find(a.text, a.mode || 'exact', null);
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
    if (action === 'rect') {   // toạ độ giữa nút + kiểm tra phần tử trên cùng tại điểm đó đúng là nút (không bị che, không phải "cập nhật")
      const L = locate(arg);
      if (L.error) return { ok: false, error: L.error };
      L.el.scrollIntoView && L.el.scrollIntoView({ block: 'center' });
      const r = L.el.getBoundingClientRect();
      const x = Math.round(Math.min(Math.max(r.left + r.width / 2, 1), window.innerWidth - 1)), y = Math.round(Math.min(Math.max(r.top + r.height / 2, 1), window.innerHeight - 1));
      if (r.width < 1 || r.height < 1) return { ok: false, error: 'nút không có kích thước' };
      const top = document.elementFromPoint(x, y);
      const topOk = !!top && L.el.contains(top) && !FORBID.test(norm(top.textContent));   // điểm bấm phải rơi vào chính nút (không bị lớp khác che)
      return { ok: true, x, y, topOk, topText: top ? norm(top.textContent).slice(0, 40) : '' };
    }
    if (action === 'hover') {   // phương án dự phòng khi không gửi được chuột thật
      const L = locate(arg);
      if (L.error) return { ok: false, error: L.error };
      ['mouseover', 'mouseenter', 'mousemove'].forEach(n => L.el.dispatchEvent(new MouseEvent(n, { bubbles: n !== 'mouseenter', cancelable: true, view: window })));
      return { ok: true };
    }
    if (action === 'exists') { const L = locate(arg); return L.error ? { ok: false, error: L.error } : { ok: true }; }
    if (action === 'texts') {   // chữ đang nhìn thấy (ngắn) để chẩn đoán — không có giá trị dữ liệu dài
      const seen = []; const set = new Set();
      Array.from(document.querySelectorAll('button,a,label,span,div,p,li,h1,h2,h3,h4')).filter(visible).forEach(el => {
        if (el.children.length && Array.from(el.children).some(c => norm(c.textContent))) return;
        const t = String(el.textContent || '').replace(/\s+/g, ' ').trim();
        if (t && t.length <= 40 && !/\d{6,}/.test(t) && !set.has(t)) { set.add(t); seen.push(t); }
      });
      return { ok: true, texts: seen.slice(0, 60) };
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
    this.opts = Object.assign({ pageTimeout: 45000, stepPause: 2500, downloadTimeout: 120000, watchPause: 2500, actWait: 8000, clickRetry: 6000, keepOnError: 120000 }, opts || {});
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
  /** Chuột THẬT (Salework mở menu Xuất excel khi di chuột vào, không phải khi bấm). mode: 'move' | 'click'. false = không dùng được. */
  async _mouse(win, arg, mode) {
    const wc = win.webContents;
    if (typeof wc.sendInputEvent !== 'function') return false;
    const r = await this._exec(win, 'rect', arg);
    if (!r || !r.ok || typeof r.x !== 'number') return false;
    if (!r.topOk) throw new Error('Nút "' + arg.text + '" đang bị che bởi "' + (r.topText || '?') + '" — không bấm để tránh bấm nhầm');
    wc.sendInputEvent({ type: 'mouseMove', x: r.x, y: r.y });
    await sleep(250);
    if (mode === 'click') {
      wc.sendInputEvent({ type: 'mouseDown', x: r.x, y: r.y, button: 'left', clickCount: 1 });
      await sleep(80);
      wc.sendInputEvent({ type: 'mouseUp', x: r.x, y: r.y, button: 'left', clickCount: 1 });
    }
    return true;
  }
  async _act(win, arg, label, watch, wait) {
    if (this.abort) throw new Error('Đã dừng theo yêu cầu.');
    // đợi phần tử xuất hiện (cửa sổ xuất có thể hiện chậm) trước khi kết luận là không thấy
    const limit = wait === undefined ? this.opts.actWait : wait;
    const t0 = Date.now(); let ex = await this._exec(win, 'exists', arg);
    while (!ex.ok && Date.now() - t0 < limit) {
      if (this.abort) throw new Error('Đã dừng theo yêu cầu.');
      await sleep(400); ex = await this._exec(win, 'exists', arg);
    }
    if (!ex.ok) {
      let more = '';
      try { const tx = await this._exec(win, 'texts'); if (tx && tx.texts) more = ' — trên trang đang thấy: [' + tx.texts.join(' | ') + ']'; } catch (e) {}
      throw new Error(ex.error + more);
    }
    if (watch) {
      try { await this._exec(win, 'banner', { text: '🤖 App đang: ' + label + ' — đóng cửa sổ này hoặc bấm "Dừng" trong app để DỪNG NGAY' }); } catch (e) {}
      const m = await this._exec(win, 'mark', arg);
      if (!m.ok) throw new Error(m.error);
      await sleep(this.opts.watchPause);
      if (this.abort) throw new Error('Đã dừng theo yêu cầu.');
    }
    if (arg.hover) {   // chỉ đưa chuột vào (không bấm): menu mở khi rê chuột
      if (!(await this._mouse(win, arg, 'move'))) { const h = await this._exec(win, 'hover', arg); if (!h.ok) throw new Error(h.error); }
      return { ok: true };
    }
    // Chuột thật trước (đúng như người dùng); không được thì bấm bằng sự kiện trong trang
    if (await this._mouse(win, arg, 'click')) return { ok: true };
    // Sau khi chờ, giao diện có thể đã vẽ lại (vd nút đổi chữ / đổi chỗ): tìm lại và bấm, thử lại vài lần trước khi báo lỗi
    let r = await this._exec(win, 'click', arg);
    const t1 = Date.now();
    while (!r.ok && Date.now() - t1 < this.opts.clickRetry) {
      if (this.abort) throw new Error('Đã dừng theo yêu cầu.');
      await sleep(400); r = await this._exec(win, 'click', arg);
    }
    if (!r.ok) {
      let more = '';
      try { const tx = await this._exec(win, 'texts'); if (tx && tx.texts) more = ' — trên trang đang thấy: [' + tx.texts.join(' | ') + ']'; } catch (e) {}
      throw new Error(r.error + more);
    }
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
    try { if (this.curWin && !this.curWin.isDestroyed()) this.curWin.destroy(); } catch (e) {}
    this.running = true; this.abort = false;
    const watch = cfg.watch !== false;
    const { BrowserWindow } = this.electron;
    let win = null, tmp = null, step = 'mở trang', keep = false;
    const note = (ok, msg) => { const c = this._cfg(); c.lastOk = ok; c.lastNote = new Date().toISOString() + ' ' + msg; this._save(c); };
    try {
      win = new BrowserWindow({ show: watch, width: 1366, height: 900, title: 'Salework — app đang thao tác (đóng cửa sổ này để dừng)', webPreferences: { partition: PARTITION, contextIsolation: true, nodeIntegration: false, sandbox: true, backgroundThrottling: false } });
      this.curWin = win;
      win.on('closed', () => { if (this.curWin === win) this.abort = true; });   // người dùng đóng cửa sổ = dừng ngay
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
        let direct = true;
        try { await this._act(win, { text: 'Đơn hoàn' }, 'vào "Đơn hoàn"', watch, 1200); } catch (e1) {
          if (this.abort) throw e1; direct = false;   // menu chưa mở: mở nhóm rồi bấm
        }
        if (!direct) {
          await this._act(win, { text: 'Kiểm tra, đối soát' }, 'mở menu "Kiểm tra, đối soát"', watch);
          await sleep(this.opts.stepPause);
          await this._act(win, { text: 'Đơn hoàn' }, 'vào "Đơn hoàn"', watch);
        }
        st = await this._waitFor(win, s => s.hasExport || s.hasPassword, this.opts.pageTimeout, 'Đã vào Đơn hoàn bằng menu nhưng vẫn không thấy nút "Xuất excel" sau ' + Math.round(this.opts.pageTimeout / 1000) + 's');
      }
      if (st.hasPassword && !st.hasExport) { const e = new Error('Salework yêu cầu đăng nhập lại.'); e.relogin = true; throw e; }
      step = 'chọn tab Đang chờ hoàn';
      await this._act(win, { text: 'Đang chờ hoàn', mode: 'prefix' }, 'chọn tab "Đang chờ hoàn"', watch);
      await sleep(this.opts.stepPause);
      step = 'kiểm tra kênh Tiktok';
      const s2 = await this._exec(win, 'state');
      if (!s2.channelTiktok) throw new Error('Không thấy kênh Tiktok trên trang — mở cửa sổ đăng nhập, chọn kênh Tiktok rồi lưu lại trang.' + this._diag(s2));
      step = 'đưa chuột vào Xuất excel';
      // Salework mở menu xuất khi RÊ CHUỘT vào nút "Xuất excel" (không phải bấm)
      await this._act(win, { text: 'Xuất excel', hover: true }, 'đưa chuột vào "Xuất excel"', watch);
      await sleep(this.opts.stepPause);
      step = 'chọn "Theo bộ lọc"';
      const seen = await this._exec(win, 'exists', { text: 'Theo bộ lọc', mode: 'prefix', inDialog: true });
      if (!seen.ok) {   // rê chuột chưa mở menu: thử bấm nút
        step = 'bấm Xuất excel';
        await this._act(win, { text: 'Xuất excel' }, 'bấm "Xuất excel"', watch);
        await sleep(this.opts.stepPause);
        step = 'chọn "Theo bộ lọc"';
      }
      await this._act(win, { text: 'Theo bộ lọc', mode: 'prefix', inDialog: true }, 'chọn "Theo bộ lọc"', watch);
      await sleep(600);
      step = 'bấm Bắt Đầu Xuất';
      arm();
      await this._act(win, { text: 'Bắt Đầu Xuất', mode: 'prefix', inDialog: true }, 'bấm "Bắt Đầu Xuất"', watch);
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
      if (watch && !this.abort && win && !win.isDestroyed() && this.opts.keepOnError > 0) {
        // chế độ xem: giữ cửa sổ lại để người dùng nhìn thấy trang đang ở đâu (tự đóng sau ít phút)
        keep = true;
        try { win.webContents.executeJavaScript(pageScript('banner', { text: '⚠️ App dừng ở bước "' + step + '" — bạn có thể xem trang này, rồi đóng cửa sổ' }), true).catch(() => {}); } catch (e2) {}
        const w = win; const tm = setTimeout(() => { try { if (!w.isDestroyed()) w.destroy(); } catch (e3) {} }, this.opts.keepOnError); tm.unref && tm.unref();
      }
      return { success: false, step, error: msg, relogin: !!(e && e.relogin) };
    } finally {
      this.running = false; if (!keep) this.curWin = null;
      try { if (win && !win.isDestroyed() && !keep) win.destroy(); } catch (e) {}
      try { if (tmp) fs.rmSync(tmp, { recursive: true, force: true }); } catch (e) {}
    }
  }
}

module.exports = { SaleworkAuto, pageScript, PARTITION, FORBIDDEN };
