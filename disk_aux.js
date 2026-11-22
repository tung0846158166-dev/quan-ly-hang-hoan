// Luu "du lieu phu" (kho luu tru, bang don huy, hop don moi, danh muc, thong bao...) ra o cung
// va sao luu tu dong moi ngay. Tach rieng khoi main.js de kiem thu duoc bang file that.
const fs = require('fs');
const path = require('path');

// Cac khoa localStorage cua giao dien duoc phan chieu ra o cung (KHONG gom mat khau / trang thai khoa).
const AUX_KEYS = [
  'shopee_archived_orders_v1',
  'shopee_cancelled_orders_v1',
  'shopee_new_orders_inbox_v1',
  'shopee_excel_catalog_v1',
  'shopee_excel_accountant_stock_v1',
  'ShopeeReturnCellEditHistory_v1',
  'shopee_excel_cell_locks_v1',
  'shopee_excel_audit_log_v1',
  'shopee_app_notifications_v1',
  'shopee_excel_custom_week_tabs_v2',
  'shopee_excel_active_import_week_v2',
  'shopee_excel_order_db_v1',
  'deadline_notified_v1',
  'shopee_preferred_platform_workspace',
  'tiktok_price_col_mode_v1',
  'shopee_excel_phone_room_v1',
  'shopee_deleted_orders_v1'
];
const MAX_AUX_BYTES = 512 * 1024 * 1024;

function createAuxStore(getDataDir) {
  let tail = Promise.resolve();
  let seq = 0;
  const fileOf = key => path.join(getDataDir(), 'phu_' + key + '.json');

  function readAllSync() {
    const data = {};
    for (const key of AUX_KEYS) {
      try {
        const file = fileOf(key);
        if (!fs.existsSync(file)) continue;
        const parsed = JSON.parse(fs.readFileSync(file, 'utf8'));
        if (parsed && typeof parsed.v === 'string') data[key] = parsed.v;
      } catch (e) { /* file hong: bo qua, khong lam hong luc khoi dong */ }
    }
    return { keys: AUX_KEYS.slice(), data };
  }

  async function writeAtomic(file, text, n) {
    const temp = `${file}.${process.pid || 'app'}.${n}.tmp`;
    let handle;
    try {
      handle = await fs.promises.open(temp, 'wx', 0o600);
      await handle.writeFile(text, 'utf8');
      await handle.sync();
      await handle.close();
      handle = null;
      await fs.promises.rename(temp, file);
    } finally {
      if (handle) await handle.close().catch(() => {});
      await fs.promises.unlink(temp).catch(() => {});
    }
  }

  // value === null nghia la khoa da bi xoa: xoa file de lan mo sau khong "song lai".
  function save(key, value) {
    if (!AUX_KEYS.includes(key)) return Promise.resolve({ success: false, error: 'Khoa khong duoc phep' });
    if (value !== null && typeof value !== 'string') return Promise.resolve({ success: false, error: 'Gia tri khong hop le' });
    if (typeof value === 'string' && Buffer.byteLength(value, 'utf8') > MAX_AUX_BYTES) return Promise.resolve({ success: false, error: 'Vuot gioi han 512 MB' });
    const n = ++seq;
    const run = tail.then(async () => {
      const file = fileOf(key);
      if (value === null) { await fs.promises.unlink(file).catch(() => {}); return { success: true }; }
      await writeAtomic(file, JSON.stringify({ v: value }), n);
      return { success: true };
    });
    tail = run.catch(() => {});
    return run.catch(err => ({ success: false, error: err.message }));
  }

  const pad = n => String(n).padStart(2, '0');
  const dayName = d => `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;

  // Sao chep file du lieu + file phu vao sao_luu_tu_dong/<ngay>; ghi de ban cua hom nay, giu `keep` ngay gan nhat.
  function runBackup(now = new Date(), keep = 7) {
    try {
      const dir = getDataDir();
      const root = path.join(dir, 'sao_luu_tu_dong');
      const dest = path.join(root, dayName(now));
      fs.mkdirSync(dest, { recursive: true });
      let copied = 0;
      for (const name of fs.readdirSync(dir)) {
        if (name === 'du_lieu_hang_hoan.json' || /^phu_[A-Za-z0-9_]+\.json$/.test(name)) {
          fs.copyFileSync(path.join(dir, name), path.join(dest, name));
          copied++;
        }
      }
      const days = fs.readdirSync(root).filter(n => /^\d{4}-\d{2}-\d{2}$/.test(n)).sort().reverse();
      for (const old of days.slice(keep)) fs.rmSync(path.join(root, old), { recursive: true, force: true });
      return { success: true, path: dest, copied };
    } catch (e) {
      return { success: false, error: e.message };
    }
  }

  return { AUX_KEYS, readAllSync, save, runBackup, fileOf };
}

module.exports = { createAuxStore, AUX_KEYS };
